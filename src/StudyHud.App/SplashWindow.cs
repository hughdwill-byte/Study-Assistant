using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace StudyHud.App;

/// <summary>
/// A lightweight branded launch screen shown for a couple of seconds while the app initialises.
/// Fully self-contained (no theme resources, no DI) so it can appear before the host is built.
/// </summary>
public sealed class SplashWindow : Window
{
    public SplashWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = 440;
        Height = 300;
        Opacity = 0;

        var bg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        bg.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#0E1B2C"), 0.0));
        bg.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#080D16"), 1.0));

        var cyan = (Color)ColorConverter.ConvertFromString("#00B4FF");

        var card = new Border
        {
            CornerRadius = new CornerRadius(22),
            Background = bg,
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 0, 180, 255)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(18),
            Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Direction = 270, Color = Colors.Black, Opacity = 0.55 }
        };

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Logo mark
        try
        {
            var img = new Image
            {
                Width = 104, Height = 104,
                Source = new BitmapImage(new Uri("pack://application:,,,/Assets/studyhud-icon.png")),
                Margin = new Thickness(0, 0, 0, 14),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            stack.Children.Add(img);
        }
        catch { /* image is decorative — never block startup on it */ }

        // Wordmark: STUDY (white) + HUD (cyan)
        var word = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        word.Children.Add(new TextBlock
        {
            Text = "STUDY ", FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Brushes.White
        });
        word.Children.Add(new TextBlock
        {
            Text = "HUD", FontSize = 30, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(cyan)
        });
        stack.Children.Add(word);

        stack.Children.Add(new TextBlock
        {
            Text = "NOTE FINDER  ·  NON-AI",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9B, 0xB0, 0xC6)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 16)
        });

        // Indeterminate loading bar
        stack.Children.Add(new ProgressBar
        {
            IsIndeterminate = true,
            Width = 220, Height = 4,
            Foreground = new SolidColorBrush(cyan),
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(0)
        });

        card.Child = stack;
        Content = card;

        Loaded += (_, _) => BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
    }

    /// <summary>Fades the splash out and closes it.</summary>
    public void FadeOutAndClose()
    {
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(260));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
