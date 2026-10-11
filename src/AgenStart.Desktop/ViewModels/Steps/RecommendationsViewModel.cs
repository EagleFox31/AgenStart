using System.Collections.ObjectModel;
using AgenStart.Core.Catalogue;
using AgenStart.Recommendations;

namespace AgenStart.Desktop.ViewModels.Steps;

public sealed class RecommendationsViewModel : StepViewModelBase
{
    private bool _hasRecommendations;
    private string _recommendationStatus = "Choose a usage profile to build recommendations.";

    public RecommendationsViewModel()
    {
        Recommendations.CollectionChanged += (_, _) =>
        {
            RelabelOrder();
            RaiseSummary();
            OnPropertyChanged(nameof(Recommendations));
        };
    }

    public ObservableCollection<RecommendationRowViewModel> Recommendations { get; } = [];

    public bool HasRecommendations
    {
        get => _hasRecommendations;
        set => SetField(ref _hasRecommendations, value);
    }

    public string RecommendationStatus
    {
        get => _recommendationStatus;
        set => SetField(ref _recommendationStatus, value);
    }

    public int RecommendationCount => Recommendations.Count;
    public int SelectedCount => Recommendations.Count(row => row.IsSelected);
    public int AlreadyInstalledCount => Recommendations.Count(row => row.Disposition == RecommendationDisposition.AlreadyInstalled);
    public int OptionalCount => Recommendations.Count(row => row.Level == RecommendationLevel.Optional && row.Disposition == RecommendationDisposition.Recommended);

    public void RaiseSummary()
    {
        OnPropertyChanged(nameof(RecommendationCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(AlreadyInstalledCount));
        OnPropertyChanged(nameof(OptionalCount));
    }

    private void RelabelOrder()
    {
        for (var i = 0; i < Recommendations.Count; i++)
        {
            Recommendations[i].OrderLabel = $"{i + 1:00}";
        }
    }
}
