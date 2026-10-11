using System.Collections.ObjectModel;

namespace AgenStart.Desktop.ViewModels.Steps;

public sealed class ReviewViewModel : StepViewModelBase
{
    private bool _hasReview;
    private bool _agreementsAccepted;

    public ReviewViewModel()
    {
        ReviewItems.CollectionChanged += (_, _) => RaiseSummary();
    }

    public ObservableCollection<ReviewRowViewModel> ReviewItems { get; } = [];

    public bool HasReview
    {
        get => _hasReview;
        private set => SetField(ref _hasReview, value);
    }

    public bool AgreementsAccepted
    {
        get => _agreementsAccepted;
        set => SetField(ref _agreementsAccepted, value);
    }

    public int ReviewInstallCount => ReviewItems.Count(row => row.WillInstall);
    public int ReviewAlreadyInstalledCount => ReviewItems.Count(row => row.AlreadyInstalled);
    public int ReviewTotalCount => ReviewItems.Count;

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
    }
}
