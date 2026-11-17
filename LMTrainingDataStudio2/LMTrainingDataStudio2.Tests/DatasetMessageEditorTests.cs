using System.Text.Json.Nodes;
using LMTrainingDataStudio2.Services.Datasets;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class DatasetMessageEditorTests
{
    private const string ChatJson = """
        [{"role":"user","content":"hi","name":"u1"},
         {"role":"assistant","content":"hello",
          "tool_calls":[{"id":"call_1","type":"function","function":{"name":"lookup","arguments":"{}"}}]}]
        """;

    [Test]
    public void TryReadMessages_ReadsRolesContentAndToolCalls()
    {
        var ok = DatasetMessageEditor.TryReadMessages(ChatJson, out var messages, out var error);

        Assert.That(ok, Is.True, error);
        Assert.That(messages, Has.Count.EqualTo(2));
        Assert.That(messages[0].Role, Is.EqualTo("user"));
        Assert.That(messages[0].ContentText, Is.EqualTo("hi"));
        Assert.That(messages[0].ContentIsJson, Is.False);
        Assert.That(messages[0].ToolCallsText, Is.Empty);

        Assert.That(messages[1].Role, Is.EqualTo("assistant"));
        Assert.That(messages[1].ToolCallsText, Does.Contain("\"lookup\""));
    }

    [Test]
    public void TryBuild_AppliesEditsAndPreservesOtherProperties()
    {
        DatasetMessageEditor.TryReadMessages(ChatJson, out var messages, out _);

        var edited = messages
            .Select((message, index) => index == 0
                ? message with { Role = "system", ContentText = "be brief" }
                : message with { ContentText = "ok" })
            .ToArray();

        var ok = DatasetMessageEditor.TryBuild(edited, out var json, out var error);

        Assert.That(ok, Is.True, error);
        var array = JsonNode.Parse(json)!.AsArray();
        Assert.That(array, Has.Count.EqualTo(2));
        Assert.That(array[0]!["role"]!.GetValue<string>(), Is.EqualTo("system"));
        Assert.That(array[0]!["content"]!.GetValue<string>(), Is.EqualTo("be brief"));
        Assert.That(array[0]!["name"]!.GetValue<string>(), Is.EqualTo("u1"));
        Assert.That(array[1]!["tool_calls"]!.AsArray(), Has.Count.EqualTo(1));
        Assert.That(
            array[1]!["tool_calls"]![0]!["function"]!["name"]!.GetValue<string>(),
            Is.EqualTo("lookup"));
    }

    [Test]
    public void TryReadMessages_StructuredContent_IsEditedAsJson()
    {
        const string json = """[{"role":"user","content":[{"type":"text","text":"hi"}]}]""";

        var ok = DatasetMessageEditor.TryReadMessages(json, out var messages, out var error);

        Assert.That(ok, Is.True, error);
        Assert.That(messages[0].ContentIsJson, Is.True);
        Assert.That(messages[0].ContentText, Does.Contain("\"text\""));

        var edited = new[] { messages[0] with { ContentText = """[{"type":"text","text":"bye"}]""" } };
        Assert.That(DatasetMessageEditor.TryBuild(edited, out var built, out var buildError),
            Is.True, buildError);
        Assert.That(JsonNode.Parse(built)![0]!["content"]![0]!["text"]!.GetValue<string>(),
            Is.EqualTo("bye"));
    }

    [Test]
    public void TryBuild_EmptyToolCalls_RemovesProperty()
    {
        DatasetMessageEditor.TryReadMessages(ChatJson, out var messages, out _);

        var trimmed = messages
            .Select(message => message with { ToolCallsText = string.Empty })
            .ToArray();

        Assert.That(DatasetMessageEditor.TryBuild(trimmed, out var json, out var error),
            Is.True, error);
        Assert.That(json, Does.Not.Contain("tool_calls"));
    }

    [Test]
    public void TryReadMessages_NonArray_Fails()
    {
        var ok = DatasetMessageEditor.TryReadMessages("""{"role":"user"}""", out _, out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("must be a JSON array"));
    }

    [Test]
    public void TryReadMessages_NonObjectElement_Fails()
    {
        var ok = DatasetMessageEditor.TryReadMessages("""["hi"]""", out _, out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("Message 1"));
    }

    [Test]
    public void TryBuild_EmptyRole_Fails()
    {
        var ok = DatasetMessageEditor.TryBuild(
            new[] { new DatasetMessage("  ", "hi", false, string.Empty, "{}") },
            out _,
            out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("role must not be empty"));
    }

    [Test]
    public void TryBuild_InvalidToolCalls_Fails()
    {
        var ok = DatasetMessageEditor.TryBuild(
            new[] { new DatasetMessage("assistant", "hi", false, "{broken", "{}") },
            out _,
            out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("tool_calls"));
        Assert.That(error, Does.Contain("not valid JSON"));
    }

    [Test]
    public void TryBuild_NonArrayToolCalls_Fails()
    {
        var ok = DatasetMessageEditor.TryBuild(
            new[] { new DatasetMessage("assistant", "hi", false, """{"id":"x"}""", "{}") },
            out _,
            out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("must be a JSON array"));
    }

    [Test]
    public void TryBuild_InvalidStructuredContent_Fails()
    {
        var ok = DatasetMessageEditor.TryBuild(
            new[] { new DatasetMessage("user", "{broken", true, string.Empty, "{}") },
            out _,
            out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("content is not valid JSON"));
    }
}
