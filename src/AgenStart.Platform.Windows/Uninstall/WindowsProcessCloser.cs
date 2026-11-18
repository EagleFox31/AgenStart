using System.Diagnostics;
using AgenStart.Uninstall;

namespace AgenStart.Platform.Windows.Uninstall;

/// <summary>The process operations the closer needs, so that tests never touch a real process.</summary>
public interface IProcessControl
{
    /// <summary>Asks the main window to close. False when the process is gone or has no window.</summary>
    bool RequestClose(int processId);

    bool HasExited(int processId);

    void Kill(int processId);
}

/// <summary>
/// Closes processes only with consent (ADR-0004 §5): each main window is asked to close; a process
/// that is still running after the timeout is ended only when <see cref="ProcessCloseConsent.ForceAfterTimeout"/>
/// was approved separately.
/// </summary>
public sealed class WindowsProcessCloser(IProcessControl? control = null, TimeSpan? closeTimeout = null, TimeProvider? time = null)
    : IRunningProcessCloser
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IProcessControl _control = control ?? new SystemProcessControl();
    private readonly TimeSpan _closeTimeout = closeTimeout ?? TimeSpan.FromSeconds(15);
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<ProcessCloseResult> CloseAsync(
        IReadOnlyList<RunningProcess> processes,
        ProcessCloseConsent consent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(consent);
        if (!consent.CloseWindows)
        {
            throw new InvalidOperationException("Closing processes requires the person's consent.");
        }

        foreach (var process in processes)
        {
            _control.RequestClose(process.ProcessId);
        }

        var deadline = _time.GetUtcNow() + _closeTimeout;
        var running = processes.Where(process => !_control.HasExited(process.ProcessId)).ToList();
        while (running.Count > 0 && _time.GetUtcNow() < deadline)
        {
            await Task.Delay(PollInterval, _time, cancellationToken).ConfigureAwait(false);
            running.RemoveAll(process => _control.HasExited(process.ProcessId));
        }

        if (running.Count > 0 && consent.ForceAfterTimeout)
        {
            foreach (var process in running)
            {
                _control.Kill(process.ProcessId);
            }

            running.RemoveAll(process => _control.HasExited(process.ProcessId));
        }

        return new ProcessCloseResult(processes.Except(running).ToArray(), running.ToArray());
    }

    private sealed class SystemProcessControl : IProcessControl
    {
        public bool RequestClose(int processId)
        {
            using var process = Find(processId);
            try
            {
                return process is not null && process.CloseMainWindow();
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public bool HasExited(int processId)
        {
            using var process = Find(processId);
            try
            {
                return process is null || process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        public void Kill(int processId)
        {
            using var process = Find(processId);
            try
            {
                process?.Kill();
            }
            catch (InvalidOperationException)
            {
                // Exited in the meantime.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Access denied: it is reported as still running.
            }
        }

        private static Process? Find(int processId)
        {
            try
            {
                return Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
