using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StudyHud.Core.Models;
using StudyHud.Core.Services;

namespace StudyHud.App;

/// <summary>
/// Full Question Finder page (a normal, focusable window — so the search box can actually take
/// keyboard input, unlike the always-on-top HUD). It ties three things together:
///   • a search bar for key words / key phrases (deterministic phrase-precise search of your notes),
///   • a Capture button (screenshot → column-aware local OCR → the same search),
///   • an in-app render of the matching Notion page, scrolled to the exact spot, and freely scrollable
///     so you can read just above and below.
///
/// It only ever locates and shows the user's own notes — it never generates an answer — and the page
/// render goes through <see cref="INotionPageReader"/>, which returns nothing in Assessment Mode.
/// </summary>
public sealed class QuestionFinderView : UserControl
{
    private readonly IQuestionFinder _finder;
    private readonly ICaptureService _capture;
    private readonly INotionPageReader _pages;
    private readonly ICourseRepository _courses;

    private readonly TextBox _search;
    private readonly ComboBox _course;
    private readonly StackPanel _results;
    private readonly StackPanel _pageStack;
    private readonly ScrollViewer _pageScroll;
    private readonly TextBlock _status;
    private readonly TextBlock _pageTitle;

    private readonly List<Course> _courseList = new();

    public QuestionFinderView(IQuestionFinder finder, ICaptureService capture,
        INotionPageReader pages, ICourseRepository courses)
    {
        _finder = finder;
        _capture = capture;
        _pages = pages;
        _courses = courses;

        var root = new Grid { Margin = new Thickness(4) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // search bar
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // status
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // body

        // ── Search bar ────────────────────────────────────────────────────────
        var bar = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _search = new TextBox
        {
            Height = 32, VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 14, Padding = new Thickness(8, 0, 8, 0),
            ToolTip = "Type key words or a key phrase (e.g. \"bending stress\") and press Enter"
        };
        _search.KeyDown += (_, e) => { if (e.Key == Key.Enter) _ = RunTextSearchAsync(); };
        Grid.SetColumn(_search, 0);
        bar.Children.Add(_search);

        var searchBtn = MakeButton("Search", accent: true);
        searchBtn.Margin = new Thickness(6, 0, 0, 0);
        searchBtn.Click += (_, _) => _ = RunTextSearchAsync();
        Grid.SetColumn(searchBtn, 1);
        bar.Children.Add(searchBtn);

        var captureBtn = MakeButton("⬛ Capture", accent: false);
        captureBtn.Margin = new Thickness(6, 0, 0, 0);
        captureBtn.ToolTip = "Screenshot a question — it's read with local OCR and searched";
        captureBtn.Click += (_, _) => _ = RunCaptureAsync();
        Grid.SetColumn(captureBtn, 2);
        bar.Children.Add(captureBtn);

        Grid.SetRow(bar, 0);
        root.Children.Add(bar);

        // Course scope + status line.
        var statusRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        _course = new ComboBox { Width = 220, Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
        _course.Items.Add("All courses");
        _course.SelectedIndex = 0;
        DockPanel.SetDock(_course, Dock.Right);
        statusRow.Children.Add(_course);

        _status = new TextBlock
        {
            Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("SecondaryText", Colors.Gray), TextTrimming = TextTrimming.CharacterEllipsis
        };
        statusRow.Children.Add(_status);
        Grid.SetRow(statusRow, 1);
        root.Children.Add(statusRow);

        // ── Body: results (left) + page preview (right) ────────────────────────
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var resultsScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _results = new StackPanel();
        resultsScroll.Content = _results;
        Grid.SetColumn(resultsScroll, 0);
        body.Children.Add(resultsScroll);

        var pagePanel = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(0),
            Background = Brush("PanelBackground", Color.FromArgb(30, 255, 255, 255)),
            BorderBrush = Brush("PanelBorder", Color.FromRgb(60, 60, 70)), BorderThickness = new Thickness(1)
        };
        var pageGrid = new Grid();
        pageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pageGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _pageTitle = new TextBlock
        {
            Text = "Select a result to preview the note here.",
            FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(14, 12, 14, 6),
            TextWrapping = TextWrapping.Wrap, Foreground = Brush("PrimaryText", Colors.White)
        };
        Grid.SetRow(_pageTitle, 0);
        pageGrid.Children.Add(_pageTitle);

        _pageScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(14, 0, 14, 14) };
        _pageStack = new StackPanel();
        _pageScroll.Content = _pageStack;
        Grid.SetRow(_pageScroll, 1);
        pageGrid.Children.Add(_pageScroll);
        pagePanel.Child = pageGrid;
        Grid.SetColumn(pagePanel, 2);
        body.Children.Add(pagePanel);

        Grid.SetRow(body, 2);
        root.Children.Add(body);

        Content = root;
        Loaded += async (_, _) => await LoadCoursesAsync();
        SetStatus("Type a key phrase and press Enter, or Capture a question.");
    }

