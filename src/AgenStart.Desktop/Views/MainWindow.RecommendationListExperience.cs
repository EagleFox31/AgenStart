using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using AgenStart.Desktop.ViewModels;

namespace AgenStart.Desktop.Views;

public sealed partial class MainWindow
{
    private static readonly IBrush RecommendationStickyBackground =
        new SolidColorBrush(Color.Parse("#F7F5EF"));

    private bool _recommendationListExperienceInstalled;
    private ScrollViewer? _recommendationsScrollViewer;
    private Border? _recommendationStickyHeader;
    private TextBlock? _recommendationStickyCount;
    private TextBlock? _recommendationStickySelected;

    private void InstallRecommendationListExperience()
    {
        if (_recommendationListExperienceInstalled)
        {
            return;
        }

        _recommendationsScrollViewer = this.GetLogicalDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault(scroll => scroll.GetLogicalDescendants()
                .Any(control => ReferenceEquals(control, RecommendationsPanel)));

        if (_recommendationsScrollViewer?.Parent is not Grid contentRoot)
        {
            return;
        }

        _recommendationListExperienceInstalled = true;
        _recommendationStickyHeader = BuildRecommendationStickyHeader();
        contentRoot.Children.Add(_recommendationStickyHeader);

        _recommendationsScrollViewer.ScrollChanged += RecommendationsScrollViewer_OnScrollChanged;
        _viewModel.PropertyChanged += RecommendationListViewModel_OnPropertyChanged;
        _viewModel.Recommendations.CollectionChanged += (_, _) =>
            Dispatcher.UIThread.Post(RefreshRecommendationListExperience);

        RecommendationsButton.Click += (_, _) =>
            Dispatcher.UIThread.Post(RefreshRecommendationListExperience);

        var backToRecommendations = FindButtonByContent("Back to recommendations");
        if (backToRecommendations is not null)
        {
            backToRecommendations.Click += (_, _) =>
                Dispatcher.UIThread.Post(RefreshRecommendationListExperience);
        }

        Closed += (_, _) =>
        {
            if (_recommendationsScrollViewer is not null)
            {
                _recommendationsScrollViewer.ScrollChanged -= RecommendationsScrollViewer_OnScrollChanged;
            }

            _viewModel.PropertyChanged -= RecommendationListViewModel_OnPropertyChanged;
        };

        RefreshRecommendationListExperience();
    }

    private Border BuildRecommendationStickyHeader()
    {
        _recommendationStickyCount = new TextBlock
        {
            Foreground = GuidanceMutedBrush,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        _recommendationStickySelected = new TextBlock
        {
            Foreground = TealBrush,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var summary = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        summary.Children.Add(_recommendationStickyCount);
        summary.Children.Add(_recommendationStickySelected);

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 24
        };
        header.Children.Add(new TextBlock
        {
            Text = "Recommended for this PC",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Foreground = GuidanceTextBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(summary, 1);
        header.Children.Add(summary);

        return new Border
        {
            IsVisible = false,
            Background = RecommendationStickyBackground,
            BorderBrush = ProfileDefaultBorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 13),
            Margin = new Thickness(72, 0, 72, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = header
        };
    }

    private void RecommendationsScrollViewer_OnScrollChanged(object? sender, ScrollChangedEventArgs e) =>
        UpdateRecommendationStickyHeader();

    private void RecommendationListViewModel_OnPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.RecommendationCount)
            or nameof(MainWindowViewModel.SelectedCount)
            or nameof(MainWindowViewModel.HasRecommendations))
        {
            Dispatcher.UIThread.Post(() =>
            {
                RefreshRecommendationStickySummary();
                UpdateRecommendationStickyHeader();
            });
        }
    }

    private void RefreshRecommendationListExperience()
    {
        DecorateRecommendationRowsWithOrder();
        RefreshRecommendationStickySummary();
        UpdateRecommendationStickyHeader();
    }

    private void DecorateRecommendationRowsWithOrder()
    {
        var rows = RecommendationsPanel.GetLogicalDescendants()
            .OfType<Border>()
            .Where(border =>
                border.DataContext is RecommendationRowViewModel &&
                border.Child is Grid grid &&
                grid.ColumnDefinitions.Count == 4)
            .ToArray();

        foreach (var card in rows)
        {
            if (card.DataContext is not RecommendationRowViewModel row || card.Child is not Grid grid)
            {
                continue;
            }

            var position = _viewModel.Recommendations.IndexOf(row);
            if (position < 0)
            {
                continue;
            }

            var orderLabel = grid.Children
                .OfType<TextBlock>()
                .FirstOrDefault(text => text.Classes.Contains("recommendationOrder"));

            if (orderLabel is null)
            {
                orderLabel = new TextBlock
                {
                    Width = 20,
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = GuidanceMutedBrush,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Left
                };
                orderLabel.Classes.Add("recommendationOrder");
                Grid.SetColumn(orderLabel, 0);
                grid.Children.Add(orderLabel);

                var logoTile = grid.Children
                    .OfType<Border>()
                    .FirstOrDefault(border => Grid.GetColumn(border) == 0);
                if (logoTile is not null)
                {
                    logoTile.Width = 40;
                    logoTile.Height = 40;
                    logoTile.HorizontalAlignment = HorizontalAlignment.Right;
                }
            }

            orderLabel.Text = $"{position + 1:00}";
        }
    }

    private void RefreshRecommendationStickySummary()
    {
        if (_recommendationStickyCount is not null)
        {
            _recommendationStickyCount.Text = $"{_viewModel.RecommendationCount} apps";
        }

        if (_recommendationStickySelected is not null)
        {
            _recommendationStickySelected.Text = $"{_viewModel.SelectedCount} selected";
        }
    }

    private void UpdateRecommendationStickyHeader()
    {
        if (_recommendationStickyHeader is null || _recommendationsScrollViewer is null)
        {
            return;
        }

        _recommendationStickyHeader.IsVisible =
            RecommendationsPanel.IsVisible &&
            _viewModel.HasRecommendations &&
            _recommendationsScrollViewer.Offset.Y >= 118;
    }
}
