using AgenStart.Core.Catalogue;
using AgenStart.Core.Recommendations;
using AgenStart.Desktop.LocalData;
using AgenStart.Desktop.ViewModels.Onboarding;

namespace AgenStart.Desktop.Tests;

public sealed class OnboardingTests
{
    [Fact]
    public async Task A_first_run_shows_the_splash_for_the_minimum_time_then_asks_the_language()
    {
        var (onboarding, settings, _, delays) = Create();

        Assert.Equal(OnboardingStep.Splash, onboarding.Step);
        await onboarding.StartAsync(firstRun: true);

        Assert.Equal([OnboardingViewModel.MinimumSplash], delays);
        Assert.Equal(OnboardingStep.Language, onboarding.Step);
        Assert.Equal(1, onboarding.StepNumber);
        Assert.Equal(0, settings.Completions);
    }

    [Fact]
    public async Task A_returning_user_goes_from_the_splash_to_home_without_saving()
    {
        var (onboarding, settings, actions, _) = Create();
        var outcomes = Track(onboarding);

        await onboarding.StartAsync(firstRun: false);

        Assert.Equal(OnboardingStep.Done, onboarding.Step);
        Assert.False(onboarding.IsActive);
        Assert.Equal([OnboardingOutcome.Home], outcomes);
        Assert.Equal(0, settings.Completions);
        Assert.Equal(0, actions.Scans);
    }

    [Fact]
    public async Task The_happy_path_scans_builds_and_saves_completion_once()
    {
        var (onboarding, settings, actions, _) = Create();
        var outcomes = Track(onboarding);
        await onboarding.StartAsync(firstRun: true);

        onboarding.ContinueToUsageCommand.Execute(null);
        Assert.Equal(OnboardingStep.Usage, onboarding.Step);
        await onboarding.ScanAndBuildAsync();

        Assert.Equal(1, actions.Scans);
        Assert.Equal([onboarding.SelectedProfiles], actions.Built);
        Assert.Equal(1, settings.Completions);
        Assert.Equal([OnboardingOutcome.Recommendations], outcomes);
        Assert.Equal(OnboardingStep.Done, onboarding.Step);
    }

    [Fact]
    public async Task Nothing_is_saved_before_recommendations_are_ready()
    {
        var (onboarding, settings, actions, _) = Create();
        var build = new TaskCompletionSource<bool>();
        actions.Build = _ => build.Task;
        await ToUsage(onboarding);

        var running = onboarding.ScanAndBuildAsync();

        Assert.Equal(OnboardingStep.Building, onboarding.Step);
        Assert.True(onboarding.IsWorking);
        Assert.Equal(0, settings.Completions);
        build.SetResult(true);
        await running;
        Assert.Equal(1, settings.Completions);
    }

    [Fact]
    public async Task A_failed_scan_offers_retry_and_home()
    {
        var (onboarding, settings, actions, _) = Create();
        var outcomes = Track(onboarding);
        actions.Scan = () => Task.FromResult(false);
        await ToUsage(onboarding);

        await onboarding.ScanAndBuildAsync();

        Assert.Equal(OnboardingStep.ScanFailed, onboarding.Step);
        Assert.True(onboarding.RetryScanCommand.CanExecute(null));
        Assert.True(onboarding.GoHomeCommand.CanExecute(null));
        Assert.Equal(0, settings.Completions);

        actions.Scan = () => Task.FromResult(true);
        await onboarding.ScanAndBuildAsync();
        Assert.Equal([OnboardingOutcome.Recommendations], outcomes);
    }

    [Fact]
    public async Task Leaving_for_home_after_a_failed_scan_completes_the_onboarding()
    {
        var (onboarding, settings, actions, _) = Create();
        var outcomes = Track(onboarding);
        actions.Scan = () => throw new InvalidOperationException("scan crashed");
        await ToUsage(onboarding);

        await onboarding.ScanAndBuildAsync();
        Assert.Equal(OnboardingStep.ScanFailed, onboarding.Step);
        await onboarding.FinishAsync(OnboardingOutcome.Home);

        Assert.Equal(1, settings.Completions);
        Assert.Equal([OnboardingOutcome.Home], outcomes);
        Assert.Empty(actions.Built);
    }

    [Fact]
    public async Task A_failed_build_can_go_back_to_the_uses_or_retry()
    {
        var (onboarding, settings, actions, _) = Create();
        actions.Build = _ => Task.FromResult(false);
        await ToUsage(onboarding);

        await onboarding.ScanAndBuildAsync();
        Assert.Equal(OnboardingStep.BuildFailed, onboarding.Step);
        Assert.Equal(4, onboarding.StepNumber);

        onboarding.BackToUsageCommand.Execute(null);
        Assert.Equal(OnboardingStep.Usage, onboarding.Step);
        await onboarding.ScanAndBuildAsync();
        Assert.Equal(OnboardingStep.BuildFailed, onboarding.Step);

        actions.Build = _ => Task.FromResult(true);
        await onboarding.BuildAsync();
        Assert.Equal(OnboardingStep.Done, onboarding.Step);
        Assert.Equal(1, settings.Completions);
        Assert.Equal(2, actions.Scans);
    }

