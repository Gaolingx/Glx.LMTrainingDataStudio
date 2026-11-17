using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
[NonParallelizable]
public sealed class StartupLaunchTests
{
    private static string NewInstanceName() => "LMTDS.Test." + Guid.NewGuid().ToString("N");

    [Test]
    public void FilterRecipePaths_SelectsSupportedArgumentsInOrder()
    {
        var paths = RecipeFileExtensions.FilterRecipePaths(
        [
            "--open",
            @"C:\data\first.json",
            @"C:\data\notes.txt",
            @"C:\data\second.recipe",
            @"C:\data\THIRD.RECIPE",
            ""
        ]);

        Assert.That(paths, Is.EqualTo(new[]
        {
            @"C:\data\first.json",
            @"C:\data\second.recipe",
            @"C:\data\THIRD.RECIPE"
        }));
    }

    [Test]
    public void FilterRecipePaths_ReturnsEmptyWhenNoRecipeArgument()
    {
        Assert.That(RecipeFileExtensions.FilterRecipePaths(["--help", "notes.txt"]), Is.Empty);
        Assert.That(RecipeFileExtensions.FilterRecipePaths([]), Is.Empty);
    }

    [Test]
    public void StartupRecipe_IsConsumedOnce()
    {
        App.QueueStartupRecipe(@"C:\data\startup.recipe");

        Assert.That(App.TakeStartupRecipe(), Is.EqualTo(@"C:\data\startup.recipe"));
        Assert.That(App.TakeStartupRecipe(), Is.Null);
    }

    [Test]
    public void SecondInstanceOnAnotherThread_IsNotPrimary()
    {
        var name = NewInstanceName();
        using var primary = new SingleInstanceService(name);
        Assert.That(primary.IsFirstInstance, Is.True);

        bool? secondaryIsFirst = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var secondary = new SingleInstanceService(name);
                secondaryIsFirst = secondary.IsFirstInstance;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.Start();
        Assert.That(thread.Join(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(failure, Is.Null);
        Assert.That(secondaryIsFirst, Is.False);
    }

    [Test]
    public void ForwardToPrimary_DeliversRecipePathToServer()
    {
        var name = NewInstanceName();
        using var primary = new SingleInstanceService(name);
        Assert.That(primary.IsFirstInstance, Is.True);

        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.StartServer(path => received.TrySetResult(path));

        var sent = SingleInstanceService.TrySendToPrimary(
            @"C:\data\forwarded.recipe",
            primary.PipeName,
            TimeSpan.FromSeconds(10));

        Assert.That(sent, Is.True);
        Assert.That(received.Task.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(received.Task.Result, Is.EqualTo(@"C:\data\forwarded.recipe"));
    }

    [Test]
    public void ForwardToPrimary_ReturnsFalseWhenServerIsMissing()
    {
        var pipeName = NewInstanceName() + ".missing";

        var sent = SingleInstanceService.TrySendToPrimary(
            @"C:\data\orphan.recipe",
            pipeName,
            TimeSpan.FromMilliseconds(200));

        Assert.That(sent, Is.False);
    }
}
