using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class RunOutputLockTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [Test]
    public void TryAcquire_HoldsTargetUntilDisposed()
    {
        var target = Path.Combine(_directory, "out.jsonl");
        var lockPath = target + RunOutputLock.LockFileSuffix;

        using (var first = RunOutputLock.TryAcquire(new[] { target }, "first", out var conflicts))
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(conflicts, Is.Empty);
            Assert.That(first!.Targets, Is.EqualTo(new[] { target }));
            Assert.That(File.Exists(lockPath), Is.True);

            var second = RunOutputLock.TryAcquire(new[] { target }, "second", out var secondConflicts);

            Assert.That(second, Is.Null);
            Assert.That(secondConflicts, Is.EqualTo(new[] { target }));
        }

        Assert.That(File.Exists(lockPath), Is.False);
        using var third = RunOutputLock.TryAcquire(new[] { target }, "third", out _);
        Assert.That(third, Is.Not.Null);
    }

    [Test]
    public void TryAcquire_DifferentTargetsDoNotConflict()
    {
        var first = Path.Combine(_directory, "first.jsonl");
        var second = Path.Combine(_directory, "second.jsonl");

        using var left = RunOutputLock.TryAcquire(new[] { first }, "left", out _);
        using var right = RunOutputLock.TryAcquire(new[] { second }, "right", out var conflicts);

        Assert.That(left, Is.Not.Null);
        Assert.That(right, Is.Not.Null);
        Assert.That(conflicts, Is.Empty);
    }

    [Test]
    public void TryAcquire_DuplicateAndBlankTargetsAreCollapsed()
    {
        var target = Path.Combine(_directory, "dup.jsonl");

        using var handle = RunOutputLock.TryAcquire(
            new[] { target, target, "  " },
            "owner",
            out var conflicts);

        Assert.That(handle, Is.Not.Null);
        Assert.That(conflicts, Is.Empty);
        Assert.That(handle!.Targets, Is.EqualTo(new[] { target }));
    }

    [Test]
    public void TryAcquire_CreatesMissingTargetDirectory()
    {
        var target = Path.Combine(_directory, "nested", "deep", "out.jsonl");

        using var handle = RunOutputLock.TryAcquire(new[] { target }, "owner", out _);

        Assert.That(handle, Is.Not.Null);
        Assert.That(Directory.Exists(Path.GetDirectoryName(target)), Is.True);
    }
}
