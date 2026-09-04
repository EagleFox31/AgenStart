using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using AgenStart.Core.Recommendations;
using AgenStart.Desktop.ViewModels;

namespace AgenStart.Desktop.Views;

public sealed partial class MainWindow
{
    private static readonly IBrush RecommendationProgressDangerBrush =
        new SolidColorBrush(Color.Parse("#A84E3E"));

    private readonly List<RecommendationProgressVisual> _recommendationProgressSteps = [];
    private IDisposable? _recommendationProgressSubscription;
    private TextBlock? _recommendationProgressTitle;
    private RecommendationPipelineStage? _activeRecommendationStage;
    private bool _recommendationProgressTrackingInstalled;
    private bool _recommendationProgressFailed;

    private void InstallRecommendationProgressTracking()
    {
        if (_recommendationProgressTrackingInstalled || _recommendationLoadingCard is null)
        {
            return;
        }

        _recommendationProgressTrackingInstalled = true;
        CaptureRecommendationProgressVisuals();
        _recommendationProgressSubscription = RecommendationPipelineDiagnostics.Subscribe(stage =>
            Dispatcher.UIThread.Post(() => OnRecommendationPipelineStageChanged(stage)));

        _viewModel.PropertyChanged += RecommendationProgressViewModel_OnPropertyChanged;
        _viewModel.Recommendations.CollectionChanged += (_, args) =>
        {
            if (_viewModel.IsBusy && UsageProfilePanel.IsVisible && args.NewItems is { Count: > 0 })
            {
                SetRecommendationProgressStage(RecommendationPipelineStage.FinalizingRecommendations);
            }
        };

        Closed += (_, _) =>
        {
            _recommendationProgressSubscription?.Dispose();
            _recommendationProgressSubscription = null;
        };

        ResetRecommendationProgress();
    }

    private void CaptureRecommendationProgressVisuals()
    {
        if (_recommendationLoadingCard is null)
        {
            return;
        }

        _recommendationProgressTitle = _recommendationLoadingCard
            .GetLogicalDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(text => string.Equals(
                text.Text,
                "Building your recommendations",
                StringComparison.Ordinal));

        string[] labels =
        [
            "Machine capabilities already analysed",
            "Load the trusted software catalogue",
            "Read installed applications",
            "Apply compatibility, installed-state and profile rules",
            "Finalize the recommendation list"
        ];

        foreach (var labelText in labels)
        {
            var label = _recommendationLoadingCard
                .GetLogicalDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(text => string.Equals(text.Text, labelText, StringComparison.Ordinal));

            if (label?.Parent is not StackPanel row || row.Children.FirstOrDefault() is not TextBlock icon)
            {
                continue;
            }

            _recommendationProgressSteps.Add(new RecommendationProgressVisual(icon, label));
        }
    }

