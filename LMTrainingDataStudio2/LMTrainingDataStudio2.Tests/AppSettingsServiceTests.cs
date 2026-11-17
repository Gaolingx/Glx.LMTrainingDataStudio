using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class AppSettingsServiceTests
{
    [Test]
    public void Update_RaisesChangedWithPreviousAndCurrent()
    {
        var initial = new AppSettings { MaxParallelism = 2 };
        var service = new AppSettingsService(initial);
        SettingsChangedEventArgs? captured = null;
        service.Changed += (_, args) => captured = args;

        var updated = new AppSettings { MaxParallelism = 8 };
        service.Update(updated);

        Assert.That(service.Current, Is.SameAs(updated));
        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Previous, Is.SameAs(initial));
        Assert.That(captured.Current, Is.SameAs(updated));
    }

    [Test]
    public void Update_SameInstance_DoesNotRaiseChanged()
    {
        var settings = new AppSettings();
        var service = new AppSettingsService(settings);
        var raised = 0;
        service.Changed += (_, _) => raised++;

        service.Update(settings);

        Assert.That(raised, Is.Zero);
    }
}
