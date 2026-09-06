using AgenStart.Core.Catalogue;

namespace AgenStart.Desktop.Views;

public sealed partial class MainWindow
{
    private UsageProfilesView? _usageProfilesView;

    protected override void OnOpened(EventArgs e)
    {
        InstallMultiProfileExperience();
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
        view.SelectionChanged += (_, profiles) =>
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
            _viewModel.SelectProfile(view.SelectedProfiles);
            await _viewModel.BuildRecommendationsAsync(_lifetimeCancellation.Token);
            if (_viewModel.HasRecommendations)
            {
                RecommendationsButton.IsEnabled = true;
                ShowRecommendations();
            }
        };

        view.BackRequested += (_, _) => ShowYourPc();

        UsageProfilePanel.Children.Clear();
        UsageProfilePanel.Children.Add(view);
        _usageProfilesView = view;
    }
}
