using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class ExpressionTemplateTests
{
    [Test]
    public async Task FluidTemplate_SupportsConditionsFiltersAndLoops()
    {
        var template = """
            {"has_score":{% if score > 10 %}true{% else %}false{% endif %},"upper":"{{ content | upcase }}","roles":[{% for m in messages %}"{{ m.role }}"{% if forloop.last != true %},{% endif %}{% endfor %}],"count":{{ messages | size }}}
            """.Trim();
        var block = CreateBlock(new ExpressionBlockConfig(template, new Dictionary<string, string>()));
        var input = new[]
        {
            new DatasetRow(0,
                """{"score":42,"content":"hello","messages":[{"role":"user"},{"role":"assistant"}]}""")
        };

        var executor = new ExpressionBlockExecutor();
        var rows = await CollectAsync(
            (await executor.ExecuteAsync(CreateContext(block, input), CancellationToken.None)).Output);

        Assert.That(rows, Has.Count.EqualTo(1));
        using var document = JsonDocument.Parse(rows[0].Json);
        var root = document.RootElement;
        Assert.That(root.GetProperty("has_score").GetBoolean(), Is.True);
        Assert.That(root.GetProperty("upper").GetString(), Is.EqualTo("HELLO"));
        Assert.That(root.GetProperty("roles").EnumerateArray().Select(item => item.GetString()),
            Is.EqualTo(new[] { "user", "assistant" }));
        Assert.That(root.GetProperty("count").GetInt32(), Is.EqualTo(2));
    }

    [Test]
    public void FluidTemplate_InvalidSyntax_IsRejectedBeforeEnumeration()
    {
        var block = CreateBlock(new ExpressionBlockConfig("{% if %}", new Dictionary<string, string>()));
        var executor = new ExpressionBlockExecutor();

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await executor.ExecuteAsync(CreateContext(block, Array.Empty<DatasetRow>()), CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("invalid template"));
    }

    [Test]
    public async Task ExpressionExecutor_InvalidRenderedJson_ThrowsWithLineNumber()
    {
        var block = CreateBlock(new ExpressionBlockConfig(
            """{"value": {{ content }}}""",
            new Dictionary<string, string>()));
        var input = new[] { new DatasetRow(7, """{"content":"a\"b"}""") };

        var executor = new ExpressionBlockExecutor();
        var result = await executor.ExecuteAsync(CreateContext(block, input), CancellationToken.None);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await CollectAsync(result.Output));
        Assert.That(ex!.Message, Does.Contain("invalid JSON"));
        Assert.That(ex.Message, Does.Contain("line 7"));
    }

    [Test]
    public async Task ExpressionEditor_PreviewRendersAndReportsErrors()
    {
        var applied = new List<ExpressionBlockConfig>();
        var editor = new ExpressionConfigEditorViewModel(
            new ExpressionBlockConfig("""{"n":{{ messages | size }}}""", new Dictionary<string, string>()),
            applied.Add);

        await editor.RenderPreviewCommand.ExecuteAsync(null);

        Assert.That(editor.PreviewOutput, Is.EqualTo("""{"n":1}"""));
        Assert.That(editor.PreviewError, Is.Null);

        editor.SampleJson = "{ broken";
        await editor.RenderPreviewCommand.ExecuteAsync(null);
        Assert.That(editor.PreviewError, Is.Not.Null);

        editor.FieldsJson = "{ broken";
        Assert.That(editor.FieldsError, Is.Not.Null);
    }

    [Test]
    public void ExpressionEditor_InvalidTemplate_ShowsSyntaxError()
    {
        var editor = new ExpressionConfigEditorViewModel(
            new ExpressionBlockConfig("{% if %}", new Dictionary<string, string>()),
            _ => { });

        Assert.That(editor.TemplateError, Is.Not.Null);

        editor.ValidateTemplateCommand.Execute(null);

        Assert.That(editor.PreviewError, Is.Not.Null);
    }

    private static RecipeBlock CreateBlock(ExpressionBlockConfig config) => new()
    {
        Id = "expr",
        Type = BlockType.Expression,
        Name = "expr",
        DisplayName = "expr",
        Config = config
    };

    private static BlockExecutionContext CreateContext(
        RecipeBlock block,
        IEnumerable<DatasetRow> input) => new()
    {
        Block = block,
        Input = ToAsync(input),
        Log = _ => { }
    };

    private static async IAsyncEnumerable<DatasetRow> ToAsync(IEnumerable<DatasetRow> rows)
    {
        await Task.Yield();
        foreach (var row in rows)
        {
            yield return row;
        }
    }

    private static async Task<List<DatasetRow>> CollectAsync(IAsyncEnumerable<DatasetRow> rows)
    {
        var result = new List<DatasetRow>();
        await foreach (var row in rows)
        {
            result.Add(row);
        }

        return result;
    }
}