using System.Diagnostics;

namespace AgenStart.Platform.Windows.WinGet;

public sealed record WinGetExecutableResolution(
    bool Found,
    string? Path = null,
    string? DiagnosticCode = null,
    string? Message = null);

public sealed record WinGetProcessResult(
    bool Started,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool Cancelled = false,
    bool TimedOut = false,
    string? StartError = null);

public interface IWinGetExecutableLocator
{
    WinGetExecutableResolution Resolve();
}

public interface IWinGetProcessRunner
{
    Task<WinGetProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public sealed class WinGetExecutableLocator : IWinGetExecutableLocator
{
    public WinGetExecutableResolution Resolve()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WinGetExecutableResolution(
                false,
                DiagnosticCode: "winget.unsupported-platform",
                Message: "WinGet is only available on Windows.");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return new WinGetExecutableResolution(
                false,
                DiagnosticCode: "winget.localappdata-unavailable",
                Message: "Unable to resolve the current user's LocalAppData directory.");
        }

        var windowsApps = Path.GetFullPath(Path.Combine(localAppData, "Microsoft", "WindowsApps"));
        var candidates = new[]
        {
            Path.Combine(windowsApps, "winget.exe"),
            Path.Combine(
                windowsApps,
                "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe",
                "winget.exe")
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (!fullPath.StartsWith(windowsApps, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (File.Exists(fullPath))
            {
                return new WinGetExecutableResolution(true, fullPath);
            }
        }

        return new WinGetExecutableResolution(
            false,
            DiagnosticCode: "winget.alias-not-found",
            Message: "The trusted WinGet App Execution Alias was not found for the current user.");
    }
}

public sealed class WinGetProcessRunner : IWinGetProcessRunner
{
    public async Task<WinGetProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                return new WinGetProcessResult(
                    false,
                    null,
                    string.Empty,
                    string.Empty,
                    StartError: "Process.Start returned false.");
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new WinGetProcessResult(
                false,
                null,
                string.Empty,
                string.Empty,
                StartError: exception.Message);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);

            try
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // The process may already have terminated between checks.
            }

            var cancelled = cancellationToken.IsCancellationRequested;
            return new WinGetProcessResult(
                true,
                process.HasExited ? process.ExitCode : null,
                await ReadSafelyAsync(standardOutputTask).ConfigureAwait(false),
                await ReadSafelyAsync(standardErrorTask).ConfigureAwait(false),
                Cancelled: cancelled,
                TimedOut: !cancelled);
        }

        return new WinGetProcessResult(
            true,
            process.ExitCode,
            await standardOutputTask.ConfigureAwait(false),
            await standardErrorTask.ConfigureAwait(false));
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process exited before the kill request reached it.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Best effort. The caller still receives Cancelled/TimedOut.
        }
    }

    private static async Task<string> ReadSafelyAsync(Task<string> readTask)
    {
        try
        {
            return await readTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
    }
}
