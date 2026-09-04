using AgenStart.Core.Recommendations;
using AgenStart.SoftwareInventory;

namespace AgenStart.Platform.Windows.SoftwareInventory;

public sealed class WindowsInstalledSoftwareInventoryProvider : IInstalledSoftwareInventoryProvider
{
    private readonly CompositeInstalledSoftwareInventoryProvider _inner;

    public WindowsInstalledSoftwareInventoryProvider()
        : this(
        [
            new RegistryInstalledSoftwareCollector(),
            new WinGetInstalledSoftwareCollector()
        ])
    {
    }

    public WindowsInstalledSoftwareInventoryProvider(
        IEnumerable<IInstalledSoftwareCollector> collectors,
        TimeProvider? timeProvider = null)
    {
        _inner = new CompositeInstalledSoftwareInventoryProvider(
            collectors,
            timeProvider);
    }

    public async Task<InstalledSoftwareSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        RecommendationPipelineDiagnostics.Report(RecommendationPipelineStage.ReadingInstalledApplications);
        var snapshot = await _inner.CaptureAsync(cancellationToken).ConfigureAwait(false);
        RecommendationPipelineDiagnostics.Report(RecommendationPipelineStage.ApplyingInstalledStateRules);
        return snapshot;
    }
}
