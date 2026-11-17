using LMTrainingDataStudio2.Services.Datasets;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class DatasetFieldEditorTests
{
    [Test]
    public void TryReadObject_ReturnsTopLevelFieldsAndKinds()
    {
        var ok = DatasetFieldEditor.TryReadObject(
            """{"id":1,"name":"a","ok":true,"nested":{"x":1},"items":[1],"empty":null}""",
            out var fields,
            out var error);

        Assert.That(ok, Is.True, error);
        Assert.That(fields.Select(field => field.Name),
            Is.EqualTo(new[] { "id", "name", "ok", "nested", "items", "empty" }));
        Assert.That(fields.Select(field => field.Kind),
            Is.EqualTo(new[] { "number", "string", "bool", "object", "array", "null" }));
        Assert.That(fields[0].ValueText, Is.EqualTo("1"));
        Assert.That(fields[1].ValueText, Is.EqualTo("\"a\""));
    }

    [Test]
    public void TryReadObject_NonObject_Fails()
    {
        var ok = DatasetFieldEditor.TryReadObject("[1,2,3]", out _, out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("not an object"));
    }

    [Test]
    public void TryReadObject_InvalidJson_Fails()
    {
        var ok = DatasetFieldEditor.TryReadObject("{broken", out _, out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("Invalid JSON"));
    }

    [Test]
    public void TryBuild_ChangesOneFieldAndPreservesOthers()
    {
        var original = """{"id":1,"name":"a","nested":{"x":1},"extra":"keep"}""";
        DatasetFieldEditor.TryReadObject(original, out var fields, out _);

        var edited = fields
            .Select(field => field.Name == "name"
                ? field with { ValueText = "\"b\"" }
                : field)
            .ToArray();

        var ok = DatasetFieldEditor.TryBuild(edited, out var json, out var error);

        Assert.That(ok, Is.True, error);
        var rebuilt = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        Assert.That(rebuilt["id"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(rebuilt["name"]!.GetValue<string>(), Is.EqualTo("b"));
        Assert.That(rebuilt["nested"]!["x"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(rebuilt["extra"]!.GetValue<string>(), Is.EqualTo("keep"));
    }

    [Test]
    public void TryBuild_RemovedFieldIsOmitted()
    {
        DatasetFieldEditor.TryReadObject("""{"a":1,"b":2}""", out var fields, out _);

        var ok = DatasetFieldEditor.TryBuild(
            fields.Where(field => field.Name != "a").ToArray(), out var json, out var error);

        Assert.That(ok, Is.True, error);
        Assert.That(json, Does.Not.Contain("\"a\""));
        Assert.That(json, Does.Contain("\"b\":2"));
    }

    [Test]
    public void TryBuild_InvalidValue_NamesTheField()
    {
        var ok = DatasetFieldEditor.TryBuild(
            new[] { new DatasetFieldValue("name", "{broken", "string") },
            out _,
            out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("Field 'name'"));
        Assert.That(error, Does.Contain("not valid JSON"));
    }

    [Test]
    public void TryBuild_DuplicateName_Fails()
    {
        var ok = DatasetFieldEditor.TryBuild(
            new[]
            {
                new DatasetFieldValue("a", "1", "number"),
                new DatasetFieldValue("a", "2", "number")
            },
            out _,
            out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("more than once"));
    }

    [Test]
    public void TryBuild_EmptyName_Fails()
    {
        var ok = DatasetFieldEditor.TryBuild(
            new[] { new DatasetFieldValue("  ", "1", "number") }, out _, out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("must not be empty"));
    }
}
