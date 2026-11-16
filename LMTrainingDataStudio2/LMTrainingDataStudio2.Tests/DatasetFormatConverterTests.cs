using System.Text.Json.Nodes;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class DatasetFormatConverterTests
{
    private readonly DatasetFormatConverter _converter = new();

    [Test]
    public void DetectFormat_RecognizesSupportedShapes()
    {
        Assert.That(Detect("""{"messages":[]}"""), Is.EqualTo(DatasetFormat.OpenAI));
        Assert.That(Detect("""{"conversations":[]}"""), Is.EqualTo(DatasetFormat.ShareGPT));
        Assert.That(Detect("""{"instruction":"x","output":"y"}"""), Is.EqualTo(DatasetFormat.Alpaca));
        Assert.That(Detect("""{"foo":"bar"}"""), Is.EqualTo(DatasetFormat.Auto));
    }

    [Test]
    public void Convert_OpenAI_NormalizesMessages()
    {
        var sample = _converter.Convert(Parse("""
            {
              "messages": [
                {"role":"system","content":"be nice"},
                {"role":"user","content":"hi"},
                {"role":"assistant","content":"hello"}
              ]
            }
            """));

        Assert.That(sample.Messages, Has.Count.EqualTo(3));
        Assert.That(sample.Messages[0].Role, Is.EqualTo("system"));
        Assert.That(sample.Messages[0].Content, Is.EqualTo("be nice"));
        Assert.That(sample.Messages[1].Content, Is.EqualTo("hi"));
        Assert.That(sample.Messages[2].Content, Is.EqualTo("hello"));
    }

    [Test]
    public void Convert_ShareGPT_MapsHumanAndGptRoles()
    {
        var sample = _converter.Convert(Parse("""
            {"conversations":[
              {"from":"human","value":"hi"},
              {"from":"gpt","value":"hello"}
            ]}
            """));

        Assert.That(sample.Messages.Select(m => m.Role), Is.EqualTo(new[] { "user", "assistant" }));
        Assert.That(sample.Messages.Select(m => m.Content), Is.EqualTo(new[] { "hi", "hello" }));
    }

    [Test]
    public void Convert_Alpaca_BuildsPromptAndOutput()
    {
        var sample = _converter.Convert(Parse("""
            {"system":"sys","instruction":"translate","input":"hello","output":"bonjour"}
            """));

        Assert.That(sample.Messages, Has.Count.EqualTo(3));
        Assert.That(sample.Messages[0].Role, Is.EqualTo("system"));
        Assert.That(sample.Messages[1].Role, Is.EqualTo("user"));
        Assert.That(sample.Messages[1].Content, Is.EqualTo("translate\nhello"));
        Assert.That(sample.Messages[2].Role, Is.EqualTo("assistant"));
        Assert.That(sample.Messages[2].Content, Is.EqualTo("bonjour"));
    }

    [Test]
    public void Convert_UnknownShape_FallsBackToPromptResponse()
    {
        var sample = _converter.Convert(Parse("""{"prompt":"q","response":"a"}"""));

        Assert.That(sample.Messages, Has.Count.EqualTo(2));
        Assert.That(sample.Messages[0].Content, Is.EqualTo("q"));
        Assert.That(sample.Messages[1].Content, Is.EqualTo("a"));
    }

    [Test]
    public void ConvertTo_OpenAI_KeepsToolCalls()
    {
        var sample = new SftSample
        {
            Messages =
            {
                new ChatMessage
                {
                    Role = "assistant",
                    Content = "",
                    ToolCalls = new List<ToolCall>
                    {
                        new()
                        {
                            Id = "call_1",
                            Function = new ToolCallFunction
                            {
                                Name = "get_weather",
                                Arguments = """{"city":"Paris"}"""
                            }
                        }
                    }
                }
            }
        };

        var row = _converter.ConvertTo(sample, DatasetFormat.OpenAI);
        var message = row["messages"]!.AsArray()[0]!.AsObject();
        var toolCalls = message["tool_calls"]!.AsArray();
        var toolCall = toolCalls[0]!.AsObject();

        Assert.That(toolCalls, Has.Count.EqualTo(1));
        Assert.That(toolCall["id"]!.GetValue<string>(), Is.EqualTo("call_1"));
        Assert.That(toolCall["function"]!.AsObject()["name"]!.GetValue<string>(), Is.EqualTo("get_weather"));
    }

    [Test]
    public void ConvertTo_ShareGPT_MapsRolesAndKeepsTools()
    {
        var sample = new SftSample
        {
            Messages =
            {
                new ChatMessage { Role = "user", Content = "hi" },
                new ChatMessage { Role = "assistant", Content = "hello" }
            },
            Tools = new List<ToolDefinition>
            {
                new()
                {
                    Function = new ToolFunction { Name = "lookup", Description = "Look up a value" }
                }
            }
        };

        var row = _converter.ConvertTo(sample, DatasetFormat.ShareGPT);
        var conversations = row["conversations"]!.AsArray();

        Assert.That(conversations[0]!.AsObject()["from"]!.GetValue<string>(), Is.EqualTo("human"));
        Assert.That(conversations[1]!.AsObject()["from"]!.GetValue<string>(), Is.EqualTo("gpt"));
        Assert.That(row["tools"]!.AsArray(), Has.Count.EqualTo(1));
    }

    private DatasetFormat Detect(string json) => _converter.DetectFormat(Parse(json));

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();
}
