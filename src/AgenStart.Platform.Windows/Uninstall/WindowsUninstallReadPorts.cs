using AgenStart.Uninstall;

namespace AgenStart.Platform.Windows.Uninstall;

/// <summary>The read-only Windows adapters the uninstall planner needs, and nothing that can write.</summary>
public static class WindowsUninstallReadPorts
{
    public static UninstallReadPorts Create()
    {
        var files = new WindowsFileSystemProbe();
        return new UninstallReadPorts(
            new WindowsKnownFolderResolver(),
            files,
            new WindowsRegistryProbe(),
            new WindowsShortcutResolver(files),
            new WindowsProcessInspector());
    }
}
