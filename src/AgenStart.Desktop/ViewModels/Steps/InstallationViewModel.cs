using System.Collections.ObjectModel;
using AgenStart.Application.Installation;

namespace AgenStart.Desktop.ViewModels.Steps;

public sealed class InstallationViewModel : StepViewModelBase
{
    private bool _isInstalling;
    private string _installationStatus = "Ready to install the approved setup.";
    private string _currentInstallationName = "Waiting to start";

    public InstallationViewModel()
    {
        InstallationItems.CollectionChanged += (_, _) => RaiseSummary();
    }

    public ObservableCollection<InstallationRowViewModel> InstallationItems { get; } = [];

    public bool IsInstalling
    {
        get => _isInstalling;
        private set => SetField(ref _isInstalling, value);
    }

    public string InstallationStatus
    {
        get => _installationStatus;
        private set => SetField(ref _installationStatus, value);
    }

    public string CurrentInstallationName
    {
        get => _currentInstallationName;
        private set => SetField(ref _currentInstallationName, value);
    }

    public int InstallationCompletedCount => InstallationItems.Count(row => row.State == InstallationQueueItemState.Succeeded);
    public int InstallationFailedCount => InstallationItems.Count(row => row.State == InstallationQueueItemState.Failed);
    public int InstallationRemainingCount => InstallationItems.Count(row => row.State is InstallationQueueItemState.Queued or InstallationQueueItemState.Running);
    public int InstallationTotalCount => InstallationItems.Count;

    public void SetIsInstalling(bool value) => IsInstalling = value;
    public void SetStatus(string value) => InstallationStatus = value;
    public void SetCurrentName(string value) => CurrentInstallationName = value;

    public void Reset()
    {
        InstallationItems.Clear();
        InstallationStatus = "Ready to install the approved setup.";
        CurrentInstallationName = "Waiting to start";
    }

    public void RaiseSummary()
    {
        OnPropertyChanged(nameof(InstallationCompletedCount));
        OnPropertyChanged(nameof(InstallationFailedCount));
        OnPropertyChanged(nameof(InstallationRemainingCount));
        OnPropertyChanged(nameof(InstallationTotalCount));
    }
}
