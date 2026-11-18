namespace AgenStart.Uninstall;

public enum InstalledProgramInventoryStatus
{
    Complete = 0,

    /// <summary>Some locations or entries could not be read; the list may be incomplete.</summary>
    Partial = 1,

    /// <summary>The inventory cannot run here (for example on a non-Windows platform).</summary>
    Unavailable = 2
}

public sealed record InventoryDiagnostic(string Code, string Message);

public sealed record InstalledProgramInventoryResult(
    IReadOnlyList<InstalledProgram> Programs,
    InstalledProgramInventoryStatus Status,
    IReadOnlyList<InventoryDiagnostic> Diagnostics);

/// <summary>Read port: lists installed programs. It has no write method by design (P1 and P2 are read-only).</summary>
public interface IInstalledProgramInventory
{
    Task<InstalledProgramInventoryResult> CaptureAsync(CancellationToken cancellationToken = default);
}
