using System.ComponentModel;
using System.Windows.Input;
using AgenStart.Application.Uninstallation;
using AgenStart.Desktop.Services;
using AgenStart.Uninstall;

namespace AgenStart.Desktop.ViewModels.Steps;

public enum OfficialUninstallPhase
{
    Ready,
    Confirming,
    Running,
    Finished
}

/// <summary>
/// The experimental official uninstall of a reviewed plan: an explicit confirmation (name, method,
/// scope, administrator note, running apps), then one session with its progress, then the report.
/// Running apps are asked to close only when the person ticks that box; they are never ended.
/// Leftovers stay a list: cleaning them up is not part of this flow.
/// </summary>
public sealed class OfficialUninstallViewModel : INotifyPropertyChanged
{
    private readonly UninstallPlanViewModel _plan;
    private readonly InstalledProgram _program;
    private readonly IOfficialUninstallRunner _runner;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILocalizer _text;
    private readonly Action<bool> _runningChanged;
    private readonly RelayCommand _startCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly RelayCommand _confirmCommand;
    private OfficialUninstallPhase _phase = OfficialUninstallPhase.Ready;
    private bool _closeAppsConsent;
    private UninstallStage _stage = UninstallStage.CheckingPresence;
    private UninstallReport? _report;

    public OfficialUninstallViewModel(
        UninstallPlanViewModel plan,
        InstalledProgram program,
        IOfficialUninstallRunner runner,
        IUiDispatcher dispatcher,
        ILocalizer text,
        Action<bool>? runningChanged = null)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _program = program ?? throw new ArgumentNullException(nameof(program));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _runningChanged = runningChanged ?? (_ => { });
        _startCommand = new RelayCommand(Start, () => CanStart);
        _cancelCommand = new RelayCommand(Cancel, () => IsConfirming);
        _confirmCommand = new RelayCommand(() => _ = ConfirmAsync(), () => IsConfirming);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Opens the confirmation. Nothing runs until <see cref="ConfirmCommand"/>.</summary>
    public ICommand StartCommand => _startCommand;

    public ICommand CancelCommand => _cancelCommand;

    /// <summary>Runs the official uninstall once, with the consent shown on the confirmation.</summary>
    public ICommand ConfirmCommand => _confirmCommand;

