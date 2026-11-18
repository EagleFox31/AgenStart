using System.Security.Cryptography;
using AgenStart.PackageManagement;
using AgenStart.Uninstall;

namespace AgenStart.Application.Uninstallation;

public enum UninstallSessionState
{
    Ready = 0,
    Running = 1,
    Cancelling = 2,
    Completed = 3,
    Cancelled = 4
}

public enum UninstallStage
{
    CheckingPresence = 0,
    ClosingProcesses = 1,
    RunningUninstaller = 2,
    Verifying = 3,
    Finished = 4
}

public sealed record UninstallProgressEvent(UninstallStage Stage);

/// <summary>What the person approved on the confirmation screen. Nothing runs without <see cref="Confirmed"/>.</summary>
public sealed record UninstallConsent(bool Confirmed, ProcessCloseConsent Processes)
{
    public static UninstallConsent None { get; } = new(false, ProcessCloseConsent.None);
}

public sealed record UninstallRequest(UninstallPlan Plan, UninstallConsent Consent, bool Simulation = false);

public sealed record UninstallReport(
    string SessionId,
    string ProgramId,
    string DisplayName,
    OfficialStrategy Strategy,
    UninstallOutcome Outcome,
    int? ExitCode,
    string DiagnosticCode,
    bool RebootRequired,
    bool Retryable,
    bool Simulation,
    IReadOnlyList<RunningProcess> ProcessesLeftRunning,
    string JournalPath);

/// <summary>Whether the program is still registered, read from a fresh inventory.</summary>
public sealed record ProgramPresence(bool Readable, InstalledProgram? Program);

public interface IUninstallVerifier
{
    Task<ProgramPresence> FindAsync(string programId, CancellationToken cancellationToken = default);
}

public sealed class InventoryUninstallVerifier(IInstalledProgramInventory inventory) : IUninstallVerifier
{
    private readonly IInstalledProgramInventory _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));

    public async Task<ProgramPresence> FindAsync(string programId, CancellationToken cancellationToken = default)
    {
        var result = await _inventory.CaptureAsync(cancellationToken).ConfigureAwait(false);
        return result.Status == InstalledProgramInventoryStatus.Unavailable
            ? new ProgramPresence(false, null)
            : new ProgramPresence(true, result.Programs.FirstOrDefault(program => string.Equals(program.Id, programId, StringComparison.Ordinal)));
    }
}

public sealed record UninstallSessionOptions
{
    public TimeProvider Time { get; init; } = TimeProvider.System;

    public Func<string>? NewSessionId { get; init; }

    /// <summary>Some uninstallers hand over to a child process and return early: the entry is re-read a few times.</summary>
    public int VerificationAttempts { get; init; } = 5;

