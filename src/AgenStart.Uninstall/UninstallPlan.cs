namespace AgenStart.Uninstall;

public enum ConfidenceLevel
{
    Low = 1,
    Medium = 2,
    High = 3
}

/// <summary>One signal that moved the score, kept so the plan can explain itself (ADR-0006).</summary>
public sealed record ConfidenceReason(string Code, int Weight);

public sealed record ConfidenceScore(int Score, ConfidenceLevel Level, IReadOnlyList<ConfidenceReason> Reasons);

/// <summary>
/// One leftover the plan proposes. A plan item is a description: nothing in it can delete, move or
/// change what it points at.
/// </summary>
public sealed record PlanItem(
    string ItemId,
    string RuleId,
    LeftoverKind Kind,
    string Target,
    long? SizeBytes,
    int? FileCount,
    bool SizeIsPartial,
    string Fingerprint,
    ConfidenceScore Confidence,
    DataClass DataClass,
    bool DefaultSelected,
    bool RequiresElevation);

/// <summary>Why a candidate was left out of the plan. Tokens are stable and exported as is.</summary>
public static class SkipReasons
{
    public const string Shared = "shared";
    public const string Forbidden = "forbidden";
    public const string AncestorOfForbidden = "ancestor-of-forbidden";
    public const string TooShallow = "too-shallow";
    public const string Invalid = "invalid-path";
    public const string ReparsePoint = "reparse-point";
}

public sealed record SkippedCandidate(string RuleId, LeftoverKind Kind, string Target, string Reason);

public enum OfficialStrategy
{
    WinGet = 1,
    Msi = 2,
    QuietUninstallString = 3,
    InstallerSilentFlags = 4,
    Interactive = 5,
    Unavailable = 6
}

/// <summary>
/// How the vendor's own uninstall would run. P2 only describes it: <see cref="CommandPreview"/> is
/// text for the user, never handed to a process launcher.
/// </summary>
public sealed record OfficialUninstallStep(
    OfficialStrategy Strategy,
    string? Executable,
    IReadOnlyList<string> Arguments,
    string? CommandPreview,
    bool IsSilent,
    bool RequiresElevation);

public sealed record PlanDiagnostic(string Code, string Message);

public sealed record UninstallPlan(
    string PlanId,
    string ProgramId,
    string ProgramName,
    string StateFingerprint,
    string? RulesVersion,
    OfficialUninstallStep Official,
    IReadOnlyList<PlanItem> Leftovers,
    IReadOnlyList<PlanItem> ProtectedUserData,
    IReadOnlyList<SkippedCandidate> Skipped,
    IReadOnlyList<RunningProcess> Processes,
    VolumeThresholds Thresholds,
    IReadOnlyList<PlanDiagnostic> Diagnostics);

public static class PlanTokens
{
    public static string Strategy(OfficialStrategy strategy) => strategy switch
    {
        OfficialStrategy.WinGet => "winget",
        OfficialStrategy.Msi => "msi",
        OfficialStrategy.QuietUninstallString => "quietUninstallString",
        OfficialStrategy.InstallerSilentFlags => "installerSilentFlags",
        OfficialStrategy.Interactive => "interactive",
        OfficialStrategy.Unavailable => "unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unknown strategy.")
    };

    public static string Level(ConfidenceLevel level) => level switch
    {
        ConfidenceLevel.Low => "low",
        ConfidenceLevel.Medium => "medium",
        ConfidenceLevel.High => "high",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown level.")
    };
}
