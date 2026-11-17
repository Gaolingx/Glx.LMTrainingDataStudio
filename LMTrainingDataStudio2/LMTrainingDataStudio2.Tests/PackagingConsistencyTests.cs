using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class PackagingConsistencyTests
{
    private static string ProjectVersion => XDocument
        .Load(TestRepository.PathOf("Directory.Build.props"))
        .Descendants("Version")
        .Single()
        .Value
        .Trim();

    private static XDocument LoadMsixManifest() => XDocument.Load(
        TestRepository.PathOf("packaging", "msix", "AppxManifest.xml"));

    private static string MsixVersion()
    {
        var identity = LoadMsixManifest()
            .Descendants()
            .Single(element => element.Name.LocalName == "Identity");
        return identity.Attribute("Version")!.Value;
    }

    [Test]
    public void ProjectVersion_IsTheSingleSourceForPackages()
    {
        var version = ProjectVersion;

        var msixVersion = MsixVersion();
        var expectedMsix = version.Split('.').Length >= 4 ? version : version + ".0";
        Assert.That(msixVersion, Is.EqualTo(expectedMsix), "MSIX Identity version");

        var inno = TestRepository.ReadText("packaging", "inno", "LMTrainingDataStudio.iss");
        var innoDefault = Regex.Match(inno, @"#define\s+MyAppVersion\s+""([^""]+)""").Groups[1].Value;
        Assert.That(innoDefault, Is.EqualTo(version), "Inno default version");

        var versionScript = TestRepository.ReadText("scripts", "version.ps1");
        Assert.That(versionScript, Does.Contain($"return \"{version}\""), "version.ps1 fallback");

        var csproj = TestRepository.ReadText(
            "LMTrainingDataStudio2", "LMTrainingDataStudio2", "LMTrainingDataStudio2.csproj");
        Assert.That(csproj, Does.Not.Contain("<Version>"), "csproj must not override the shared version");
    }

    [Test]
    public void MsixManifest_ReferencesOnlyGeneratedAssets()
    {
        var referenced = LoadMsixManifest()
            .Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .Where(value => value.StartsWith(@"Assets\", StringComparison.OrdinalIgnoreCase))
            .Select(value => value[(value.LastIndexOf('\\') + 1)..])
            .Distinct()
            .ToArray();

        Assert.That(referenced, Is.Not.Empty);

        var script = TestRepository.ReadText("scripts", "package-msix.ps1");
        foreach (var asset in referenced)
        {
            Assert.That(script, Does.Contain(asset), $"package-msix.ps1 must generate {asset}");
        }
    }

    [Test]
    public void MsixManifest_RegistersRecipeAssociationAndAlias()
    {
        var manifest = LoadMsixManifest();
        var fileTypes = manifest.Descendants()
            .Where(element => element.Name.LocalName == "FileType")
            .Select(element => element.Value)
            .ToArray();

        Assert.That(fileTypes, Does.Contain(".recipe"));

        var alias = manifest.Descendants().Single(element => element.Name.LocalName == "ExecutionAlias");
        Assert.That(alias.Attribute("Alias")!.Value, Is.EqualTo("lmtds.exe"));

        var executable = manifest.Descendants()
            .Single(element => element.Name.LocalName == "Application")
            .Attribute("Executable")!.Value;
        Assert.That(executable, Is.EqualTo("LMTrainingDataStudio2.exe"));
    }

    [Test]
    public void InnoInstaller_AssociatesRecipeAndOffersJsonOpenWith()
    {
        var iss = TestRepository.ReadText("packaging", "inno", "LMTrainingDataStudio.iss");

        Assert.That(iss, Does.Contain(@"Subkey: ""Software\Classes\.recipe"""));
        Assert.That(iss, Does.Contain(@"""%1"""), "shell open command must forward the selected file");
        Assert.That(iss, Does.Contain(@"Subkey: ""Software\Classes\.json\OpenWithProgids"""));
        Assert.That(iss, Does.Contain("AppVersion={#MyAppVersion}"));
        Assert.That(iss, Does.Contain("LMTrainingDataStudio-Setup-{#MyAppVersion}"));
    }

    [Test]
    public void PackageScripts_AcceptAndApplyVersion()
    {
        var publishScript = TestRepository.ReadText("scripts", "publish-win-x64.ps1");
        Assert.That(publishScript, Does.Contain("[string]$Version"));
        Assert.That(publishScript, Does.Contain("-p:Version=$Version"));
        Assert.That(publishScript, Does.Contain("$Output-$Version.zip"));

        var innoScript = TestRepository.ReadText("scripts", "package-inno.ps1");
        Assert.That(innoScript, Does.Contain("[string]$Version"));
        Assert.That(innoScript, Does.Contain("/DMyAppVersion=$Version"));

        var msixScript = TestRepository.ReadText("scripts", "package-msix.ps1");
        Assert.That(msixScript, Does.Contain("[string]$Version"));
        Assert.That(msixScript, Does.Contain("$identity.SetAttribute(\"Version\", $Version)"));
    }

    [Test]
    public void ReleaseBuild_DoesNotShipAvaloniaDiagnostics()
    {
        var csproj = TestRepository.ReadText(
            "LMTrainingDataStudio2", "LMTrainingDataStudio2", "LMTrainingDataStudio2.csproj");
        var diagnostics = Regex.Match(
            csproj,
            @"<PackageReference\s+Include=""Avalonia\.Diagnostics""[^>]*>");

        Assert.That(diagnostics.Success, Is.True, "Avalonia.Diagnostics reference");
        Assert.That(diagnostics.Value, Does.Contain("'$(Configuration)' == 'Debug'"));
    }
}
