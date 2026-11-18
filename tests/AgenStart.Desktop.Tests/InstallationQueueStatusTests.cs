using AgenStart.Application.Installation;
using AgenStart.Desktop.Services;
using AgenStart.Desktop.ViewModels;

namespace AgenStart.Desktop.Tests;

public sealed class InstallationQueueStatusTests
{
    private static readonly ILocalizer French = new DictionaryLocalizer(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Installation.Current.Downloading"] = "Téléchargement de {0}",
        ["Installation.Current.Installing"] = "Installation de {0}",
        ["Installation.Current.InstallingSilent"] = "Installation de {0} (installeur silencieux)",
        ["Installation.Current.Resolving"] = "Recherche de {0}",
        ["Installation.Current.Verifying"] = "Vérification de {0}",
        ["Units.DurationMinutes"] = "{0} min {1:00} s",
        ["Units.DurationSeconds"] = "{0} s"
    });

    [Fact]
    public void Headline_NamesTheSilentInstallerOverAnyDownload()
    {
        InstallationQueueEntry[] entries =
        [
            Entry("PowerShell 7", InstallationQueueItemState.Running, InstallationItemActivity.Installing, silent: true),
            Entry("Python", InstallationQueueItemState.Queued, InstallationItemActivity.Downloading)
        ];

        Assert.Equal(
            "Installation de PowerShell 7 (installeur silencieux)",
            InstallationQueueStatus.Headline(entries)?.Resolve(French));
    }

    [Fact]
    public void Headline_NamesTheInteractiveInstallerWithoutTheSilentMention()
    {
        InstallationQueueEntry[] entries =
        [
            Entry("Git", InstallationQueueItemState.Running, InstallationItemActivity.Installing)
        ];

        Assert.Equal("Installation de Git", InstallationQueueStatus.Headline(entries)?.Resolve(French));
    }

    [Fact]
    public void Headline_NamesTheFirstDownloadInQueueOrder()
    {
        InstallationQueueEntry[] entries =
        [
            Entry("PowerShell 7", InstallationQueueItemState.Running, InstallationItemActivity.Downloading),
            Entry("Python", InstallationQueueItemState.Queued, InstallationItemActivity.Downloading),
            Entry("Git", InstallationQueueItemState.Queued, InstallationItemActivity.Resolving)
        ];

        Assert.Equal("Téléchargement de PowerShell 7", InstallationQueueStatus.Headline(entries)?.Resolve(French));
    }

    [Fact]
    public void Headline_IsNullWhenNothingIsActive()
    {
        InstallationQueueEntry[] entries =
        [
            Entry("Git", InstallationQueueItemState.Succeeded, InstallationItemActivity.Completed),
            Entry("Python", InstallationQueueItemState.Queued, InstallationItemActivity.Waiting)
        ];

        Assert.Null(InstallationQueueStatus.Headline(entries));
    }

    [Fact]
    public void WaitingFor_NamesTheFirstEarlierPackageThatIsNotFinished()
    {
        InstallationQueueEntry[] entries =
        [
            Entry("Git", InstallationQueueItemState.Succeeded, InstallationItemActivity.Completed),
            Entry("PowerShell 7", InstallationQueueItemState.Running, InstallationItemActivity.Downloading),
            Entry("Node.js", InstallationQueueItemState.Queued, InstallationItemActivity.Downloading),
            Entry("Python", InstallationQueueItemState.Queued, InstallationItemActivity.Ready)
        ];

        Assert.Equal("PowerShell 7", InstallationQueueStatus.WaitingFor(entries, 3));
        Assert.Null(InstallationQueueStatus.WaitingFor(entries, 1));
        Assert.Null(InstallationQueueStatus.WaitingFor(entries, 2));
    }

    [Fact]
    public void WaitingFor_IsNullWhenEverythingEarlierIsDone()
    {
        InstallationQueueEntry[] entries =
        [
            Entry("Git", InstallationQueueItemState.Failed, InstallationItemActivity.Failed),
            Entry("Python", InstallationQueueItemState.Queued, InstallationItemActivity.Ready)
        ];

        Assert.Null(InstallationQueueStatus.WaitingFor(entries, 1));
    }

    [Theory]
    [InlineData(null, 100L, null)]
    [InlineData(10L, null, null)]
    [InlineData(10L, 0L, null)]
    [InlineData(25L, 100L, 25d)]
    [InlineData(150L, 100L, 100d)]
    public void DownloadPercent_IsKnownOnlyWithAPositiveTotal(long? downloaded, long? required, double? expected)
    {
        Assert.Equal(expected, InstallationQueueStatus.DownloadPercent(downloaded, required));
    }

    [Theory]
    [InlineData(0, "0 s")]
    [InlineData(59.9, "59 s")]
    [InlineData(60, "1 min 00 s")]
    [InlineData(125, "2 min 05 s")]
    [InlineData(-3, "0 s")]
    public void FormatElapsed_ReadsSecondsThenMinutes(double seconds, string expected)
    {
        Assert.Equal(expected, InstallationQueueStatus.FormatElapsed(TimeSpan.FromSeconds(seconds)).Resolve(French));
    }

    [Theory]
    [InlineData(InstallationQueueItemState.Running, InstallationItemActivity.Downloading, true)]
    [InlineData(InstallationQueueItemState.Queued, InstallationItemActivity.Ready, true)]
    [InlineData(InstallationQueueItemState.Queued, InstallationItemActivity.Waiting, false)]
    [InlineData(InstallationQueueItemState.Succeeded, InstallationItemActivity.Completed, false)]
    [InlineData(InstallationQueueItemState.Failed, InstallationItemActivity.Installing, false)]
    public void IsActive_CoversOnlyPendingMovingPhases(
        InstallationQueueItemState state,
        InstallationItemActivity activity,
        bool expected)
    {
        Assert.Equal(expected, InstallationQueueStatus.IsActive(state, activity));
    }

    private static InstallationQueueEntry Entry(
        string name,
        InstallationQueueItemState state,
        InstallationItemActivity activity,
        bool silent = false) =>
        new(name, state, activity, silent);

    private sealed class DictionaryLocalizer(IReadOnlyDictionary<string, string> templates) : LocalizerBase
    {
        public override string Language => "fr";

        public override string this[string key] => templates.TryGetValue(key, out var template) ? template : key;
    }
}
