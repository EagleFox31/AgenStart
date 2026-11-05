using System.ComponentModel;
using AgenStart.Core.Catalogue;
using AgenStart.Core.Profiles;
using AgenStart.Core.Recommendations;
using AgenStart.Desktop.LocalData;
using AgenStart.Desktop.Services;
using AgenStart.Desktop.ViewModels.Steps;

namespace AgenStart.Desktop.ViewModels.Onboarding;

public enum OnboardingStep
{
    Splash,
    Language,
    Usage,
    Scanning,
    ScanFailed,
    Building,
    BuildFailed,
    Done
}

/// <summary>Where the shell opens once the onboarding is over.</summary>
public enum OnboardingOutcome
{
    Home,
    Recommendations
}

/// <summary>Language choice and first-run completion, owned by the local settings.</summary>
public interface IOnboardingSettings
{
    IReadOnlyList<LanguageOption> LanguageOptions { get; }

    /// <summary>Setting it applies the language at once and saves it.</summary>
    LanguageOption SelectedLanguage { get; set; }

    Task CompleteOnboardingAsync();
}

/// <summary>What the onboarding asks the setup flow to do.</summary>
public interface IOnboardingActions
{
    /// <returns>True when a machine snapshot is available.</returns>
    Task<bool> ScanAsync(CancellationToken cancellationToken);

    /// <returns>True when recommendations were produced for <paramref name="profiles"/>.</returns>
    Task<bool> BuildRecommendationsAsync(UserProfile profiles, CancellationToken cancellationToken);
}

/// <summary>
/// First-run flow: splash, language, uses, scan, recommendations. Completion is saved once,
/// when recommendations are ready (or when the user leaves for Home after a failed scan);
/// quitting earlier restarts the flow at the language step.
/// </summary>
public sealed class OnboardingViewModel : StepViewModelBase
{
    public static readonly TimeSpan MinimumSplash = TimeSpan.FromMilliseconds(600);

    private readonly IOnboardingSettings _settings;
    private readonly IOnboardingActions _actions;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private OnboardingStep _step = OnboardingStep.Splash;
    private RecommendationProgressStep _buildProgress = RecommendationProgressMap.Starting;

    public OnboardingViewModel(
        IOnboardingSettings settings,
        IOnboardingActions actions,
        ProfileRegistry? profiles = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        ILocalizer? text = null)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(actions);
        _settings = settings;
        _actions = actions;
        _delay = delay ?? Task.Delay;

        var registry = profiles ?? ProfileRegistry.Default;
        Options = registry.Active
            .Select(profile => new ProfileOptionViewModel(profile, registry.ToUserProfile(profile.Id)))
            .ToArray();
        foreach (var option in Options)
        {
            option.IsSelected = option.Flag == UserProfile.Development;
            option.PropertyChanged += Option_OnPropertyChanged;
        }

        if (SelectedProfiles == UserProfile.None && Options.Count > 0)
        {
            Options[0].IsSelected = true;
        }

