using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
[NonParallelizable]
public sealed class AppLogTests
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
    public void Write_AppendsTimestampedLevelAndException()
    {
        AppLog.Initialize(_directory, maxFileBytes: 64 * 1024, maxFiles: 3);

        AppLog.Info("hello");
        AppLog.Error("boom", new InvalidOperationException("detail"));

        var text = File.ReadAllText(AppLog.FilePath);
        Assert.That(text, Does.Contain("[INFO] hello"));
        Assert.That(text, Does.Contain("[ERROR] boom"));
        Assert.That(text, Does.Contain("InvalidOperationException"));
        Assert.That(text, Does.Contain("detail"));
    }

    [Test]
    public void Write_RotatesFilesAtSizeLimit()
    {
        AppLog.Initialize(_directory, maxFileBytes: 1024, maxFiles: 3);
        var payload = new string('x', 700);

        for (var i = 0; i < 8; i++)
        {
            AppLog.Info($"{i}-{payload}");
        }

        Assert.That(File.Exists(Path.Combine(_directory, "app.log")), Is.True);
        Assert.That(File.Exists(Path.Combine(_directory, "app.1.log")), Is.True);
        Assert.That(File.Exists(Path.Combine(_directory, "app.3.log")), Is.False);
    }
}