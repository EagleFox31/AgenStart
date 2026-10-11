using Avalonia.Threading;
using AgenStart.Core.Catalogue;
using AgenStart.Core.Recommendations;
using AgenStart.Desktop.ViewModels;

namespace AgenStart.Desktop.Views;

public sealed partial class MainWindow
{
    private IDisposable? _multiProfileProgressSubscription;

    protected override void OnOpened(EventArgs e)
    {
        InstallMultiProfileExperience();
        InstallRecommendationListExperience();
        base.OnOpened(e);
    }

    private void InstallMultiProfileExperience()
    {
        UsageProfilesControl.SetSelection(_viewModel.SelectedProfile);

        UsageProfilesControl.SelectionChanged += profiles =>
        {
            if (profiles == UserProfile.None)
            {
                return;
            }

            _viewModel.SelectProfile(profiles);
            RecommendationsButton.IsEnabled = false;
        };

        UsageProfilesControl.BuildRequested += async (_, _) =>
        {
            UsageProfilesControl.BeginRecommendationBuild();
            _viewModel.SelectProfile(UsageProfilesControl.SelectedProfiles);

            await _viewModel.BuildRecommendationsAsync(_lifetimeCancellation.Token);
            if (_viewModel.HasRecommendations)
            {
                UsageProfilesControl.CompleteRecommendationBuild();
                RecommendationsButton.IsEnabled = true;
                _viewModel.ShowStep(AppStep.Recommendations);
                RefreshRecommendationListExperience();
            }
            else
            {
                UsageProfilesControl.FailRecommendationBuild(_viewModel.RecommendationStatus);
            }
        };

        UsageProfilesControl.BackRequested += (_, _) => _viewModel.ShowStep(AppStep.YourPc);

        _multiProfileProgressSubscription = RecommendationPipelineDiagnostics.Subscribe(stage =>
            Dispatcher.UIThread.Post(() =>
            {
                if (_viewModel.IsBusy && UsageProfilesControl.IsVisible)
                {
                    UsageProfilesControl.UpdateRecommendationProgress(stage);
                }
            }));

        Closed += (_, _) =>
        {
            _multiProfileProgressSubscription?.Dispose();
            _multiProfileProgressSubscription = null;
        };
    }
}
