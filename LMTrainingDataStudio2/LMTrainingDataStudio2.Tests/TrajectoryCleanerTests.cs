using System.Text.Json.Nodes;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Simulation;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class TrajectoryCleanerTests
{
    [Test]
    public void CleanText_AppliesRegexReplacementsWithIgnoreCase()
    {
        var rules = new DatasetCleanupRules
        {
            RegexReplacements = new[] { new RegexReplacementRule(@"foo\s+bar", "baz", IgnoreCase: true) }
        };

        var cleaned = TrajectoryCleaner.CleanText("FOO   bar and foo bar", rules);

        Assert.That(cleaned, Is.EqualTo("baz and baz"));
    }

    [Test]
    public void CleanText_NormalizesNewlinesAndWhitespace()
    {
        var rules = new DatasetCleanupRules
        {
            NormalizeNewlines = true,
            NormalizeWhitespace = true
        };

        var cleaned = TrajectoryCleaner.CleanText("line1   \r\n\r\n\r\n\r\nline2  ", rules);

        Assert.That(cleaned, Is.EqualTo("line1\n\nline2"));
    }

    [Test]
    public void CleanText_StripsAnsiEscapes()
    {
        var rules = new DatasetCleanupRules { StripAnsi = true };

        var cleaned = TrajectoryCleaner.CleanText("\u001b[31mred\u001b[0m text", rules);

        Assert.That(cleaned, Is.EqualTo("red text"));
    }

    [Test]
    public void CleanText_InvalidRegex_ThrowsWithPattern()
    {
        var rules = new DatasetCleanupRules
        {
            RegexReplacements = new[] { new RegexReplacementRule("[", "x") }
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TrajectoryCleaner.CleanText("abc", rules));

        Assert.That(exception!.Message, Does.Contain("["));
    }

    [Test]
    public void CleanSample_RemovesReasoningAndCleansToolDescriptions()
    {
        var sample = JsonNode.Parse(
            """
            {
              "messages": [
                {"role":"assistant","content":"hi","reasoning_content":"secret"}
              ],
              "tools": [
                {"type":"function","function":{"name":"t","description":"  spaced   text  "}}
              ]
            }
            """)!.AsObject();
        var rules = new DatasetCleanupRules
        {
            RemoveReasoningContent = true,
            NormalizeWhitespace = true
        };

        var changed = TrajectoryCleaner.CleanSample(sample, rules);

        Assert.That(changed, Is.True);
        var message = sample["messages"]![0]!.AsObject();
        Assert.That(message.ContainsKey("reasoning_content"), Is.False);
        var description = sample["tools"]![0]!["function"]!["description"]!.GetValue<string>();
        Assert.That(description, Is.EqualTo("spaced   text"));
    }

    [Test]
    public void CleanSample_DropEmptyMessages_KeepsToolCallMessages()
    {
        var sample = JsonNode.Parse(
            """
            {
              "messages": [
                {"role":"system","content":""},
                {"role":"user","content":"   "},
                {"role":"assistant","content":"","tool_calls":[{"id":"c1","type":"function","function":{"name":"t","arguments":"{}"}}]},
                {"role":"tool","tool_call_id":"c1","content":"ok"}
              ]
            }
            """)!.AsObject();
        var rules = new DatasetCleanupRules { DropEmptyMessages = true };

        var changed = TrajectoryCleaner.CleanSample(sample, rules);

        Assert.That(changed, Is.True);
        var messages = sample["messages"]!.AsArray();
        Assert.That(messages, Has.Count.EqualTo(2));
        Assert.That(messages[0]!["role"]!.GetValue<string>(), Is.EqualTo("assistant"));
        Assert.That(messages[1]!["role"]!.GetValue<string>(), Is.EqualTo("tool"));
    }

    [Test]
    public void CleanSample_CleansArrayTextContent()
    {
        var sample = JsonNode.Parse(
            """
            {
              "messages": [
                {"role":"user","content":[{"type":"text","text":"  hi  "},{"type":"image_url","value":"http://x/y.png"}]}
              ]
            }
            """)!.AsObject();
        var rules = new DatasetCleanupRules { NormalizeWhitespace = true };

        TrajectoryCleaner.CleanSample(sample, rules);

        var parts = sample["messages"]![0]!["content"]!.AsArray();
        Assert.That(parts[0]!["text"]!.GetValue<string>(), Is.EqualTo("hi"));
        Assert.That(parts[1]!["value"]!.GetValue<string>(), Is.EqualTo("http://x/y.png"));
    }

    [Test]
    public void CleanJson_RowWithoutMessages_IsReturnedUnchanged()
    {
        const string json = """{"a":1}""";
        var rules = new DatasetCleanupRules { NormalizeWhitespace = true };

        var cleaned = TrajectoryCleaner.CleanJson(json, rules);

        Assert.That(cleaned, Is.EqualTo(json));
    }

    [Test]
    public void CleanJson_ChangedRow_IsCompactJson()
    {
        const string json =
            """{"messages":[{"role":"assistant","content":"  hello  "}],"meta":{"keep":true}}""";
        var rules = new DatasetCleanupRules { NormalizeWhitespace = true };

        var cleaned = TrajectoryCleaner.CleanJson(json, rules);

        var node = JsonNode.Parse(cleaned)!.AsObject();
        Assert.That(node["messages"]![0]!["content"]!.GetValue<string>(), Is.EqualTo("hello"));
        Assert.That(node["meta"]!["keep"]!.GetValue<bool>(), Is.True);
    }
}
