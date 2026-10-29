using System.Collections.ObjectModel;
using System.ComponentModel;
using AgenStart.Catalogue;
using AgenStart.Core.Catalogue;
using AgenStart.Desktop.LocalData;
using AgenStart.Desktop.Services;
using AgenStart.Recommendations;
using AgenStart.SoftwareInventory;

namespace AgenStart.Desktop.ViewModels.Steps;

public sealed class RecommendationsViewModel : StepViewModelBase
{
    private readonly IInstalledSoftwareInventoryProvider _softwareInventory;
    private readonly SoftwareStateResolver _softwareStateResolver;
    private readonly RecommendationEngine _recommendationEngine;
    private readonly SoftwareCatalogueLoader _catalogueLoader;
    private readonly ISessionState _session;

    private bool _hasRecommendations;
    private bool _isBusy;
    private string _recommendationStatus = "Choose a usage profile to build recommendations.";

    public RecommendationsViewModel(
        IInstalledSoftwareInventoryProvider softwareInventory,
        SoftwareStateResolver softwareStateResolver,
        RecommendationEngine recommendationEngine,
        SoftwareCatalogueLoader catalogueLoader,
        ISessionState session)
    {
        _softwareInventory = softwareInventory;
        _softwareStateResolver = softwareStateResolver;
        _recommendationEngine = recommendationEngine;
        _catalogueLoader = catalogueLoader;
        _session = session;

        Recommendations.CollectionChanged += (_, _) =>
        {
            RelabelOrder();
            RaiseSummary();
            OnPropertyChanged(nameof(Recommendations));
        };
    }

    public event EventHandler? SelectionChanged;

    public ObservableCollection<RecommendationRowViewModel> Recommendations { get; } = [];

    public bool HasRecommendations
    {
        get => _hasRecommendations;
        set => SetField(ref _hasRecommendations, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
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

    public async Task BuildAsync(
        UserProfile profile,
        string profileName,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy || _session.IsInstalling || _session.Snapshot is null)
        {
            return;
        }

        IsBusy = true;
        RecommendationStatus = "Checking installed software and building recommendations…";

        try
        {
            using var catalogueStream = PackagedCatalogue.OpenRead();
            var catalogue = _catalogueLoader.Load(catalogueStream);

            var installedSoftware = await _softwareInventory
                .CaptureAsync(cancellationToken)
                .ConfigureAwait(true);
            var softwareState = _softwareStateResolver.Resolve(catalogue.DetectionTargets, installedSoftware);
            var plan = _recommendationEngine.Build(new RecommendationRequest(
                profile,
                _session.Snapshot,
                softwareState,
                catalogue.Definitions));

            ClearRows();
            _session.SetCatalogue(catalogue);
            var metadata = catalogue.Applications.ToDictionary(
                application => application.Id,
                StringComparer.OrdinalIgnoreCase);

            foreach (var decision in plan.Decisions)
            {
                var description = metadata.TryGetValue(decision.ApplicationId, out var application)
                    ? application.Description
                    : string.Empty;
                var row = new RecommendationRowViewModel(decision, description);
                row.PropertyChanged += RecommendationRow_OnPropertyChanged;
                Recommendations.Add(row);
            }

            HasRecommendations = true;
            RecommendationStatus = $"Based on your {profileName} profile, machine capabilities and software already installed.";
            RaiseSummary();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RecommendationStatus = "Recommendation analysis cancelled.";
        }
        catch (Exception)
        {
            ClearRows();
            _session.SetCatalogue(null);
            HasRecommendations = false;
            RecommendationStatus = "AgenStart could not build recommendations from the local catalogue and inventory.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SelectEssentialsOnly()
    {
        if (_session.IsInstalling)
        {
            return;
        }

        foreach (var row in Recommendations.Where(row => row.CanSelect))
        {
            row.IsSelected = row.Level == RecommendationLevel.Essential;
        }

        RaiseSummary();
    }

    public void ClearRows()
    {
        foreach (var row in Recommendations)
        {
            row.PropertyChanged -= RecommendationRow_OnPropertyChanged;
        }

        Recommendations.Clear();
        HasRecommendations = false;
    }

    public void RaiseSummary()
    {
        OnPropertyChanged(nameof(RecommendationCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(AlreadyInstalledCount));
        OnPropertyChanged(nameof(OptionalCount));
    }

    private void RecommendationRow_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecommendationRowViewModel.IsSelected))
        {
            RaiseSummary();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RelabelOrder()
    {
        for (var i = 0; i < Recommendations.Count; i++)
        {
            Recommendations[i].OrderLabel = $"{i + 1:00}";
        }
    }
}
