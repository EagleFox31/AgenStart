using System.Runtime.CompilerServices;
using AgenStart.Application.Installation;
using AgenStart.Desktop.Services;
using AgenStart.Desktop.Icons;
using AgenStart.PackageManagement;
using Avalonia.Media;

namespace AgenStart.Desktop.ViewModels;

public sealed class ReviewRowViewModel : LocalizedRow, IAppRowLogo
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
        IconSource = AppIconService.Shared.Resolve(applicationId);
        HasLogo = IconSource is not null;
        ShowInitials = !HasLogo;
        Reason = reason;
        WillInstall = willInstall;
        AlreadyInstalled = alreadyInstalled;
    }

    public string ApplicationId { get; }
    public string Name { get; }
    public string Initials { get; }
    public IImage? IconSource { get; }
    public bool HasLogo { get; }
    public bool ShowInitials { get; }
    public string Reason { get; }
    public bool WillInstall { get; }
    public bool AlreadyInstalled { get; }
    public bool CanRemove => WillInstall;
    public string Status => Text[AlreadyInstalled ? "Review.Status.AlreadyInstalled" : "Review.Status.WillInstall"];
}

public sealed class InstallationRowViewModel : LocalizedRow, IAppRowLogo
{
    private InstallationQueueItemState _state;
    private InstallationItemActivity _activity;
    private UiText _status = UiText.Key("Installation.Item.Waiting");
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
        IconSource = AppIconService.Shared.Resolve(applicationId);
        HasLogo = IconSource is not null;
        ShowInitials = !HasLogo;
    }

    public string ApplicationId { get; }
    public string Name { get; }
    public string Initials { get; }
    public IImage? IconSource { get; }
    public bool HasLogo { get; }
    public bool ShowInitials { get; }

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

    public string Status => _status.Resolve(Text);

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
        ? FormatDownloadDetail(BytesDownloaded, BytesRequired).Resolve(Text)
        : string.Empty;

    public void Apply(InstallationItemSnapshot snapshot)
    {
        State = snapshot.State;
        Activity = snapshot.Activity;
        BytesDownloaded = snapshot.BytesDownloaded;
        BytesRequired = snapshot.BytesRequired;
        _status = ResolveStatus(snapshot);
        OnPropertyChanged(nameof(Status));
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

    private static UiText ResolveStatus(InstallationItemSnapshot snapshot)
    {
        if (snapshot.State == InstallationQueueItemState.Succeeded &&
            snapshot.LastOperationStatus == PackageOperationStatus.AlreadyInstalled)
        {
            return UiText.Key("Installation.Item.AlreadyInstalled");
        }

        if (snapshot.State == InstallationQueueItemState.Succeeded)
        {
            return UiText.Key(snapshot.RequiresReboot ? "Installation.Item.VerifiedRestart" : "Installation.Item.Verified");
        }

        if (snapshot.State == InstallationQueueItemState.Failed)
        {
            return UiText.Key("Installation.Item.Failed");
        }

        if (snapshot.State == InstallationQueueItemState.Skipped)
        {
            return UiText.Key("Installation.Item.Skipped");
        }

        if (snapshot.State == InstallationQueueItemState.Cancelled)
        {
            return UiText.Key("Installation.Item.Cancelled");
        }

        return UiText.Key(snapshot.Activity switch
        {
            InstallationItemActivity.Resolving => "Installation.Item.Resolving",
            InstallationItemActivity.Downloading => "Installation.Item.Downloading",
            InstallationItemActivity.Ready => "Installation.Item.Ready",
            InstallationItemActivity.Installing => "Installation.Item.Installing",
            InstallationItemActivity.Verifying => "Installation.Item.Verifying",
            _ => "Installation.Item.Waiting"
        });
    }

    private static UiText FormatDownloadDetail(long? downloaded, long? required)
    {
        if (downloaded is null)
        {
            return UiText.Key("Installation.Item.DownloadingTrusted");
        }

        if (required is > 0)
        {
            return UiText.Key("Installation.Item.DownloadProgress", UiUnits.Bytes(downloaded.Value), UiUnits.Bytes(required.Value));
        }

        return UiText.Key("Installation.Item.Downloaded", UiUnits.Bytes(downloaded.Value));
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
}

public sealed class ReportRowViewModel : LocalizedRow, IAppRowLogo
{
    private readonly UiText _result;

    public ReportRowViewModel(
        string applicationId,
        string name,
        string initials,
        UiText result,
        bool alreadyInstalled,
        string? installedVersion,
        bool requiresReboot)
    {
        ApplicationId = applicationId;
        Name = name;
        Initials = initials;
        IconSource = AppIconService.Shared.Resolve(applicationId);
        HasLogo = IconSource is not null;
        ShowInitials = !HasLogo;
        _result = result;
        IsAlreadyInstalled = alreadyInstalled;
        InstalledVersion = installedVersion ?? "—";
        RequiresReboot = requiresReboot;
    }

    public string ApplicationId { get; }
    public string Name { get; }
    public string Initials { get; }
    public IImage? IconSource { get; }
    public bool HasLogo { get; }
    public bool ShowInitials { get; }
    public string Result => _result.Resolve(Text);
    public bool IsAlreadyInstalled { get; }
    public string InstalledVersion { get; }
    public bool RequiresReboot { get; }
}
