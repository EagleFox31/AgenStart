using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AgenStart.Desktop.Views;

public sealed partial class MainWindow
{
    private static readonly Uri AgenStartIconUri = new("avares://AgenStart.Desktop/Assets/agenstart-app-icon.png");

    private void ApplyBranding()
    {
        TryApplyWindowIcon();
        TryApplySidebarBrandLockup();
    }

    private void TryApplyWindowIcon()
    {
        try
        {
            using var iconStream = AssetLoader.Open(AgenStartIconUri);
            Icon = new WindowIcon(iconStream);
        }
        catch (Exception exception)
        {
            Trace.TraceWarning("AgenStart window icon could not be loaded: {0}", exception.Message);
        }
    }

    private void TryApplySidebarBrandLockup()
    {
        try
        {
            var productName = this.GetLogicalDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(text => string.Equals(text.Text, "AgenStart", StringComparison.Ordinal));

            if (productName?.Parent is not StackPanel brandHost || brandHost.Tag as string == "agenstart-brand-lockup")
            {
                return;
            }

            using var imageStream = AssetLoader.Open(AgenStartIconUri);
            var mark = new Image
            {
                Source = new Bitmap(imageStream),
                Width = 46,
                Height = 46,
                Stretch = Stretch.Uniform
            };

            var words = new StackPanel
            {
                Spacing = 2,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            words.Children.Add(new TextBlock
            {
                Text = "AgenStart",
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeight.SemiBold
            });
            words.Children.Add(new TextBlock
            {
                Text = "BY AGENSTUDIO",
                Foreground = new SolidColorBrush(Color.Parse("#45CFC1")),
                FontSize = 9.5,
                FontWeight = FontWeight.SemiBold,
                LetterSpacing = 2.1
            });

            var lockup = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 12
            };
            lockup.Children.Add(mark);
            lockup.Children.Add(words);

            brandHost.Children.Clear();
            brandHost.Children.Add(lockup);
            brandHost.Tag = "agenstart-brand-lockup";
        }
        catch (Exception exception)
        {
            Trace.TraceWarning("AgenStart sidebar branding could not be loaded: {0}", exception.Message);
        }
    }
}
