using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Principal;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
[NonParallelizable]
public sealed class DiagnosticSecurityTests
{
    private const string Secret = "sk-live-abcdef0123456789";

    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-diagnostics", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [Test]
    public void Redact_RemovesAuthorizationHeaderAndBearerToken()
    {
        var text = $"Authorization: Bearer {Secret}\r\nused bearer abcdefgh12345 standalone";

        var result = DiagnosticRedactor.Redact(text);

        Assert.That(result, Does.Not.Contain(Secret));
        Assert.That(result, Does.Not.Contain("abcdefgh12345"));
        Assert.That(result, Does.Contain("Authorization: [REDACTED]"));
        Assert.That(result, Does.Contain("bearer [REDACTED]"));
    }

    [Test]
    public void Redact_RemovesJsonSecretFieldsButKeepsOtherFields()
    {
        var text =
            $"{{\"model\":\"gpt-4o-mini\",\"apiKey\":\"{Secret}\",\"token\":\"plain-token-value\"," +
            "\"messages\":[{\"role\":\"user\",\"content\":\"hello\"}]}";

        var result = DiagnosticRedactor.Redact(text);

        Assert.That(result, Does.Not.Contain(Secret));
        Assert.That(result, Does.Not.Contain("plain-token-value"));
        Assert.That(result, Does.Contain("\"model\":\"gpt-4o-mini\""));
        Assert.That(result, Does.Contain("\"content\":\"hello\""));
        Assert.That(result, Does.Contain("\"apiKey\":\"[REDACTED]\""));
    }

    [Test]
    public void Redact_RemovesKnownSecretValuesInAnyShape()
    {
        const string secret = "custom-secret-value-12345";
        var text = $"credential={secret}; retry";

        var result = DiagnosticRedactor.Redact(text, new[] { secret });

        Assert.That(result, Does.Not.Contain(secret));
        Assert.That(result, Does.Contain("credential=[REDACTED]; retry"));
    }

    [Test]
    public void Redact_LeavesOrdinaryDiagnosticsAlone()
    {
        const string text = "Processed 120 tokens across 3 messages; tokenization finished.";

        Assert.That(DiagnosticRedactor.Redact(text), Is.EqualTo(text));
    }

    [Test]
    public void CrashReport_RedactsSecretsFromLogsAndSummary()
    {
        AppLog.Initialize(Path.Combine(_directory, "logs"));
        AppLog.Info($"Calling provider with Authorization: Bearer {Secret}");
        AppLog.Info($"{{\"apiKey\":\"{Secret}\"}}");

        var path = Path.Combine(_directory, "diagnostics.zip");
        CrashReportService.CreateReport(
            path, $"reason {Secret}", new InvalidOperationException($"failed with {Secret}"));

        using var archive = ZipFile.OpenRead(path);
        var logText = ReadEntry(archive, "logs/app.log");
        var summary = ReadEntry(archive, "summary.txt");

        Assert.That(logText, Does.Not.Contain(Secret));
        Assert.That(logText, Does.Contain("Calling provider"));
        Assert.That(summary, Does.Not.Contain(Secret));
        Assert.That(summary, Does.Contain("LLM Training Data Studio diagnostics"));
        Assert.That(archive.GetEntry("settings.redacted.json"), Is.Not.Null);

        var settingsEntry = ReadEntry(archive, "settings.redacted.json");
        Assert.That(settingsEntry, Does.Not.Contain(Secret));
    }

    [Test]
    public void DirectorySecurity_RejectsEmptyPath()
    {
        Assert.That(AppDirectorySecurity.TryRestrictToCurrentUser(null), Is.False);
        Assert.That(AppDirectorySecurity.TryRestrictToCurrentUser("  "), Is.False);
    }

    [Test]
    public void DirectorySecurity_RestrictsDirectoryOnWindows()
    {
        var target = Path.Combine(_directory, "protected");
        Directory.CreateDirectory(target);

        var applied = AppDirectorySecurity.TryRestrictToCurrentUser(target);
        if (!OperatingSystem.IsWindows())
        {
            Assert.That(applied, Is.False);
            return;
        }

        Assert.That(applied, Is.True);
        var security = new DirectoryInfo(target).GetAccessControl();
        Assert.That(security.AreAccessRulesProtected, Is.True);

        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        var currentUser = WindowsIdentity.GetCurrent().User;
        Assert.That(currentUser, Is.Not.Null);
        Assert.That(
            rules.Any(rule =>
                rule.AccessControlType == AccessControlType.Allow &&
                rule.IdentityReference == currentUser &&
                (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl),
            Is.True);

        var world = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        Assert.That(
            rules.Any(rule =>
                rule.AccessControlType == AccessControlType.Allow &&
                rule.IdentityReference == world),
            Is.False);

        File.WriteAllText(Path.Combine(target, "probe.txt"), "ok");
    }

    [Test]
    public async Task SettingsStore_SaveRestrictsSettingsDirectory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var settingsPath = Path.Combine(_directory, "settings", "settings.json");
        var store = new SettingsStore(settingsPath);
        await store.SaveAsync(new AppSettings());

        var security = new DirectoryInfo(Path.GetDirectoryName(settingsPath)!).GetAccessControl();
        Assert.That(security.AreAccessRulesProtected, Is.True);
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new AssertionException($"Missing entry '{name}'.");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
