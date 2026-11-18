using AgenStart.Core.Catalogue;

namespace AgenStart.Desktop.ViewModels.Onboarding;

/// <summary>Runs the onboarding's scan and build through the shell, so the shell ends up with the same data.</summary>
public sealed class ShellOnboardingActions(MainWindowViewModel shell) : IOnboardingActions
{
    public async Task<bool> ScanAsync(CancellationToken cancellationToken)
    {
        await shell.Overview.AnalyzeAsync(cancellationToken).ConfigureAwait(true);
        return shell.Overview.HasSnapshot;
    }

    public async Task<bool> BuildRecommendationsAsync(UserProfile profiles, CancellationToken cancellationToken)
    {
        shell.SelectProfile(profiles);
        await shell.BuildRecommendationsAsync(cancellationToken).ConfigureAwait(true);
        return shell.RecommendationsStep.HasRecommendations;
    }
}
