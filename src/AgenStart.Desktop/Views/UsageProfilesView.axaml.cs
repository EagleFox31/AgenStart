using Avalonia.Controls;
using Avalonia.Interactivity;
using AgenStart.Core.Catalogue;
using AgenStart.Core.Profiles;
using AgenStart.Core.Recommendations;
using AgenStart.Desktop.Services;
using AgenStart.Desktop.ViewModels;

namespace AgenStart.Desktop.Views;

public sealed partial class UsageProfilesView : UserControl
{
    private bool _updating;

    public UsageProfilesView()
        : this(ProfileRegistry.Default)
    {
    }

    public UsageProfilesView(ProfileRegistry profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        Options = profiles.Active
            .Select(profile => new ProfileOptionViewModel(profile, profiles.ToUserProfile(profile.Id)))
            .ToArray();

        InitializeComponent();
        // Scoped to the root grid so the host's IsVisible binding keeps the window view model.
        RootGrid.DataContext = this;
        SetSelection(UserProfile.Development);
        Ui.LanguageChanged += (_, _) => RefreshOptionText();
    }

    public event Action<UserProfile>? SelectionChanged;
    public event EventHandler? BuildRequested;
    public event EventHandler? BackRequested;

    public IReadOnlyList<ProfileOptionViewModel> Options { get; }

    public UserProfile SelectedProfiles => BuildSelection();

    public void SetSelection(UserProfile profiles)
    {
        _updating = true;
        try
        {
            foreach (var option in Options)
            {
                option.IsSelected = profiles.HasFlag(option.Flag);
            }

            if (BuildSelection() == UserProfile.None)
            {
                var fallback = Options.FirstOrDefault(option => option.Flag == UserProfile.Development)
                    ?? Options[0];
                fallback.IsSelected = true;
            }

            RefreshSummary();
        }
        finally
        {
            _updating = false;
        }
    }

    public void BeginRecommendationBuild()
    {
        BuildButton.IsEnabled = false;
        BackButton.IsEnabled = false;
        RecommendationProgressHost.IsVisible = true;
        SetRecommendationProgress(RecommendationProgressMap.Starting);
    }

    public void UpdateRecommendationProgress(RecommendationPipelineStage stage)
    {
        if (RecommendationProgressMap.For(stage) is { } step)
        {
            SetRecommendationProgress(step);
        }
    }

    public void CompleteRecommendationBuild()
    {
        SetRecommendationProgress(RecommendationProgressMap.Ready);
        BuildButton.IsEnabled = true;
        BackButton.IsEnabled = true;
    }

    public void FailRecommendationBuild(string? message)
    {
        RecommendationProgressHost.IsVisible = true;
        RecommendationProgressBar.IsIndeterminate = false;
        RecommendationProgressBar.Value = 0;
        RecommendationProgressPercent.Text = Ui["Recommendations.Progress.Stopped"];
        RecommendationProgressLabel.Text = string.IsNullOrWhiteSpace(message)
            ? Ui["Recommendations.Progress.Failed"]
            : message;
        BuildButton.IsEnabled = true;
        BackButton.IsEnabled = true;
    }

    private void ProfileCheckBox_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var selected = BuildSelection();
        if (selected == UserProfile.None)
        {
            _updating = true;
            try
            {
                if (sender is CheckBox { DataContext: ProfileOptionViewModel option })
                {
                    option.IsSelected = true;
                }
                else if (Options.Count > 0)
                {
                    Options[0].IsSelected = true;
                }
            }
            finally
            {
                _updating = false;
            }

            selected = BuildSelection();
        }

        RefreshSummary();
        SelectionChanged?.Invoke(selected);
    }

    private void BuildButton_OnClick(object? sender, RoutedEventArgs e) =>
        BuildRequested?.Invoke(this, EventArgs.Empty);

    private void BackButton_OnClick(object? sender, RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);

    private UserProfile BuildSelection()
    {
        var result = UserProfile.None;
        foreach (var option in Options.Where(option => option.IsSelected))
        {
            result |= option.Flag;
        }

        return result;
    }

    private void RefreshSummary()
    {
        SelectedProfilesText.Text = string.Join(
            " + ",
            Options.Where(option => option.IsSelected).Select(option => option.Label));
    }

    private static ILocalizer Ui => AvaloniaLocalizationService.Shared;

    private void SetRecommendationProgress(RecommendationProgressStep step)
    {
        var bounded = Math.Clamp(step.Percent, 0, 100);
        RecommendationProgressHost.IsVisible = true;
        RecommendationProgressBar.IsIndeterminate = step.Indeterminate;
        RecommendationProgressBar.Value = bounded;
        RecommendationProgressPercent.Text = step.Indeterminate
            ? Ui["Recommendations.Progress.Scanning"]
            : string.Format(Ui.Culture, "{0:0}%", bounded);
        RecommendationProgressLabel.Text = Ui[step.LabelKey];
    }

    private void RefreshOptionText()
    {
        foreach (var option in Options)
        {
            option.RefreshText();
        }

        RefreshSummary();
    }
}
