using AgenStart.Platform.Windows.Uninstall;
using AgenStart.SoftwareInventory;
using AgenStart.Uninstall;

namespace AgenStart.Platform.Windows.SoftwareInventory;

public sealed class RegistryInstalledSoftwareCollector : IInstalledSoftwareCollector
{
    private readonly IUninstallRegistrySource _registry;

    public RegistryInstalledSoftwareCollector()
        : this(new UninstallRegistryReader())
    {
    }

    public RegistryInstalledSoftwareCollector(IUninstallRegistrySource registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public Task<InstalledSoftwareCollectionResult> CollectAsync(
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new InstalledSoftwareCollectionResult(
                [],
                [new InventorySourceStatus(
                    SoftwareInventorySourceIds.WindowsRegistry,
                    InventorySourceState.Unavailable,
                    "registry.unsupported-platform",
                    "Windows uninstall registry inventory is unavailable on this platform.")]));
        }

        return Task.FromResult(Collect(_registry.ReadAll(cancellationToken)));
    }

    /// <summary>
    /// Maps uninstall entries to installed-software records. Only hidden system components and
    /// unnamed keys are left out; secondary values the reader could not read are ignored here.
    /// </summary>
    public static InstalledSoftwareCollectionResult Collect(IReadOnlyList<UninstallRegistryProbeResult> probes)
    {
        ArgumentNullException.ThrowIfNull(probes);

        var records = new HashSet<InstalledSoftwareRecord>();
        var hadErrors = false;

        foreach (var probe in probes)
        {
            hadErrors |= probe.HadErrors;

            foreach (var entry in probe.Entries)
            {
                if (entry.SystemComponent || string.IsNullOrWhiteSpace(entry.DisplayName))
                {
                    continue;
                }

                records.Add(new InstalledSoftwareRecord(
                    InstalledSoftwareSourceKind.Registry,
                    SoftwareInventorySourceIds.WindowsRegistry,
                    entry.DisplayName.Trim(),
                    entry.Publisher,
                    entry.DisplayVersion,
                    ToInventoryScope(entry.Location.Scope)));
            }
        }

        var status = hadErrors
            ? new InventorySourceStatus(
                SoftwareInventorySourceIds.WindowsRegistry,
                InventorySourceState.Partial,
                "registry.partial-read",
                "Some installed-software registry entries could not be read as the current user.")
            : new InventorySourceStatus(
                SoftwareInventorySourceIds.WindowsRegistry,
                InventorySourceState.Complete);

        return new InstalledSoftwareCollectionResult(
            records
                .OrderBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.Version, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            [status]);
    }

    private static InstalledSoftwareScope ToInventoryScope(ProgramScope scope) => scope switch
    {
        ProgramScope.Machine => InstalledSoftwareScope.Machine,
        ProgramScope.User => InstalledSoftwareScope.User,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown uninstall scope.")
    };
}
