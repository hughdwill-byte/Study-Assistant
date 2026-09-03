using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Services;

namespace StudyHud.App;

/// <summary>
/// Layouts management page (spec §19, §21): shows the saved HUD panel layouts and lets the user
/// reset panel positions back to defaults. Built in code (no XAML) to keep it self-contained.
/// </summary>
public sealed class LayoutsView : UserControl
{
    private readonly ILayoutService _layouts;
    private readonly IApplicationStateService _appState;
    private readonly ILogger<LayoutsView> _logger;

    private readonly StackPanel _list;
    private readonly TextBlock _status;

    public LayoutsView(
        ILayoutService layouts,
        IApplicationStateService appState,
        ILogger<LayoutsView> logger)
    {
        _layouts = layouts;
        _appState = appState;
        _logger = logger;

        var root = new StackPanel { Margin = new Thickness(4) };

        root.Children.Add(new TextBlock
        {
            Text = "Layouts",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("PrimaryText", Colors.White),
            Margin = new Thickness(0, 0, 0, 8)
        });

        root.Children.Add(new TextBlock
        {
            Text = "Your HUD panel positions are saved automatically per workspace and restored next "
                 + "launch. If a panel ends up off-screen or you want a clean slate, reset the saved "
                 + "layouts below — panels return to their default positions the next time you switch "
                 + "workspace.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(0, 0, 0, 14)
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };

        var refreshBtn = MakeButton("Refresh", transparent: true);
        refreshBtn.Click += (_, _) => _ = RefreshAsync();
        buttons.Children.Add(refreshBtn);

        var resetBtn = MakeButton("Reset panel positions", transparent: false);
        resetBtn.Margin = new Thickness(8, 0, 0, 0);
        resetBtn.Click += (_, _) => _ = ResetAsync();
        buttons.Children.Add(resetBtn);

        root.Children.Add(buttons);

        root.Children.Add(new TextBlock
        {
            Text = "SAVED LAYOUTS",
            FontSize = 10,
            Opacity = 0.5,
            Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(0, 0, 0, 6)
        });

        _list = new StackPanel();
        root.Children.Add(_list);

        _status = new TextBlock
        {
            Opacity = 0.7,
            Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(_status);

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = root
        };

        Loaded += (_, _) => _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var names = await _layouts.GetSavedLayoutNamesAsync();
            _list.Children.Clear();

            if (names.Count == 0)
            {
                _list.Children.Add(new TextBlock
                {
                    Text = "No saved layouts yet — they're created automatically as you use the HUD.",
                    Opacity = 0.6,
                    Foreground = Brush("SecondaryText", Colors.Gray),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            else
            {
                foreach (var name in names)
                    _list.Children.Add(BuildRow(name));
            }

            SetStatus($"Current workspace: {_appState.Current.CurrentWorkspace}.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not read layouts: " + ex.Message);
            _logger.LogWarning(ex, "Listing layouts failed.");
        }
    }

    private UIElement BuildRow(string layoutId)
    {
        var border = new Border
        {
            Background = Brush("SecondaryBackground", Color.FromArgb(180, 40, 40, 48)),
            BorderBrush = Brush("PanelBorder", Color.FromRgb(60, 60, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(12, 8, 12, 8)
        };

        var dock = new DockPanel();

        var del = MakeButton("Delete", transparent: true);
        DockPanel.SetDock(del, Dock.Right);
        del.Click += (_, _) => _ = DeleteAsync(layoutId);
        dock.Children.Add(del);

        dock.Children.Add(new TextBlock
        {
            Text = layoutId,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("PrimaryText", Colors.White)
        });

        border.Child = dock;
        return border;
    }

    private async Task ResetAsync()
    {
        var confirm = MessageBox.Show(
            "Reset all saved panel layouts to defaults? Your notes, courses and settings are not affected.",
            "Reset layouts", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var names = await _layouts.GetSavedLayoutNamesAsync();
            foreach (var name in names)
                await _layouts.DeleteLayoutAsync(name);

            SetStatus("Panel layouts reset. Switch workspace (or relaunch) to see the defaults.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            SetStatus("Reset failed: " + ex.Message);
            _logger.LogWarning(ex, "Resetting layouts failed.");
        }
    }

    private async Task DeleteAsync(string layoutId)
    {
        try
        {
            await _layouts.DeleteLayoutAsync(layoutId);
            SetStatus($"Deleted layout “{layoutId}”.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Could not delete “{layoutId}”: {ex.Message}");
            _logger.LogWarning(ex, "Deleting layout failed.");
        }
    }

    private void SetStatus(string msg) => _status.Text = msg;

    private Button MakeButton(string content, bool transparent)
        => new()
        {
            Content = content,
            Padding = new Thickness(12, 4, 12, 4),
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0),
            Foreground = transparent ? Brush("SecondaryText", Colors.Gray) : Brushes.White,
            Background = transparent
                ? Brushes.Transparent
                : Brush("Accent", Color.FromRgb(0, 180, 255))
        };

    private Brush Brush(string token, Color fallback)
        => TryFindResource(token) as Brush ?? new SolidColorBrush(fallback);
}
