using AgenStart.Application.Installation;
using AgenStart.Desktop.Services;

namespace AgenStart.Desktop.ViewModels;

/// <summary>One installation row as the queue status needs it, in queue order.</summary>
public readonly record struct InstallationQueueEntry(
    string Name,
    InstallationQueueItemState State,
    InstallationItemActivity Activity,
    bool Silent);

/// <summary>
/// Pure wording of the installation queue: which package and phase the header names, which earlier
/// package a ready one waits for, and how elapsed time and download progress read. Packages are
/// downloaded ahead in parallel but installed one at a time in queue order, so a ready package waits
/// for the first earlier package that is not finished yet.
/// </summary>
public static class InstallationQueueStatus
{
    /// <summary>
    /// The header for the current queue state, or <see langword="null"/> when nothing is active and
    /// the caller should keep its current header. Installing outranks verifying, which outranks
    /// downloading (first in queue order), which outranks resolving.
    /// </summary>
    public static UiText? Headline(IReadOnlyList<InstallationQueueEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (FirstActive(entries, InstallationItemActivity.Installing) is { } installing)
        {
            return UiText.Key(
                installing.Silent ? "Installation.Current.InstallingSilent" : "Installation.Current.Installing",
                installing.Name);
        }

        if (FirstActive(entries, InstallationItemActivity.Verifying) is { } verifying)
        {
            return UiText.Key("Installation.Current.Verifying", verifying.Name);
        }

        if (FirstActive(entries, InstallationItemActivity.Downloading) is { } downloading)
        {
            return UiText.Key("Installation.Current.Downloading", downloading.Name);
        }

        if (FirstActive(entries, InstallationItemActivity.Resolving) is { } resolving)
        {
            return UiText.Key("Installation.Current.Resolving", resolving.Name);
        }

        return null;
    }

    /// <summary>
    /// The name of the package that a ready package at <paramref name="index"/> waits for, or
    /// <see langword="null"/> when it is not ready or nothing earlier in the queue is still pending.
    /// </summary>
    public static string? WaitingFor(IReadOnlyList<InstallationQueueEntry> entries, int index)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, entries.Count);

        var entry = entries[index];
        if (entry.Activity != InstallationItemActivity.Ready || !IsPending(entry.State))
        {
            return null;
        }

        for (var earlier = 0; earlier < index; earlier++)
        {
            if (IsPending(entries[earlier].State))
            {
                return entries[earlier].Name;
            }
        }

        return null;
    }

    /// <summary>Whether a row is in a phase that is still moving (a bar and elapsed time are shown).</summary>
    public static bool IsActive(InstallationQueueItemState state, InstallationItemActivity activity) =>
        IsPending(state) && activity is
            InstallationItemActivity.Resolving or
            InstallationItemActivity.Downloading or
            InstallationItemActivity.Ready or
            InstallationItemActivity.Installing or
            InstallationItemActivity.Verifying;

    /// <summary>Download progress from 0 to 100 when the total size is known, otherwise <see langword="null"/>.</summary>
    public static double? DownloadPercent(long? downloaded, long? required)
    {
        if (downloaded is null || required is not > 0)
        {
            return null;
        }

        return Math.Clamp(downloaded.Value * 100d / required.Value, 0, 100);
    }

    /// <summary>Elapsed time as seconds under a minute, then minutes and two-digit seconds.</summary>
    public static UiText FormatElapsed(TimeSpan elapsed)
    {
        var seconds = (long)Math.Max(0, Math.Floor(elapsed.TotalSeconds));
        return seconds < 60
            ? UiText.Key("Units.DurationSeconds", seconds)
            : UiText.Key("Units.DurationMinutes", seconds / 60, seconds % 60);
    }

    private static InstallationQueueEntry? FirstActive(
        IReadOnlyList<InstallationQueueEntry> entries,
        InstallationItemActivity activity)
    {
        foreach (var entry in entries)
        {
            if (entry.Activity == activity && IsPending(entry.State))
            {
                return entry;
            }
        }

        return null;
    }

    private static bool IsPending(InstallationQueueItemState state) =>
        state is InstallationQueueItemState.Queued or InstallationQueueItemState.Running;
}
