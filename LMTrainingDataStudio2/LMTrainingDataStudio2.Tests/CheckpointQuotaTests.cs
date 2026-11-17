using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class CheckpointQuotaTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    [Test]
    public void Enforce_UnderQuota_KeepsAllCheckpoints()
    {
        var old = CreateCheckpoint("run-old", bytes: 100, DateTime.UtcNow.AddHours(-2));
        var recent = CreateCheckpoint("run-new", bytes: 100, DateTime.UtcNow);

        var result = CheckpointQuota.Enforce(_root, quotaBytes: 1024);

        Assert.That(result.DeletedCheckpoints, Is.EqualTo(0));
        Assert.That(Directory.Exists(old), Is.True);
        Assert.That(Directory.Exists(recent), Is.True);
    }

    [Test]
    public void Enforce_OverQuota_DeletesOldestFirstAndKeepsRunSummary()
    {
        var old = CreateCheckpoint("run-old", bytes: 100, DateTime.UtcNow.AddHours(-3));
        var mid = CreateCheckpoint("run-mid", bytes: 100, DateTime.UtcNow.AddHours(-2));
        var recent = CreateCheckpoint("run-new", bytes: 100, DateTime.UtcNow);
        var summary = Path.Combine(_root, "run-old", "run-summary.json");
        File.WriteAllText(summary, "{}");

        var result = CheckpointQuota.Enforce(_root, quotaBytes: 250);

        Assert.That(result.DeletedCheckpoints, Is.EqualTo(1));
        Assert.That(result.FreedBytes, Is.EqualTo(100));
        Assert.That(Directory.Exists(old), Is.False);
        Assert.That(Directory.Exists(mid), Is.True);
        Assert.That(Directory.Exists(recent), Is.True);
        Assert.That(File.Exists(summary), Is.True);
    }

    [Test]
    public void Enforce_ProtectsCurrentRunEvenWhenOldest()
    {
        var current = CreateCheckpoint("run-current", bytes: 100, DateTime.UtcNow.AddHours(-5));
        var older = CreateCheckpoint("run-older", bytes: 100, DateTime.UtcNow.AddHours(-2));
        var recent = CreateCheckpoint("run-recent", bytes: 100, DateTime.UtcNow);

        var result = CheckpointQuota.Enforce(
            _root,
            quotaBytes: 100,
            protectedRunDirectory: Path.Combine(_root, "run-current"));

        Assert.That(result.DeletedCheckpoints, Is.EqualTo(1));
        Assert.That(Directory.Exists(current), Is.True);
        Assert.That(Directory.Exists(older), Is.False);
        Assert.That(Directory.Exists(recent), Is.True);
    }

    [Test]
    public void Enforce_ZeroQuotaOrMissingDirectory_IsNoOp()
    {
        var checkpoint = CreateCheckpoint("run-old", bytes: 100, DateTime.UtcNow);

        Assert.That(CheckpointQuota.Enforce(_root, quotaBytes: 0).DeletedCheckpoints, Is.EqualTo(0));
        Assert.That(
            CheckpointQuota.Enforce(Path.Combine(_root, "missing"), quotaBytes: 1).DeletedCheckpoints,
            Is.EqualTo(0));
        Assert.That(Directory.Exists(checkpoint), Is.True);
    }

    private string CreateCheckpoint(string runId, int bytes, DateTime lastWriteUtc)
    {
        var directory = Path.Combine(_root, runId, "nodes");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "block.rows"), new byte[bytes]);
        Directory.SetLastWriteTimeUtc(directory, lastWriteUtc);
        return directory;
    }
}
