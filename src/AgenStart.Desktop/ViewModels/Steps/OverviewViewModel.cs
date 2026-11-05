using AgenStart.Core.Machine;
using AgenStart.Desktop.Services;

namespace AgenStart.Desktop.ViewModels.Steps;

public sealed class OverviewViewModel : StepViewModelBase
{
    private static readonly UiText NotAnalysed = UiText.Key("Common.NotAnalysed");
    private static readonly UiText Unknown = UiText.Key("Common.Unknown");
    private static readonly UiText PrivacyNote = UiText.Key("Overview.Diagnostic.Privacy");

    private readonly IMachineInventoryProvider _machineInventory;
    private readonly ISessionState _session;

    private MachineSnapshot? _snapshot;
    private bool _hasSnapshot;
    private bool _isBusy;
    private UiText _statusTitle = UiText.Key("Overview.Status.Ready.Title");
    private UiText _statusMessage = UiText.Key("Overview.Status.Ready.Message");
    private UiText _operatingSystem = NotAnalysed;
    private UiText _cpu = NotAnalysed;
    private UiText _memory = NotAnalysed;
    private UiText _gpu = NotAnalysed;
    private UiText _systemDrive = NotAnalysed;
    private UiText _freeSpace = NotAnalysed;
    private UiText _architecture = NotAnalysed;
    private UiText _winGet = NotAnalysed;
    private UiText _winGetVersion = NotAnalysed;
    private UiText _analysisDiagnostic = PrivacyNote;

    public OverviewViewModel(IMachineInventoryProvider machineInventory, ISessionState session, ILocalizer? text = null)
        : base(text)
    {
        _machineInventory = machineInventory;
        _session = session;
    }

    public MachineSnapshot? Snapshot => _snapshot;

    public bool HasSnapshot
    {
        get => _hasSnapshot;
        private set => SetField(ref _hasSnapshot, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public string StatusTitle => _statusTitle.Resolve(Text);

    public string StatusMessage => _statusMessage.Resolve(Text);

    public string OperatingSystem => _operatingSystem.Resolve(Text);

    public string Cpu => _cpu.Resolve(Text);

    public string Memory => _memory.Resolve(Text);

    public string Gpu => _gpu.Resolve(Text);

    public string SystemDrive => _systemDrive.Resolve(Text);

    public string FreeSpace => _freeSpace.Resolve(Text);

    public string Architecture => _architecture.Resolve(Text);

    public string WinGet => _winGet.Resolve(Text);

    public string WinGetVersion => _winGetVersion.Resolve(Text);

    public string AnalysisDiagnostic => _analysisDiagnostic.Resolve(Text);

    public async Task AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || _session.IsInstalling)
        {
            return;
        }

        IsBusy = true;
        SetStatus("Overview.Status.Analysing.Title", "Overview.Status.Analysing.Message");

        try
        {
            var snapshot = await _machineInventory.CaptureAsync(cancellationToken).ConfigureAwait(true);
            ApplySnapshot(snapshot);
            _session.SetSnapshot(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus("Overview.Status.Cancelled.Title", "Overview.Status.Cancelled.Message");
        }
        catch (Exception)
        {
            SetStatus("Overview.Status.Failed.Title", "Overview.Status.Failed.Message");
            SetText(ref _analysisDiagnostic, UiText.Key("Overview.Diagnostic.Retry"), nameof(AnalysisDiagnostic));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ApplySnapshot(MachineSnapshot snapshot)
    {
        _snapshot = snapshot;
        HasSnapshot = snapshot.Platform.Kind == PlatformKind.Windows;

        _operatingSystem = snapshot.Platform.Edition is { } edition
            ? UiText.Raw(edition)
            : snapshot.Platform.Kind == PlatformKind.Windows ? UiText.Raw("Windows") : UiText.Key("Overview.UnsupportedPlatform");
        _cpu = snapshot.Cpu.Model is { } model ? UiText.Raw(model) : UiText.Key("Overview.CpuDetected");
        _memory = FormatBytes(snapshot.Memory.TotalPhysicalBytes);
        _gpu = snapshot.Gpus.FirstOrDefault()?.Name is { } gpu ? UiText.Raw(gpu) : Unknown;

        var systemDrive = snapshot.SystemDrive;
        _systemDrive = systemDrive is null
            ? Unknown
            : UiText.Key("Overview.SystemDriveSize", FormatBytes(systemDrive.TotalBytes));
        _freeSpace = systemDrive is null
            ? Unknown
            : UiText.Key("Overview.FreeSpace", FormatBytes(systemDrive.AvailableBytes));

        _architecture = UiText.Key(snapshot.Platform.Architecture switch
        {
            MachineArchitecture.X64 => "Overview.Architecture.X64",
            MachineArchitecture.X86 => "Overview.Architecture.X86",
            MachineArchitecture.Arm64 => "Overview.Architecture.Arm64",
            _ => "Overview.Architecture.Unknown"
        });
        _winGet = snapshot.PackageManager.State == CapabilityState.Available
            ? UiText.Key("Overview.WinGetAvailable")
            : UiText.Raw(snapshot.PackageManager.State.ToString());
        _winGetVersion = snapshot.PackageManager.Version?.ToString() is { } version ? UiText.Raw(version) : Unknown;

        _analysisDiagnostic = snapshot.Diagnostics.Count == 0
            ? PrivacyNote
            : UiText.Plural("Overview.Diagnostic.Notes", snapshot.Diagnostics.Count);

        // One refresh for every text property, then the status pair.
        OnPropertyChanged(string.Empty);
        if (snapshot.Platform.Kind == PlatformKind.Windows)
        {
            SetStatus("Overview.Status.PcReady.Title", "Overview.Status.PcReady.Message");
        }
        else
        {
            SetStatus("Overview.Status.Unsupported.Title", "Overview.Status.Unsupported.Message");
        }
    }

    private void SetStatus(string titleKey, string messageKey)
    {
        SetText(ref _statusTitle, UiText.Key(titleKey), nameof(StatusTitle));
        SetText(ref _statusMessage, UiText.Key(messageKey), nameof(StatusMessage));
    }

    private static UiText FormatBytes(ulong? bytes) => bytes is null ? Unknown : Gigabytes(bytes.Value);

    private static UiText FormatBytes(long? bytes) => bytes is null or < 0 ? Unknown : Gigabytes(bytes.Value);

    private static UiText Gigabytes(double bytes) =>
        UiText.Key("Units.Gigabytes", Math.Round(bytes / (1024d * 1024d * 1024d)));
}
