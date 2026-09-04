using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgenStart.Application.Installation;
using AgenStart.PackageManagement;

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
    private InstallationItemActivity _activity;
    private string _status = "Waiting";
    private string? _message;
    private bool _canRetry;
    private string? _installedVersion;
    private bool _requiresReboot;
    private long? _bytesDownloaded;
    private long? _bytesRequired;

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

    public InstallationItemActivity Activity
    {
        get => _activity;
        private set => SetField(ref _activity, value);
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

    public long? BytesDownloaded
    {
        get => _bytesDownloaded;
        private set => SetField(ref _bytesDownloaded, value);
    }

    public long? BytesRequired
    {
        get => _bytesRequired;
        private set => SetField(ref _bytesRequired, value);
    }

    public bool IsRunning => State == InstallationQueueItemState.Running;
    public bool IsDownloading => Activity == InstallationItemActivity.Downloading;
    public bool IsReady => Activity == InstallationItemActivity.Ready;
    public bool IsInstalling => Activity == InstallationItemActivity.Installing;
    public bool HasFailure => State == InstallationQueueItemState.Failed;

    public string DownloadDetail => Activity == InstallationItemActivity.Downloading
        ? FormatDownloadDetail(BytesDownloaded, BytesRequired)
        : string.Empty;

    public void Apply(InstallationItemSnapshot snapshot)
    {
        State = snapshot.State;
        Activity = snapshot.Activity;
        BytesDownloaded = snapshot.BytesDownloaded;
        BytesRequired = snapshot.BytesRequired;
        Status = ResolveStatus(snapshot);
        Message = snapshot.Message;
        CanRetry = snapshot.CanRetry;
        InstalledVersion = snapshot.InstalledVersion;
        RequiresReboot = snapshot.RequiresReboot;
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(IsInstalling));
        OnPropertyChanged(nameof(HasFailure));
        OnPropertyChanged(nameof(DownloadDetail));
    }

    private static string ResolveStatus(InstallationItemSnapshot snapshot)
    {
        if (snapshot.State == InstallationQueueItemState.Succeeded &&
            snapshot.LastOperationStatus == PackageOperationStatus.AlreadyInstalled)
        {
            return "Already installed";
        }

        if (snapshot.State == InstallationQueueItemState.Succeeded)
        {
            return snapshot.RequiresReboot ? "Verified · restart required" : "Verified";
        }

        if (snapshot.State == InstallationQueueItemState.Failed)
        {
            return "Failed";
        }

        if (snapshot.State == InstallationQueueItemState.Skipped)
        {
            return "Skipped";
        }

        if (snapshot.State == InstallationQueueItemState.Cancelled)
        {
            return "Cancelled";
        }

        return snapshot.Activity switch
        {
            InstallationItemActivity.Resolving => "Resolving",
            InstallationItemActivity.Downloading => "Downloading",
            InstallationItemActivity.Ready => "Ready",
            InstallationItemActivity.Installing => "Installing",
            InstallationItemActivity.Verifying => "Verifying",
            _ => "Waiting"
        };
    }

    private static string FormatDownloadDetail(long? downloaded, long? required)
    {
        if (downloaded is null)
        {
            return "Downloading through trusted provider";
        }

        if (required is > 0)
        {
            return $"{FormatBytes(downloaded.Value)} / {FormatBytes(required.Value)}";
        }

        return $"{FormatBytes(downloaded.Value)} downloaded";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024d * 1024 * 1024):0.0} GB";
        }

        if (bytes >= 1024L * 1024)
        {
            return $"{bytes / (1024d * 1024):0.0} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024d:0.0} KB";
        }

        return $"{bytes} B";
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
