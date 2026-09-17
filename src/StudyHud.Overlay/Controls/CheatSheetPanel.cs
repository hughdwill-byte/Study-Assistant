using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StudyHud.Core.Models;
using StudyHud.Core.Services;

namespace StudyHud.Overlay.Controls;

/// <summary>
/// Optional Cheat Sheet panel (spec: pin a Notion page as a topic/test reference). Displays just the
/// notes from a chosen Notion page — headings, lists, quotes, callouts, code, inline formatting and
/// images — with all of Notion's chrome stripped out. Scrollable; formatting is preserved via
/// <see cref="INotionPageReader"/>, which fetches the page and hands back a formatting-preserving
/// <see cref="CheatSheetDocument"/>. Read-only and non-AI, like the rest of the app.
/// </summary>
public sealed class CheatSheetPanel : HudPanelBase
{
    private readonly INotionPageReader _reader;
    private readonly IAssessmentPolicyService _policy;
    private readonly ISettingsStore _settings;

    private ComboBox _picker = null!;
    private TextBlock _status = null!;
    private StackPanel _content = null!;
    private ScrollViewer _scroller = null!;

    private bool _pagesLoaded;
    private bool _initialised;
    private CancellationTokenSource? _loadCts;

    public CheatSheetPanel(
        IApplicationStateService appState, IThemeService theme,
        INotionPageReader reader, IAssessmentPolicyService policy, ISettingsStore settings)
        : base("cheat-sheet-panel", appState, theme)
    {
        _reader = reader;
        _policy = policy;
        _settings = settings;

        MinWidth = 280;
        MinHeight = 200;
        Width = 440;
        Height = 480;
    }

    protected override string PanelTitle => "Cheat Sheet";

    protected override void PopulateContent(Grid contentGrid)
    {
        var root = new DockPanel { Margin = new Thickness(10, 8, 10, 10), LastChildFill = true };

        // ── Page picker row ───────────────────────────────────────────────────
        var bar = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };

        var refresh = SmallButton("↻");
        refresh.ToolTip = "Reload the page list / re-fetch this page";
        refresh.Click += (_, _) => { _pagesLoaded = false; _ = EnsurePagesAsync(force: true); ReloadCurrent(); };
        DockPanel.SetDock(refresh, Dock.Right);
        bar.Children.Add(refresh);

