using System.Runtime.InteropServices;
using AgenStart.Uninstall;

namespace AgenStart.Platform.Windows.Uninstall;

/// <summary>Which processes hold the given files. Returns null when the question cannot be answered.</summary>
public interface IFileHolderQuery
{
    IReadOnlyList<RunningProcess>? FindHolders(IReadOnlyList<string> filePaths);
}

/// <summary>
/// Read-only: lists the processes running the program's executables, found under its install
/// location without following links. Only <c>.exe</c> files are registered, so a shell extension
/// loaded by Explorer never makes Explorer a candidate for closing.
/// </summary>
public sealed class RestartManagerProcessDetector(IFileSystemProbe files, IFileHolderQuery? holders = null, int maxFiles = 64, int maxDepth = 3)
    : IRunningProcessDetector
{
    private readonly IFileSystemProbe _files = files ?? throw new ArgumentNullException(nameof(files));
    private readonly IFileHolderQuery _holders = holders ?? new RestartManagerFileHolderQuery();

    public IReadOnlyList<RunningProcess> Detect(InstalledProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var root = WindowsPath.Normalize(program.InstallLocation);
        if (root is null || _files.GetEntry(root) is not { Kind: FileSystemEntryKind.Directory, IsReparsePoint: false })
        {
            return [];
        }

        var executables = new List<string>();
        var pending = new Queue<(string Path, int Depth)>([(root, 0)]);
        while (pending.Count > 0 && executables.Count < maxFiles)
        {
            var (directory, depth) = pending.Dequeue();
            foreach (var entry in _files.ListChildren(directory).OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase))
            {
                if (entry.IsReparsePoint)
                {
                    continue;
                }

                if (entry.Kind == FileSystemEntryKind.Directory && depth < maxDepth)
                {
                    pending.Enqueue((entry.Path, depth + 1));
                }
                else if (entry.Kind == FileSystemEntryKind.File
                    && entry.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    && executables.Count < maxFiles)
                {
                    executables.Add(entry.Path);
                }
            }
        }

        if (executables.Count == 0)
        {
            return [];
        }

        return (_holders.FindHolders(executables) ?? [])
            .DistinctBy(process => process.ProcessId)
            .OrderBy(process => process.ProcessId)
            .ToArray();
    }
}

/// <summary>
/// Restart Manager query: start a session, register files, read the list, end the session. It never
/// calls <c>RmShutdown</c> or <c>RmRestart</c>.
/// </summary>
public sealed class RestartManagerFileHolderQuery : IFileHolderQuery
{
    private const int ErrorSuccess = 0;
    private const int ErrorMoreData = 234;
    private const int SessionKeyLength = 32;

    public IReadOnlyList<RunningProcess>? FindHolders(IReadOnlyList<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        if (!OperatingSystem.IsWindows() || filePaths.Count == 0)
        {
            return null;
        }

        var key = new char[SessionKeyLength + 1];
        if (RmStartSession(out var session, 0, key) != ErrorSuccess)
        {
            return null;
        }

        try
        {
            var paths = filePaths.ToArray();
            if (RmRegisterResources(session, (uint)paths.Length, paths, 0, IntPtr.Zero, 0, null) != ErrorSuccess)
            {
                return null;
            }

            uint count = 0;
            uint reasons = 0;
            var status = RmGetList(session, out var needed, ref count, null, ref reasons);
            if (status == ErrorSuccess)
            {
                return [];
            }

            if (status != ErrorMoreData)
            {
                return null;
            }

            var infos = new RmProcessInfo[needed];
            count = needed;
            if (RmGetList(session, out _, ref count, infos, ref reasons) != ErrorSuccess)
            {
                return null;
            }

            return infos.Take((int)count)
                .Select(info => new RunningProcess(info.Process.ProcessId, info.AppName, null))
                .ToArray();
        }
        finally
        {
            _ = RmEndSession(session);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int ProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string AppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string ServiceShortName;

        public int ApplicationType;
        public uint AppStatus;
        public uint TerminalServicesSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Restartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint sessionHandle, int sessionFlags, char[] sessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint sessionHandle,
        uint fileCount,
        string[] fileNames,
        uint applicationCount,
        IntPtr applications,
        uint serviceCount,
        string[]? serviceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint sessionHandle,
        out uint processInfoNeeded,
        ref uint processInfoCount,
        [In, Out] RmProcessInfo[]? affectedApplications,
        ref uint rebootReasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sessionHandle);
}
