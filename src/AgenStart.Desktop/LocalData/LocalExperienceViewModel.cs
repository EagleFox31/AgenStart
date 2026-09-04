using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AgenStart.Desktop.LocalData;

public sealed class SetupHistoryRowViewModel
{
    public SetupHistoryRowViewModel(SetupHistoryEntry entry)
    {
        Entry = entry;
    }

    public SetupHistoryEntry Entry { get; }
    public Guid Id => Entry.Id;
    public string Profile => Entry.Profile;
    public int ProcessedCount => Entry.ProcessedCount;
    public int InstalledCount => Entry.InstalledCount;
    public int AlreadyInstalledCount => Entry.AlreadyInstalledCount;
    public int FailedCount => Entry.FailedCount;
    public int SkippedCount => Entry.SkippedCount;
    public int CancelledCount => Entry.CancelledCount;
    public bool RequiresReboot => Entry.RequiresReboot;
    public IReadOnlyList<SetupHistoryApplication> Applications => Entry.Applications;
    public string CompletedAt => Entry.CompletedAtUtc.ToLocalTime().ToString("MMM d, yyyy · HH:mm");
    public string Outcome => FailedCount > 0
        ? $"Completed with {FailedCount} issue{(FailedCount == 1 ? string.Empty : "s")}"
        : CancelledCount > 0
            ? $"Cancelled · {CancelledCount} item{(CancelledCount == 1 ? string.Empty : "s")} not completed"
            : SkippedCount > 0
                ? $"Completed · {SkippedCount} skipped"
                : "Completed successfully";
    public string ApplicationSummary => $"{ProcessedCount} application{(ProcessedCount == 1 ? string.Empty : "s")} processed";
}

public sealed class LocalExperienceViewModel : INotifyPropertyChanged
{
    private readonly LocalExperienceStore _store;
    private SetupHistoryRowViewModel? _selectedHistory;
    private bool _analyzeOnStartup = true;
    private bool _clearConfirmationVisible;
    private string _storageStatus = "Stored only on this device.";
    private bool _initialized;

    public LocalExperienceViewModel(LocalExperienceStore? store = null)
    {
        _store = store ?? new LocalExperienceStore();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SetupHistoryRowViewModel> HistoryEntries { get; } = [];

    public SetupHistoryRowViewModel? SelectedHistory
    {
        get => _selectedHistory;
        set => SetField(ref _selectedHistory, value);
    }

    public bool AnalyzeOnStartup
    {
        get => _analyzeOnStartup;
        set
        {
            if (!SetField(ref _analyzeOnStartup, value))
            {
                return;
            }

            _ = SaveSettingsSafeAsync();
        }
    }

    public bool ClearConfirmationVisible
    {
        get => _clearConfirmationVisible;
        private set => SetField(ref _clearConfirmationVisible, value);
    }

    public string StorageStatus
    {
        get => _storageStatus;
        private set => SetField(ref _storageStatus, value);
    }

    public bool HasHistory => HistoryEntries.Count > 0;
    public bool TelemetryAvailable => false;
    public bool ExportManagementAvailable => false;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        var settings = await _store.LoadSettingsAsync(cancellationToken).ConfigureAwait(true);
        _analyzeOnStartup = settings.AnalyzeOnStartup;
        OnPropertyChanged(nameof(AnalyzeOnStartup));
        await ReloadHistoryAsync(cancellationToken).ConfigureAwait(true);
        _initialized = true;
    }

    public async Task RecordSessionAsync(
        Guid id,
        string profile,
        int processedCount,
        int installedCount,
        int alreadyInstalledCount,
        int failedCount,
        int skippedCount,
        int cancelledCount,
        bool requiresReboot,
        IEnumerable<SetupHistoryApplication> applications,
        CancellationToken cancellationToken = default)
    {
        var safeApplications = applications
            .Where(item => !string.IsNullOrWhiteSpace(item.ApplicationId) && !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => item with
            {
                ApplicationId = item.ApplicationId.Trim(),
                Name = item.Name.Trim(),
                Result = item.Result.Trim(),
                InstalledVersion = string.IsNullOrWhiteSpace(item.InstalledVersion) || item.InstalledVersion == "—"
                    ? null
                    : item.InstalledVersion.Trim()
            })
            .ToArray();

        var entry = new SetupHistoryEntry(
            id,
            DateTimeOffset.UtcNow,
            string.IsNullOrWhiteSpace(profile) ? "Unknown" : profile.Trim(),
            processedCount,
            installedCount,
            alreadyInstalledCount,
            failedCount,
            skippedCount,
            cancelledCount,
            requiresReboot,
            safeApplications);

        await _store.UpsertHistoryAsync(entry, cancellationToken).ConfigureAwait(true);
        await ReloadHistoryAsync(cancellationToken).ConfigureAwait(true);
    }

    public void BeginClearLocalData() => ClearConfirmationVisible = true;

    public void CancelClearLocalData() => ClearConfirmationVisible = false;

    public async Task ConfirmClearLocalDataAsync(CancellationToken cancellationToken = default)
    {
        await _store.ClearAsync(cancellationToken).ConfigureAwait(true);
        HistoryEntries.Clear();
        SelectedHistory = null;
        _analyzeOnStartup = true;
        OnPropertyChanged(nameof(AnalyzeOnStartup));
        OnPropertyChanged(nameof(HasHistory));
        ClearConfirmationVisible = false;
        StorageStatus = "Local AgenStart history and preferences were cleared.";
    }

    private async Task ReloadHistoryAsync(CancellationToken cancellationToken)
    {
        var selectedId = SelectedHistory?.Id;
        var entries = await _store.LoadHistoryAsync(cancellationToken).ConfigureAwait(true);
        HistoryEntries.Clear();
        foreach (var entry in entries.OrderByDescending(item => item.CompletedAtUtc))
        {
            HistoryEntries.Add(new SetupHistoryRowViewModel(entry));
        }

        SelectedHistory = selectedId is null
            ? HistoryEntries.FirstOrDefault()
            : HistoryEntries.FirstOrDefault(item => item.Id == selectedId) ?? HistoryEntries.FirstOrDefault();
        OnPropertyChanged(nameof(HasHistory));
    }

    private async Task SaveSettingsSafeAsync()
    {
        try
        {
            await _store.SaveSettingsAsync(new DesktopSettings(AnalyzeOnStartup)).ConfigureAwait(false);
            StorageStatus = "Preferences saved locally.";
        }
        catch (IOException)
        {
            StorageStatus = "AgenStart could not save the local preference.";
        }
        catch (UnauthorizedAccessException)
        {
            StorageStatus = "AgenStart does not have access to its local settings folder.";
        }
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
