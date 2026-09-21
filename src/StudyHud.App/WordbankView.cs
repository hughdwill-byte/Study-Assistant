using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StudyHud.Core.Models;
using StudyHud.Core.Services;

namespace StudyHud.App;

/// <summary>
/// Wordbank settings page: a searchable glossary of your OWN key terms and phrases, harvested from your
/// indexed notes — titles, headings, note text and the OCR'd text on your images/attachments. Pick a
/// course, browse the key terms (the concepts you named in headings float to the top), or type to find
/// the exact wording of a term and jump straight to the note it came from.
///
/// It never invents wording and never writes an answer — it only reflects and locates what is already
/// in your notes, so it makes revision and open-book study faster without generating anything.
/// </summary>
public sealed class WordbankView : UserControl
{
    private readonly IWordbank _wordbank;
    private readonly ICourseRepository _courses;

    private readonly ComboBox _course;
    private readonly TextBox _search;
    private readonly StackPanel _list;
    private readonly TextBlock _status;

    private readonly List<Course> _courseList = new();
    private string? _courseId;

    public WordbankView(IWordbank wordbank, ICourseRepository courses)
    {
        _wordbank = wordbank;
        _courses = courses;

        var root = new StackPanel { Margin = new Thickness(4) };
        root.Children.Add(Title("Wordbank"));
        root.Children.Add(Body("A glossary of your own key terms and phrases, pulled from your notes' "
            + "titles, headings, text and the words on your images. Type to find the exact wording of a "
            + "term, then open the note it came from. It only ever locates your own notes — it never "
            + "writes answers."));

        root.Children.Add(Header("COURSE"));
        _course = new ComboBox { Height = 30, VerticalContentAlignment = VerticalAlignment.Center };
        _course.SelectionChanged += (_, _) => OnCourseChanged();
        root.Children.Add(_course);

        root.Children.Add(Header("SEARCH TERMS"));
        _search = new TextBox { Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
        _search.TextChanged += (_, _) => _ = RefreshAsync();
        root.Children.Add(_search);

        _status = new TextBlock
        {
            Opacity = 0.75, Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(_status);

        _list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        root.Children.Add(_list);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        Loaded += async (_, _) => await LoadCoursesAsync();
    }

    private async Task LoadCoursesAsync()
    {
        try
        {
            var courses = await _courses.GetAllAsync();
            _courseList.Clear();
            _courseList.AddRange(courses);

            _course.Items.Clear();
            foreach (var c in _courseList) _course.Items.Add(c.Name);

            if (_courseList.Count == 0)
            {
                SetStatus("No courses yet. Add and sync a course in the Library first, then its wordbank appears here.");
                return;
            }

            _course.SelectedIndex = 0; // triggers OnCourseChanged → build
        }
        catch (Exception ex)
        {
            SetStatus("Couldn't load courses: " + ex.Message);
        }
    }

    private void OnCourseChanged()
    {
        int i = _course.SelectedIndex;
        _courseId = i >= 0 && i < _courseList.Count ? _courseList[i].CourseId : null;
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_courseId is null) return;
        var courseId = _courseId;
        var query = _search.Text?.Trim() ?? "";

        try
        {
            IReadOnlyList<WordbankEntry> entries = query.Length == 0
                ? await _wordbank.BuildAsync(courseId)
                : await _wordbank.LookupAsync(courseId, query, 60);

            // The course could have changed while we awaited — ignore a stale result.
            if (courseId != _courseId) return;

            Render(entries, query);
        }
        catch (Exception ex)
        {
            SetStatus("Wordbank error: " + ex.Message);
        }
    }

    private void Render(IReadOnlyList<WordbankEntry> entries, string query)
    {
        _list.Children.Clear();

        if (entries.Count == 0)
        {
            SetStatus(query.Length == 0
                ? "This course has no indexed notes yet. Sync it in the Library, then its terms appear here."
                : $"No terms matching “{query}”.");
            return;
        }

        int keyCount = entries.Count(e => e.IsKeyTerm);
        SetStatus(query.Length == 0
            ? $"{entries.Count} terms · {keyCount} key terms (from your titles & headings)."
            : $"{entries.Count} matches for “{query}”.");

        // Cap what we render so a huge course stays responsive; the list is already ranked.
        foreach (var e in entries.Take(400))
            _list.Children.Add(EntryRow(e));
    }

    private UIElement EntryRow(WordbankEntry entry)
    {
        var card = new Border
        {
            Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(10, 7, 10, 8),
            CornerRadius = new CornerRadius(6),
            Background = Brush("PanelBackground", Color.FromArgb(40, 255, 255, 255))
        };
        var stack = new StackPanel();

        var head = new StackPanel { Orientation = Orientation.Horizontal };
        if (entry.IsKeyTerm)
            head.Children.Add(new TextBlock
            {
                Text = "★", FontSize = 13, Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush("Accent", Color.FromRgb(90, 178, 168)),
                ToolTip = "Key term — you named this in a title or heading"
            });

        head.Children.Add(new TextBlock
        {
            Text = entry.Term, FontSize = 14, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("PrimaryText", Colors.White)
        });

        head.Children.Add(new TextBlock
        {
            Text = (entry.IsPhrase ? "phrase · " : "") + $"{entry.Frequency}×",
            FontSize = 10, Opacity = 0.6, Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("SecondaryText", Colors.Gray)
        });
        stack.Children.Add(head);

        // Location chips — click to open the exact note in Notion.
        var chips = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
        foreach (var loc in entry.Locations.Take(6))
        {
            var label = string.IsNullOrWhiteSpace(loc.WeekLabel) ? loc.PageName : $"{loc.WeekLabel} · {loc.PageName}";
            var chip = new Button
            {
                Content = label, Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(8, 3, 8, 3),
                Cursor = System.Windows.Input.Cursors.Hand, BorderThickness = new Thickness(1),
                BorderBrush = Brush("PanelBorder", Color.FromRgb(70, 70, 80)),
                Background = Brushes.Transparent, Foreground = Brush("SecondaryText", Colors.Gray),
                ToolTip = string.IsNullOrWhiteSpace(loc.HeadingPath) ? "Open in Notion" : loc.HeadingPath + " — open in Notion"
            };
            var url = loc.NotionPageUrl;
            chip.Click += (_, _) => OpenUrl(url);
            chips.Children.Add(chip);
        }
        if (entry.Locations.Count > 6)
            chips.Children.Add(new TextBlock
            {
                Text = $"+{entry.Locations.Count - 6} more", FontSize = 10, Opacity = 0.6,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush("SecondaryText", Colors.Gray)
            });
        stack.Children.Add(chips);

        card.Child = stack;
        return card;
    }

    private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { SetStatus("Couldn't open the note: " + ex.Message); }
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

    private Brush Brush(string token, Color fallback)
        => TryFindResource(token) as Brush ?? new SolidColorBrush(fallback);
}
