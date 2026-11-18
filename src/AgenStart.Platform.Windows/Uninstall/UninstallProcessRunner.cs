using System.ComponentModel;
using System.Diagnostics;

namespace AgenStart.Platform.Windows.Uninstall;

/// <summary>
/// How a vendor uninstaller ended. <see cref="StartErrorCode"/> is the Win32 error when the process
/// could not start (740 when the program asks for elevation).
/// </summary>
public sealed record UninstallProcessResult(bool Started, int? ExitCode, bool TimedOut = false, int? StartErrorCode = null);

/// <summary>Starts one vendor uninstaller from a fully qualified path and an argument list.</summary>
public interface IUninstallProcessRunner
{
    Task<UninstallProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout);
}

/// <summary>
/// No shell: <see cref="ProcessStartInfo.UseShellExecute"/> is false, each argument is passed on its
/// own, nothing is redirected. It never elevates (an elevated program fails to start with error 740)
/// and never kills: on timeout it stops waiting and leaves the uninstaller running.
/// </summary>
public sealed class UninstallProcessRunner : IUninstallProcessRunner
{
    public async Task<UninstallProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        if (!Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException("The uninstaller path must be fully qualified.", nameof(executablePath));
        }

        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception exception)
        {
            return new UninstallProcessResult(false, null, StartErrorCode: exception.NativeErrorCode);
        }

        if (process is null)
        {
            return new UninstallProcessResult(false, null);
        }

        using (process)
        {
            using var wait = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new UninstallProcessResult(true, null, TimedOut: true);
            }

            return new UninstallProcessResult(true, process.ExitCode);
        }
    }
}
