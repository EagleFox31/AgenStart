using AgenStart.Uninstall;

namespace AgenStart.Platform.Windows.Uninstall;

/// <summary>
/// Installed programs from the Windows uninstall registry, read off the calling thread.
/// Read-only: no registry key is opened for writing and nothing is executed.
/// winget package ids are not correlated in this phase (see the uninstall design, P1).
/// </summary>
public sealed class WindowsInstalledProgramInventory : IInstalledProgramInventory
{
    public const string UnsupportedPlatformCode = "registry.unsupported-platform";

    private readonly RegistryInstalledProgramInventory _inventory;

    public WindowsInstalledProgramInventory()
        : this(new UninstallRegistryReader())
    {
    }

    public WindowsInstalledProgramInventory(IUninstallRegistrySource registry)
    {
        _inventory = new RegistryInstalledProgramInventory(registry);
    }

    public Task<InstalledProgramInventoryResult> CaptureAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new InstalledProgramInventoryResult(
                [],
                InstalledProgramInventoryStatus.Unavailable,
                [new InventoryDiagnostic(
                    UnsupportedPlatformCode,
                    "The Windows uninstall registry is unavailable on this platform.")]));
        }

        return Task.Run(() => _inventory.CaptureAsync(cancellationToken), cancellationToken);
    }
}
