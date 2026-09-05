using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using AgenStart.Core.Recommendations;
using AgenStart.Desktop.ViewModels;

namespace AgenStart.Desktop.Views;

public sealed partial class MainWindow
{
    private static readonly IBrush StableSoftSuccessBrush = new SolidColorBrush(Color.Parse("#E7F3EC"));
    private static readonly IBrush StableSuccessBorderBrush = new SolidColorBrush(Color.Parse("#B9D8C5"));

    private bool _stableUiPolishInstalled;
    private IDisposable? _stableRecommendationProgressSubscription;
    private StackPanel? _stableRecommendationProgressBlock;
    private ProgressBar? _stableRecommendationProgressBar;
    private TextBlock? _stableRecommendationProgressLabel;
    private TextBlock? _stableRecommendationProgressPercent;

    private void ApplyStableUiPolish()
    {
        if (_stableUiPolishInstalled)
        {
            return;
        }

        _stableUiPolishInstalled = true;
        NormalizeSidebarNavigation();
        DecorateMachineStatusRows();
        InstallStableRecommendationProgress();
    }

    private void NormalizeSidebarNavigation()
    {
        foreach (var button in this.GetLogicalDescendants()
                     .OfType<Button>()
                     .Where(button => button.Classes.Contains("nav")))
        {
            button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            button.Padding = new Thickness(18, 0);

            if (button.Content is not StackPanel stack ||
                stack.Orientation != Avalonia.Layout.Orientation.Horizontal ||
                stack.Children.Count < 2 ||
                stack.Children[0] is not TextBlock icon ||
                stack.Children[1] is not TextBlock label)
            {
                continue;
            }

            // Use one fixed icon rail and one fixed text start for every navigation item.
            stack.Spacing = 0;
            icon.Width = 34;
            icon.MinWidth = 34;
            icon.FontFamily = new FontFamily("Segoe UI Symbol");
            icon.FontSize = 19;
            icon.LineHeight = 24;
            icon.TextAlignment = TextAlignment.Center;
            icon.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            icon.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            icon.Margin = new Thickness(0);

            label.Margin = new Thickness(12, 0, 0, 0);
            label.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            label.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        }
    }

