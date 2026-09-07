using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AgenStart.Desktop.Icons;
using AgenStart.Desktop.ViewModels;

namespace AgenStart.Desktop.Views;

public sealed partial class MainWindow
{
    private bool _flowLogoExperienceInstalled;

    private void InstallFlowLogoExperience()
    {
        if (_flowLogoExperienceInstalled)
        {
            return;
        }

        _flowLogoExperienceInstalled = true;

        // These collections are populated just before / while the corresponding
        // flow pages become visible. Defer one UI tick so Avalonia has realized
        // the data templates before decorating their icon tiles.
        _viewModel.ReviewItems.CollectionChanged += (_, _) => QueueFlowLogoRefresh();
        _viewModel.InstallationItems.CollectionChanged += (_, _) => QueueFlowLogoRefresh();
        _viewModel.ReportItems.CollectionChanged += (_, _) => QueueFlowLogoRefresh();

        ReviewButton.Click += (_, _) => QueueFlowLogoRefresh();
        InstallationButton.Click += (_, _) => QueueFlowLogoRefresh();
        ReportButton.Click += (_, _) => QueueFlowLogoRefresh();

        QueueFlowLogoRefresh();
    }

    private void QueueFlowLogoRefresh() =>
        Dispatcher.UIThread.Post(RefreshFlowLogos);

    private void RefreshFlowLogos()
    {
        DecorateFlowLogoTiles(ReviewPanel);
        DecorateFlowLogoTiles(InstallationPanel);
        DecorateFlowLogoTiles(ReportPanel);
    }

    private static void DecorateFlowLogoTiles(Control panel)
    {
        foreach (var tile in panel.GetVisualDescendants().OfType<Border>())
        {
            switch (tile.DataContext)
            {
                case ReviewRowViewModel row:
                    TryDecorateFlowLogoTile(tile, row.ApplicationId, row.Initials);
                    break;
                case InstallationRowViewModel row:
                    TryDecorateFlowLogoTile(tile, row.ApplicationId, row.Initials);
                    break;
                case ReportRowViewModel row:
                    TryDecorateFlowLogoTile(tile, row.ApplicationId, row.Initials);
                    break;
            }
        }
    }

    private static void TryDecorateFlowLogoTile(Border tile, string applicationId, string initials)
    {
        // The first tile in each flow row is currently an initials TextBlock.
        // Only replace that exact tile; once replaced with a Grid this method is idempotent.
        if (tile.Child is not TextBlock initialsText ||
            !string.Equals(initialsText.Text, initials, StringComparison.Ordinal))
        {
            return;
        }

        var icon = AppIconService.Shared.Resolve(applicationId);
        if (icon is null)
        {
            return;
        }

        var host = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        host.Children.Add(new Image
        {
            Source = icon,
            Width = 24,
            Height = 24,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        tile.Child = host;
    }
}
