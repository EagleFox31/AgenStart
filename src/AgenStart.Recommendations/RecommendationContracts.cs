using AgenStart.Core.Catalogue;
using AgenStart.Core.Machine;
using AgenStart.SoftwareInventory;

namespace AgenStart.Recommendations;

public enum RecommendationDisposition
{
    Recommended,
    AlreadyInstalled,
    Incompatible,
    CompatibilityUnknown,
    InventoryUnknown,
    Conflict,
    Unavailable
}

public sealed record RecommendationRequest(
    UserProfile Profile,
    MachineSnapshot Machine,
    SoftwareDetectionResult Software,
    IReadOnlyList<ApplicationDefinition> Applications);

public sealed record RecommendationReason(
    string Code,
    string Message);

public sealed record RecommendationDecision(
    string ApplicationId,
    string ApplicationName,
    UserProfile Profile,
    RecommendationLevel Level,
    string ProfileReasonKey,
    RecommendationDisposition Disposition,
    bool SelectedByDefault,
    IReadOnlyList<RecommendationReason> Reasons,
    IReadOnlyList<UserProfile>? MatchedProfiles = null);

public sealed record RecommendationPlan(
    UserProfile Profile,
    IReadOnlyList<RecommendationDecision> Decisions)
{
    public IReadOnlyList<RecommendationDecision> DefaultSelection =>
        Decisions
            .Where(static decision => decision.SelectedByDefault)
            .ToArray();
}