        ContinueToUsageCommand = new RelayCommand(() => GoTo(OnboardingStep.Language, OnboardingStep.Usage), () => Step == OnboardingStep.Language);
        BackToLanguageCommand = new RelayCommand(() => GoTo(OnboardingStep.Usage, OnboardingStep.Language), () => Step == OnboardingStep.Usage);
        StartScanCommand = new RelayCommand(() => _ = ScanAndBuildAsync(), () => Step == OnboardingStep.Usage && SelectedProfiles != UserProfile.None);
        RetryScanCommand = new RelayCommand(() => _ = ScanAndBuildAsync(), () => Step == OnboardingStep.ScanFailed);
        GoHomeCommand = new RelayCommand(() => _ = FinishAsync(OnboardingOutcome.Home), () => Step == OnboardingStep.ScanFailed);
        RetryBuildCommand = new RelayCommand(() => _ = BuildAsync(), () => Step == OnboardingStep.BuildFailed);
        BackToUsageCommand = new RelayCommand(() => GoTo(OnboardingStep.BuildFailed, OnboardingStep.Usage), () => Step == OnboardingStep.BuildFailed);
    }

    /// <summary>Raised once, when the shell should take over.</summary>
    public event EventHandler<OnboardingOutcome>? Completed;

    public CancellationToken Lifetime { get; set; }

    public OnboardingStep Step
    {
        get => _step;
        private set
        {
            if (!SetField(ref _step, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(IsSplash));
            OnPropertyChanged(nameof(IsLanguage));
            OnPropertyChanged(nameof(IsUsage));
            OnPropertyChanged(nameof(IsWorking));
            OnPropertyChanged(nameof(IsScanning));
            OnPropertyChanged(nameof(IsBuilding));
            OnPropertyChanged(nameof(IsScanFailed));
            OnPropertyChanged(nameof(IsBuildFailed));
            OnPropertyChanged(nameof(StepNumber));
            RefreshCommands();
        }
    }

    public bool IsActive => Step != OnboardingStep.Done;

    public bool IsSplash => Step == OnboardingStep.Splash;

    public bool IsLanguage => Step == OnboardingStep.Language;

    public bool IsUsage => Step == OnboardingStep.Usage;

    public bool IsScanning => Step == OnboardingStep.Scanning;

    public bool IsBuilding => Step == OnboardingStep.Building;

    /// <summary>Scan or build in progress: the progress page is shown.</summary>
    public bool IsWorking => IsScanning || IsBuilding;

    public bool IsScanFailed => Step == OnboardingStep.ScanFailed;

    public bool IsBuildFailed => Step == OnboardingStep.BuildFailed;

    /// <summary>1-based position in the four visible steps (language, uses, scan, recommendations).</summary>
    public int StepNumber => Step switch
    {
        OnboardingStep.Language => 1,
        OnboardingStep.Usage => 2,
        OnboardingStep.Scanning or OnboardingStep.ScanFailed => 3,
        OnboardingStep.Building or OnboardingStep.BuildFailed => 4,
        _ => 0
    };

    public IReadOnlyList<LanguageOption> LanguageOptions => _settings.LanguageOptions;

    public LanguageOption SelectedLanguage
    {
        get => _settings.SelectedLanguage;
        set
        {
            if (value is null || value == _settings.SelectedLanguage)
            {
                return;
            }

            _settings.SelectedLanguage = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<ProfileOptionViewModel> Options { get; }

    public UserProfile SelectedProfiles =>
        Options.Where(option => option.IsSelected).Aggregate(UserProfile.None, (all, option) => all | option.Flag);

    public string ProgressTitle => Text[IsBuilding ? "Onboarding.Building.Title" : "Onboarding.Scanning.Title"];

    public string ProgressLabel => Text[IsBuilding ? _buildProgress.LabelKey : "Onboarding.Scanning.Detail"];

    public double ProgressPercent => IsBuilding ? _buildProgress.Percent : 0;

    public bool ProgressIndeterminate => !IsBuilding || _buildProgress.Indeterminate;

    public RelayCommand ContinueToUsageCommand { get; }

    public RelayCommand BackToLanguageCommand { get; }

    public RelayCommand StartScanCommand { get; }

    public RelayCommand RetryScanCommand { get; }

    public RelayCommand GoHomeCommand { get; }

    public RelayCommand RetryBuildCommand { get; }

    public RelayCommand BackToUsageCommand { get; }

    /// <summary>Shows the splash for at least <see cref="MinimumSplash"/>, then starts the flow or hands over to the shell.</summary>
    public async Task StartAsync(bool firstRun)
    {
        if (Step != OnboardingStep.Splash)
        {
            return;
        }

        try
        {
            await _delay(MinimumSplash, Lifetime).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (firstRun)
        {
            Step = OnboardingStep.Language;
        }
        else
        {
            Complete(OnboardingOutcome.Home);
        }
    }

    public void UpdateBuildProgress(RecommendationPipelineStage stage)
    {
        if (IsBuilding && RecommendationProgressMap.For(stage) is { } progress)
        {
            SetBuildProgress(progress);
        }
    }

    public async Task ScanAndBuildAsync()
    {
        if (Step is not (OnboardingStep.Usage or OnboardingStep.ScanFailed) || SelectedProfiles == UserProfile.None)
        {
            return;
        }

        Step = OnboardingStep.Scanning;
        OnPropertyChanged(nameof(ProgressTitle));
        OnPropertyChanged(nameof(ProgressLabel));
        var scanned = await RunAsync(token => _actions.ScanAsync(token)).ConfigureAwait(true);
        if (scanned is null)
        {
            return;
        }

        if (scanned == false)
        {
            Step = OnboardingStep.ScanFailed;
            return;
        }

        await BuildCoreAsync().ConfigureAwait(true);
    }

    public Task BuildAsync() => Step == OnboardingStep.BuildFailed ? BuildCoreAsync() : Task.CompletedTask;

    public Task FinishAsync(OnboardingOutcome outcome) =>
        Step == OnboardingStep.Done ? Task.CompletedTask : CompleteAndSaveAsync(outcome);

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        foreach (var option in Options)
        {
            option.RefreshText();
        }
    }

    private async Task BuildCoreAsync()
    {
        Step = OnboardingStep.Building;
        SetBuildProgress(RecommendationProgressMap.Starting);
        var built = await RunAsync(token => _actions.BuildRecommendationsAsync(SelectedProfiles, token)).ConfigureAwait(true);
        if (built is null)
        {
            return;
        }

        if (built == false)
        {
            Step = OnboardingStep.BuildFailed;
            return;
        }

        SetBuildProgress(RecommendationProgressMap.Ready);
        await CompleteAndSaveAsync(OnboardingOutcome.Recommendations).ConfigureAwait(true);
    }

    /// <returns>The action's result, false when it threw, null when the window is closing.</returns>
    private async Task<bool?> RunAsync(Func<CancellationToken, Task<bool>> action)
    {
        try
        {
            return await action(Lifetime).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
        {
            return null;
        }
#pragma warning disable CA1031 // A failed step is shown to the user with Retry; nothing is swallowed silently.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    private async Task CompleteAndSaveAsync(OnboardingOutcome outcome)
    {
        await _settings.CompleteOnboardingAsync().ConfigureAwait(true);
        Complete(outcome);
    }

    private void Complete(OnboardingOutcome outcome)
    {
        Step = OnboardingStep.Done;
        Completed?.Invoke(this, outcome);
    }

    private void GoTo(OnboardingStep from, OnboardingStep to)
    {
        if (Step == from)
        {
            Step = to;
        }
    }

    private void SetBuildProgress(RecommendationProgressStep progress)
    {
        _buildProgress = progress;
        OnPropertyChanged(nameof(ProgressTitle));
        OnPropertyChanged(nameof(ProgressLabel));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(ProgressIndeterminate));
    }

    private void Option_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfileOptionViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedProfiles));
            StartScanCommand.RaiseCanExecuteChanged();
        }
    }

    private void RefreshCommands()
    {
        ContinueToUsageCommand.RaiseCanExecuteChanged();
        BackToLanguageCommand.RaiseCanExecuteChanged();
        StartScanCommand.RaiseCanExecuteChanged();
        RetryScanCommand.RaiseCanExecuteChanged();
        GoHomeCommand.RaiseCanExecuteChanged();
        RetryBuildCommand.RaiseCanExecuteChanged();
        BackToUsageCommand.RaiseCanExecuteChanged();
    }
}
