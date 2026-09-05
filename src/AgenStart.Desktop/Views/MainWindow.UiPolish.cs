using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using AgenStart.Core.Catalogue;
using AgenStart.Desktop.ViewModels;

namespace AgenStart.Desktop.Views;

public sealed partial class MainWindow
{
    private static readonly IBrush ProfileHoverBrush = new SolidColorBrush(Color.Parse("#EEF3F1"));
    private static readonly IBrush ProfileSelectedBrush = new SolidColorBrush(Color.Parse("#E7F1EE"));
    private static readonly IBrush ProfileDefaultBorderBrush = new SolidColorBrush(Color.Parse("#D9DEDC"));
    private static readonly IBrush ProfileHoverBorderBrush = new SolidColorBrush(Color.Parse("#9EBAB4"));
    private static readonly IBrush ProfileSelectedBorderBrush = new SolidColorBrush(Color.Parse("#176D64"));
    private static readonly IBrush GuidanceBackgroundBrush = new SolidColorBrush(Color.Parse("#EEF3F1"));
    private static readonly IBrush GuidanceMutedBrush = new SolidColorBrush(Color.Parse("#69747A"));
    private static readonly IBrush GuidanceTextBrush = new SolidColorBrush(Color.Parse("#10242B"));
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#2F7D5B"));
    private static readonly IBrush TealBrush = new SolidColorBrush(Color.Parse("#176D64"));