    private async Task LoadCoursesAsync()
    {
        try
        {
            var courses = await _courses.GetAllAsync();
            _courseList.Clear();
            _courseList.AddRange(courses);
            _course.Items.Clear();
            _course.Items.Add("All courses");
            foreach (var c in _courseList) _course.Items.Add(c.Name);
            _course.SelectedIndex = 0;
        }
        catch { /* course scoping is optional */ }
    }

    private string? SelectedCourseId()
    {
        int i = _course.SelectedIndex;
        return i >= 1 && i - 1 < _courseList.Count ? _courseList[i - 1].CourseId : null;
    }

    private async Task RunTextSearchAsync()
    {
        var text = _search.Text?.Trim() ?? "";
        if (text.Length == 0) { SetStatus("Type something to search for."); return; }
        SetStatus($"Searching for “{text}”…");
        try
        {
            var result = await _finder.FindFromTextAsync(text, SelectedCourseId(), maxResults: 20);
            ShowResults(result.Results, text);
        }
        catch (Exception ex) { SetStatus("Search error: " + ex.Message); }
    }

    private async Task RunCaptureAsync()
    {
        try
        {
            SetStatus("Draw a rectangle around the question…");
            var capture = await _capture.CaptureRegionAsync();
            if (capture is null || capture.WasCancelled || capture.ImageBytes.Length == 0)
            {
                SetStatus("Capture cancelled.");
                return;
            }
            SetStatus("Reading the question (local OCR)…");
            var result = await _finder.FindFromImageAsync(capture.ImageBytes, SelectedCourseId(), maxResults: 20);
            if (!string.IsNullOrWhiteSpace(result.OcrText)) _search.Text = result.OcrText;
            ShowResults(result.Results, result.OcrText);
            if (result.IsLowConfidence)
                SetStatus($"Low OCR confidence — read: “{Truncate(result.OcrText, 80)}”. Edit the search and press Enter.");
        }
        catch (Exception ex) { SetStatus("Capture error: " + ex.Message); }
    }

    private void ShowResults(IReadOnlyList<SearchResult> results, string queryText)
    {
        _results.Children.Clear();
        if (results.Count == 0)
        {
            SetStatus(string.IsNullOrWhiteSpace(queryText)
                ? "No text detected — try a tighter capture."
                : $"No matches for “{Truncate(queryText, 60)}”. Try a keyword or heading, and check the course is synced.");
            return;
        }

        SetStatus($"{results.Count} result(s). Click one to preview the note.");
        foreach (var r in results) _results.Children.Add(ResultCard(r));

        // Auto-open the top result so the page preview is immediately useful.
        _ = OpenResultAsync(results[0]);
    }

