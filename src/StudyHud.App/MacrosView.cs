using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StudyHud.Macros.Models;
using StudyHud.Macros.Services;

namespace StudyHud.App;

/// <summary>
/// Macros page (spec §29, §30): shows the loaded macro profiles and their macros and which profile
/// is active. Editing/creating macros is a later milestone; this surfaces the current state honestly
/// rather than leaving the sidebar button dead. Built in code (no XAML).
/// </summary>
public sealed class MacrosView : UserControl
{
    private readonly MacroEngine _engine;
    private readonly StackPanel _body;

    public MacrosView(MacroEngine engine)
    {
        _engine = engine;

        var root = new StackPanel { Margin = new Thickness(4) };

        root.Children.Add(new TextBlock
        {
            Text = "Macros",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("PrimaryText", Colors.White),
            Margin = new Thickness(0, 0, 0, 8)
        });

        root.Children.Add(new TextBlock
        {
            Text = "Macros run key/mouse actions from a trigger, scoped by workspace and app. Profiles "
                 + "group macros and can switch automatically with your workspace.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(0, 0, 0, 14)
        });

        _body = new StackPanel();
        root.Children.Add(_body);

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = root
        };

        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        _body.Children.Clear();

        var profiles = _engine.Profiles;
        var macros = _engine.Macros;

        if (profiles.Count == 0 && macros.Count == 0)
        {
            _body.Children.Add(new Border
            {
                Background = Brush("SecondaryBackground", Color.FromArgb(180, 40, 40, 48)),
                BorderBrush = Brush("PanelBorder", Color.FromRgb(60, 60, 70)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 10, 12, 10),
                Child = new TextBlock
                {
                    Text = "No macros are configured yet. The macro engine is running, but no profiles "
                         + "have been loaded. A macro editor to create and bind macros is the next step "
                         + "for this page.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.8,
                    Foreground = Brush("PrimaryText", Colors.White)
                }
            });
            return;
        }

        _body.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(_engine.ActiveProfileId)
                ? "Active profile: none"
                : $"Active profile: {_engine.ActiveProfileId}",
            Opacity = 0.7,
            Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(0, 0, 0, 10)
        });

        foreach (var profile in profiles)
            _body.Children.Add(BuildProfileCard(profile, macros));
    }

    private UIElement BuildProfileCard(MacroProfile profile, IReadOnlyList<MacroDefinition> allMacros)
    {
        var card = new Border
        {
            Background = Brush("SecondaryBackground", Color.FromArgb(180, 40, 40, 48)),
            BorderBrush = Brush("PanelBorder", Color.FromRgb(60, 60, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 10, 12, 10)
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = profile.Name + (profile.ProfileId == _engine.ActiveProfileId ? "  (active)" : ""),
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("PrimaryText", Colors.White)
        });

        foreach (var id in profile.MacroIds)
        {
            var macro = allMacros.FirstOrDefault(m => m.Id == id);
            stack.Children.Add(new TextBlock
            {
                Text = "• " + (macro?.Name ?? id) + (macro is { Enabled: false } ? "  (disabled)" : ""),
                FontSize = 12,
                Opacity = 0.8,
                Foreground = Brush("SecondaryText", Colors.Gray),
                Margin = new Thickness(8, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }

        card.Child = stack;
        return card;
    }

    private Brush Brush(string token, Color fallback)
        => TryFindResource(token) as Brush ?? new SolidColorBrush(fallback);
}
