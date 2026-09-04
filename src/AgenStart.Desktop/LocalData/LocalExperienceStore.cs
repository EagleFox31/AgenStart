using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgenStart.Desktop.LocalData;

public sealed record DesktopSettings(bool AnalyzeOnStartup = true);

public sealed record SetupHistoryApplication(
    string ApplicationId,
    string Name,
    string Result,
    string? InstalledVersion);

public sealed record SetupHistoryEntry(
    Guid Id,
    DateTimeOffset CompletedAtUtc,
    string Profile,
    int ProcessedCount,
    int InstalledCount,
    int AlreadyInstalledCount,
    int FailedCount,
    int SkippedCount,
    int CancelledCount,
    bool RequiresReboot,
    IReadOnlyList<SetupHistoryApplication> Applications);

public sealed class LocalExperienceStore
{
    private const int MaxHistoryEntries = 50;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _directoryPath;
    private readonly string _settingsPath;
    private readonly string _historyPath;

    public LocalExperienceStore(string? baseDirectory = null)
    {
        _directoryPath = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgenStart");
        _settingsPath = Path.Combine(_directoryPath, "settings.json");
        _historyPath = Path.Combine(_directoryPath, "history.json");
    }

    public async Task<DesktopSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new DesktopSettings();
            }

            await using var stream = File.OpenRead(_settingsPath);
            return await JsonSerializer.DeserializeAsync<DesktopSettings>(stream, _jsonOptions, cancellationToken)
                .ConfigureAwait(false) ?? new DesktopSettings();
        }
        catch (JsonException)
        {
            return new DesktopSettings();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveSettingsAsync(DesktopSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directoryPath);
            await WriteAtomicAsync(_settingsPath, settings, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<SetupHistoryEntry>> LoadHistoryAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadHistoryUnsafeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertHistoryAsync(SetupHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directoryPath);
            var entries = (await LoadHistoryUnsafeAsync(cancellationToken).ConfigureAwait(false)).ToList();
            entries.RemoveAll(existing => existing.Id == entry.Id);
            entries.Add(entry);
            var ordered = entries
                .OrderByDescending(item => item.CompletedAtUtc)
                .Take(MaxHistoryEntries)
                .ToArray();
            await WriteAtomicAsync(_historyPath, ordered, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_historyPath))
            {
                File.Delete(_historyPath);
            }

            if (File.Exists(_settingsPath))
            {
                File.Delete(_settingsPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<SetupHistoryEntry>> LoadHistoryUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_historyPath))
        {
            return Array.Empty<SetupHistoryEntry>();
        }

        try
        {
            await using var stream = File.OpenRead(_historyPath);
            return await JsonSerializer.DeserializeAsync<SetupHistoryEntry[]>(stream, _jsonOptions, cancellationToken)
                .ConfigureAwait(false) ?? Array.Empty<SetupHistoryEntry>();
        }
        catch (JsonException)
        {
            return Array.Empty<SetupHistoryEntry>();
        }
    }

    private async Task WriteAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var tempPath = path + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 16_384, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, value, _jsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);
    }
}
