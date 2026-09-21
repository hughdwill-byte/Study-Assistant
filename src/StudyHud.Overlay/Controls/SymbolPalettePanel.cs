using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StudyHud.Core.Models;
using StudyHud.Core.Services;

namespace StudyHud.Overlay.Controls;

/// <summary>
/// Optional engineering-symbol palette. Click a symbol to type it straight into whatever app you're
/// working in (the HUD is a no-activate window, so focus stays put). Favourite the ones you use so they
/// pin to the top, search by symbol or name, and add your own if it isn't built in. Persisted in
/// settings; deterministic and offline like the rest of the app.
/// </summary>
public sealed class SymbolPalettePanel : HudPanelBase
{
    private readonly ITextInputService _input;
    private readonly ISettingsStore _settings;

    private TextBox _search = null!;
    private TextBox _newGlyph = null!;
    private TextBox _newName = null!;
    private StackPanel _list = null!;

    public SymbolPalettePanel(IApplicationStateService appState, IThemeService theme,
        ITextInputService input, ISettingsStore settings)
        : base("symbol-palette-panel", appState, theme)
    {
        _input = input;
        _settings = settings;
        MinWidth = 260;
        MinHeight = 240;
        Width = 330;
        Height = 440;
    }

    protected override string PanelTitle => "Symbols";

    protected override void PopulateContent(Grid contentGrid)
    {
        var root = new DockPanel { Margin = new Thickness(10, 8, 10, 10), LastChildFill = true };

        // ── Search ─────────────────────────────────────────────────────────────
        _search = new TextBox
        {
            Height = 28, VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8), ToolTip = "Search by symbol or name (e.g. ohm, theta, ±)"
        };
        _search.TextChanged += (_, _) => Rebuild();
        DockPanel.SetDock(_search, Dock.Top);
        root.Children.Add(_search);

        // ── Add your own ─────────────────────────────────────────────────────────
        var addRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
        var addBtn = SmallButton("＋");
        addBtn.ToolTip = "Add this symbol to the palette";
        addBtn.Click += (_, _) => AddCustom();
        DockPanel.SetDock(addBtn, Dock.Right);
        addRow.Children.Add(addBtn);

        _newGlyph = new TextBox
        {
            Width = 46, Height = 26, Margin = new Thickness(0, 0, 6, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Paste or type a symbol", MaxLength = 8
        };
        DockPanel.SetDock(_newGlyph, Dock.Left);
        addRow.Children.Add(_newGlyph);

        _newName = new TextBox
        {
            Height = 26, VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0), ToolTip = "Name (optional, for search)"
        };
        addRow.Children.Add(_newName);

        DockPanel.SetDock(addRow, Dock.Top);
        root.Children.Add(addRow);

