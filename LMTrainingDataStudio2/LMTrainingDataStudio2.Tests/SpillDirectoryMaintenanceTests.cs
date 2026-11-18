using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class SpillDirectoryMaintenanceTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"), "spill");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(Path.GetDirectoryName(_root)!, true); } catch { /* best effort */ }
    }

    [Test]
    public void Cleanup_RemovesRunDirectoriesAndKeepsRoot()
    {
        var single = Path.Combine(_root, "run-1-abc");
        var nested = Path.Combine(_root, "run-2-def", "buffers");
        Directory.CreateDirectory(single);
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(single, "rows.tmp"), "stale");
        File.WriteAllText(Path.Combine(nested, "rows.tmp"), "stale");

        var removed = SpillDirectoryMaintenance.Cleanup(_root);

        Assert.That(removed, Is.EqualTo(2));
        Assert.That(Directory.Exists(_root), Is.True);
        Assert.That(Directory.GetDirectories(_root), Is.Empty);
    }

    [Test]
    public void Cleanup_MissingRoot_ReturnsZero()
    {
        var removed = SpillDirectoryMaintenance.Cleanup(Path.Combine(_root, "missing"));

        Assert.That(removed, Is.EqualTo(0));
    }

    [Test]
    public void Cleanup_EmptyRoot_ReturnsZero()
    {
        var removed = SpillDirectoryMaintenance.Cleanup(_root);

        Assert.That(removed, Is.EqualTo(0));
        Assert.That(Directory.Exists(_root), Is.True);
    }
}
