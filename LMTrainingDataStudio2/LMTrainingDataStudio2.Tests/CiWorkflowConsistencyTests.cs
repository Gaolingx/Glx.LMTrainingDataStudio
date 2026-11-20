namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class CiWorkflowConsistencyTests
{
    [Test]
    public void CiWorkflow_KeepsCancelBudgetAndRunsDependencyAudit()
    {
        var workflow = TestRepository.ReadText(".github", "workflows", "ci.yml");

        Assert.That(workflow, Does.Contain("LMTDS_CANCEL_BUDGET_MS"));
        Assert.That(workflow, Does.Contain("audit:"));
        Assert.That(workflow, Does.Contain("--vulnerable"));
    }

    [Test]
    public void NightlyWorkflow_RunsGatedPerformanceAndProviderSmoke()
    {
        var workflow = TestRepository.ReadText(".github", "workflows", "nightly.yml");

        Assert.That(workflow, Does.Contain("schedule:"));
        Assert.That(workflow, Does.Contain("workflow_dispatch:"));
        Assert.That(workflow, Does.Contain("LMTDS_PERF_TESTS"));
        Assert.That(workflow, Does.Contain("ProviderSmokeTests"));
    }

    [Test]
    public void ReleaseWorkflow_ValidatesVersionAndPublishesArtifacts()
    {
        var workflow = TestRepository.ReadText(".github", "workflows", "release.yml");

        Assert.That(workflow, Does.Contain("tags:"));
        Assert.That(workflow, Does.Contain("contents: write"));
        Assert.That(workflow, Does.Contain("Directory.Build.props"));
        Assert.That(workflow, Does.Contain("gh release create"));
        Assert.That(workflow, Does.Contain("package-msix.ps1"));
        Assert.That(workflow, Does.Contain("package-inno.ps1"));
    }

    [Test]
    public void Dependabot_TracksNuGetAndActions()
    {
        var config = TestRepository.ReadText(".github", "dependabot.yml");

        Assert.That(config, Does.Contain("package-ecosystem: nuget"));
        Assert.That(config, Does.Contain("package-ecosystem: github-actions"));
    }
}