    [Fact]
    public async Task The_scan_cannot_start_without_a_use()
    {
        var (onboarding, _, actions, _) = Create();
        await ToUsage(onboarding);
        Assert.NotEqual(UserProfile.None, onboarding.SelectedProfiles);
        Assert.True(onboarding.StartScanCommand.CanExecute(null));

        foreach (var option in onboarding.Options)
        {
            option.IsSelected = false;
        }

        Assert.False(onboarding.StartScanCommand.CanExecute(null));
        await onboarding.ScanAndBuildAsync();
        Assert.Equal(OnboardingStep.Usage, onboarding.Step);
        Assert.Equal(0, actions.Scans);
    }

    [Fact]
    public async Task Commands_only_work_on_their_own_step()
    {
        var (onboarding, _, _, _) = Create();
        Assert.False(onboarding.ContinueToUsageCommand.CanExecute(null));
        onboarding.ContinueToUsageCommand.Execute(null);
        Assert.Equal(OnboardingStep.Splash, onboarding.Step);

        await onboarding.StartAsync(firstRun: true);
        Assert.True(onboarding.ContinueToUsageCommand.CanExecute(null));
        Assert.False(onboarding.BackToLanguageCommand.CanExecute(null));
        Assert.False(onboarding.RetryBuildCommand.CanExecute(null));

        onboarding.ContinueToUsageCommand.Execute(null);
        onboarding.BackToLanguageCommand.Execute(null);
        Assert.Equal(OnboardingStep.Language, onboarding.Step);
    }

    [Fact]
    public void Choosing_a_language_goes_through_the_settings()
    {
        var (onboarding, settings, _, _) = Create();
        var french = settings.LanguageOptions.Single(option => option.Code == "fr");

        onboarding.SelectedLanguage = french;

        Assert.Equal(french, settings.SelectedLanguage);
        Assert.Equal(french, onboarding.SelectedLanguage);
    }

    [Fact]
    public async Task Closing_the_window_during_the_scan_leaves_nothing_saved()
    {
        using var lifetime = new CancellationTokenSource();
        var (onboarding, settings, actions, _) = Create();
        onboarding.Lifetime = lifetime.Token;
        var outcomes = Track(onboarding);
        actions.Scan = () => lifetime.IsCancellationRequested ? throw new OperationCanceledException() : Task.FromResult(true);
        await ToUsage(onboarding);
        await lifetime.CancelAsync();

        await onboarding.ScanAndBuildAsync();

        Assert.Equal(OnboardingStep.Scanning, onboarding.Step);
        Assert.Empty(outcomes);
        Assert.Equal(0, settings.Completions);
    }

    [Fact]
    public async Task Build_progress_follows_the_pipeline_only_while_building()
    {
        var (onboarding, _, actions, _) = Create();
        var build = new TaskCompletionSource<bool>();
        actions.Build = _ => build.Task;
        await ToUsage(onboarding);

        onboarding.UpdateBuildProgress(RecommendationPipelineStage.EvaluatingRecommendationRules);
        Assert.Equal(0, onboarding.ProgressPercent);

        var running = onboarding.ScanAndBuildAsync();
        onboarding.UpdateBuildProgress(RecommendationPipelineStage.EvaluatingRecommendationRules);

        Assert.Equal(75, onboarding.ProgressPercent);
        Assert.False(onboarding.ProgressIndeterminate);
        Assert.Equal("Recommendations.Progress.ApplyingRules", onboarding.ProgressLabel);
        build.SetResult(true);
        await running;
    }

    private static async Task ToUsage(OnboardingViewModel onboarding)
    {
        await onboarding.StartAsync(firstRun: true);
        onboarding.ContinueToUsageCommand.Execute(null);
    }

    private static List<OnboardingOutcome> Track(OnboardingViewModel onboarding)
    {
        var outcomes = new List<OnboardingOutcome>();
        onboarding.Completed += (_, outcome) => outcomes.Add(outcome);
        return outcomes;
    }

    private static (OnboardingViewModel Onboarding, FakeSettings Settings, FakeActions Actions, List<TimeSpan> Delays) Create()
    {
        var settings = new FakeSettings();
        var actions = new FakeActions();
        var delays = new List<TimeSpan>();
        var onboarding = new OnboardingViewModel(settings, actions, delay: (duration, _) =>
        {
            delays.Add(duration);
            return Task.CompletedTask;
        });
        return (onboarding, settings, actions, delays);
    }

    private sealed class FakeSettings : IOnboardingSettings
    {
        public IReadOnlyList<LanguageOption> LanguageOptions { get; } = [new("en", "English"), new("fr", "Français")];

        public LanguageOption SelectedLanguage { get; set; } = new("en", "English");

        public int Completions { get; private set; }

        public Task CompleteOnboardingAsync()
        {
            Completions++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeActions : IOnboardingActions
    {
        public Func<Task<bool>> Scan { get; set; } = () => Task.FromResult(true);

        public Func<UserProfile, Task<bool>> Build { get; set; } = _ => Task.FromResult(true);

        public int Scans { get; private set; }

        public List<UserProfile> Built { get; } = [];

        public Task<bool> ScanAsync(CancellationToken cancellationToken)
        {
            Scans++;
            return Scan();
        }

        public Task<bool> BuildRecommendationsAsync(UserProfile profiles, CancellationToken cancellationToken)
        {
            Built.Add(profiles);
            return Build(profiles);
        }
    }
}