    private void RecommendationProgressViewModel_OnPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainWindowViewModel.SelectedProfile))
        {
            Dispatcher.UIThread.Post(() =>
            {
                ResetRecommendationProgress();
                RefreshRecommendationLoadingCard();
            });
            return;
        }

        if (args.PropertyName is not nameof(MainWindowViewModel.IsBusy))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel.IsBusy && UsageProfilePanel.IsVisible)
            {
                _recommendationProgressFailed = false;
                SetRecommendationProgressStage(RecommendationPipelineStage.LoadingTrustedCatalogue);
                RefreshRecommendationLoadingCard();
                return;
            }

            if (_viewModel.HasRecommendations)
            {
                CompleteRecommendationProgress();
                _recommendationProgressFailed = false;
                RefreshRecommendationLoadingCard();
                return;
            }

            if (UsageProfilePanel.IsVisible &&
                _activeRecommendationStage is not null &&
                _viewModel.RecommendationStatus.Contains("could not", StringComparison.OrdinalIgnoreCase))
            {
                _recommendationProgressFailed = true;
                FailCurrentRecommendationProgressStage();
                RefreshRecommendationLoadingCard();
                return;
            }

            _recommendationProgressFailed = false;
            RefreshRecommendationLoadingCard();
        });
    }

    private void OnRecommendationPipelineStageChanged(RecommendationPipelineStage stage)
    {
        if (!_viewModel.IsBusy || !UsageProfilePanel.IsVisible)
        {
            return;
        }

        SetRecommendationProgressStage(stage);
    }

    private void SetRecommendationProgressStage(RecommendationPipelineStage stage)
    {
        _activeRecommendationStage = stage;
        _recommendationProgressFailed = false;
        if (_recommendationProgressTitle is not null)
        {
            _recommendationProgressTitle.Text = "Building your recommendations";
            _recommendationProgressTitle.Foreground = GuidanceTextBrush;
        }

        var activeIndex = stage switch
        {
            RecommendationPipelineStage.LoadingTrustedCatalogue => 1,
            RecommendationPipelineStage.ReadingInstalledApplications => 2,
            RecommendationPipelineStage.EvaluatingRecommendationRules => 3,
            RecommendationPipelineStage.FinalizingRecommendations => 4,
            _ => 1
        };

        for (var index = 0; index < _recommendationProgressSteps.Count; index++)
        {
            var state = index < activeIndex
                ? RecommendationProgressVisualState.Completed
                : index == activeIndex
                    ? RecommendationProgressVisualState.Active
                    : RecommendationProgressVisualState.Pending;
            ApplyRecommendationProgressVisual(_recommendationProgressSteps[index], state);
        }
    }

    private void CompleteRecommendationProgress()
    {
        _activeRecommendationStage = RecommendationPipelineStage.FinalizingRecommendations;
        foreach (var step in _recommendationProgressSteps)
        {
            ApplyRecommendationProgressVisual(step, RecommendationProgressVisualState.Completed);
        }
    }

    private void FailCurrentRecommendationProgressStage()
    {
        if (_recommendationProgressTitle is not null)
        {
            _recommendationProgressTitle.Text = "Recommendation build stopped";
            _recommendationProgressTitle.Foreground = RecommendationProgressDangerBrush;
        }

        var activeIndex = _activeRecommendationStage switch
        {
            RecommendationPipelineStage.LoadingTrustedCatalogue => 1,
            RecommendationPipelineStage.ReadingInstalledApplications => 2,
            RecommendationPipelineStage.EvaluatingRecommendationRules => 3,
            RecommendationPipelineStage.FinalizingRecommendations => 4,
            _ => 1
        };

        for (var index = 0; index < _recommendationProgressSteps.Count; index++)
        {
            var state = index < activeIndex
                ? RecommendationProgressVisualState.Completed
                : index == activeIndex
                    ? RecommendationProgressVisualState.Failed
                    : RecommendationProgressVisualState.Pending;
            ApplyRecommendationProgressVisual(_recommendationProgressSteps[index], state);
        }
    }

    private void ResetRecommendationProgress()
    {
        _activeRecommendationStage = null;
        _recommendationProgressFailed = false;

        if (_recommendationProgressTitle is not null)
        {
            _recommendationProgressTitle.Text = "Building your recommendations";
            _recommendationProgressTitle.Foreground = GuidanceTextBrush;
        }

        for (var index = 0; index < _recommendationProgressSteps.Count; index++)
        {
            ApplyRecommendationProgressVisual(
                _recommendationProgressSteps[index],
                index == 0
                    ? RecommendationProgressVisualState.Completed
                    : RecommendationProgressVisualState.Pending);
        }
    }

    private static void ApplyRecommendationProgressVisual(
        RecommendationProgressVisual visual,
        RecommendationProgressVisualState state)
    {
        switch (state)
        {
            case RecommendationProgressVisualState.Completed:
                visual.Icon.Text = "✓";
                visual.Icon.Foreground = SuccessBrush;
                visual.Label.Foreground = SuccessBrush;
                visual.Label.FontWeight = FontWeight.Normal;
                break;
            case RecommendationProgressVisualState.Active:
                visual.Icon.Text = "●";
                visual.Icon.Foreground = TealBrush;
                visual.Label.Foreground = GuidanceTextBrush;
                visual.Label.FontWeight = FontWeight.SemiBold;
                break;
            case RecommendationProgressVisualState.Failed:
                visual.Icon.Text = "!";
                visual.Icon.Foreground = RecommendationProgressDangerBrush;
                visual.Label.Foreground = RecommendationProgressDangerBrush;
                visual.Label.FontWeight = FontWeight.SemiBold;
                break;
            default:
                visual.Icon.Text = "○";
                visual.Icon.Foreground = GuidanceMutedBrush;
                visual.Label.Foreground = GuidanceMutedBrush;
                visual.Label.FontWeight = FontWeight.Normal;
                break;
        }
    }

    private sealed record RecommendationProgressVisual(TextBlock Icon, TextBlock Label);

    private enum RecommendationProgressVisualState
    {
        Pending,
        Active,
        Completed,
        Failed
    }
}
