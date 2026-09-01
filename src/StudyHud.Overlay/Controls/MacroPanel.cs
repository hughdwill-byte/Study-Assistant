using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StudyHud.Core.Models;
using StudyHud.Core.Services;

namespace StudyHud.Overlay.Controls;

/// <summary>
/// The Note Taking workspace macro panel (spec §24).
/// Shows configurable macro trigger buttons.
/// Compact: icon buttons only.
/// Normal: icon + label.
/// Expanded: icon + label + description.
/// </summary>
public sealed class MacroPanel : HudPanelBase
{
    private readonly IApplicationStateService _appState;
    private StackPanel _buttonStack = null!;
    private PanelResponsiveState _responsive = PanelResponsiveState.Normal;

    public MacroPanel(IApplicationStateService appState, IThemeService theme)
        : base("macro-panel", appState, theme)
    {
        _appState = appState;
        MinWidth = 80;
        MinHeight = 60;
        Width = 200;
        Height = 240;
    }

    protected override string PanelTitle => "Macros";

    protected override void PopulateContent(Grid contentGrid)
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        _buttonStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(8, 6, 8, 6)
        };

        scroll.Content = _buttonStack;
        contentGrid.Children.Add(scroll);

        // Add placeholder macro buttons — will be populated from MacroEngine in Phase 5
        AddMacroButton("📸 Screenshot", "Hold Mouse 5 to capture");
        AddMacroButton("📝 New Note", "Ctrl+Alt+N");
        AddMacroButton("🔍 Question Finder", "Switch workspace");
        AddMacroButton("📋 Paste Image", "Ctrl+V into Notion");
    }

    private void AddMacroButton(string label, string description)
    {
        var btn = new Button
        {
            Content = label,
            ToolTip = description,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(8, 5, 8, 5),
            FontSize = 11,
            Background = Application.Current.TryFindResource("SecondaryBackground") as Brush
                         ?? new SolidColorBrush(Color.FromArgb(200, 40, 40, 48)),
            Foreground = Application.Current.TryFindResource("PrimaryText") as Brush
                         ?? Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };

        btn.Click += (_, _) =>
        {
            // Phase 5 will wire this to the actual MacroEngine
        };

        _buttonStack.Children.Add(btn);
    }

    protected override void OnResponsiveLayoutChanged(PanelResponsiveState state)
    {
        _responsive = state;

        foreach (var child in _buttonStack.Children.OfType<Button>())
        {
            child.FontSize = state == PanelResponsiveState.Compact ? 10 : 11;
        }
    }
}
