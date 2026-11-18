using System.Security;
using AgenStart.Uninstall;

namespace AgenStart.Platform.Windows.Uninstall;

/// <summary>
/// Reads file-system metadata without following links and without opening file contents, so a
/// OneDrive placeholder is never downloaded. Nothing is created, written, moved or deleted.
/// </summary>
public sealed class WindowsFileSystemProbe : IFileSystemProbe
{
    // FILE_ATTRIBUTE_RECALL_ON_OPEN and FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS (cloud files on demand).
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    private static readonly EnumerationOptions ChildrenOnly = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false
    };

    public FileSystemEntry? GetEntry(string path)
    {
        if (!OperatingSystem.IsWindows() || WindowsPath.Normalize(path) is not { } normalized)
        {
            return null;
        }

        try
        {
            var attributes = File.GetAttributes(normalized);
            FileSystemInfo info = (attributes & FileAttributes.Directory) != 0
                ? new DirectoryInfo(normalized)
                : new FileInfo(normalized);
            return ToEntry(info, normalized);
        }
        catch (Exception exception) when (IsExpectedReadFailure(exception))
        {
            return null;
        }
    }

    public IReadOnlyList<FileSystemEntry> ListChildren(string directoryPath)
    {
        if (GetEntry(directoryPath) is not { Kind: FileSystemEntryKind.Directory, IsReparsePoint: false } directory)
        {
            return [];
        }

        try
        {
            return new DirectoryInfo(directory.Path)
                .EnumerateFileSystemInfos("*", ChildrenOnly)
                .Select(info => ToEntry(info, Path.Combine(directory.Path, info.Name)))
                .ToList();
        }
        catch (Exception exception) when (IsExpectedReadFailure(exception))
        {
            return [];
        }
    }

    private static FileSystemEntry ToEntry(FileSystemInfo info, string path)
    {
        var attributes = info.Attributes;
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        return new FileSystemEntry(
            path,
            isDirectory ? FileSystemEntryKind.Directory : FileSystemEntryKind.File,
            isDirectory ? 0 : ((FileInfo)info).Length,
            (attributes & FileAttributes.ReparsePoint) != 0,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            (attributes & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0);
    }

    private static bool IsExpectedReadFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException;
}
