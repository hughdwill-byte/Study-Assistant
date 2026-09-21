using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StudyHud.Core.Models;
using StudyHud.Core.Services;

namespace StudyHud.Overlay.Controls;

/// <summary>
/// Optional engineering-symbol palette. Click a symbol to type it straight into whatever app you're
/// working in (the HUD is a no-activate window, so focus stays put and the character lands where you're
/// typing). Favourites pin to the top; category chips filter the grid without typing (the HUD can't take
/// keyboard focus). Searching by name and adding your own live on the Settings ▸ Symbols page.
/// </summary>
public sealed class SymbolPalettePanel : HudPanelBase
{
    private readonly ITextInputService _input;
    private readonly ISettingsStore _settings;

    private WrapPanel _chipRow = null!;
    private StackPanel _list = null!;
    private string _category = "All";

    public SymbolPalettePanel(IApplicationStateService appState, IThemeService theme,
        ITextInputService input, ISettingsStore settings)
        : base("symbol-palette-panel", appState, theme)
    {
        _input = input;
        _settings = settings;
        MinWidth = 260;
        MinHeight = 240;
        Width = 330;
        Height = 420;
    }

    protected override string PanelTitle => "Symbols";

    protected override void PopulateContent(Grid contentGrid)
    {
        var root = new DockPanel { Margin = new Thickness(10, 8, 10, 10), LastChildFill = true };

        _chipRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(_chipRow, Dock.Top);
        root.Children.Add(_chipRow);

        var hint = new TextBlock
        {
            Text = "Click to insert · right-click to favourite · search & add in Settings ▸ Symbols",
            FontSize = 9, Opacity = 0.55, TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("SecondaryText", Colors.Gray), Margin = new Thickness(0, 0, 0, 6)
        };
        DockPanel.SetDock(hint, Dock.Bottom);
        root.Children.Add(hint);

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

    private IEnumerable<EngineeringSymbol> AllSymbols()
    {
        foreach (var s in EngineeringSymbols.BuiltIn) yield return s;
        foreach (var c in _settings.Current.CustomSymbols)
            yield return new EngineeringSymbol { Glyph = c.Glyph, Name = c.Name, Keywords = c.Name, Category = "Custom", IsCustom = true };
    }

    private void Rebuild()
    {
        BuildChips();

        _list.Children.Clear();
        var all = AllSymbols().ToList();
        var favGlyphs = _settings.Current.SymbolFavorites;
        var favSet = new HashSet<string>(favGlyphs);

        if (_category == "★ Favourites")
        {
            var favs = favGlyphs.Select(g => all.FirstOrDefault(s => s.Glyph == g)).Where(s => s is not null).Select(s => s!).ToList();
            _list.Children.Add(Wrap(favs));
            if (favs.Count == 0)
                _list.Children.Add(Note("No favourites yet — right-click a symbol to add one."));
            return;
        }

        // In "All", pin favourites at the top; a category view shows just that category.
        if (_category == "All")
        {
            var favs = favGlyphs.Select(g => all.FirstOrDefault(s => s.Glyph == g)).Where(s => s is not null).Select(s => s!).ToList();
            if (favs.Count > 0)
            {
                _list.Children.Add(Header("★  Favourites"));
                _list.Children.Add(Wrap(favs));
                _list.Children.Add(Header("All symbols"));
            }
            _list.Children.Add(Wrap(all.Where(s => !favSet.Contains(s.Glyph))));
        }
        else
        {
            var inCat = all.Where(s => s.Category == _category).ToList();
            _list.Children.Add(Wrap(inCat));
            if (inCat.Count == 0)
                _list.Children.Add(Note(_category == "Custom" ? "No custom symbols yet — add them in Settings ▸ Symbols." : "Nothing here."));
        }
    }

    private void BuildChips()
    {
        _chipRow.Children.Clear();
        var cats = new List<string> { "All", "★ Favourites" };
        cats.AddRange(EngineeringSymbols.BuiltIn.Select(s => s.Category).Distinct());
        if (_settings.Current.CustomSymbols.Count > 0) cats.Add("Custom");

        foreach (var cat in cats)
        {
            var chip = new Button
            {
                Content = cat, FontSize = 10, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 4),
                Cursor = System.Windows.Input.Cursors.Hand, BorderThickness = new Thickness(cat == _category ? 0 : 1),
                BorderBrush = Brush("PanelBorder", Color.FromRgb(70, 70, 80)),
                Background = cat == _category ? Brush("Accent", Color.FromRgb(90, 178, 168)) : Brushes.Transparent,
                Foreground = cat == _category ? Brushes.White : Brush("SecondaryText", Colors.Gray)
            };
            var chosen = cat;
            chip.Click += (_, _) => { _category = chosen; Rebuild(); };
            _chipRow.Children.Add(chip);
        }
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
            Content = sym.Glyph, FontSize = 18, MinWidth = 40, Height = 40, Margin = new Thickness(3), Padding = new Thickness(0),
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
        btn.ContextMenu = menu;
        return btn;
    }

    private void ToggleFavourite(string glyph)
    {
        _ = _settings.UpdateAsync(s =>
        {
            var favs = new List<string>(s.SymbolFavorites);
            if (!favs.Remove(glyph)) favs.Add(glyph);
            return s with { SymbolFavorites = favs };
        }).ContinueWith(_ => Dispatcher.BeginInvoke(Rebuild));
    }

    private TextBlock Header(string text) => new()
    {
        Text = text, FontSize = 10, Opacity = 0.6, Margin = new Thickness(2, 6, 0, 2),
        Foreground = Brush("SecondaryText", Colors.Gray)
    };

    private TextBlock Note(string text) => new()
    {
        Text = text, Opacity = 0.7, Margin = new Thickness(2, 4, 0, 0), TextWrapping = TextWrapping.Wrap,
        Foreground = Brush("SecondaryText", Colors.Gray)
    };

    private Brush Brush(string token, Color fallback)
        => Application.Current?.TryFindResource(token) as Brush ?? new SolidColorBrush(fallback);
}
