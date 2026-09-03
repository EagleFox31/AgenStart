using AgenStart.Application.Installation;
using AgenStart.Core.Catalogue;
using AgenStart.Core.Machine;
using AgenStart.PackageManagement;
using AgenStart.Recommendations;

namespace AgenStart.Application.GuidedSetup;

public enum GuidedSetupStep
{
    Welcome = 0,
    MachineSummary,
    ProfileSelection,
    Recommendations,
    Review,
    Confirmation,
    Installation,
    Report
}

public sealed record GuidedApplicationCandidate(
    ApplicationDefinition Definition,
    ProviderPackageReference Package);

public sealed record GuidedRecommendationItem(
    RecommendationDecision Decision,
    ProviderPackageReference Package,
    bool IsSelected,
    bool CanSelect)
{
    public string ApplicationId => Decision.ApplicationId;
}

public sealed record GuidedSetupSnapshot(
    GuidedSetupStep Step,
    MachineSnapshot Machine,
    UserProfile? Profile,
    IReadOnlyList<GuidedRecommendationItem> Recommendations,
    InstallationReport? InstallationReport,
    bool InstallationConfirmed);
