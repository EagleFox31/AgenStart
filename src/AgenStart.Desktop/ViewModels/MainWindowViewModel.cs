using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using AgenStart.Catalogue;
using AgenStart.Core.Catalogue;
using AgenStart.Core.Machine;
using AgenStart.Platform.Windows.Inventory;
using AgenStart.Platform.Windows.SoftwareInventory;
using AgenStart.Recommendations;
using AgenStart.SoftwareInventory;

namespace AgenStart.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IMachineInventoryProvider _machineInventory;
    private readonly IInstalledSoftwareInventoryProvider _softwareInventory;
    private readonly SoftwareStateResolver _softwareStateResolver;
    private readonly RecommendationEngine _recommendationEngine;
    private readonly SoftwareCatalogueLoader _catalogueLoader;

    private MachineSnapshot? _machineSnapshot;
    private bool _isBusy;
    private bool _hasSnapshot;
    private bool _hasRecommendations;
    private UserProfile _selectedProfile = UserProfile.Development;
    private string _recommendationStatus = "Choose a usage profile to build recommendations.";
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

    public MainWindowViewModel(
        IMachineInventoryProvider? machineInventory = null,
        IInstalledSoftwareInventoryProvider? softwareInventory = null,
        SoftwareStateResolver? softwareStateResolver = null,
        RecommendationEngine? recommendationEngine = null,
        SoftwareCatalogueLoader? catalogueLoader = null)
    {
        _machineInventory = machineInventory ?? new WindowsMachineInventoryProvider();
        _softwareInventory = softwareInventory ?? new WindowsInstalledSoftwareInventoryProvider();
        _softwareStateResolver = softwareStateResolver ?? new SoftwareStateResolver();
        _recommendationEngine = recommendationEngine ?? new RecommendationEngine();
        _catalogueLoader = catalogueLoader ?? new SoftwareCatalogueLoader();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RecommendationRowViewModel> Recommendations { get; } = [];

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

    public bool HasRecommendations
    {
        get => _hasRecommendations;
        private set => SetField(ref _hasRecommendations, value);
    }

    public UserProfile SelectedProfile
    {
        get => _selectedProfile;
        private set
        {
            if (!SetField(ref _selectedProfile, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedProfileName));
            OnPropertyChanged(nameof(SelectedProfileDescription));
        }
    }

    public string SelectedProfileName => SelectedProfile.ToString();

    public string SelectedProfileDescription => SelectedProfile switch
    {
        UserProfile.Personal => "Everyday apps, browsing, communication and media.",
        UserProfile.Development => "Code, databases, terminals and developer tools.",
        UserProfile.Business => "Office, communication, productivity and collaboration.",
        UserProfile.Creation => "Design, media, content and creative workflows.",
        UserProfile.Training => "Learning, course tools and guided study setups.",
        _ => string.Empty
    };

    public string RecommendationStatus
    {
        get => _recommendationStatus;
        private set => SetField(ref _recommendationStatus, value);
    }

    public int RecommendationCount => Recommendations.Count;
    public int SelectedCount => Recommendations.Count(row => row.IsSelected);
    public int AlreadyInstalledCount => Recommendations.Count(row => row.Disposition == RecommendationDisposition.AlreadyInstalled);
    public int OptionalCount => Recommendations.Count(row => row.Level == RecommendationLevel.Optional && row.Disposition == RecommendationDisposition.Recommended);

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

    public void SelectProfile(UserProfile profile)
    {
        if (SelectedProfile == profile)
        {
            return;
        }

        SelectedProfile = profile;
        ClearRecommendations();
        RecommendationStatus = "Profile changed. Build recommendations when you're ready.";
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

    public async Task BuildRecommendationsAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || _machineSnapshot is null)
        {
            return;
        }

        IsBusy = true;
        RecommendationStatus = "Checking installed software and building recommendations…";

        try
        {
            var cataloguePath = Path.Combine(AppContext.BaseDirectory, "Data", "catalogue.json");
            using var catalogueStream = File.OpenRead(cataloguePath);
            var catalogue = _catalogueLoader.Load(catalogueStream);

            var installedSoftware = await _softwareInventory
                .CaptureAsync(cancellationToken)
                .ConfigureAwait(true);
            var softwareState = _softwareStateResolver.Resolve(catalogue.DetectionTargets, installedSoftware);
            var plan = _recommendationEngine.Build(new RecommendationRequest(
                SelectedProfile,
                _machineSnapshot,
                softwareState,
                catalogue.Definitions));

            ClearRecommendations();
            var metadata = catalogue.Applications.ToDictionary(
                application => application.Id,
                StringComparer.OrdinalIgnoreCase);

            foreach (var decision in plan.Decisions)
            {
                var description = metadata.TryGetValue(decision.ApplicationId, out var application)
                    ? application.Description
                    : string.Empty;
                var row = new RecommendationRowViewModel(decision, description);
                row.PropertyChanged += RecommendationRow_OnPropertyChanged;
                Recommendations.Add(row);
            }

            HasRecommendations = true;
            RecommendationStatus = $"Based on your {SelectedProfileName} profile, machine capabilities and software already installed.";
            RaiseRecommendationSummary();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RecommendationStatus = "Recommendation analysis cancelled.";
        }
        catch (Exception)
        {
            ClearRecommendations();
            RecommendationStatus = "AgenStart could not build recommendations from the local catalogue and inventory.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SelectEssentialsOnly()
    {
        foreach (var row in Recommendations.Where(row => row.CanSelect))
        {
            row.IsSelected = row.Level == RecommendationLevel.Essential;
        }

        RaiseRecommendationSummary();
    }

    private void RecommendationRow_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecommendationRowViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedCount));
        }
    }

    private void ClearRecommendations()
    {
        foreach (var row in Recommendations)
        {
            row.PropertyChanged -= RecommendationRow_OnPropertyChanged;
        }

        Recommendations.Clear();
        HasRecommendations = false;
        RaiseRecommendationSummary();
    }

    private void RaiseRecommendationSummary()
    {
        OnPropertyChanged(nameof(RecommendationCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(AlreadyInstalledCount));
        OnPropertyChanged(nameof(OptionalCount));
    }

    private void ApplySnapshot(MachineSnapshot snapshot)
    {
        _machineSnapshot = snapshot;
        HasSnapshot = snapshot.Platform.Kind == PlatformKind.Windows;
        ClearRecommendations();

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
            RecommendationStatus = "Choose a usage profile to build recommendations.";
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

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
