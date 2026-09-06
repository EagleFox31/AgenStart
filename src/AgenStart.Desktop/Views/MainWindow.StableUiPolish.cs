using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;

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

    private void ApplyStableUiPolish()
    {
        if (_stableUiPolishInstalled)
        {
            return;
        }

        _stableUiPolishInstalled = true;
        NormalizeSidebarNavigation();
        DecorateMachineStatusRows();

        // Recommendation progress belongs exclusively to UsageProfilesView.
        // Do not inject or re-parent its ProgressBar here: doing so creates a
        // second label/percentage row for the exact same pipeline state.
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
}
