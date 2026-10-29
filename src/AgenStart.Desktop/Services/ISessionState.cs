using AgenStart.Application.Installation;
using AgenStart.Catalogue;
using AgenStart.Core.Machine;

namespace AgenStart.Desktop.Services;

public interface ISessionState
{
    MachineSnapshot? Snapshot { get; }
    SoftwareCatalogue? Catalogue { get; }
    InstallationReport? LastReport { get; }
    bool IsInstalling { get; }

    event EventHandler? SnapshotChanged;
    event EventHandler? CatalogueChanged;
    event EventHandler? LastReportChanged;
    event EventHandler? IsInstallingChanged;

    void SetSnapshot(MachineSnapshot? snapshot);
    void SetCatalogue(SoftwareCatalogue? catalogue);
    void SetLastReport(InstallationReport? report);
    void SetIsInstalling(bool value);
}

internal sealed class SessionState : ISessionState
{
    private MachineSnapshot? _snapshot;
    private SoftwareCatalogue? _catalogue;
    private InstallationReport? _lastReport;
    private bool _isInstalling;

    public MachineSnapshot? Snapshot => _snapshot;
    public SoftwareCatalogue? Catalogue => _catalogue;
    public InstallationReport? LastReport => _lastReport;
    public bool IsInstalling => _isInstalling;

    public event EventHandler? SnapshotChanged;
    public event EventHandler? CatalogueChanged;
    public event EventHandler? LastReportChanged;
    public event EventHandler? IsInstallingChanged;

    public void SetSnapshot(MachineSnapshot? snapshot)
    {
        if (ReferenceEquals(_snapshot, snapshot))
        {
            return;
        }

        _snapshot = snapshot;
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetCatalogue(SoftwareCatalogue? catalogue)
    {
        if (ReferenceEquals(_catalogue, catalogue))
        {
            return;
        }

        _catalogue = catalogue;
        CatalogueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetLastReport(InstallationReport? report)
    {
        if (ReferenceEquals(_lastReport, report))
        {
            return;
        }

        _lastReport = report;
        LastReportChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetIsInstalling(bool value)
    {
        if (_isInstalling == value)
        {
            return;
        }

        _isInstalling = value;
        IsInstallingChanged?.Invoke(this, EventArgs.Empty);
    }
}
