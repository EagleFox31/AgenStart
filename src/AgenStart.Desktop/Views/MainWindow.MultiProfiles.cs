using Avalonia.Threading;
using AgenStart.Core.Catalogue;
using AgenStart.Core.Recommendations;

namespace AgenStart.Desktop.Views;

public sealed partial class MainWindow
{
    private UsageProfilesView? _usageProfilesView;
    private IDisposable? _multiProfileProgressSubscription;

    protected override void OnOpened(EventArgs e)
    {
        InstallMultiProfileExperience();
        InstallRecommendationListExperience();
        base.OnOpened(e);
    }

    private void InstallMultiProfileExperience()
    {
        if (_usageProfilesView is not null)
        {
            return;
        }

        var view = new UsageProfilesView();
        view.SetSelection(_viewModel.SelectedProfile);
        view.SelectionChanged += profiles =>
        {
            if (profiles == UserProfile.None)
            {
                return;
            }

            _viewModel.SelectProfile(profiles);
            RecommendationsButton.IsEnabled = false;
        };

        view.BuildRequested += async (_, _) =>
        {
            view.BeginRecommendationBuild();
            _viewModel.SelectProfile(view.SelectedProfiles);

            await _viewModel.BuildRecommendationsAsync(_lifetimeCancellation.Token);
            if (_viewModel.HasRecommendations)
            {
                view.CompleteRecommendationBuild();
                RecommendationsButton.IsEnabled = true;
                ShowRecommendations();
                RefreshRecommendationListExperience();
            }
            else
            {
                view.FailRecommendationBuild(_viewModel.RecommendationStatus);
            }
        };

        view.BackRequested += (_, _) => ShowYourPc();

        _multiProfileProgressSubscription = RecommendationPipelineDiagnostics.Subscribe(stage =>
            Dispatcher.UIThread.Post(() =>
            {
                if (_viewModel.IsBusy && UsageProfilePanel.IsVisible)
                {
                    view.UpdateRecommendationProgress(stage);
                }
            }));

        Closed += (_, _) =>
        {
            _multiProfileProgressSubscription?.Dispose();
            _multiProfileProgressSubscription = null;
        };

        UsageProfilePanel.Children.Clear();
        UsageProfilePanel.Children.Add(view);
        _usageProfilesView = view;
    }
}
