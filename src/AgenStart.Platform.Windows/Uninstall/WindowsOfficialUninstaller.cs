using System.Security.Principal;
using AgenStart.PackageManagement;
using AgenStart.Uninstall;

namespace AgenStart.Platform.Windows.Uninstall;

/// <summary>
/// Runs the reviewed official step (ADR-0004). Before anything starts, the step is selected again from
/// the current entry: a different command is reported as changed since review, a refused one as
/// rejected. A machine-wide MSI removal needs elevation, which P3 never requests. Once a vendor
/// uninstaller has started it is not cancelled and never killed.
/// </summary>
public sealed class WindowsOfficialUninstaller : IOfficialUninstaller
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);

    private readonly IPackageUninstallProvider? _winget;
    private readonly IUninstallProcessRunner _runner;
    private readonly IKnownFolderResolver _folders;
    private readonly IFileSystemProbe _files;
    private readonly Func<bool> _isElevated;
    private readonly TimeSpan _timeout;

    public WindowsOfficialUninstaller(
        IPackageUninstallProvider? winget,
        IUninstallProcessRunner runner,
        IKnownFolderResolver folders,
        IFileSystemProbe files,
        Func<bool>? isElevated = null,
        TimeSpan? timeout = null)
    {
        _winget = winget;
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _folders = folders ?? throw new ArgumentNullException(nameof(folders));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _isElevated = isElevated ?? IsProcessElevated;
        _timeout = timeout ?? DefaultTimeout;
    }

    public async Task<OfficialUninstallResult> UninstallAsync(
        InstalledProgram program,
        OfficialUninstallStep step,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(step);

        var current = OfficialStrategySelector.Select(program, _folders, _files, []);
        if (step.Strategy == OfficialStrategy.Unavailable || current.Strategy == OfficialStrategy.Unavailable)
        {
            return OfficialUninstallResult.NotExecuted(PackageOperationStatus.BlockedByPolicy, OfficialStrategySelector.CommandRejected);
        }

        if (!SameCommand(step, current))
        {
            return OfficialUninstallResult.NotExecuted(PackageOperationStatus.BlockedByPolicy, UninstallExitCodes.ChangedSinceReviewCode);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return current.Strategy == OfficialStrategy.WinGet
            ? await RunWinGetAsync(program).ConfigureAwait(false)
            : await RunVendorAsync(program, current).ConfigureAwait(false);
    }

    private async Task<OfficialUninstallResult> RunWinGetAsync(InstalledProgram program)
    {
        if (_winget is null)
        {
            return OfficialUninstallResult.NotExecuted(PackageOperationStatus.ProviderUnavailable, "winget.unavailable");
        }

        var scope = program.Scope == ProgramScope.Machine ? PackageScope.Machine : PackageScope.User;
        var request = new PackageUninstallRequest(
            program.WinGetPackageId!,
            new ProviderPackageReference(PackageProviderIds.WinGet, program.WinGetPackageId!, OfficialStrategySelector.WinGetSource, scope));

        // Not cancellable once started: the provider would end winget and the uninstaller under it.
        var result = await _winget.UninstallAsync(request, CancellationToken.None).ConfigureAwait(false);
        var code = result.DiagnosticCode ?? "winget.unknown";
        return result.Status switch
        {
            PackageOperationStatus.Succeeded => new(PackageOperationStatus.Succeeded, result.NativeExitCode, code),
            PackageOperationStatus.Failed when code == "winget.install-in-progress" =>
                new(PackageOperationStatus.Failed, result.NativeExitCode, code, Retryable: true),
            PackageOperationStatus.ProviderUnavailable => OfficialUninstallResult.NotExecuted(PackageOperationStatus.ProviderUnavailable, code),
            _ => new(result.Status, result.NativeExitCode, code)
        };
    }

    private async Task<OfficialUninstallResult> RunVendorAsync(InstalledProgram program, OfficialUninstallStep step)
    {
        if (step.Executable is null || !Path.IsPathFullyQualified(step.Executable))
        {
            return OfficialUninstallResult.NotExecuted(PackageOperationStatus.BlockedByPolicy, OfficialStrategySelector.CommandRejected);
        }

        if (step.Strategy == OfficialStrategy.Msi && program.Scope == ProgramScope.Machine && !_isElevated())
        {
            return OfficialUninstallResult.NotExecuted(PackageOperationStatus.RequiresElevation, UninstallExitCodes.ElevationRequiredCode);
        }

        var run = await _runner.RunAsync(step.Executable, step.Arguments, _timeout).ConfigureAwait(false);
        if (!run.Started)
        {
            return run.StartErrorCode == UninstallExitCodes.ElevationRequired
                ? OfficialUninstallResult.NotExecuted(PackageOperationStatus.RequiresElevation, UninstallExitCodes.ElevationRequiredCode)
                : OfficialUninstallResult.NotExecuted(PackageOperationStatus.Failed, UninstallExitCodes.StartFailedCode);
        }

        return run.TimedOut || run.ExitCode is not { } exitCode
            ? new OfficialUninstallResult(PackageOperationStatus.TimedOut, null, UninstallExitCodes.TimedOutCode)
            : UninstallExitCodes.Normalize(exitCode);
    }

    private static bool SameCommand(OfficialUninstallStep reviewed, OfficialUninstallStep current) =>
        reviewed.Strategy == current.Strategy
        && string.Equals(reviewed.Executable, current.Executable, StringComparison.OrdinalIgnoreCase)
        && reviewed.Arguments.SequenceEqual(current.Arguments, StringComparer.Ordinal);

    private static bool IsProcessElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
