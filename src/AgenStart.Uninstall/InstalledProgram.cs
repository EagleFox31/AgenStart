namespace AgenStart.Uninstall;

public enum ProgramScope
{
    User = 1,
    Machine = 2
}

public enum ProgramSource
{
    Registry = 1,
    WinGet = 2,
    Msix = 3
}

public enum InstallerKind
{
    Unknown = 0,
    Msi = 1,
    Inno = 2,
    Nsis = 3,
    Other = 4
}

/// <summary>
/// A program registered on the machine, as read from its uninstall entry. Read-only data: nothing
/// in this model can change the machine.
/// </summary>
public sealed record InstalledProgram
{
    /// <summary>Stable identity: a hash of the source and the registry key path (see <see cref="InstalledProgramId"/>).</summary>
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public string? Publisher { get; init; }

    public string? Version { get; init; }

    public required ProgramScope Scope { get; init; }

    public required ProgramSource Source { get; init; }

    public string? RegistryKeyPath { get; init; }

    public string? InstallLocation { get; init; }

    public string? UninstallString { get; init; }

    public string? QuietUninstallString { get; init; }

    public string? MsiProductCode { get; init; }

    public InstallerKind InstallerKind { get; init; }

    public long? EstimatedSizeBytes { get; init; }

    public DateOnly? InstallDate { get; init; }

    /// <summary>The winget package this program was matched to; null when no correlation was made.</summary>
    public string? WinGetPackageId { get; init; }
}
