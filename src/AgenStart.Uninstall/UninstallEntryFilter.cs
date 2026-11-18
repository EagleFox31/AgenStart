namespace AgenStart.Uninstall;

/// <summary>
/// Which uninstall entries are programs a person would recognise, close to what Windows lists in
/// Settings &gt; Apps. Hidden system components follow the existing installed-software collector
/// (<c>SystemComponent</c> = 1, read the same way); updates and child entries are also left out.
/// </summary>
public static class UninstallEntryFilter
{
    private static readonly HashSet<string> UpdateReleaseTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Security Update",
        "Update",
        "Update Rollup",
        "Hotfix",
        "Service Pack"
    };

    public static bool IsListed(UninstallRegistryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.SystemComponent || string.IsNullOrWhiteSpace(entry.DisplayName))
        {
            return false;
        }

        if (entry.ParentKeyName is not null)
        {
            return false;
        }

        if (entry.ReleaseType is { } releaseType && UpdateReleaseTypes.Contains(releaseType))
        {
            return false;
        }

        // Windows lists an entry only when it can be removed: an uninstall command or an MSI registration.
        return entry.UninstallString is not null || entry.WindowsInstaller;
    }
}