    private bool _uiPolishApplied;
    private TextBlock? _profileGuidanceText;
    private TextBlock? _profileSummaryIcon;
    private Border? _recommendationLoadingCard;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Dynamic visual-tree decoration is deliberately deferred from startup.
        // The packaged Windows launch smoke test must prove the base shell is stable
        // before optional polish is reintroduced one safe piece at a time.
        _uiPolishApplied = true;
    }

    private void PolishMachineRows()
    {
        foreach (var row in YourPcPanel
                     .GetVisualDescendants()
                     .OfType<Border>()
                     .Where(border => border.Classes.Contains("dataRow")))
        {
            row.ClipToBounds = true;
            if (row.Child is not Grid grid || grid.ColumnDefinitions.Count < 3)
            {
                continue;
            }

            grid.ColumnSpacing = 14;
            grid.ColumnDefinitions[2].Width = new GridLength(132);

            foreach (var textBlock in grid.Children.OfType<TextBlock>())
            {
                textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
                textBlock.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            }
        }
    }

    private void PolishUsageProfiles()
    {
        foreach (var radio in UsageProfilePanel.GetLogicalDescendants().OfType<RadioButton>())
        {
            if (radio.Parent is not Border card)
            {
                continue;
            }

            card.BorderThickness = new Avalonia.Thickness(1);
            card.CornerRadius = new Avalonia.CornerRadius(8);

            card.PointerEntered += (_, _) =>
            {
                if (radio.IsChecked != true)
                {
                    card.Background = ProfileHoverBrush;
                    card.BorderBrush = ProfileHoverBorderBrush;
                }
            };

            card.PointerExited += (_, _) => RefreshProfileCardState(radio, card);
            radio.Click += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                RefreshProfileCardStates();
                RefreshProfileGuidance();
            });
        }

        _profileSummaryIcon = UsageProfilePanel
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(text => string.Equals(text.Text, "</>", StringComparison.Ordinal));

        RefreshProfileCardStates();
        RefreshProfileGuidance();
    }

    private void RefreshProfileCardStates()
    {
        foreach (var radio in UsageProfilePanel.GetLogicalDescendants().OfType<RadioButton>())
        {
            if (radio.Parent is Border card)
            {
                RefreshProfileCardState(radio, card);
            }
        }
    }

    private static void RefreshProfileCardState(RadioButton radio, Border card)
    {
        if (radio.IsChecked == true)
        {
            card.Background = ProfileSelectedBrush;
            card.BorderBrush = ProfileSelectedBorderBrush;
            card.BorderThickness = new Avalonia.Thickness(1.5);
        }
        else
        {
            card.Background = Brushes.Transparent;
            card.BorderBrush = ProfileDefaultBorderBrush;
            card.BorderThickness = new Avalonia.Thickness(1);
        }
    }

    private void AddProfileGuidance()
    {
        var selectedLabel = UsageProfilePanel
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(text => string.Equals(text.Text, "Selected:", StringComparison.Ordinal));

        if (selectedLabel?.Parent is not StackPanel summary || _profileGuidanceText is not null)
        {
            return;
        }

        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(new TextBlock
        {
            Text = "AgenStart will prioritize",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = GuidanceTextBrush
        });

        _profileGuidanceText = new TextBlock
        {
            FontSize = 13,
            Foreground = GuidanceMutedBrush,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20
        };
        content.Children.Add(_profileGuidanceText);

        summary.Children.Add(new Border
        {
            Background = GuidanceBackgroundBrush,
            CornerRadius = new Avalonia.CornerRadius(8),
            Padding = new Avalonia.Thickness(14, 12),
            Margin = new Avalonia.Thickness(0, 12, 0, 0),
            Child = content
        });

        RefreshProfileGuidance();
    }

    private void RefreshProfileGuidance()
    {
        if (_profileGuidanceText is not null)
        {
            _profileGuidanceText.Text = _viewModel.SelectedProfile switch
            {
                UserProfile.Personal => "Browsers · communication · media · everyday utilities",
                UserProfile.Development => "Editors & IDEs · terminals · Git · databases · developer utilities",
                UserProfile.Business => "Office · communication · productivity · collaboration",
                UserProfile.Creation => "Design · media · content · creative utilities",
                UserProfile.Training => "Learning tools · browsers · document utilities · guided study",
                _ => "Tools that fit the way you use this PC"
            };
        }

        if (_profileSummaryIcon is not null)
        {
            _profileSummaryIcon.Text = _viewModel.SelectedProfile switch
            {
                UserProfile.Personal => "⌂",
                UserProfile.Development => "</>",
                UserProfile.Business => "▦",
                UserProfile.Creation => "✦",
                UserProfile.Training => "◎",
                _ => "○"
            };
        }
    }

    private void AddRecommendationLoadingCard()
    {
        var progressBar = UsageProfilePanel.GetVisualDescendants().OfType<ProgressBar>().FirstOrDefault();
        if (progressBar?.Parent is not StackPanel leftColumn || _recommendationLoadingCard is not null)
        {
            return;
        }

        progressBar.IsVisible = false;

        var steps = new StackPanel { Spacing = 8 };
        steps.Children.Add(BuildLoadingStep("✓", "Machine capabilities already analysed", SuccessBrush));
        steps.Children.Add(BuildLoadingStep("○", "Load the trusted software catalogue", GuidanceMutedBrush));
        steps.Children.Add(BuildLoadingStep("○", "Read installed applications", GuidanceMutedBrush));
        steps.Children.Add(BuildLoadingStep("○", "Apply compatibility, installed-state and profile rules", GuidanceMutedBrush));
        steps.Children.Add(BuildLoadingStep("○", "Finalize the recommendation list", GuidanceMutedBrush));

        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = "Building your recommendations",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = GuidanceTextBrush
        });
        content.Children.Add(new TextBlock
        {
            Text = "AgenStart shows the real pipeline phase currently in progress. No fake percentage is used.",
            FontSize = 13,
            Foreground = GuidanceMutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 4)
        });
        content.Children.Add(steps);

        _recommendationLoadingCard = new Border
        {
            Background = GuidanceBackgroundBrush,
            BorderBrush = ProfileDefaultBorderBrush,
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(8),
            Padding = new Avalonia.Thickness(16, 14),
            Margin = new Avalonia.Thickness(0, 16, 0, 0),
            IsVisible = false,
            Child = content
        };

        leftColumn.Children.Add(_recommendationLoadingCard);
        RefreshRecommendationLoadingCard();
    }

    private static Control BuildLoadingStep(string icon, string text, IBrush brush)
    {
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 9 };
        row.Children.Add(new TextBlock
        {
            Text = icon,
            Width = 18,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
            Foreground = brush,
            FontWeight = FontWeight.SemiBold
        });
        row.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = brush,
            FontSize = 13,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        return row;
    }

    private void RefreshRecommendationLoadingCard()
    {
        if (_recommendationLoadingCard is not null)
        {
            _recommendationLoadingCard.IsVisible = UsageProfilePanel.IsVisible &&
                                                   (_viewModel.IsBusy || _recommendationProgressFailed);
        }
    }

    private void ApplyRecommendationStatusVisuals()
    {
        if (!RecommendationsPanel.IsVisible)
        {
            return;
        }

        foreach (var textBlock in RecommendationsPanel.GetVisualDescendants().OfType<TextBlock>())
        {
            if (textBlock.DataContext is not RecommendationRowViewModel row)
            {
                continue;
            }

            if (!string.Equals(textBlock.Text, row.Status, StringComparison.Ordinal))
            {
                continue;
            }

            textBlock.Text = $"{row.StatusIcon}  {row.Status}";
            textBlock.Foreground = row.StatusBrush;
            textBlock.FontWeight = FontWeight.SemiBold;
            textBlock.FontSize = 13;
        }
    }
}
