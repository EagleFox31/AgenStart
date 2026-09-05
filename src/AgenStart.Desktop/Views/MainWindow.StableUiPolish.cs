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
    private static readonly IBrush StableWarningBrush = new SolidColorBrush(Color.Parse("#9A6700"));
    private static readonly IBrush StableSoftWarningBrush = new SolidColorBrush(Color.Parse("#FFF4D6"));
    private static readonly IBrush StableWarningBorderBrush = new SolidColorBrush(Color.Parse("#E5C66B"));
    private static readonly IBrush StableDangerBrush = new SolidColorBrush(Color.Parse("#B42318"));
    private static readonly IBrush StableSoftDangerBrush = new SolidColorBrush(Color.Parse("#FDECEA"));
    private static readonly IBrush StableDangerBorderBrush = new SolidColorBrush(Color.Parse("#F1B5AE"));

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
        var buttons = this.GetLogicalDescendants()
            .OfType<Button>()
            .Where(button => button.Classes.Contains("nav"))
            .ToList();

        foreach (var button in buttons)
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

            // One fixed icon rail + one fixed text start for every navigation item.
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
        var rows = YourPcPanel.GetLogicalDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("dataRow"))
            .ToList();

        foreach (var row in rows)
        {
            row.ClipToBounds = true;

            if (row.Child is not Grid grid || grid.ColumnDefinitions.Count < 3)
            {
                continue;
            }

            grid.ColumnSpacing = 16;
            grid.ColumnDefinitions[0].Width = new GridLength(46);
            grid.ColumnDefinitions[2].Width = new GridLength(150);

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

            // Keep the original bound TextBlock alive so status changes remain live after analysis.
            grid.Children.Remove(status);
            status.FontSize = 13;
            status.FontWeight = FontWeight.SemiBold;
            status.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;

            var statusIcon = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            var badgeContent = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 6
            };
            badgeContent.Children.Add(statusIcon);
            badgeContent.Children.Add(status);

            var badge = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(10, 5),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Child = badgeContent
            };

            void RefreshSemanticBadge()
            {
                var text = status.Text ?? string.Empty;
                var lower = text.ToLowerInvariant();

                if (lower.Contains("unavailable") || lower.Contains("failed") || lower.Contains("unsupported") || lower.Contains("missing"))
                {
                    statusIcon.Text = "×";
                    statusIcon.Foreground = StableDangerBrush;
                    status.Foreground = StableDangerBrush;
                    badge.Background = StableSoftDangerBrush;
                    badge.BorderBrush = StableDangerBorderBrush;
                    return;
                }

                if (lower.Contains("limited") || lower.Contains("warning") || lower.Contains("unknown"))
                {
                    statusIcon.Text = "!";
                    statusIcon.Foreground = StableWarningBrush;
                    status.Foreground = StableWarningBrush;
                    badge.Background = StableSoftWarningBrush;
                    badge.BorderBrush = StableWarningBorderBrush;
                    return;
                }

                statusIcon.Text = "✓";
                statusIcon.Foreground = SuccessBrush;
                status.Foreground = SuccessBrush;
                badge.Background = StableSoftSuccessBrush;
                badge.BorderBrush = StableSuccessBorderBrush;
            }

            status.PropertyChanged += (_, args) =>
            {
                if (args.Property == TextBlock.TextProperty)
                {
                    RefreshSemanticBadge();
                }
            };

            RefreshSemanticBadge();
            Grid.SetColumn(badge, 2);
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
