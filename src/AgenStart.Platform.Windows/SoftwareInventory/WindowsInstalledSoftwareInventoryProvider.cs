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

    public Task<InstalledSoftwareSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default) =>
        _inner.CaptureAsync(cancellationToken);
}