        // ── Scrollable list (favourites + all) ────────────────────────────────────
        _list = new StackPanel();
        root.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _list
        });

        contentGrid.Children.Add(root);

        Loaded += (_, _) => Rebuild();
    }

    // ── Building the grid ───────────────────────────────────────────────────────

    private IEnumerable<EngineeringSymbol> AllSymbols()
    {
        foreach (var s in EngineeringSymbols.BuiltIn) yield return s;
        foreach (var c in _settings.Current.CustomSymbols)
            yield return new EngineeringSymbol { Glyph = c.Glyph, Name = c.Name, Keywords = c.Name, IsCustom = true };
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        var query = _search?.Text ?? "";
        var all = AllSymbols().ToList();
        var favGlyphs = _settings.Current.SymbolFavorites;
        var byGlyph = new Dictionary<string, EngineeringSymbol>();
        foreach (var s in all) byGlyph[s.Glyph] = s;

        // Favourites (in saved order), filtered by the query.
        var favs = favGlyphs
            .Where(byGlyph.ContainsKey)
            .Select(g => byGlyph[g])
            .Where(s => s.Matches(query))
            .ToList();
        if (favs.Count > 0)
        {
            _list.Children.Add(Header("★  Favourites"));
            _list.Children.Add(Wrap(favs));
        }

        // Everything else that matches.
        var favSet = new HashSet<string>(favGlyphs);
        var rest = all.Where(s => !favSet.Contains(s.Glyph) && s.Matches(query)).ToList();
        _list.Children.Add(Header(favs.Count > 0 ? "All symbols" : "Symbols"));
        if (rest.Count == 0 && favs.Count == 0)
            _list.Children.Add(new TextBlock
            {
                Text = "No matches. Add it below with ＋.", Opacity = 0.7, Margin = new Thickness(2, 4, 0, 0),
                Foreground = Brush("SecondaryText", Colors.Gray), TextWrapping = TextWrapping.Wrap
            });
        else
            _list.Children.Add(Wrap(rest));
    }

    private WrapPanel Wrap(IEnumerable<EngineeringSymbol> symbols)
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
        foreach (var s in symbols) wrap.Children.Add(SymbolButton(s));
        return wrap;
    }

    private Button SymbolButton(EngineeringSymbol sym)
    {
        var isFav = _settings.Current.SymbolFavorites.Contains(sym.Glyph);
        var btn = new Button
        {
            Content = sym.Glyph,
            FontSize = 18,
            MinWidth = 40, Height = 40,
            Margin = new Thickness(3),
            Padding = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = Brush("SecondaryBackground", Color.FromArgb(60, 255, 255, 255)),
            BorderBrush = isFav ? Brush("Accent", Color.FromRgb(90, 178, 168)) : Brush("PanelBorder", Color.FromRgb(70, 70, 80)),
            BorderThickness = new Thickness(isFav ? 2 : 1),
            Foreground = Brush("PrimaryText", Colors.White),
            ToolTip = $"{sym.Name}  ·  click to insert, right-click to favourite"
        };
        btn.Click += (_, _) => _input.InsertText(sym.Glyph);

        var menu = new ContextMenu();
        var fav = new MenuItem { Header = isFav ? "★ Unfavourite" : "☆ Favourite" };
        fav.Click += (_, _) => ToggleFavourite(sym.Glyph);
        menu.Items.Add(fav);
        if (sym.IsCustom)
        {
            var remove = new MenuItem { Header = "Remove" };
            remove.Click += (_, _) => RemoveCustom(sym.Glyph);
            menu.Items.Add(remove);
        }
        btn.ContextMenu = menu;
        return btn;
    }

    // ── Persistence ─────────────────────────────────────────────────────────────

    private void ToggleFavourite(string glyph)
    {
        _ = _settings.UpdateAsync(s =>
        {
            var favs = new List<string>(s.SymbolFavorites);
            if (!favs.Remove(glyph)) favs.Add(glyph);
            return s with { SymbolFavorites = favs };
        }).ContinueWith(_ => Dispatcher.BeginInvoke(Rebuild));
    }

    private void RemoveCustom(string glyph)
    {
        _ = _settings.UpdateAsync(s =>
        {
            var customs = s.CustomSymbols.Where(c => c.Glyph != glyph).ToList();
            var favs = s.SymbolFavorites.Where(g => g != glyph).ToList();
            return s with { CustomSymbols = customs, SymbolFavorites = favs };
        }).ContinueWith(_ => Dispatcher.BeginInvoke(Rebuild));
    }

    private void AddCustom()
    {
        var glyph = _newGlyph.Text?.Trim() ?? "";
        var name = _newName.Text?.Trim() ?? "";
        if (glyph.Length == 0) return;

        // Ignore duplicates (built-in or already-added).
        if (AllSymbols().Any(s => s.Glyph == glyph))
        {
            _newGlyph.Clear();
            _newName.Clear();
            return;
        }

        _ = _settings.UpdateAsync(s =>
        {
            var customs = new List<CustomSymbol>(s.CustomSymbols)
            {
                new() { Glyph = glyph, Name = string.IsNullOrEmpty(name) ? glyph : name }
            };
            return s with { CustomSymbols = customs };
        }).ContinueWith(_ => Dispatcher.BeginInvoke(() =>
        {
            _newGlyph.Clear();
            _newName.Clear();
            Rebuild();
        }));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private TextBlock Header(string text) => new()
    {
        Text = text, FontSize = 10, Opacity = 0.6, Margin = new Thickness(2, 6, 0, 2),
        Foreground = Brush("SecondaryText", Colors.Gray)
    };

    private Button SmallButton(string content) => new()
    {
        Content = content, Width = 30, Height = 26, Padding = new Thickness(0),
        Cursor = System.Windows.Input.Cursors.Hand, BorderThickness = new Thickness(0),
        Background = Brush("Accent", Color.FromRgb(90, 178, 168)), Foreground = Brushes.White
    };

    private Brush Brush(string token, Color fallback)
        => Application.Current?.TryFindResource(token) as Brush ?? new SolidColorBrush(fallback);
}
