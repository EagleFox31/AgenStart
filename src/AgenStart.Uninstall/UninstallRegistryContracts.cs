namespace AgenStart.Uninstall;

public enum UninstallRegistryView
{
    Registry64 = 1,
    Registry32 = 2
}

/// <summary>One uninstall root that is read: a hive (through its scope) seen through one registry view.</summary>
public sealed record UninstallRegistryLocation(ProgramScope Scope, UninstallRegistryView View);

/// <summary>
/// The raw values of one uninstall subkey. Strings are trimmed and null when blank; environment
/// variables are left unexpanded, as stored.
/// </summary>
public sealed record UninstallRegistryEntry
{
    public required UninstallRegistryLocation Location { get; init; }

    /// <summary>The subkey name under the uninstall root (an MSI product code for most MSI packages).</summary>
    public required string KeyName { get; init; }

    /// <summary>Full path of the subkey where it is physically stored, e.g. <c>HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\…\Uninstall\X</c>.</summary>
    public required string KeyPath { get; init; }

    public string? DisplayName { get; init; }

    public string? Publisher { get; init; }

    public string? DisplayVersion { get; init; }

    public string? InstallLocation { get; init; }

    public string? UninstallString { get; init; }

    public string? QuietUninstallString { get; init; }

    public bool SystemComponent { get; init; }

    public bool WindowsInstaller { get; init; }

    public string? ParentKeyName { get; init; }

    public string? ReleaseType { get; init; }

    public string? InnoSetupAppPath { get; init; }

    /// <summary><c>EstimatedSize</c> as stored, in kilobytes.</summary>
    public long? EstimatedSizeKilobytes { get; init; }

    /// <summary><c>InstallDate</c> as stored (normally <c>yyyyMMdd</c>).</summary>
    public string? InstallDate { get; init; }

    public IReadOnlyList<string> ValueNames { get; init; } = [];

    /// <summary>
    /// True when the key was listed but some of its secondary values (install folder, uninstall
    /// commands, size, date, installer markers) could not be read; those values are then null.
    /// </summary>
    public bool HasUnreadableValues { get; init; }
}

/// <summary>The entries read from one location, and whether part of it could not be read.</summary>
public sealed record UninstallRegistryProbeResult(
    UninstallRegistryLocation Location,
    IReadOnlyList<UninstallRegistryEntry> Entries,
    bool RootUnreadable = false,
    int UnreadableKeyCount = 0)
{
    public bool HadErrors => RootUnreadable || UnreadableKeyCount > 0;
}

/// <summary>Read port over the Windows uninstall registry. It has no write method by design.</summary>
public interface IUninstallRegistrySource
{
    /// <summary>Reads every location in a fixed order: HKLM 64-bit, HKLM 32-bit, HKCU 64-bit, HKCU 32-bit.</summary>
    IReadOnlyList<UninstallRegistryProbeResult> ReadAll(CancellationToken cancellationToken = default);
}
