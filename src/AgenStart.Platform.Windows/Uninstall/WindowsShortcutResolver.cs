using System.Security;
using AgenStart.Uninstall;

namespace AgenStart.Platform.Windows.Uninstall;

/// <summary>
/// Reads a <c>.lnk</c> file (at most <see cref="ShellLinkParser.MaximumLength"/> bytes, read-only,
/// shared) and parses its target without COM. A cloud placeholder is skipped so it is not downloaded.
/// </summary>
public sealed class WindowsShortcutResolver : IShortcutResolver
{
    private readonly IFileSystemProbe _files;

    public WindowsShortcutResolver()
        : this(new WindowsFileSystemProbe())
    {
    }

    public WindowsShortcutResolver(IFileSystemProbe files)
    {
        _files = files ?? throw new ArgumentNullException(nameof(files));
    }

    public string? ResolveTarget(string shortcutPath)
    {
        if (!OperatingSystem.IsWindows()
            || _files.GetEntry(shortcutPath) is not { Kind: FileSystemEntryKind.File, IsReparsePoint: false, IsCloudPlaceholder: false } entry
            || entry.SizeBytes > ShellLinkParser.MaximumLength)
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[Math.Min(stream.Length, ShellLinkParser.MaximumLength)];
            stream.ReadExactly(buffer);
            return ShellLinkParser.ReadTarget(buffer);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }
}
