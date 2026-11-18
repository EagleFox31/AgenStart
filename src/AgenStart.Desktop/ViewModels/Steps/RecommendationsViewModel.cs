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
    private bool _isCardLayout;
    private UiText _recommendationStatus = UiText.Key("Recommendations.Status.ChooseProfile");

    public RecommendationsViewModel(
        IInstalledSoftwareInventoryProvider softwareInventory,
        SoftwareStateResolver softwareStateResolver,
        RecommendationEngine recommendationEngine,
        SoftwareCatalogueLoader catalogueLoader,
        ISessionState session,
        ILocalizer? text = null)
        : base(text)
    {
        _softwareInventory = softwareInventory;
        _softwareStateResolver = softwareStateResolver;
        _recommendationEngine = recommendationEngine;
        _catalogueLoader = catalogueLoader;
        _session = session;
        _session.IsInstallingChanged += (_, _) => OnPropertyChanged(nameof(CanReview));

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
        set
        {
            if (SetField(ref _hasRecommendations, value))
            {
                OnPropertyChanged(nameof(CanReview));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public string RecommendationStatus => _recommendationStatus.Resolve(Text);

    public void SetStatus(UiText value) => SetText(ref _recommendationStatus, value, nameof(RecommendationStatus));

    /// <summary>Presentation only: cards grid when true, compact list otherwise.</summary>
    public bool IsCardLayout
    {
        get => _isCardLayout;
        set
        {
            if (SetField(ref _isCardLayout, value))
            {
                OnPropertyChanged(nameof(IsListLayout));
            }
        }
    }

    public bool IsListLayout => !_isCardLayout;

    public int RecommendationCount => Recommendations.Count;
    public int SelectedCount => Recommendations.Count(row => row.IsSelected);
    public string RecommendationCountText => Text.Plural("Recommendations.Summary.Apps", RecommendationCount);
    public string SelectedCountText => Text.Plural("Recommendations.Summary.Selected", SelectedCount);
    public int AlreadyInstalledCount => Recommendations.Count(row => row.Disposition == RecommendationDisposition.AlreadyInstalled);
    public bool CanReview => HasRecommendations && SelectedCount > 0 && !_session.IsInstalling;
    public int OptionalCount => Recommendations.Count(row => row.Level == RecommendationLevel.Optional && row.Disposition == RecommendationDisposition.Recommended);

    public async Task BuildAsync(
        UserProfile profile,
        UiText profileName,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy || _session.IsInstalling || _session.Snapshot is null)
        {
            return;
        }

        IsBusy = true;
        SetStatus(UiText.Key("Recommendations.Status.Building"));

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
            SetStatus(UiText.Key("Recommendations.Status.BasedOn", profileName));
            RaiseSummary();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus(UiText.Key("Recommendations.Status.Cancelled"));
        }
        catch (Exception)
        {
            ClearRows();
            _session.SetCatalogue(null);
            HasRecommendations = false;
            SetStatus(UiText.Key("Recommendations.Status.Failed"));
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

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        foreach (var row in Recommendations)
        {
            row.RefreshText();
        }
    }

    public void RaiseSummary()
    {
        OnPropertyChanged(nameof(RecommendationCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(RecommendationCountText));
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(AlreadyInstalledCount));
        OnPropertyChanged(nameof(OptionalCount));
        OnPropertyChanged(nameof(CanReview));
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