    private void DecorateMachineStatusRows()
    {
        foreach (var row in YourPcPanel.GetLogicalDescendants()
                     .OfType<Border>()
                     .Where(border => border.Classes.Contains("dataRow")))
        {
            row.ClipToBounds = true;

            if (row.Child is not Grid grid || grid.ColumnDefinitions.Count < 3)
            {
                continue;
            }

            grid.ColumnSpacing = 16;
            grid.ColumnDefinitions[0].Width = new GridLength(46);
            grid.ColumnDefinitions[2].Width = new GridLength(132);

            var icon = grid.Children
                .OfType<TextBlock>()
                .FirstOrDefault(text => Grid.GetColumn(text) == 0);

            if (icon is not null)
            {
                icon.Width = 28;
                icon.FontSize = icon.Text == "64" ? 13 : 19;
                icon.TextAlignment = TextAlignment.Center;
                icon.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
                icon.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
                icon.Foreground = GuidanceTextBrush;
            }

            var value = grid.Children
                .OfType<TextBlock>()
                .FirstOrDefault(text => Grid.GetColumn(text) == 1);

            if (value is not null)
            {
                value.TextTrimming = TextTrimming.CharacterEllipsis;
                value.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            }

            var status = grid.Children
                .OfType<TextBlock>()
                .FirstOrDefault(text => Grid.GetColumn(text) == 2);

            if (status is null)
            {
                continue;
            }

            var statusText = status.Text ?? string.Empty;
            var badgeText = new TextBlock
            {
                Text = $"✓  {statusText}",
                Foreground = SuccessBrush,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            var badge = new Border
            {
                Background = StableSoftSuccessBrush,
                BorderBrush = StableSuccessBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(10, 5),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Child = badgeText
            };

            Grid.SetColumn(badge, 2);
            grid.Children.Remove(status);
            grid.Children.Add(badge);
        }
    }

    private void InstallStableRecommendationProgress()
    {
        if (_stableRecommendationProgressSubscription is not null)
        {
            return;
        }

        var existingProgressBar = UsageProfilePanel.GetLogicalDescendants()
            .OfType<ProgressBar>()
            .FirstOrDefault();

        if (existingProgressBar?.Parent is not StackPanel parent)
        {
            return;
        }

        var index = parent.Children.IndexOf(existingProgressBar);
        if (index < 0)
        {
            return;
        }

        parent.Children.RemoveAt(index);

        existingProgressBar.IsIndeterminate = false;
        existingProgressBar.Minimum = 0;
        existingProgressBar.Maximum = 100;
        existingProgressBar.Value = 0;
        existingProgressBar.Height = 6;
        existingProgressBar.Margin = new Thickness(0);
        existingProgressBar.IsVisible = true;

        _stableRecommendationProgressLabel = new TextBlock
        {
            Text = "Preparing recommendation pipeline…",
            Foreground = GuidanceMutedBrush,
            FontSize = 13,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        _stableRecommendationProgressPercent = new TextBlock
        {
            Text = "0%",
            Foreground = GuidanceTextBrush,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12
        };
        header.Children.Add(_stableRecommendationProgressLabel);
        Grid.SetColumn(_stableRecommendationProgressPercent, 1);
        header.Children.Add(_stableRecommendationProgressPercent);

        _stableRecommendationProgressBlock = new StackPanel
        {
            Spacing = 7,
            Margin = new Thickness(0, 16, 80, 0),
            IsVisible = false
        };
        _stableRecommendationProgressBlock.Children.Add(header);
        _stableRecommendationProgressBlock.Children.Add(existingProgressBar);
        _stableRecommendationProgressBar = existingProgressBar;

        parent.Children.Insert(index, _stableRecommendationProgressBlock);

        _stableRecommendationProgressSubscription = RecommendationPipelineDiagnostics.Subscribe(stage =>
            Dispatcher.UIThread.Post(() => UpdateStableRecommendationProgress(stage)));

        _viewModel.PropertyChanged += StableRecommendationProgressViewModel_OnPropertyChanged;
        _viewModel.Recommendations.CollectionChanged += (_, args) =>
        {
            if (_viewModel.IsBusy && args.NewItems is { Count: > 0 })
            {
                Dispatcher.UIThread.Post(() => SetStableRecommendationProgress(
                    95,
                    "Finalizing recommendation list…"));
            }
        };

        Closed += (_, _) =>
        {
            _stableRecommendationProgressSubscription?.Dispose();
            _stableRecommendationProgressSubscription = null;
            _viewModel.PropertyChanged -= StableRecommendationProgressViewModel_OnPropertyChanged;
        };
    }

    private void StableRecommendationProgressViewModel_OnPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not nameof(MainWindowViewModel.IsBusy))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_stableRecommendationProgressBlock is null)
            {
                return;
            }

            if (_viewModel.IsBusy && UsageProfilePanel.IsVisible)
            {
                _stableRecommendationProgressBlock.IsVisible = true;
                SetStableRecommendationProgress(10, "Starting recommendation analysis…");
                return;
            }

            if (_viewModel.HasRecommendations)
            {
                SetStableRecommendationProgress(100, "Recommendations ready");
            }

            _stableRecommendationProgressBlock.IsVisible = false;
        });
    }

    private void UpdateStableRecommendationProgress(RecommendationPipelineStage stage)
    {
        if (!_viewModel.IsBusy || !UsageProfilePanel.IsVisible || _stableRecommendationProgressBlock is null)
        {
            return;
        }

        _stableRecommendationProgressBlock.IsVisible = true;

        switch (stage)
        {
            case RecommendationPipelineStage.LoadingTrustedCatalogue:
                SetStableRecommendationProgress(25, "Loading trusted software catalogue…");
                break;
            case RecommendationPipelineStage.ReadingInstalledApplications:
                SetStableRecommendationProgress(50, "Reading installed applications…");
                break;
            case RecommendationPipelineStage.EvaluatingRecommendationRules:
                SetStableRecommendationProgress(75, "Applying compatibility and profile rules…");
                break;
            case RecommendationPipelineStage.FinalizingRecommendations:
                SetStableRecommendationProgress(90, "Finalizing recommendation list…");
                break;
        }
    }

    private void SetStableRecommendationProgress(double value, string label)
    {
        if (_stableRecommendationProgressBar is null ||
            _stableRecommendationProgressLabel is null ||
            _stableRecommendationProgressPercent is null)
        {
            return;
        }

        var bounded = Math.Clamp(value, 0, 100);
        _stableRecommendationProgressBar.Value = bounded;
        _stableRecommendationProgressLabel.Text = label;
        _stableRecommendationProgressPercent.Text = $"{bounded:0}%";
    }
}