    public TimeSpan VerificationDelay { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// One official uninstall, journaled (ADR-0007): re-check presence, require consent, close processes
/// only if approved, run the reviewed step, then verify against a fresh inventory. A simulation writes
/// the same records and calls neither the uninstaller nor the process closer.
/// </summary>
public sealed class UninstallSession
{
    public const string ItemId = "official";
    public const string StillPresent = "uninstall.still-present";
    public const string NotApproved = "uninstall.not-approved";
    public const string InventoryUnavailable = "uninstall.inventory-unavailable";
    public const string Simulated = "uninstall.simulated";

    private readonly IUninstallVerifier _verifier;
    private readonly IOfficialUninstaller _uninstaller;
    private readonly IUninstallJournal _journal;
    private readonly IRunningProcessDetector? _detector;
    private readonly IRunningProcessCloser? _closer;
    private readonly UninstallSessionOptions _options;
    private int _state = (int)UninstallSessionState.Ready;

    public UninstallSession(
        IUninstallVerifier verifier,
        IOfficialUninstaller uninstaller,
        IUninstallJournal journal,
        IRunningProcessDetector? detector = null,
        IRunningProcessCloser? closer = null,
        UninstallSessionOptions? options = null)
    {
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _uninstaller = uninstaller ?? throw new ArgumentNullException(nameof(uninstaller));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _detector = detector;
        _closer = closer;
        _options = options ?? new UninstallSessionOptions();
    }

    public UninstallSessionState State => (UninstallSessionState)Volatile.Read(ref _state);

    public async Task<UninstallReport> RunAsync(
        UninstallRequest request,
        IProgress<UninstallProgressEvent>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Interlocked.CompareExchange(ref _state, (int)UninstallSessionState.Running, (int)UninstallSessionState.Ready) != (int)UninstallSessionState.Ready)
        {
            throw new InvalidOperationException("An uninstall session runs once.");
        }

        var plan = request.Plan;
        var sessionId = _options.NewSessionId?.Invoke() ?? NewSessionId(_options.Time);
        var step = plan.Official;
        var leftRunning = (IReadOnlyList<RunningProcess>)[];

        async Task<UninstallReport> Finish(UninstallOutcome outcome, OfficialUninstallResult? result, string code, string message)
        {
            await Append(new ResultRecord(sessionId, Now(), ItemId, outcome, result?.ExitCode, null, message)).ConfigureAwait(false);
            await Append(new SessionFinishedRecord(sessionId, Now(), outcome, new Dictionary<UninstallOutcome, int> { [outcome] = 1 }, null)).ConfigureAwait(false);
            progress?.Report(new UninstallProgressEvent(UninstallStage.Finished));
            Volatile.Write(ref _state, (int)(outcome == UninstallOutcome.Cancelled ? UninstallSessionState.Cancelled : UninstallSessionState.Completed));
            return new UninstallReport(
                sessionId, plan.ProgramId, plan.ProgramName, step.Strategy, outcome, result?.ExitCode, code,
                result?.RebootRequired ?? false, result?.Retryable ?? false, request.Simulation, leftRunning, _journal.PathFor(sessionId));
        }

        // Journal writes are not cancellable: a started session always ends with its result lines.
        Task Append(UninstallJournalRecord record) => _journal.AppendAsync(record, CancellationToken.None);

        await Append(new SessionStartedRecord(sessionId, Now(), plan.PlanId, plan.ProgramId, plan.ProgramName, plan.StateFingerprint, request.Simulation)).ConfigureAwait(false);

        progress?.Report(new UninstallProgressEvent(UninstallStage.CheckingPresence));
        var presence = await _verifier.FindAsync(plan.ProgramId, cancellationToken).ConfigureAwait(false);
        if (!presence.Readable)
        {
            return await Finish(UninstallOutcome.Failed, null, InventoryUnavailable, "The installed programs could not be read; nothing was run.").ConfigureAwait(false);
        }

        if (presence.Program is not { } program)
        {
            return await Finish(UninstallOutcome.AlreadyRemoved, null, UninstallExitCodes.NotInstalledCode, "The program is no longer installed.").ConfigureAwait(false);
        }

        if (!request.Consent.Confirmed)
        {
            return await Finish(UninstallOutcome.SkippedNotApproved, null, NotApproved, "The uninstall was not confirmed.").ConfigureAwait(false);
        }

        var running = _detector?.Detect(program) ?? plan.Processes;
        leftRunning = running;
        if (running.Count > 0 && request.Consent.Processes.CloseWindows && !request.Simulation && _closer is not null)
        {
            progress?.Report(new UninstallProgressEvent(UninstallStage.ClosingProcesses));
            var closed = await _closer.CloseAsync(running, request.Consent.Processes, cancellationToken).ConfigureAwait(false);
            leftRunning = closed.StillRunning;
        }

        await Append(new IntentRecord(
            sessionId, Now(), ItemId, UninstallJournalTokens.OfficialUninstallKind, plan.ProgramId, plan.StateFingerprint,
            PlanTokens.Strategy(step.Strategy), step.Executable, step.Arguments)).ConfigureAwait(false);

        if (request.Simulation)
        {
            return await Finish(UninstallOutcome.NoOp, null, Simulated, "Simulation: nothing was run.").ConfigureAwait(false);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            Volatile.Write(ref _state, (int)UninstallSessionState.Cancelling);
            return await Finish(UninstallOutcome.Cancelled, null, UninstallExitCodes.CancelledCode, "Cancelled before the uninstaller started.").ConfigureAwait(false);
        }

        progress?.Report(new UninstallProgressEvent(UninstallStage.RunningUninstaller));
        var result = await _uninstaller.UninstallAsync(program, step, cancellationToken).ConfigureAwait(false);

        if (result.DiagnosticCode == OfficialStrategySelector.CommandRejected)
        {
            return await Finish(UninstallOutcome.RejectedByPolicy, result, result.DiagnosticCode, "The uninstall command was refused by policy; nothing was run.").ConfigureAwait(false);
        }

        if (result.DiagnosticCode == UninstallExitCodes.ChangedSinceReviewCode)
        {
            return await Finish(UninstallOutcome.SkippedChangedSinceReview, result, result.DiagnosticCode, "The uninstall entry changed since it was reviewed; nothing was run.").ConfigureAwait(false);
        }

        switch (result.Status)
        {
            case PackageOperationStatus.NotFound:
                return await Finish(UninstallOutcome.AlreadyRemoved, result, result.DiagnosticCode, "The program was already removed.").ConfigureAwait(false);
            case PackageOperationStatus.CancelledByUser or PackageOperationStatus.Cancelled:
                return await Finish(UninstallOutcome.Cancelled, result, result.DiagnosticCode, "The uninstall was cancelled.").ConfigureAwait(false);
            case PackageOperationStatus.Succeeded or PackageOperationStatus.RebootRequired:
                break;
            default:
                return await Finish(UninstallOutcome.Failed, result, result.DiagnosticCode, $"The uninstall did not complete ({result.DiagnosticCode}).").ConfigureAwait(false);
        }

        progress?.Report(new UninstallProgressEvent(UninstallStage.Verifying));
        if (await IsGoneAsync(plan.ProgramId).ConfigureAwait(false) || result.RebootRequired)
        {
            return await Finish(UninstallOutcome.Removed, result, result.DiagnosticCode, result.RebootRequired ? "Removed; a restart is required to finish." : "Removed.").ConfigureAwait(false);
        }

        return await Finish(UninstallOutcome.Failed, result, StillPresent, "The uninstaller finished but the program is still registered.").ConfigureAwait(false);
    }

    private async Task<bool> IsGoneAsync(string programId)
    {
        var attempts = Math.Max(1, _options.VerificationAttempts);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            // Verification runs after the uninstaller: it is not cancelled with the session.
            var presence = await _verifier.FindAsync(programId, CancellationToken.None).ConfigureAwait(false);
            if (presence.Readable && presence.Program is null)
            {
                return true;
            }

            if (attempt < attempts && _options.VerificationDelay > TimeSpan.Zero)
            {
                await Task.Delay(_options.VerificationDelay, _options.Time).ConfigureAwait(false);
            }
        }

        return false;
    }

    private DateTimeOffset Now() => _options.Time.GetUtcNow();

    private static string NewSessionId(TimeProvider time) =>
        "uninstall-" + time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture)
        + "-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
}
