using AgenStart.Core.Machine;

namespace AgenStart.Core.Catalogue;

[Flags]
public enum UserProfile
{
    None = 0,
    Personal = 1 << 0,
    Development = 1 << 1,
    Business = 1 << 2,
    Creative = 1 << 3,
    Learning = 1 << 4,
    Gaming = 1 << 5,

    // Backward-compatible aliases for existing setup profiles/catalogue data.
    [Obsolete("Use Creative.")]
    Creation = Creative,
    [Obsolete("Use Learning.")]
    Training = Learning
}

public enum RecommendationLevel
{
    Essential,
    Recommended,
    Gem,
    Optional
}

public enum ApplicationLifecycleStatus
{
    Active,
    Deprecated,
    Blocked
}

public enum PlatformSupportStatus
{
    Supported,
    Planned,
    Unsupported
}

public sealed record ApplicationDefinition(
    string Id,
    string Name,
    ApplicationLifecycleStatus Lifecycle,
    IReadOnlyList<ProfileRecommendation> Recommendations,
    ApplicationRequirements Requirements,
    IReadOnlyList<PlatformSupportRule> PlatformSupport,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Conflicts);

public sealed record ProfileRecommendation(
    UserProfile Profile,
    RecommendationLevel Level,
    string ReasonKey);

public sealed record ApplicationRequirements(
    CapabilityRequirements Minimum,
    CapabilityRequirements Recommended);

public sealed record CapabilityRequirements(
    long? MinRamMiB,
    long? MinFreeStorageMiB,
    bool GpuRequired,
    IReadOnlyList<MachineArchitecture> Architectures);

public sealed record PlatformSupportRule(
    PlatformKind Platform,
    PlatformSupportStatus Status,
    IReadOnlyList<MachineArchitecture> Architectures);
