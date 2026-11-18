namespace AgenStart.Uninstall;

/// <summary>What the planner observed about one candidate, before weighting.</summary>
public sealed record ConfidenceSignals(
    int RuleConfidence,
    bool IsDeclaredInstallLocation,
    bool PublisherMatched,
    bool DirectlyUnderKnownFolder,
    bool ExecutablesPresent,
    bool PublisherUsedByAnotherProgram,
    bool InUse,
    bool IsStoreApp);

/// <summary>
/// Weights and thresholds of ADR-0006 (method adapted from BCU, no code reused). Pure: the same
/// signals always give the same score and the same reasons, in the same order.
/// </summary>
public static class ConfidenceScorer
{
    public const int UninstallKeyWeight = 20;
    public const int PublisherMatchWeight = 4;
    public const int DirectlyUnderKnownFolderWeight = -1;
    public const int ExecutablesPresentWeight = -4;
    public const int PublisherSharedWeight = -4;
    public const int InUseWeight = -7;
    public const int StoreAppWeight = -10;

    public const int HighThreshold = 8;
    public const int MediumThreshold = 4;

    public static ConfidenceScore Score(ConfidenceSignals signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        var reasons = new List<ConfidenceReason> { new("rule", signals.RuleConfidence) };
        Add(reasons, signals.IsDeclaredInstallLocation, "uninstall-key", UninstallKeyWeight);
        Add(reasons, signals.PublisherMatched, "publisher-match", PublisherMatchWeight);
        Add(reasons, signals.DirectlyUnderKnownFolder, "directly-under-known-folder", DirectlyUnderKnownFolderWeight);
        Add(reasons, signals.ExecutablesPresent, "executables-present", ExecutablesPresentWeight);
        Add(reasons, signals.PublisherUsedByAnotherProgram, "publisher-shared", PublisherSharedWeight);
        Add(reasons, signals.InUse, "in-use", InUseWeight);
        Add(reasons, signals.IsStoreApp, "store-app", StoreAppWeight);

        var score = reasons.Sum(reason => reason.Weight);
        return new ConfidenceScore(score, LevelOf(score), reasons);
    }

    public static ConfidenceLevel LevelOf(int score) => score switch
    {
        >= HighThreshold => ConfidenceLevel.High,
        >= MediumThreshold => ConfidenceLevel.Medium,
        _ => ConfidenceLevel.Low
    };

    /// <summary>Only high-confidence items are pre-selected, and user data never is.</summary>
    public static bool IsPreselected(ConfidenceScore confidence, DataClass dataClass)
    {
        ArgumentNullException.ThrowIfNull(confidence);
        return confidence.Level == ConfidenceLevel.High && dataClass != DataClass.UserData;
    }

    private static void Add(List<ConfidenceReason> reasons, bool present, string code, int weight)
    {
        if (present)
        {
            reasons.Add(new ConfidenceReason(code, weight));
        }
    }
}
