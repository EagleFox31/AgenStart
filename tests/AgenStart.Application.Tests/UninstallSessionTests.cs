using AgenStart.Application.Uninstallation;
using AgenStart.PackageManagement;
using AgenStart.Uninstall;

namespace AgenStart.Application.Tests;

public sealed class UninstallSessionTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

    private static readonly InstalledProgram Program = new()
    {
        Id = "prog:7zip",
        DisplayName = "7-Zip",
        Scope = ProgramScope.Machine,
        Source = ProgramSource.Registry,
        MsiProductCode = "{11111111-2222-3333-4444-555555555555}"
    };

    private static readonly RunningProcess Editor = new(42, "7zFM.exe", @"C:\Program Files\7-Zip\7zFM.exe");

    private static UninstallPlan Plan(params RunningProcess[] processes) => new(
        "plan:1",
        Program.Id,
        Program.DisplayName,
        "state:1",
        "rules:1",
        new OfficialUninstallStep(
            OfficialStrategy.Msi,
            @"C:\Windows\System32\msiexec.exe",
            ["/x", "{11111111-2222-3333-4444-555555555555}", "/qn", "/norestart"],
            "msiexec /x ...",
            IsSilent: true,
            RequiresElevation: true),
        [],
        [],
        [],
        processes,
        new VolumeThresholds(1, 1),
        []);

    private static readonly UninstallConsent Approved = new(true, ProcessCloseConsent.None);

    private sealed class Harness
    {
        public Harness() => Uninstaller = new FakeUninstaller(Journal);

        public FakeVerifier Verifier { get; } = new();

        public FakeUninstaller Uninstaller { get; }

        public MemoryJournal Journal { get; } = new();

        public FakeCloser Closer { get; } = new();

        public FakeDetector? Detector { get; set; }

        public UninstallSession Session() => new(
            Verifier,
            Uninstaller,
            Journal,
            Detector,
            Closer,
            new UninstallSessionOptions
            {
                Time = new FixedTime(At),
                NewSessionId = () => "s-1",
                VerificationAttempts = 3,
                VerificationDelay = TimeSpan.Zero
            });
    }

    [Fact]
    public async Task Runs_the_reviewed_step_after_journaling_its_intent_and_verifies_removal()
    {
        var harness = new Harness();
        harness.Verifier.Results.Enqueue(Present());
        harness.Verifier.Results.Enqueue(Gone());

        var report = await harness.Session().RunAsync(new UninstallRequest(Plan(), Approved), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(UninstallOutcome.Removed, report.Outcome);
        Assert.Equal(OfficialStrategy.Msi, report.Strategy);
        var call = Assert.Single(harness.Uninstaller.Calls);
        Assert.Same(Program, call.Program);
        Assert.Equal(OfficialStrategy.Msi, call.Step.Strategy);
        Assert.Equal(2, call.JournalLinesBefore);
        Assert.Equal(["session-started", "intent", "result", "session-finished"], harness.Journal.Types);
        var intent = Assert.IsType<IntentRecord>(harness.Journal.Records[1]);
        Assert.Equal("msi", intent.Strategy);
        Assert.Equal(@"C:\Windows\System32\msiexec.exe", intent.Executable);
        Assert.Equal(["/x", "{11111111-2222-3333-4444-555555555555}", "/qn", "/norestart"], intent.Arguments);
        Assert.Equal("s-1.jsonl", report.JournalPath);
    }

    [Fact]
    public async Task A_second_pass_is_already_removed_and_runs_nothing()
    {
        var harness = new Harness();
        harness.Verifier.Results.Enqueue(Gone());

        var report = await harness.Session().RunAsync(new UninstallRequest(Plan(), Approved), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(UninstallOutcome.AlreadyRemoved, report.Outcome);
        Assert.Empty(harness.Uninstaller.Calls);
        Assert.Equal(["session-started", "result", "session-finished"], harness.Journal.Types);
    }

    [Fact]
    public async Task Without_confirmation_nothing_runs_and_nothing_is_closed()
    {
        var harness = new Harness();
        harness.Verifier.Results.Enqueue(Present());

        var report = await harness.Session().RunAsync(
            new UninstallRequest(Plan(Editor), new UninstallConsent(false, new ProcessCloseConsent(true, true))),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(UninstallOutcome.SkippedNotApproved, report.Outcome);
        Assert.Empty(harness.Uninstaller.Calls);
        Assert.Empty(harness.Closer.Calls);
    }

    [Fact]
    public async Task A_simulation_journals_the_same_records_and_executes_nothing()
    {
        var harness = new Harness();
        harness.Verifier.Results.Enqueue(Present());

        var report = await harness.Session().RunAsync(
            new UninstallRequest(Plan(Editor), new UninstallConsent(true, new ProcessCloseConsent(true, true)), Simulation: true),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(UninstallOutcome.NoOp, report.Outcome);
        Assert.True(report.Simulation);
        Assert.Empty(harness.Uninstaller.Calls);
        Assert.Empty(harness.Closer.Calls);
        Assert.Equal(["session-started", "intent", "result", "session-finished"], harness.Journal.Types);
        Assert.True(Assert.IsType<SessionStartedRecord>(harness.Journal.Records[0]).Simulation);
    }

    [Fact]
    public async Task Running_processes_are_closed_only_with_consent()
    {
        var withoutConsent = new Harness { Detector = new FakeDetector([Editor]) };
        withoutConsent.Verifier.Results.Enqueue(Present());
        withoutConsent.Verifier.Results.Enqueue(Gone());

        var report = await withoutConsent.Session().RunAsync(new UninstallRequest(Plan(), Approved), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(withoutConsent.Closer.Calls);
        Assert.Equal([Editor], report.ProcessesLeftRunning);

        var withConsent = new Harness { Detector = new FakeDetector([Editor]) };
        withConsent.Verifier.Results.Enqueue(Present());
        withConsent.Verifier.Results.Enqueue(Gone());
        var consent = new ProcessCloseConsent(CloseWindows: true);

        report = await withConsent.Session().RunAsync(new UninstallRequest(Plan(), new UninstallConsent(true, consent)), cancellationToken: TestContext.Current.CancellationToken);

        var call = Assert.Single(withConsent.Closer.Calls);
        Assert.Equal([Editor], call.Processes);
        Assert.Same(consent, call.Consent);
        Assert.Empty(report.ProcessesLeftRunning);
        Assert.Equal(UninstallOutcome.Removed, report.Outcome);
    }

    [Theory]
    [InlineData(3010, UninstallOutcome.Removed, true, false)]
    [InlineData(1641, UninstallOutcome.Removed, true, false)]
    [InlineData(1618, UninstallOutcome.Failed, false, true)]
    [InlineData(1602, UninstallOutcome.Cancelled, false, false)]
    [InlineData(1223, UninstallOutcome.Cancelled, false, false)]
    [InlineData(1605, UninstallOutcome.AlreadyRemoved, false, false)]
    [InlineData(1603, UninstallOutcome.Failed, false, false)]
    public async Task Exit_codes_become_journaled_outcomes(int exitCode, UninstallOutcome outcome, bool reboot, bool retryable)
    {
        var harness = new Harness();
        harness.Verifier.Results.Enqueue(Present());
        harness.Verifier.Results.Enqueue(Present());
        harness.Uninstaller.Result = UninstallExitCodes.Normalize(exitCode);

        var report = await harness.Session().RunAsync(new UninstallRequest(Plan(), Approved), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(outcome, report.Outcome);
        Assert.Equal(reboot, report.RebootRequired);
        Assert.Equal(retryable, report.Retryable);
        Assert.Equal(exitCode, report.ExitCode);
        var result = Assert.IsType<ResultRecord>(harness.Journal.Records[^2]);
        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(exitCode, result.ExitCode);
    }

    [Theory]
    [InlineData("uninstall.command-rejected", UninstallOutcome.RejectedByPolicy)]
    [InlineData("uninstall.changed-since-review", UninstallOutcome.SkippedChangedSinceReview)]
    [InlineData("uninstall.requires-elevation", UninstallOutcome.Failed)]
    public async Task Refused_steps_are_reported_without_verification(string code, UninstallOutcome outcome)
    {
        var harness = new Harness();
        harness.Verifier.Results.Enqueue(Present());
        harness.Uninstaller.Result = OfficialUninstallResult.NotExecuted(
            code == "uninstall.requires-elevation" ? PackageOperationStatus.RequiresElevation : PackageOperationStatus.BlockedByPolicy, code);

        var report = await harness.Session().RunAsync(new UninstallRequest(Plan(), Approved), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(outcome, report.Outcome);
        Assert.Equal(code, report.DiagnosticCode);
        Assert.Equal(1, harness.Verifier.Calls);
    }

    [Fact]
    public async Task A_program_still_registered_after_success_is_a_failure_after_bounded_rechecks()
    {
        var harness = new Harness();
        for (var index = 0; index < 4; index++)
        {
            harness.Verifier.Results.Enqueue(Present());
        }

        var report = await harness.Session().RunAsync(new UninstallRequest(Plan(), Approved), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(UninstallOutcome.Failed, report.Outcome);
        Assert.Equal(UninstallSession.StillPresent, report.DiagnosticCode);
        Assert.Equal(4, harness.Verifier.Calls);
    }

    [Fact]
    public async Task An_unreadable_inventory_runs_nothing()
    {
        var harness = new Harness();
        harness.Verifier.Results.Enqueue(new ProgramPresence(false, null));

        var report = await harness.Session().RunAsync(new UninstallRequest(Plan(), Approved), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(UninstallOutcome.Failed, report.Outcome);
        Assert.Equal(UninstallSession.InventoryUnavailable, report.DiagnosticCode);
        Assert.Empty(harness.Uninstaller.Calls);
    }

    [Fact]
    public async Task Cancelling_before_the_uninstaller_starts_runs_nothing_and_a_session_runs_once()
    {
        var harness = new Harness();
        harness.Verifier.Results.Enqueue(Present());
        var session = harness.Session();
        using var cancellation = new CancellationTokenSource();
        harness.Verifier.OnCall = cancellation.Cancel;

        var report = await session.RunAsync(new UninstallRequest(Plan(), Approved), cancellationToken: cancellation.Token);

        Assert.Equal(UninstallOutcome.Cancelled, report.Outcome);
        Assert.Equal(UninstallSessionState.Cancelled, session.State);
        Assert.Empty(harness.Uninstaller.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync(new UninstallRequest(Plan(), Approved), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_inventory_verifier_reads_presence_by_program_id()
    {
        var verifier = new InventoryUninstallVerifier(new FakeInventory(new InstalledProgramInventoryResult([Program], InstalledProgramInventoryStatus.Partial, [])));
        Assert.Same(Program, (await verifier.FindAsync(Program.Id, TestContext.Current.CancellationToken)).Program);
        Assert.Null((await verifier.FindAsync("prog:other", TestContext.Current.CancellationToken)).Program);

        var unavailable = new InventoryUninstallVerifier(new FakeInventory(new InstalledProgramInventoryResult([], InstalledProgramInventoryStatus.Unavailable, [])));
        Assert.False((await unavailable.FindAsync(Program.Id, TestContext.Current.CancellationToken)).Readable);
    }

    private static ProgramPresence Present() => new(true, Program);

    private static ProgramPresence Gone() => new(true, null);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeVerifier : IUninstallVerifier
    {
        public Queue<ProgramPresence> Results { get; } = new();

        public int Calls { get; private set; }

        public Action? OnCall { get; set; }

        public Task<ProgramPresence> FindAsync(string programId, CancellationToken cancellationToken = default)
        {
            Calls++;
            OnCall?.Invoke();
            return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : Present());
        }
    }

    private sealed record UninstallCall(InstalledProgram Program, OfficialUninstallStep Step, int JournalLinesBefore);

    private sealed class FakeUninstaller(MemoryJournal journal) : IOfficialUninstaller
    {
        public OfficialUninstallResult Result { get; set; } = UninstallExitCodes.Normalize(0);

        public List<UninstallCall> Calls { get; } = [];

        public Task<OfficialUninstallResult> UninstallAsync(InstalledProgram program, OfficialUninstallStep step, CancellationToken cancellationToken = default)
        {
            Calls.Add(new UninstallCall(program, step, journal.Records.Count));
            return Task.FromResult(Result);
        }
    }

    private sealed class MemoryJournal : IUninstallJournal
    {
        public List<UninstallJournalRecord> Records { get; } = [];

        public string[] Types => Records.Select(record => UninstallJournalFormat.Serialize(record))
            .Select(line => System.Text.Json.JsonDocument.Parse(line).RootElement.GetProperty("type").GetString()!)
            .ToArray();

        public string PathFor(string sessionId) => sessionId + ".jsonl";

        public Task AppendAsync(UninstallJournalRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDetector(IReadOnlyList<RunningProcess> processes) : IRunningProcessDetector
    {
        public IReadOnlyList<RunningProcess> Detect(InstalledProgram program) => processes;
    }

    private sealed record CloseCall(IReadOnlyList<RunningProcess> Processes, ProcessCloseConsent Consent);

    private sealed class FakeCloser : IRunningProcessCloser
    {
        public List<CloseCall> Calls { get; } = [];

        public Task<ProcessCloseResult> CloseAsync(IReadOnlyList<RunningProcess> processes, ProcessCloseConsent consent, CancellationToken cancellationToken = default)
        {
            Calls.Add(new CloseCall(processes, consent));
            return Task.FromResult(new ProcessCloseResult(processes, []));
        }
    }

    private sealed class FakeInventory(InstalledProgramInventoryResult result) : IInstalledProgramInventory
    {
        public Task<InstalledProgramInventoryResult> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
