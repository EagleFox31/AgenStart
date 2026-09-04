using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using AgenStart.Core.Machine;
using AgenStart.Platform.Windows.Inventory;

namespace AgenStart.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IMachineInventoryProvider _machineInventory;
    private bool _isBusy;
    private bool _hasSnapshot;
    private string _statusTitle = "Ready for analysis";
    private string _statusMessage = "AgenStart can inspect the essential capabilities of this PC locally.";
    private string _operatingSystem = "Not analysed";
    private string _cpu = "Not analysed";
    private string _memory = "Not analysed";
    private string _gpu = "Not analysed";
    private string _systemDrive = "Not analysed";
    private string _freeSpace = "Not analysed";
    private string _architecture = "Not analysed";
    private string _winGet = "Not analysed";
    private string _winGetVersion = "Not analysed";
    private string _analysisDiagnostic = "No personal files or account information are inspected.";

    public MainWindowViewModel(IMachineInventoryProvider? machineInventory = null)
    {
        _machineInventory = machineInventory ?? new WindowsMachineInventoryProvider();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public bool HasSnapshot
    {
        get => _hasSnapshot;
        private set => SetField(ref _hasSnapshot, value);
    }

    public string StatusTitle
    {
        get => _statusTitle;
        private set => SetField(ref _statusTitle, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string OperatingSystem
    {
        get => _operatingSystem;
        private set => SetField(ref _operatingSystem, value);
    }

    public string Cpu
    {
        get => _cpu;
        private set => SetField(ref _cpu, value);
    }

    public string Memory
    {
        get => _memory;
        private set => SetField(ref _memory, value);
    }

    public string Gpu
    {
        get => _gpu;
        private set => SetField(ref _gpu, value);
    }

    public string SystemDrive
    {
        get => _systemDrive;
        private set => SetField(ref _systemDrive, value);
    }

    public string FreeSpace
    {
        get => _freeSpace;
        private set => SetField(ref _freeSpace, value);
    }

    public string Architecture
    {
        get => _architecture;
        private set => SetField(ref _architecture, value);
    }

    public string WinGet
    {
        get => _winGet;
        private set => SetField(ref _winGet, value);
    }

    public string WinGetVersion
    {
        get => _winGetVersion;
        private set => SetField(ref _winGetVersion, value);
    }

    public string AnalysisDiagnostic
    {
        get => _analysisDiagnostic;
        private set => SetField(ref _analysisDiagnostic, value);
    }

    public async Task AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusTitle = "Analysing this PC";
        StatusMessage = "Reading essential system capabilities locally…";

        try
        {
            var snapshot = await _machineInventory.CaptureAsync(cancellationToken).ConfigureAwait(true);
            ApplySnapshot(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusTitle = "Analysis cancelled";
            StatusMessage = "No changes were made to this PC.";
        }
        catch (Exception)
        {
            StatusTitle = "Analysis unavailable";
            StatusMessage = "AgenStart could not complete the local machine analysis.";
            AnalysisDiagnostic = "Try the analysis again. No machine changes were made.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplySnapshot(MachineSnapshot snapshot)
    {
        HasSnapshot = snapshot.Platform.Kind == PlatformKind.Windows;

        OperatingSystem = snapshot.Platform.Edition
            ?? (snapshot.Platform.Kind == PlatformKind.Windows ? "Windows" : "Unsupported platform");
        Cpu = snapshot.Cpu.Model ?? "CPU detected";
        Memory = FormatBytes(snapshot.Memory.TotalPhysicalBytes);
        Gpu = snapshot.Gpus.FirstOrDefault()?.Name ?? "Unknown";

        var systemDrive = snapshot.SystemDrive;
        SystemDrive = systemDrive is null
            ? "Unknown"
            : $"{FormatBytes(systemDrive.TotalBytes)} system drive";
        FreeSpace = systemDrive is null
            ? "Unknown"
            : $"{FormatBytes(systemDrive.AvailableBytes)} free";

        Architecture = FormatArchitecture(snapshot.Platform.Architecture);
        WinGet = snapshot.PackageManager.State == CapabilityState.Available
            ? "Available"
            : snapshot.PackageManager.State.ToString();
        WinGetVersion = snapshot.PackageManager.Version?.ToString() ?? "Unknown";

        if (snapshot.Platform.Kind == PlatformKind.Windows)
        {
            StatusTitle = "PC ready";
            StatusMessage = "Essential capabilities detected locally.";
        }
        else
        {
            StatusTitle = "Unsupported platform";
            StatusMessage = "The current AgenStart MVP supports Windows 10 and 11.";
        }

        AnalysisDiagnostic = snapshot.Diagnostics.Count == 0
            ? "No personal files or account information are inspected."
            : $"Analysis completed with {snapshot.Diagnostics.Count.ToString(CultureInfo.InvariantCulture)} diagnostic note(s). No personal files were inspected.";
    }

    private static string FormatArchitecture(MachineArchitecture architecture) => architecture switch
    {
        MachineArchitecture.X64 => "x64 architecture",
        MachineArchitecture.X86 => "x86 architecture",
        MachineArchitecture.Arm64 => "ARM64 architecture",
        _ => "Unknown architecture"
    };

    private static string FormatBytes(ulong? bytes)
    {
        if (bytes is null)
        {
            return "Unknown";
        }

        return FormatBytes((double)bytes.Value);
    }

    private static string FormatBytes(long? bytes)
    {
        if (bytes is null || bytes < 0)
        {
            return "Unknown";
        }

        return FormatBytes((double)bytes.Value);
    }

    private static string FormatBytes(double bytes)
    {
        const double gib = 1024d * 1024d * 1024d;
        return $"{Math.Round(bytes / gib):0} GB";
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
