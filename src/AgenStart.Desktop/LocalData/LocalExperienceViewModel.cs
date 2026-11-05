using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgenStart.Desktop.Services;

namespace AgenStart.Desktop.LocalData;

public sealed class SetupHistoryRowViewModel
{
    private readonly ILocalizer _text;

    public SetupHistoryRowViewModel(SetupHistoryEntry entry, ILocalizer? text = null)
    {
        Entry = entry;
        _text = text ?? AvaloniaLocalizationService.Shared;
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
    public string CompletedAt =>
        Entry.CompletedAtUtc.ToLocalTime().ToString(_text[TextKeys.HistoryDateFormat], _text.Culture);
    public string Outcome => FailedCount > 0
        ? _text.Plural(TextKeys.HistoryOutcomeFailed, FailedCount)
        : CancelledCount > 0
            ? _text.Plural(TextKeys.HistoryOutcomeCancelled, CancelledCount)
            : SkippedCount > 0
                ? _text.Plural(TextKeys.HistoryOutcomeSkipped, SkippedCount)
                : _text[TextKeys.HistoryOutcomeSuccess];
    public string ApplicationSummary => _text.Plural(TextKeys.HistoryApplicationSummary, ProcessedCount);
    public string InstalledSummary => _text.Plural(TextKeys.HistoryInstalledCount, InstalledCount);
    public string FailedSummary => _text.Plural(TextKeys.HistoryFailedCount, FailedCount);
}

/// <summary>A language as offered in Settings: always shown in its own language.</summary>
public sealed record LanguageOption(string Code, string NativeName)
{
    public override string ToString() => NativeName;
}

public sealed class LocalExperienceViewModel : INotifyPropertyChanged
{
    private readonly LocalExperienceStore _store;
    private readonly IThemeService? _themes;
    private readonly ILocalizationService? _localization;
    private readonly ILocalizer _text;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private DesktopSettings? _preloaded;
    private DesktopSettings _settings = new();
    private string _theme = AppThemes.Default;
    private SetupHistoryRowViewModel? _selectedHistory;
    private bool _analyzeOnStartup = true;
    private bool _clearConfirmationVisible;
    private string _storageStatusKey = TextKeys.SettingsStatusStoredOnDevice;
    private bool _initialized;

    /// <param name="store">Local settings and history; defaults to the per-user data folder.</param>
    /// <param name="themes">Applies palettes; null in tests.</param>
    /// <param name="localization">Applies the UI language; null in tests (text then reads the shared service).</param>
    /// <param name="initialSettings">Settings App already loaded at startup, so they are not read twice.</param>
    public LocalExperienceViewModel(
        LocalExperienceStore? store = null,
        IThemeService? themes = null,
        ILocalizationService? localization = null,
        DesktopSettings? initialSettings = null)
    {
        _store = store ?? new LocalExperienceStore();
        _themes = themes;
        _localization = localization;
        _text = (ILocalizer?)localization ?? AvaloniaLocalizationService.Shared;
        _preloaded = initialSettings;
        _text.LanguageChanged += OnLanguageChanged;
        LanguageOptions = AppLanguages.All
            .Select(code => new LanguageOption(code, AppLanguages.NativeName(code)))
            .ToArray();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SetupHistoryRowViewModel> HistoryEntries { get; } = [];

    /// <summary>The persisted settings as last loaded or saved; never rebuilt from a subset of fields.</summary>
    public DesktopSettings Settings => _settings;

    public SetupHistoryRowViewModel? SelectedHistory
    {
        get => _selectedHistory;
        set
        {
            if (SetField(ref _selectedHistory, value))
            {
                OnPropertyChanged(nameof(CanExportSelectedHistory));
            }
        }
    }

    public bool AnalyzeOnStartup
    {
        get => _analyzeOnStartup;
        set
        {
            if (SetField(ref _analyzeOnStartup, value))
            {
                _ = SaveSettingsSafeAsync(_settings with { AnalyzeOnStartup = value });
            }
        }
    }

    public IReadOnlyList<string> ThemeOptions => AppThemes.All;

    public string Theme
    {
        get => _theme;
        set
        {
            if (!SetField(ref _theme, AppThemes.Normalize(value)))
            {
                return;
            }

            _themes?.Apply(_theme);
            _ = SaveSettingsSafeAsync(_settings with { Theme = _theme });
        }
    }

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    public LanguageOption SelectedLanguage
    {
        get => LanguageOptions.FirstOrDefault(option => option.Code == _text.Language) ?? LanguageOptions[0];
        set
        {
            if (value is null || value.Code == _text.Language)
            {
                return;
            }

            _localization?.Apply(value.Code);
            OnPropertyChanged();
            _ = SaveSettingsSafeAsync(_settings with { Language = _text.Language });
        }
    }

    public bool OnboardingCompleted => _settings.OnboardingCompleted;

    public bool ClearConfirmationVisible
    {
        get => _clearConfirmationVisible;
        private set => SetField(ref _clearConfirmationVisible, value);
    }

    public string StorageStatus => _text[_storageStatusKey];

    public bool HasHistory => HistoryEntries.Count > 0;
    public bool CanExportSelectedHistory => SelectedHistory is not null;
    public bool TelemetryAvailable => false;
    public bool ExportManagementAvailable => false;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        var settings = _preloaded ?? await _store.LoadSettingsAsync(cancellationToken).ConfigureAwait(true);
        _preloaded = null;
        _settings = settings;
        _analyzeOnStartup = settings.AnalyzeOnStartup;
        OnPropertyChanged(nameof(AnalyzeOnStartup));
        SetThemeWithoutSaving(settings.Theme);
        if (settings.Language is not null)
        {
            _localization?.Apply(settings.Language);
        }

        OnPropertyChanged(nameof(SelectedLanguage));
        OnPropertyChanged(nameof(OnboardingCompleted));
        await ReloadHistoryAsync(cancellationToken).ConfigureAwait(true);
        _initialized = true;
    }