        _picker = new ComboBox
        {
            Height = 26,
            Margin = new Thickness(0, 0, 6, 0),
            DisplayMemberPath = nameof(DiscoveredPage.Title),
            ToolTip = "Choose a Notion page to display"
        };
        _picker.DropDownOpened += (_, _) => _ = EnsurePagesAsync(force: false);
        _picker.SelectionChanged += OnPagePicked;
        bar.Children.Add(_picker);

        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);

        // ── Status line (loading / errors) ────────────────────────────────────
        _status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = Brush("SecondaryText", Colors.Gray),
            Visibility = Visibility.Collapsed
        };
        DockPanel.SetDock(_status, Dock.Top);
        root.Children.Add(_status);

        // ── Scrollable notes ──────────────────────────────────────────────────
        _content = new StackPanel();
        _scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _content,
            CanContentScroll = false
        };
        root.Children.Add(_scroller);

        contentGrid.Children.Add(root);

        Loaded += (_, _) =>
        {
            if (_initialised) return;
            _initialised = true;
            _ = InitialLoadAsync();
        };
    }

    // ── Loading ───────────────────────────────────────────────────────────────

    private async Task InitialLoadAsync()
    {
        try
        {
            var s = await _settings.LoadAsync();
            if (!string.IsNullOrWhiteSpace(s.CheatSheetPageId))
                await LoadPageAsync(s.CheatSheetPageId!, s.CheatSheetPageTitle);
            else
                SetStatus("Pick a Notion page above to pin it here as a cheat sheet.");
        }
        catch
        {
            SetStatus("Couldn't read settings.");
        }
    }

    /// <summary>Re-loads whichever page is currently pinned (used by the refresh button).</summary>
    private async void ReloadCurrent()
    {
        try
        {
            var s = await _settings.LoadAsync();
            if (!string.IsNullOrWhiteSpace(s.CheatSheetPageId))
                await LoadPageAsync(s.CheatSheetPageId!, s.CheatSheetPageTitle);
        }
        catch { /* refresh is best-effort */ }
    }

    private async Task EnsurePagesAsync(bool force)
    {
        if (_pagesLoaded && !force) return;

        if (!_policy.IsOperationAllowed(PolicyOperation.NotionSync))
        {
            SetStatus("The page list is unavailable during Assessment Mode.");
            return;
        }

        try
        {
            var pages = await _reader.ListPagesAsync();
            _pagesLoaded = true;

            _picker.SelectionChanged -= OnPagePicked; // don't fire while repopulating
            _picker.ItemsSource = pages;
            var current = (await _settings.LoadAsync()).CheatSheetPageId;
            if (current is not null)
                _picker.SelectedItem = pages.FirstOrDefault(p => p.Id == current);
            _picker.SelectionChanged += OnPagePicked;

            if (pages.Count == 0)
                SetStatus("No Notion pages are shared with the integration yet. Share a page with your "
                          + "Study HUD integration in Notion, then press ↻.");
        }
        catch
        {
            SetStatus("Couldn't load the Notion page list. Check your Notion token in Settings.");
        }
    }

    private void OnPagePicked(object? sender, SelectionChangedEventArgs e)
    {
        if (_picker.SelectedItem is not DiscoveredPage page) return;
        _ = _settings.UpdateAsync(s => s with
        {
            CheatSheetPageId = page.Id,
            CheatSheetPageTitle = page.Title,
            CheatSheetEnabled = true
        });
        _ = LoadPageAsync(page.Id, page.Title);
    }

    private async Task LoadPageAsync(string pageId, string? knownTitle)
    {
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        _content.Children.Clear();
        _scroller.ScrollToTop();

        if (!_policy.IsOperationAllowed(PolicyOperation.NotionSync))
        {
            SetStatus("The Cheat Sheet is unavailable during Assessment Mode.");
            return;
        }

        SetStatus($"Loading {(string.IsNullOrWhiteSpace(knownTitle) ? "page" : knownTitle)}…");

        try
        {
            var doc = await _reader.LoadPageAsync(pageId, cts.Token);
            if (cts.IsCancellationRequested) return;

            if (doc is null)
            {
                SetStatus("Couldn't load this page. Make sure your Notion token is set and the page is "
                          + "shared with the Study HUD integration.");
                return;
            }

            Render(doc);
            SetStatus(null);
        }
        catch (OperationCanceledException) { /* superseded by a newer load */ }
        catch
        {
            SetStatus("Something went wrong loading this page. Press ↻ to try again.");
        }
    }

    // ── Rendering ──────────────────────────────────────────────────────────────

    private void Render(CheatSheetDocument doc)
    {
        _content.Children.Clear();

        _content.Children.Add(new TextBlock
        {
            Text = doc.Title,
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("PrimaryText", Colors.White),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        foreach (var block in doc.Blocks)
        {
            var el = BuildBlock(block);
            if (el != null) _content.Children.Add(el);
        }

        if (doc.Blocks.Count == 0)
            _content.Children.Add(new TextBlock
            {
                Text = "This page has no note content.",
                Foreground = Brush("SecondaryText", Colors.Gray),
                FontStyle = FontStyles.Italic
            });
    }

    private FrameworkElement? BuildBlock(CheatBlock b)
    {
        double indent = b.IndentLevel * 18;

        switch (b.Kind)
        {
            case CheatBlockKind.Divider:
                return new Border
                {
                    Height = 1, Margin = new Thickness(0, 8, 0, 8),
                    Background = Brush("PanelBorder", Color.FromRgb(70, 70, 80))
                };

            case CheatBlockKind.Image:
                return BuildImage(b, indent);

            case CheatBlockKind.Heading1: return Heading(b, indent, 20, 12, 6);
            case CheatBlockKind.Heading2: return Heading(b, indent, 16, 10, 4);
            case CheatBlockKind.Heading3: return Heading(b, indent, 14, 8, 3);

            case CheatBlockKind.Bulleted: return TextRow(b, indent, "•  ");
            case CheatBlockKind.Numbered: return TextRow(b, indent, $"{b.Number ?? 1}.  ");
            case CheatBlockKind.ToDo: return TextRow(b, indent, b.Checked ? "☑  " : "☐  ");
            case CheatBlockKind.ChildPageLink: return TextRow(b, indent, "📄  ", accent: true);

            case CheatBlockKind.Quote: return BuildQuote(b, indent);
            case CheatBlockKind.Callout: return BuildCallout(b, indent);
            case CheatBlockKind.Code: return BuildCode(b, indent);

            default: return TextRow(b, indent, null);
        }
    }

    private FrameworkElement Heading(CheatBlock b, double indent, double size, double top, double bottom)
    {
        var tb = new TextBlock
        {
            FontSize = size, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(indent, top, 0, bottom),
            Foreground = Brush("PrimaryText", Colors.White)
        };
        AppendInlines(tb, b.Inlines, Brush("PrimaryText", Colors.White), forceBold: true);
        return tb;
    }

    private FrameworkElement TextRow(CheatBlock b, double indent, string? marker, bool accent = false)
    {
        var grid = new Grid { Margin = new Thickness(indent, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var def = accent ? Brush("Accent", Color.FromRgb(0, 180, 255)) : Brush("PrimaryText", Colors.White);

        if (marker != null)
        {
            var m = new TextBlock
            {
                Text = marker, FontSize = 13, Foreground = def,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(m, 0);
            grid.Children.Add(m);
        }

        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        AppendInlines(tb, b.Inlines, def);
        Grid.SetColumn(tb, 1);
        grid.Children.Add(tb);
        return grid;
    }

    private FrameworkElement BuildQuote(CheatBlock b, double indent)
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, FontStyle = FontStyles.Italic };
        AppendInlines(tb, b.Inlines, Brush("SecondaryText", Color.FromRgb(200, 200, 210)));
        return new Border
        {
            Margin = new Thickness(indent, 4, 0, 4),
            Padding = new Thickness(10, 4, 6, 4),
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = Brush("Accent", Color.FromRgb(0, 180, 255)),
            Child = tb
        };
    }

    private FrameworkElement BuildCallout(CheatBlock b, double indent)
    {
        var inner = new StackPanel { Orientation = Orientation.Horizontal };
        if (!string.IsNullOrEmpty(b.Emoji))
            inner.Children.Add(new TextBlock { Text = b.Emoji + "  ", FontSize = 14, VerticalAlignment = VerticalAlignment.Top });

        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, MaxWidth = 1000 };
        AppendInlines(tb, b.Inlines, Brush("PrimaryText", Colors.White));
        inner.Children.Add(tb);

        return new Border
        {
            Margin = new Thickness(indent, 4, 0, 4),
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(6),
            Background = Brush("SecondaryBackground", Color.FromArgb(60, 255, 255, 255)),
            Child = inner
        };
    }

    private FrameworkElement BuildCode(CheatBlock b, double indent)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 12,
            FontFamily = Mono(), Foreground = Brush("PrimaryText", Colors.White)
        };
        // In a code block every run is monospace; keep colour/annotations but force the font.
        AppendInlines(tb, b.Inlines, Brush("PrimaryText", Colors.White), forceMono: true);
        return new Border
        {
            Margin = new Thickness(indent, 4, 0, 4),
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
            Child = tb
        };
    }

    private FrameworkElement? BuildImage(CheatBlock b, double indent)
    {
        if (b.ImageBytes is null || b.ImageBytes.Length == 0) return null;
        BitmapImage bmp;
        try
        {
            bmp = new BitmapImage();
            using var ms = new MemoryStream(b.ImageBytes);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
        }
        catch
        {
            return null; // unreadable image — skip rather than break the page
        }

        var img = new Image
        {
            Source = bmp,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(indent, 6, 0, 6)
        };
        // Never let an image overflow the panel width; scale down to the content area.
        img.SetBinding(FrameworkElement.MaxWidthProperty,
            new System.Windows.Data.Binding(nameof(ActualWidth)) { Source = _content });

        if (string.IsNullOrEmpty(b.ImageCaption)) return img;

        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        stack.Children.Add(img);
        stack.Children.Add(new TextBlock
        {
            Text = b.ImageCaption, FontSize = 11, FontStyle = FontStyles.Italic,
            Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(indent, 2, 0, 0), TextWrapping = TextWrapping.Wrap
        });
        return stack;
    }

    private void AppendInlines(
        TextBlock tb, IReadOnlyList<CheatInline> inlines, Brush def,
        bool forceBold = false, bool forceMono = false)
    {
        foreach (var r in inlines)
        {
            var run = new Run(r.Text);
            if (r.Bold || forceBold) run.FontWeight = FontWeights.Bold;
            if (r.Italic) run.FontStyle = FontStyles.Italic;

            if (r.Underline || r.Strikethrough)
            {
                var deco = new TextDecorationCollection();
                if (r.Underline) deco.Add(TextDecorations.Underline[0]);
                if (r.Strikethrough) deco.Add(TextDecorations.Strikethrough[0]);
                run.TextDecorations = deco;
            }

            if (r.Code || forceMono)
            {
                run.FontFamily = Mono();
                if (r.Code) run.Background = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255));
            }

            run.Foreground = r.ColorHex is string hex ? BrushFromHex(hex, def) : def;
            tb.Inlines.Add(run);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private void SetStatus(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            _status.Visibility = Visibility.Collapsed;
            return;
        }
        _status.Text = message;
        _status.Visibility = Visibility.Visible;
    }

    private Button SmallButton(string content) => new()
    {
        Content = content,
        Width = 28, Height = 26, Padding = new Thickness(0),
        Cursor = System.Windows.Input.Cursors.Hand,
        BorderThickness = new Thickness(0),
        Background = Brush("Accent", Color.FromRgb(0, 180, 255)),
        Foreground = Brushes.White
    };

    private static FontFamily Mono() =>
        Application.Current?.TryFindResource("MonoFontFamily") as FontFamily
        ?? new FontFamily("Cascadia Code, Consolas");

    private static Brush BrushFromHex(string hex, Brush fallback)
    {
        try
        {
            var h = hex.TrimStart('#');
            if (h.Length != 6) return fallback;
            return new SolidColorBrush(Color.FromRgb(
                Convert.ToByte(h.Substring(0, 2), 16),
                Convert.ToByte(h.Substring(2, 2), 16),
                Convert.ToByte(h.Substring(4, 2), 16)));
        }
        catch { return fallback; }
    }

    private Brush Brush(string token, Color fallback)
        => Application.Current?.TryFindResource(token) as Brush ?? new SolidColorBrush(fallback);
}
