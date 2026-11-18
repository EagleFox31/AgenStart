namespace AgenStart.Uninstall;

/// <summary>
/// Folders the safety floor and the rules refer to. Each one is resolved at runtime by
/// <see cref="IKnownFolderResolver"/>; no path is ever hard-coded.
/// </summary>
public enum KnownFolder
{
    UserProfile = 1,
    UserProfiles,
    LocalAppData,
    RoamingAppData,
    LocalLow,
    ProgramData,
    Documents,
    Desktop,
    Downloads,
    SavedGames,
    StartMenuPrograms,
    CommonStartMenuPrograms,
    PublicDesktop,
    PublicDocuments,
    Temp,
    Windows,
    ProgramFiles,
    ProgramFilesX86,
    CommonFiles,
    CommonFilesX86,
    OneDrive,
    OneDriveConsumer,
    OneDriveCommercial
}

/// <summary>Read port: where each known folder lives for the current user (redirection and localisation included).</summary>
public interface IKnownFolderResolver
{
    /// <summary>The folder's absolute path, or null when it does not exist on this machine.</summary>
    string? Resolve(KnownFolder folder);
}

public enum FileSystemEntryKind
{
    File = 1,
    Directory = 2
}

/// <summary>
/// One file-system entry as seen without following links. <see cref="IsReparsePoint"/> covers
/// junctions, symbolic links and mount points; <see cref="IsCloudPlaceholder"/> covers OneDrive
/// files on demand.
/// </summary>
public sealed record FileSystemEntry(
    string Path,
    FileSystemEntryKind Kind,
    long SizeBytes,
    bool IsReparsePoint,
    DateTimeOffset? LastWriteTimeUtc,
    bool IsCloudPlaceholder = false);

/// <summary>
/// Read port over the file system. It has no write method by design, and it never follows a reparse
/// point: listing a junction returns nothing below it.
/// </summary>
public interface IFileSystemProbe
{
    /// <summary>The entry at <paramref name="path"/>, or null when nothing is there or it cannot be read.</summary>
    FileSystemEntry? GetEntry(string path);

    /// <summary>Direct children of a directory; empty for a file, a reparse point or an unreadable folder.</summary>
    IReadOnlyList<FileSystemEntry> ListChildren(string directoryPath);
}

/// <summary>A registry key as seen by <see cref="IRegistryProbe"/>.</summary>
public sealed record RegistryKeyEntry(string Path, int SubKeyCount, int ValueCount);

/// <summary>Read port over the registry. Keys are only ever opened for reading.</summary>
public interface IRegistryProbe
{
    /// <summary>The key at <paramref name="keyPath"/> (<c>HKCU\…</c> or <c>HKLM\…</c>), or null when it is absent or unreadable.</summary>
    RegistryKeyEntry? GetKey(string keyPath);
}

/// <summary>Read port: the target of a <c>.lnk</c> shortcut file.</summary>
public interface IShortcutResolver
{
    /// <summary>The absolute target path, or null when the shortcut cannot be read or has no file target.</summary>
    string? ResolveTarget(string shortcutPath);
}

/// <summary>A running process, identified only for display: nothing can act on it through this record.</summary>
public sealed record RunningProcess(int ProcessId, string Name, string? ImagePath);

/// <summary>Read port: the processes running for this session. It cannot close, signal or kill anything.</summary>
public interface IProcessInspector
{
    IReadOnlyList<RunningProcess> ListProcesses();
}