    /// <summary>Persists the end of the first-run journey together with the language it was done in.</summary>
    public async Task CompleteOnboardingAsync()
    {
        await SaveSettingsSafeAsync(_settings with { OnboardingCompleted = true, Language = _text.Language })
            .ConfigureAwait(true);
        OnPropertyChanged(nameof(OnboardingCompleted));
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
        // settings.json is gone, so the next launch is a first run; this session keeps its UI language.
        _settings = new DesktopSettings();
        HistoryEntries.Clear();
        SelectedHistory = null;
        _analyzeOnStartup = true;
        OnPropertyChanged(nameof(AnalyzeOnStartup));
        SetThemeWithoutSaving(AppThemes.Default);
        OnPropertyChanged(nameof(OnboardingCompleted));
        OnPropertyChanged(nameof(HasHistory));
        ClearConfirmationVisible = false;
        SetStorageStatus(TextKeys.SettingsStatusCleared);
    }

    private async Task ReloadHistoryAsync(CancellationToken cancellationToken)
    {
        var entries = await _store.LoadHistoryAsync(cancellationToken).ConfigureAwait(true);
        ShowHistory(entries);
    }

    private void ShowHistory(IEnumerable<SetupHistoryEntry> entries)
    {
        var selectedId = SelectedHistory?.Id;
        var ordered = entries.OrderByDescending(item => item.CompletedAtUtc).ToArray();
        HistoryEntries.Clear();
        foreach (var entry in ordered)
        {
            HistoryEntries.Add(new SetupHistoryRowViewModel(entry, _text));
        }

        SelectedHistory = selectedId is null
            ? HistoryEntries.FirstOrDefault()
            : HistoryEntries.FirstOrDefault(item => item.Id == selectedId) ?? HistoryEntries.FirstOrDefault();
        OnPropertyChanged(nameof(HasHistory));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Rows format their text when read: rebuilding them from the same entries re-renders it, no I/O.
        ShowHistory(HistoryEntries.Select(row => row.Entry).ToArray());
        OnPropertyChanged(nameof(StorageStatus));
        OnPropertyChanged(nameof(SelectedLanguage));
    }

    private void SetThemeWithoutSaving(string? theme)
    {
        _theme = AppThemes.Normalize(theme);
        _themes?.Apply(_theme);
        OnPropertyChanged(nameof(Theme));
    }

    private void SetStorageStatus(string key)
    {
        _storageStatusKey = key;
        OnPropertyChanged(nameof(StorageStatus));
    }

    /// <summary>Writes <paramref name="next"/> only when it differs from what is stored (idempotent).</summary>
    private async Task SaveSettingsSafeAsync(DesktopSettings next)
    {
        await _saveGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (next == _settings)
            {
                return;
            }

            await _store.SaveSettingsAsync(next).ConfigureAwait(true);
            _settings = next;
            SetStorageStatus(TextKeys.SettingsStatusSaved);
        }
        catch (IOException)
        {
            SetStorageStatus(TextKeys.SettingsStatusSaveFailed);
        }
        catch (UnauthorizedAccessException)
        {
            SetStorageStatus(TextKeys.SettingsStatusNoAccess);
        }
        finally
        {
            _saveGate.Release();
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
