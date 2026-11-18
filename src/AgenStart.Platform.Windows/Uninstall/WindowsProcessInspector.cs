using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using AgenStart.Uninstall;

namespace AgenStart.Platform.Windows.Uninstall;

/// <summary>
/// Lists the processes of the current session with their image path, for the "running" warning.
/// Each process is opened with <c>PROCESS_QUERY_LIMITED_INFORMATION</c> only: the handle cannot
/// terminate, signal, suspend or read the process.
/// </summary>
public sealed class WindowsProcessInspector : IProcessInspector
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public IReadOnlyList<RunningProcess> ListProcesses()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        int sessionId;
        using (var current = Process.GetCurrentProcess())
        {
            sessionId = current.SessionId;
        }

        var result = new List<RunningProcess>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.SessionId == sessionId)
                    {
                        result.Add(new(process.Id, process.ProcessName, ImagePath(process.Id)));
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    // The process exited or is protected: it is simply not listed.
                }
            }
        }

        return result;
    }

    [SupportedOSPlatform("windows")]
    private static string? ImagePath(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
