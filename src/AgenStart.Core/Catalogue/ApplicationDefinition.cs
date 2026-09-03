using AgenStart.Core.Machine;

namespace AgenStart.Core.Catalogue;

public enum UserProfile
{
    Personal,
    Development,
    Business,
    Creation,
    Training
}

public enum RecommendationLevel
{
    Essential,
    Recommended,
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
