using System.Collections.ObjectModel;
using AgenStart.Desktop.Services;

namespace AgenStart.Desktop.ViewModels.Steps;

public sealed class ReviewViewModel : StepViewModelBase
{
    private readonly ISessionState _session;
    private bool _hasReview;
    private bool _agreementsAccepted;

    public ReviewViewModel(ISessionState session)
    {
        _session = session;
        _session.IsInstallingChanged += (_, _) => OnPropertyChanged(nameof(CanStartInstallation));
        ReviewItems.CollectionChanged += (_, _) => RaiseSummary();
    }

    public ObservableCollection<ReviewRowViewModel> ReviewItems { get; } = [];

    public bool HasReview
    {
        get => _hasReview;
        private set
        {
            if (SetField(ref _hasReview, value))
            {
                OnPropertyChanged(nameof(CanStartInstallation));
            }
        }
    }

    public bool AgreementsAccepted
    {
        get => _agreementsAccepted;
        set
        {
            if (SetField(ref _agreementsAccepted, value))
            {
                OnPropertyChanged(nameof(CanStartInstallation));
            }
        }
    }

    public int ReviewInstallCount => ReviewItems.Count(row => row.WillInstall);
    public int ReviewAlreadyInstalledCount => ReviewItems.Count(row => row.AlreadyInstalled);
    public int ReviewTotalCount => ReviewItems.Count;

    public bool CanStartInstallation =>
        HasReview && ReviewInstallCount > 0 && AgreementsAccepted && !_session.IsInstalling;

    public void SetHasReview(bool value) => HasReview = value;

    public void Reset()
    {
        ReviewItems.Clear();
        HasReview = false;
        AgreementsAccepted = false;
    }

    public void RaiseSummary()
    {
        OnPropertyChanged(nameof(ReviewInstallCount));
        OnPropertyChanged(nameof(ReviewAlreadyInstalledCount));
        OnPropertyChanged(nameof(ReviewTotalCount));
        OnPropertyChanged(nameof(CanStartInstallation));
    }
}