    private UIElement ResultCard(SearchResult r)
    {
        var card = new Border
        {
            Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand,
            Background = Brush("SecondaryBackground", Color.FromArgb(60, 255, 255, 255)),
            BorderBrush = Brush("PanelBorder", Color.FromRgb(60, 60, 70)), BorderThickness = new Thickness(1)
        };
        var stack = new StackPanel();

        var top = new DockPanel();
        var score = new TextBlock
        {
            Text = $"{r.MatchScore:F0}%", FontSize = 10,
            Foreground = Brush("Accent", Color.FromRgb(0, 180, 255)), HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(score, Dock.Right);
        top.Children.Add(score);
        top.Children.Add(new TextBlock
        {
            Text = r.WeekLabel ?? r.CourseName, FontSize = 10, Opacity = 0.6,
            Foreground = Brush("SecondaryText", Colors.Gray)
        });
        stack.Children.Add(top);

        stack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(r.HeadingPath) ? r.PageName : r.HeadingPath,
            FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 3), Foreground = Brush("PrimaryText", Colors.White)
        });

        if (r.Explanations.Count > 0)
            stack.Children.Add(new TextBlock
            {
                Text = "Matched: " + string.Join("  •  ", r.Explanations.Take(5).Select(x => x.Value)),
                FontSize = 10, Opacity = 0.7, TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("SecondaryText", Colors.Gray)
            });

        card.Child = stack;
        card.MouseLeftButtonUp += (_, _) => _ = OpenResultAsync(r);
        return card;
    }

    private async Task OpenResultAsync(SearchResult r)
    {
        _pageStack.Children.Clear();
        _pageTitle.Text = string.IsNullOrWhiteSpace(r.HeadingPath) ? r.PageName : r.HeadingPath;

        if (string.IsNullOrWhiteSpace(r.PageId))
        {
            AddOpenInNotion(r.NotionPageUrl, "This note isn't linked to a page id — open it in Notion instead.");
            return;
        }

        _pageStack.Children.Add(Muted("Loading the page…"));
        try
        {
            var doc = await _pages.LoadPageAsync(r.PageId!);
            _pageStack.Children.Clear();
            if (doc is null)
            {
                AddOpenInNotion(r.NotionPageUrl,
                    "Preview unavailable (no Notion token, or Assessment Mode is on). Open it in Notion instead.");
                return;
            }

            _pageTitle.Text = doc.Title;
            FrameworkElement? target = null;
            string? wanted = FirstNonEmpty(r.HeadingText, LastSegment(r.HeadingPath));

            foreach (var block in doc.Blocks)
            {
                var el = RenderBlock(block);
                if (el is null) continue;
                _pageStack.Children.Add(el);
                if (target is null && wanted is not null && IsHeading(block) && BlockText(block)
                        .Contains(wanted, StringComparison.OrdinalIgnoreCase))
                    target = el;
            }

            AddOpenInNotion(r.NotionPageUrl, "Open the full page in Notion →");

            // Scroll to the matched heading once layout has run, so you land on the exact spot and can
            // scroll up/down to read around it.
            if (target is not null)
                Dispatcher.BeginInvoke(new Action(() => target.BringIntoView()),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            else
                _pageScroll.ScrollToTop();
        }
        catch (Exception ex)
        {
            _pageStack.Children.Clear();
            AddOpenInNotion(r.NotionPageUrl, "Couldn't render the page (" + ex.Message + "). Open it in Notion instead.");
        }
    }

    // ── Compact Notion block renderer (its own, so the HUD Cheat Sheet is untouched) ─────────────

    private FrameworkElement? RenderBlock(CheatBlock b)
    {
        double indent = b.IndentLevel * 16;
        return b.Kind switch
        {
            CheatBlockKind.Divider => new Border
            {
                Height = 1, Background = Brush("PanelBorder", Color.FromRgb(70, 70, 80)),
                Margin = new Thickness(0, 8, 0, 8)
            },
            CheatBlockKind.Image => RenderImage(b, indent),
            CheatBlockKind.Heading1 => Heading(b, indent, 20),
            CheatBlockKind.Heading2 => Heading(b, indent, 16),
            CheatBlockKind.Heading3 => Heading(b, indent, 14),
            CheatBlockKind.Bulleted => TextRow(b, indent, "•  "),
            CheatBlockKind.Numbered => TextRow(b, indent, $"{b.Number ?? 1}.  "),
            CheatBlockKind.ToDo => TextRow(b, indent, b.Checked ? "☑  " : "☐  "),
            CheatBlockKind.ChildPageLink => TextRow(b, indent, "📄  "),
            CheatBlockKind.Quote => Quote(b, indent),
            CheatBlockKind.Callout => Callout(b, indent),
            CheatBlockKind.Code => Code(b, indent),
            _ => TextRow(b, indent, null)
        };
    }

    private FrameworkElement Heading(CheatBlock b, double indent, double size)
    {
        var tb = Inlines(b, size);
        tb.FontWeight = FontWeights.Bold;
        tb.Margin = new Thickness(indent, 10, 0, 4);
        return tb;
    }

    private FrameworkElement TextRow(CheatBlock b, double indent, string? marker)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(indent, 2, 0, 2) };
        if (marker is not null)
            panel.Children.Add(new TextBlock
            {
                Text = marker, Foreground = Brush("SecondaryText", Colors.Gray), VerticalAlignment = VerticalAlignment.Top
            });
        var body = Inlines(b, 13);
        body.MaxWidth = 720;
        panel.Children.Add(body);
        return panel;
    }

    private FrameworkElement Quote(CheatBlock b, double indent)
    {
        var text = Inlines(b, 13);
        text.FontStyle = FontStyles.Italic;
        return new Border
        {
            BorderBrush = Brush("Accent", Color.FromRgb(0, 180, 255)), BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 2, 0, 2), Margin = new Thickness(indent, 4, 0, 4), Child = text
        };
    }

    private FrameworkElement Callout(CheatBlock b, double indent)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (!string.IsNullOrEmpty(b.Emoji))
            row.Children.Add(new TextBlock { Text = b.Emoji + " ", VerticalAlignment = VerticalAlignment.Top });
        row.Children.Add(Inlines(b, 13));
        return new Border
        {
            Background = Brush("SecondaryBackground", Color.FromArgb(50, 255, 255, 255)),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(indent, 4, 0, 4), Child = row
        };
    }

    private FrameworkElement Code(CheatBlock b, double indent)
    {
        var text = new TextBlock
        {
            Text = string.Concat(b.Inlines.Select(i => i.Text)),
            FontFamily = new FontFamily("Cascadia Code, Consolas"), FontSize = 12,
            TextWrapping = TextWrapping.Wrap, Foreground = Brush("PrimaryText", Colors.White)
        };
        return new Border
        {
            Background = Brush("PanelBackground", Color.FromArgb(80, 0, 0, 0)), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(indent, 4, 0, 4), Child = text
        };
    }

    private FrameworkElement? RenderImage(CheatBlock b, double indent)
    {
        if (b.ImageBytes is not { Length: > 0 } bytes) return null;
        try
        {
            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(bytes))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
            }
            bmp.Freeze();
            var img = new Image { Source = bmp, MaxWidth = 720, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
            var stack = new StackPanel { Margin = new Thickness(indent, 6, 0, 6) };
            stack.Children.Add(img);
            if (!string.IsNullOrWhiteSpace(b.ImageCaption))
                stack.Children.Add(new TextBlock
                {
                    Text = b.ImageCaption, FontSize = 10, Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush("SecondaryText", Colors.Gray)
                });
            return stack;
        }
        catch { return null; }
    }

    private TextBlock Inlines(CheatBlock b, double size)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = size,
            Foreground = Brush("PrimaryText", Colors.White)
        };
        foreach (var run in b.Inlines)
        {
            var r = new Run(run.Text);
            if (run.Bold) r.FontWeight = FontWeights.Bold;
            if (run.Italic) r.FontStyle = FontStyles.Italic;
            var deco = new TextDecorationCollection();
            if (run.Underline) deco.Add(TextDecorations.Underline[0]);
            if (run.Strikethrough) deco.Add(TextDecorations.Strikethrough[0]);
            if (deco.Count > 0) r.TextDecorations = deco;
            if (run.Code) r.FontFamily = new FontFamily("Cascadia Code, Consolas");
            if (!string.IsNullOrWhiteSpace(run.ColorHex) && TryColor(run.ColorHex!, out var c))
                r.Foreground = new SolidColorBrush(c);
            tb.Inlines.Add(r);
        }
        if (b.Inlines.Count == 0) tb.Text = "";
        return tb;
    }

    private void AddOpenInNotion(string url, string label)
    {
        var link = MakeButton(label, accent: false);
        link.HorizontalAlignment = HorizontalAlignment.Left;
        link.Margin = new Thickness(0, 8, 0, 0);
        link.Click += (_, _) => OpenInNotion(url);
        _pageStack.Children.Add(link);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static bool IsHeading(CheatBlock b) =>
        b.Kind is CheatBlockKind.Heading1 or CheatBlockKind.Heading2 or CheatBlockKind.Heading3;

    private static string BlockText(CheatBlock b) => string.Concat(b.Inlines.Select(i => i.Text));

    private static string? LastSegment(string? headingPath)
    {
        if (string.IsNullOrWhiteSpace(headingPath)) return null;
        var parts = headingPath.Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts[^1] : null;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";

    private static bool TryColor(string hex, out Color color)
    {
        color = Colors.White;
        try { color = (Color)ColorConverter.ConvertFromString(hex); return true; }
        catch { return false; }
    }

    private static void OpenInNotion(string httpsUrl)
    {
        if (string.IsNullOrWhiteSpace(httpsUrl)) return;
        var appUri = httpsUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? "notion://" + httpsUrl["https://".Length..] : httpsUrl;
        if (!TryStart(appUri)) TryStart(httpsUrl);
    }

    private static bool TryStart(string uri)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = uri, UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    private TextBlock Muted(string text) => new()
    {
        Text = text, Opacity = 0.7, Margin = new Thickness(0, 4, 0, 0),
        Foreground = Brush("SecondaryText", Colors.Gray), TextWrapping = TextWrapping.Wrap
    };

    private Button MakeButton(string content, bool accent) => new()
    {
        Content = content, Padding = new Thickness(12, 6, 12, 6), Height = 32,
        Cursor = Cursors.Hand, BorderThickness = new Thickness(accent ? 0 : 1),
        BorderBrush = Brush("PanelBorder", Color.FromRgb(70, 70, 80)),
        Foreground = accent ? Brushes.White : Brush("PrimaryText", Colors.White),
        Background = accent ? Brush("Accent", Color.FromRgb(0, 180, 255)) : Brushes.Transparent
    };

    private void SetStatus(string msg) => _status.Text = msg;

    private Brush Brush(string token, Color fallback)
        => TryFindResource(token) as Brush ?? new SolidColorBrush(fallback);
}
