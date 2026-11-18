using AgenStart.Application.Uninstallation;
using AgenStart.Platform.Windows.Uninstall;
using AgenStart.Platform.Windows.WinGet;
using AgenStart.Uninstall;

namespace AgenStart.Desktop.Services;

/// <summary>
/// Runs one journaled official uninstall (ADR-0004, ADR-0007). The uninstall page only holds this
/// port when the experimental setting was on at startup: otherwise it stays read-only.
/// </summary>
public interface IOfficialUninstallRunner
{
    Task<UninstallReport> RunAsync(
        UninstallRequest request,
        IProgress<UninstallProgressEvent>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Starts a new <see cref="UninstallSession"/> for every request, since a session runs once.</summary>
public sealed class OfficialUninstallRunner(Func<UninstallSession> createSession) : IOfficialUninstallRunner
{
    private readonly Func<UninstallSession> _createSession = createSession ?? throw new ArgumentNullException(nameof(createSession));

    public Task<UninstallReport> RunAsync(
        UninstallRequest request,
        IProgress<UninstallProgressEvent>? progress = null,
        CancellationToken cancellationToken = default) =>
        _createSession().RunAsync(request, progress, cancellationToken);

    /// <summary>
    /// The Windows composition: WinGet or the vendor's uninstaller without a shell, Restart Manager
    /// detection (read-only), window closing only with consent, and the journal under the data folder.
    /// Building it starts nothing.
    /// </summary>
    public static OfficialUninstallRunner CreateForWindows(IInstalledProgramInventory inventory, string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var folders = new WindowsKnownFolderResolver();
        var files = new WindowsFileSystemProbe();
        var verifier = new InventoryUninstallVerifier(inventory);
        var uninstaller = new WindowsOfficialUninstaller(new WinGetProvider(), new UninstallProcessRunner(), folders, files);
        var journal = new JsonlUninstallJournal(dataDirectory);
        var detector = new RestartManagerProcessDetector(files, new RestartManagerFileHolderQuery());
        var closer = new WindowsProcessCloser();
        return new OfficialUninstallRunner(() => new UninstallSession(verifier, uninstaller, journal, detector, closer));
    }
}