    public OfficialUninstallPhase Phase
    {
        get => _phase;
        private set
        {
            if (_phase == value)
            {
                return;
            }

            _phase = value;
            Raise(string.Empty);
            _startCommand.RaiseCanExecuteChanged();
            _cancelCommand.RaiseCanExecuteChanged();
            _confirmCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsReady => _phase == OfficialUninstallPhase.Ready;

    public bool IsConfirming => _phase == OfficialUninstallPhase.Confirming;

    public bool IsRunning => _phase == OfficialUninstallPhase.Running;

    public bool IsFinished => _phase == OfficialUninstallPhase.Finished;

    /// <summary>False when no official method is available: the action is then disabled.</summary>
    public bool IsAvailable => _plan.Plan.Official.Strategy != OfficialStrategy.Unavailable;

    public bool CanStart => IsReady && IsAvailable;

    /// <summary>Ask the running apps to close their windows first. Off until the person ticks it.</summary>
    public bool CloseAppsConsent
    {
        get => _closeAppsConsent;
        set
        {
            if (_closeAppsConsent == value)
            {
                return;
            }

            _closeAppsConsent = value;
            Raise(nameof(CloseAppsConsent));
        }
    }

    public string ConfirmTitle => _text.Format("Uninstall.Run.ConfirmTitle", _plan.Plan.ProgramName);

    public string StrategyText => _text.Format("Uninstall.Run.Strategy", _plan.StrategyText);

    public string ScopeText => _text.Format(
        "Uninstall.Run.Scope",
        _text[_program.Scope == ProgramScope.Machine ? "Uninstall.Scope.Machine" : "Uninstall.Scope.User"]);

    public bool ShowElevationNote => _plan.OfficialRequiresElevation;

    public bool HasRunning => _plan.HasRunning;

    public string RunningText => _plan.RunningText;

    public string StageText => _text[_stage switch
    {
        UninstallStage.ClosingProcesses => "Uninstall.Run.Stage.Closing",
        UninstallStage.RunningUninstaller => "Uninstall.Run.Stage.Running",
        UninstallStage.Verifying => "Uninstall.Run.Stage.Verifying",
        UninstallStage.Finished => "Uninstall.Run.Stage.Finished",
        _ => "Uninstall.Run.Stage.Checking"
    }];

    public UninstallReport? Report => _report;

    public bool Succeeded => _report?.Outcome is UninstallOutcome.Removed or UninstallOutcome.AlreadyRemoved;

    public string ResultText => Describe(_report, _plan.Plan.ProgramName).Resolve(_text);

    public bool HasLeftRunning => _report?.ProcessesLeftRunning.Count > 0;

    public string LeftRunningText => _report is null
        ? string.Empty
        : _text.Format(
            "Uninstall.Run.LeftRunning",
            string.Join(", ", _report.ProcessesLeftRunning.Select(process => process.Name).Distinct(StringComparer.OrdinalIgnoreCase)));

    public bool HasJournal => !string.IsNullOrWhiteSpace(_report?.JournalPath);

    public string JournalText => _text.Format("Uninstall.Run.Journal", _report?.JournalPath ?? string.Empty);

    public async Task ConfirmAsync()
    {
        if (!IsConfirming)
        {
            return;
        }

        var consent = new UninstallConsent(
            Confirmed: true,
            Processes: _closeAppsConsent && HasRunning ? new ProcessCloseConsent(CloseWindows: true) : ProcessCloseConsent.None);
        Phase = OfficialUninstallPhase.Running;
        _runningChanged(true);

        UninstallReport? report;
        try
        {
            report = await _runner.RunAsync(
                new UninstallRequest(_plan.Plan, consent),
                new DispatchedProgress(this),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The session journals its own failures; anything else is shown as a message, never a crash.
            report = null;
        }

        _dispatcher.Post(() =>
        {
            _report = report;
            _stage = UninstallStage.Finished;
            Phase = OfficialUninstallPhase.Finished;
            Raise(string.Empty);
            _runningChanged(false);
        });
    }

    /// <summary>The report in words: what happened, and whether anything was run.</summary>
    public static UiText Describe(UninstallReport? report, string programName) => report switch
    {
        null => UiText.Key("Uninstall.Run.Result.Error"),
        { Outcome: UninstallOutcome.Removed, RebootRequired: true } => UiText.Key("Uninstall.Run.Result.RemovedReboot", programName),
        { Outcome: UninstallOutcome.Removed } => UiText.Key("Uninstall.Run.Result.Removed", programName),
        { Outcome: UninstallOutcome.AlreadyRemoved } => UiText.Key("Uninstall.Run.Result.AlreadyRemoved", programName),
        { Outcome: UninstallOutcome.SkippedNotApproved } => UiText.Key("Uninstall.Run.Result.NotApproved"),
        { Outcome: UninstallOutcome.SkippedChangedSinceReview } => UiText.Key("Uninstall.Run.Result.Changed"),
        { Outcome: UninstallOutcome.RejectedByPolicy } => UiText.Key("Uninstall.Run.Result.Rejected"),
        { Outcome: UninstallOutcome.Cancelled } => UiText.Key("Uninstall.Run.Result.Cancelled"),
        { Outcome: UninstallOutcome.NoOp } => UiText.Key("Uninstall.Run.Result.NoOp"),
        { DiagnosticCode: UninstallExitCodes.ElevationRequiredCode } => UiText.Key("Uninstall.Run.Result.Elevation"),
        { Retryable: true } => UiText.Key("Uninstall.Run.Result.Retry"),
        { DiagnosticCode: UninstallSession.StillPresent } => UiText.Key("Uninstall.Run.Result.StillPresent"),
        _ => UiText.Key("Uninstall.Run.Result.Failed", report.DiagnosticCode)
    };

    public void RefreshText() => Raise(string.Empty);

    private void Start()
    {
        if (CanStart)
        {
            Phase = OfficialUninstallPhase.Confirming;
        }
    }

    private void Cancel()
    {
        if (IsConfirming)
        {
            CloseAppsConsent = false;
            Phase = OfficialUninstallPhase.Ready;
        }
    }

    private void OnStage(UninstallStage stage)
    {
        if (_phase != OfficialUninstallPhase.Running)
        {
            return;
        }

        _stage = stage;
        Raise(nameof(StageText));
    }

    private void Raise(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Progress reported on the UI dispatcher, so the page never updates from a worker thread.</summary>
    private sealed class DispatchedProgress(OfficialUninstallViewModel owner) : IProgress<UninstallProgressEvent>
    {
        public void Report(UninstallProgressEvent value) => owner._dispatcher.Post(() => owner.OnStage(value.Stage));
    }
}
