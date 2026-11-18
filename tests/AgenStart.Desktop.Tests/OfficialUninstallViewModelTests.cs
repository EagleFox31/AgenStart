using AgenStart.Application.Uninstallation;
using AgenStart.Desktop.Services;
using AgenStart.Desktop.ViewModels.Steps;
using AgenStart.Uninstall;

namespace AgenStart.Desktop.Tests;

/// <summary>
/// The experimental official uninstall flow, driven only through a fake runner: nothing here can
/// start a process, close an app or uninstall anything.
/// </summary>
public sealed partial class UninstallViewModelTests
{
    [Fact]
    public async Task Without_the_experimental_setting_a_plan_offers_no_uninstall()
    {
        var page = await PlannedPage(new FakePlanSource(_ => SamplePlan("b")));

        Assert.True(page.HasPlan);
        Assert.False(page.IsOfficialUninstallEnabled);
        Assert.Null(page.OfficialUninstall);
    }

    [Fact]
    public async Task Nothing_runs_before_the_confirmation_and_a_cancel_runs_nothing()
    {
        var runner = new FakeRunner();
        var (page, official) = await OfficialPage(runner);

        Assert.True(page.IsOfficialUninstallEnabled);
        Assert.True(official.IsReady);
        Assert.True(official.CanStart);
        Assert.True(official.StartCommand.CanExecute(null));

        official.StartCommand.Execute(null);
        Assert.True(official.IsConfirming);
        Assert.Empty(runner.Requests);

        official.CloseAppsConsent = true;
        official.CancelCommand.Execute(null);
        Assert.True(official.IsReady);
        Assert.False(official.CloseAppsConsent);

        // Confirming is only possible from the confirmation itself.
        await official.ConfirmAsync();
        Assert.True(official.IsReady);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task A_confirmed_uninstall_runs_once_and_shows_the_report()
    {
        var runner = new FakeRunner();
        var (page, official) = await OfficialPage(runner);

        official.StartCommand.Execute(null);
        Assert.Equal("en:Uninstall.Run.ConfirmTitle", official.ConfirmTitle);
        Assert.True(official.HasRunning);
        await official.ConfirmAsync();

        var request = Assert.Single(runner.Requests);
        Assert.Same(page.CurrentPlan!.Plan, request.Plan);
        Assert.True(request.Consent.Confirmed);
        Assert.False(request.Simulation);
        // Running apps are left alone unless the person ticked the box.
        Assert.Equal(ProcessCloseConsent.None, request.Consent.Processes);
        Assert.True(official.IsFinished);
        Assert.True(official.Succeeded);
        Assert.Equal("en:Uninstall.Run.Result.Removed", official.ResultText);
        Assert.Equal("en:Uninstall.Run.Stage.Finished", official.StageText);
        Assert.True(official.HasJournal);
        Assert.False(page.IsUninstalling);
        Assert.True(page.CanBrowse);
    }

    [Fact]
    public async Task Closing_apps_needs_its_own_consent_and_never_forces()
    {
        var runner = new FakeRunner();
        var (_, official) = await OfficialPage(runner);

        official.StartCommand.Execute(null);
        official.CloseAppsConsent = true;
        await official.ConfirmAsync();

        var processes = Assert.Single(runner.Requests).Consent.Processes;
        Assert.True(processes.CloseWindows);
        Assert.False(processes.ForceAfterTimeout);
    }

    [Fact]
    public async Task While_uninstalling_the_page_is_locked_and_follows_the_stages()
    {
        var gate = new TaskCompletionSource<UninstallReport>();
        var runner = new FakeRunner { Pending = gate.Task };
        var (page, official) = await OfficialPage(runner);
        var selected = page.SelectedProgram;

        official.StartCommand.Execute(null);
        var running = official.ConfirmAsync();

        Assert.True(official.IsRunning);
        Assert.True(page.IsUninstalling);
        Assert.False(page.CanBrowse);
        Assert.False(page.CanPreparePlan);
        Assert.False(page.RefreshCommand.CanExecute(null));
        Assert.False(page.PreparePlanCommand.CanExecute(null));

        page.SelectedProgram = page.Programs.First(row => row.Program.Id == "a");
        Assert.Same(selected, page.SelectedProgram);
        Assert.Same(official, page.OfficialUninstall);

        runner.Progress!.Report(new UninstallProgressEvent(UninstallStage.Verifying));
        Assert.Equal("en:Uninstall.Run.Stage.Verifying", official.StageText);

        gate.SetResult(Report(UninstallOutcome.Removed));
        await running;

        Assert.True(official.IsFinished);
        Assert.False(page.IsUninstalling);
        Assert.True(page.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_runner_failure_is_shown_as_a_message()
    {
        var runner = new FakeRunner { Failure = new InvalidOperationException("boom") };
        var (page, official) = await OfficialPage(runner);

        official.StartCommand.Execute(null);
        await official.ConfirmAsync();

        Assert.True(official.IsFinished);
        Assert.False(official.Succeeded);
        Assert.Equal("en:Uninstall.Run.Result.Error", official.ResultText);
        Assert.False(page.IsUninstalling);
    }

    [Fact]
    public async Task A_plan_without_an_official_method_cannot_start()
    {
        var unavailable = SamplePlan("b") with
        {
            Official = new OfficialUninstallStep(OfficialStrategy.Unavailable, null, [], null, false, false)
        };
        var runner = new FakeRunner();
        var (_, official) = await OfficialPage(runner, unavailable);

        Assert.False(official.IsAvailable);
        Assert.False(official.CanStart);
        Assert.False(official.StartCommand.CanExecute(null));
        official.StartCommand.Execute(null);
        Assert.True(official.IsReady);
        Assert.Empty(runner.Requests);
    }

    [Theory]
    [InlineData(UninstallOutcome.Removed, false, false, "", "Uninstall.Run.Result.Removed")]
    [InlineData(UninstallOutcome.Removed, true, false, "", "Uninstall.Run.Result.RemovedReboot")]
    [InlineData(UninstallOutcome.AlreadyRemoved, false, false, "", "Uninstall.Run.Result.AlreadyRemoved")]
    [InlineData(UninstallOutcome.SkippedNotApproved, false, false, "", "Uninstall.Run.Result.NotApproved")]
    [InlineData(UninstallOutcome.SkippedChangedSinceReview, false, false, "", "Uninstall.Run.Result.Changed")]
    [InlineData(UninstallOutcome.RejectedByPolicy, false, false, "", "Uninstall.Run.Result.Rejected")]
    [InlineData(UninstallOutcome.Cancelled, false, false, "", "Uninstall.Run.Result.Cancelled")]
    [InlineData(UninstallOutcome.NoOp, false, false, "", "Uninstall.Run.Result.NoOp")]
    [InlineData(UninstallOutcome.Failed, false, false, UninstallExitCodes.ElevationRequiredCode, "Uninstall.Run.Result.Elevation")]
    [InlineData(UninstallOutcome.Failed, false, true, "uninstall.busy", "Uninstall.Run.Result.Retry")]
    [InlineData(UninstallOutcome.Failed, false, false, UninstallSession.StillPresent, "Uninstall.Run.Result.StillPresent")]
    [InlineData(UninstallOutcome.Failed, false, false, "uninstall.exit-code", "Uninstall.Run.Result.Failed")]
    public void Every_outcome_has_its_own_message(UninstallOutcome outcome, bool reboot, bool retryable, string code, string expected)
    {
        var report = Report(outcome) with { RebootRequired = reboot, Retryable = retryable, DiagnosticCode = code };

        Assert.Equal($"en:{expected}", OfficialUninstallViewModel.Describe(report, "Beta Studio").Resolve(new TestLocalizer()));
    }

    private static async Task<(UninstallViewModel Page, OfficialUninstallViewModel Official)> OfficialPage(FakeRunner runner, UninstallPlan? plan = null)
    {
        var page = new UninstallViewModel(
            new FakeInventory(Complete(Sample)),
            new ImmediateDispatcher(),
            new TestLocalizer(),
            new FakePlanSource(_ => plan ?? SamplePlan("b")),
            new FakeExportTarget(),
            runner);
        await page.EnsureLoadedAsync();
        page.SelectedProgram = page.Programs.Single(row => row.Program.Id == "b");
        await page.PreparePlanAsync();
        return (page, Assert.IsType<OfficialUninstallViewModel>(page.OfficialUninstall));
    }

    private static UninstallReport Report(UninstallOutcome outcome) => new(
        SessionId: "session",
        ProgramId: "b",
        DisplayName: "Beta Studio",
        Strategy: OfficialStrategy.Msi,
        Outcome: outcome,
        ExitCode: 0,
        DiagnosticCode: "uninstall.success",
        RebootRequired: false,
        Retryable: false,
        Simulation: false,
        ProcessesLeftRunning: [],
        JournalPath: @"C:\data\uninstall\journal\session.jsonl");

    /// <summary>Records each request; it never starts anything.</summary>
    private sealed class FakeRunner : IOfficialUninstallRunner
    {
        public List<UninstallRequest> Requests { get; } = [];

        public IProgress<UninstallProgressEvent>? Progress { get; private set; }

        public Task<UninstallReport>? Pending { get; init; }

        public Exception? Failure { get; init; }

        public Task<UninstallReport> RunAsync(UninstallRequest request, IProgress<UninstallProgressEvent>? progress = null, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            Progress = progress;
            if (Failure is not null)
            {
                return Task.FromException<UninstallReport>(Failure);
            }

            return Pending ?? Task.FromResult(Report(UninstallOutcome.Removed));
        }
    }
}
