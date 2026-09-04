using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgenStart.Application.Installation;

namespace AgenStart.Desktop.ViewModels;

public sealed class ReviewRowViewModel
{
    public ReviewRowViewModel(
        string applicationId,
        string name,
        string initials,
        string reason,
        bool willInstall,
        bool alreadyInstalled)
    {
        ApplicationId = applicationId;
        Name = name;
        Initials = initials;
        Reason = reason;
        WillInstall = willInstall;
        AlreadyInstalled = alreadyInstalled;
    }

    public string ApplicationId { get; }
    public string Name { get; }
    public string Initials { get; }
    public string Reason { get; }
    public bool WillInstall { get; }
    public bool AlreadyInstalled { get; }
    public bool CanRemove => WillInstall;
    public string Status => AlreadyInstalled ? "Already installed" : "Will install";
}

public sealed class InstallationRowViewModel : INotifyPropertyChanged
{
    private InstallationQueueItemState _state;
    private string _status = "Waiting";
    private string? _message;
    private bool _canRetry;
    private string? _installedVersion;
    private bool _requiresReboot;

    public InstallationRowViewModel(string applicationId, string name, string initials)
    {
        ApplicationId = applicationId;
        Name = name;
        Initials = initials;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ApplicationId { get; }
    public string Name { get; }
    public string Initials { get; }

    public InstallationQueueItemState State
    {
        get => _state;
        private set => SetField(ref _state, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public string? Message
    {
        get => _message;
        private set => SetField(ref _message, value);
    }

    public bool CanRetry
    {
        get => _canRetry;
        private set => SetField(ref _canRetry, value);
    }

    public string? InstalledVersion
    {
        get => _installedVersion;
        private set => SetField(ref _installedVersion, value);
    }

    public bool RequiresReboot
    {
        get => _requiresReboot;
        private set => SetField(ref _requiresReboot, value);
    }

    public bool IsRunning => State == InstallationQueueItemState.Running;
    public bool HasFailure => State == InstallationQueueItemState.Failed;

    public void Apply(InstallationItemSnapshot snapshot)
    {
        State = snapshot.State;
        Status = snapshot.State switch
        {
            InstallationQueueItemState.Queued => "Waiting",
            InstallationQueueItemState.Running => "Installing",
            InstallationQueueItemState.Succeeded when snapshot.LastOperationStatus == PackageManagement.PackageOperationStatus.AlreadyInstalled => "Already installed",
            InstallationQueueItemState.Succeeded => snapshot.RequiresReboot ? "Installed · restart required" : "Installed",
            InstallationQueueItemState.Failed => "Failed",
            InstallationQueueItemState.Skipped => "Skipped",
            InstallationQueueItemState.Cancelled => "Cancelled",
            _ => snapshot.State.ToString()
        };
        Message = snapshot.Message;
        CanRetry = snapshot.CanRetry;
        InstalledVersion = snapshot.InstalledVersion;
        RequiresReboot = snapshot.RequiresReboot;
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(HasFailure));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ReportRowViewModel
{
    public ReportRowViewModel(
        string applicationId,
        string name,
        string initials,
        string result,
        string? installedVersion,
        bool requiresReboot)
    {
        ApplicationId = applicationId;
        Name = name;
        Initials = initials;
        Result = result;
        InstalledVersion = installedVersion ?? "—";
        RequiresReboot = requiresReboot;
    }

    public string ApplicationId { get; }
    public string Name { get; }
    public string Initials { get; }
    public string Result { get; }
    public string InstalledVersion { get; }
    public bool RequiresReboot { get; }
}
