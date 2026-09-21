using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StudyHud.Core.Models;
using StudyHud.Core.Services;

namespace StudyHud.App;

/// <summary>
/// Symbols settings page: search the engineering-symbol set by name or glyph, favourite the ones you
/// use (they pin to the top of the HUD palette), and add or remove your own. Editing lives here in the
/// normal window because the always-on-top HUD can't take keyboard focus without stealing it from the
/// app you're typing into — so search + add happen here, while clicking to insert happens on the HUD.
/// </summary>
public sealed class SymbolsView : UserControl
{
    private readonly ISettingsStore _settings;

    private readonly TextBox _search;
    private readonly TextBox _newGlyph;
    private readonly TextBox _newName;
    private readonly StackPanel _list;
    private readonly TextBlock _status;

    public SymbolsView(ISettingsStore settings)
    {
        _settings = settings;

        var root = new StackPanel { Margin = new Thickness(4) };
        root.Children.Add(Title("Symbols"));
        root.Children.Add(Body("Favourite the symbols you use — they pin to the top of the ∑ palette on the "
            + "HUD. On the HUD, click a symbol to type it into whatever app you're working in."));

        // Search
        root.Children.Add(Header("SEARCH"));
        _search = new TextBox { Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
        _search.TextChanged += (_, _) => Rebuild();
        root.Children.Add(_search);

        // Add your own
        root.Children.Add(Header("ADD YOUR OWN"));
        var addRow = new StackPanel { Orientation = Orientation.Horizontal };
        _newGlyph = new TextBox { Width = 56, Height = 28, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 8, ToolTip = "Paste or type a symbol" };
        _newName = new TextBox { Width = 200, Height = 28, Margin = new Thickness(8, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Name (optional, for search)" };
        var addBtn = MakeButton("Add", accent: true);
        addBtn.Click += (_, _) => AddCustom();
        addRow.Children.Add(_newGlyph);
        addRow.Children.Add(_newName);
        addRow.Children.Add(addBtn);
        root.Children.Add(addRow);

        _status = new TextBlock
        {
            Opacity = 0.75, Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(_status);

        root.Children.Add(Header("ALL SYMBOLS"));
        _list = new StackPanel();
        root.Children.Add(_list);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        Loaded += (_, _) => Rebuild();
    }

    private IEnumerable<EngineeringSymbol> AllSymbols()
    {
        foreach (var s in EngineeringSymbols.BuiltIn) yield return s;
        foreach (var c in _settings.Current.CustomSymbols)
            yield return new EngineeringSymbol { Glyph = c.Glyph, Name = c.Name, Keywords = c.Name, IsCustom = true };
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        var query = _search.Text ?? "";
        var favs = new HashSet<string>(_settings.Current.SymbolFavorites);
        var all = AllSymbols().Where(s => s.Matches(query)).ToList();

        // Favourites first (in saved order), then the rest.
        var favOrdered = _settings.Current.SymbolFavorites
            .Select(g => all.FirstOrDefault(s => s.Glyph == g))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
        var ordered = favOrdered.Concat(all.Where(s => !favs.Contains(s.Glyph))).ToList();

        if (ordered.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = "No matches.", Opacity = 0.7, Margin = new Thickness(0, 4, 0, 0),
                Foreground = Brush("SecondaryText", Colors.Gray)
            });
            return;
        }

        foreach (var sym in ordered) _list.Children.Add(Row(sym, favs.Contains(sym.Glyph)));
    }

    private UIElement Row(EngineeringSymbol sym, bool isFav)
    {
        var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), LastChildFill = true };

        var star = MakeButton(isFav ? "★" : "☆", accent: false);
        star.Width = 34; star.ToolTip = isFav ? "Unfavourite" : "Favourite";
        star.Foreground = isFav ? Brush("Accent", Color.FromRgb(90, 178, 168)) : Brush("SecondaryText", Colors.Gray);
        star.Click += (_, _) => ToggleFavourite(sym.Glyph);
        DockPanel.SetDock(star, Dock.Left);
        row.Children.Add(star);

        var glyph = new TextBlock
        {
            Text = sym.Glyph, FontSize = 18, Width = 40, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("PrimaryText", Colors.White)
        };
        DockPanel.SetDock(glyph, Dock.Left);
        row.Children.Add(glyph);

        if (sym.IsCustom)
        {
            var remove = MakeButton("Remove", accent: false);
            remove.Click += (_, _) => RemoveCustom(sym.Glyph);
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
        }

        var name = new TextBlock
        {
            Text = sym.Name + (sym.IsCustom ? "  (custom)" : ""),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
            Foreground = Brush("SecondaryText", Colors.Gray), TextTrimming = TextTrimming.CharacterEllipsis
        };
        row.Children.Add(name);
        return row;
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

    private void RemoveCustom(string glyph)
    {
        _ = _settings.UpdateAsync(s => s with
        {
            CustomSymbols = s.CustomSymbols.Where(c => c.Glyph != glyph).ToList(),
            SymbolFavorites = s.SymbolFavorites.Where(g => g != glyph).ToList()
        }).ContinueWith(_ => Dispatcher.BeginInvoke(Rebuild));
    }

    private void AddCustom()
    {
        var glyph = _newGlyph.Text?.Trim() ?? "";
        var name = _newName.Text?.Trim() ?? "";
        if (glyph.Length == 0) { SetStatus("Enter a symbol to add."); return; }
        if (AllSymbols().Any(s => s.Glyph == glyph)) { SetStatus($"“{glyph}” is already in the palette."); _newGlyph.Clear(); _newName.Clear(); return; }

        _ = _settings.UpdateAsync(s =>
        {
            var customs = new List<CustomSymbol>(s.CustomSymbols) { new() { Glyph = glyph, Name = string.IsNullOrEmpty(name) ? glyph : name } };
            return s with { CustomSymbols = customs };
        }).ContinueWith(_ => Dispatcher.BeginInvoke(() =>
        {
            SetStatus($"Added “{glyph}”.");
            _newGlyph.Clear();
            _newName.Clear();
            Rebuild();
        }));
    }

    // ── UI helpers ───────────────────────────────────────────────────────────

    private void SetStatus(string msg) => _status.Text = msg;

    private TextBlock Title(string t) => new()
    {
        Text = t, FontSize = 20, FontWeight = FontWeights.SemiBold,
        Foreground = Brush("PrimaryText", Colors.White), Margin = new Thickness(0, 0, 0, 8)
    };

    private TextBlock Body(string t) => new()
    {
        Text = t, TextWrapping = TextWrapping.Wrap, Opacity = 0.75,
        Foreground = Brush("SecondaryText", Colors.Gray), Margin = new Thickness(0, 0, 0, 6)
    };

    private TextBlock Header(string t) => new()
    {
        Text = t, FontSize = 10, Opacity = 0.5,
        Foreground = Brush("SecondaryText", Colors.Gray), Margin = new Thickness(0, 16, 0, 6)
    };

    private Button MakeButton(string content, bool accent) => new()
    {
        Content = content, Padding = new Thickness(12, 4, 12, 4),
        Cursor = System.Windows.Input.Cursors.Hand, BorderThickness = new Thickness(0),
        Foreground = accent ? Brushes.White : Brush("SecondaryText", Colors.Gray),
        Background = accent ? Brush("Accent", Color.FromRgb(90, 178, 168)) : Brushes.Transparent
    };

    private Brush Brush(string token, Color fallback)
        => TryFindResource(token) as Brush ?? new SolidColorBrush(fallback);
}
