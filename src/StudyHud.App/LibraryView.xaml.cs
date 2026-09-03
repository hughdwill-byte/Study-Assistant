using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Models;
using StudyHud.Core.Services;

namespace StudyHud.App;

/// <summary>
/// Study Library management page (spec §43, §45, §60): store the Notion integration token, add and
/// manage courses (each with its Notion root page id), sync a course into the local index, and view
/// per-course index health. Code-behind + named controls, consistent with <see cref="SettingsView"/>.
/// </summary>
public partial class LibraryView : UserControl
{
    private readonly ICourseRepository _courses;
    private readonly INoteSource _notes;
    private readonly IAssessmentPolicyService _policy;
    private readonly ILogger<LibraryView> _logger;

    public LibraryView(
        ICourseRepository courses,
        INoteSource notes,
        IAssessmentPolicyService policy,
        ILogger<LibraryView> logger)
    {
        _courses = courses;
        _notes = notes;
        _policy = policy;
        _logger = logger;

        InitializeComponent();
        Loaded += (_, _) => _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            AssessmentBanner.Visibility = _policy.IsAssessmentModeActive
                ? Visibility.Visible : Visibility.Collapsed;

            var hasToken = await _notes.HasStoredTokenAsync();
            ConnectionStatus.Text = hasToken
                ? "A Notion token is stored on this device."
                : "No Notion token stored yet — paste your integration secret below.";

            var courses = await _courses.GetAllAsync();
            CoursesStack.Children.Clear();

            if (courses.Count == 0)
            {
                CoursesStack.Children.Add(new TextBlock
                {
                    Text = "No courses yet. Add one above, then Sync it before an assessment.",
                    Opacity = 0.6,
                    Margin = new Thickness(0, 4, 0, 0)
                });
                return;
            }

            foreach (var course in courses)
            {
                var health = await _courses.GetIndexHealthAsync(course.CourseId);
                CoursesStack.Children.Add(BuildCourseCard(course, health));
            }
        }
        catch (Exception ex)
        {
            SetStatus("Failed to load library: " + ex.Message);
            _logger.LogWarning(ex, "Library refresh failed.");
        }
    }

    private UIElement BuildCourseCard(Course course, CourseIndexHealth health)
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
            Text = course.Name,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14
        });

        stack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(course.NotionRootPageId)
                ? "No Notion root page set — edit and re-add to configure."
                : $"Notion page: {course.NotionRootPageId}",
            Opacity = 0.6,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 4),
            TextWrapping = TextWrapping.Wrap
        });

        stack.Children.Add(new TextBlock
        {
            Text = DescribeHealth(health, course.LastSyncedAt),
            Opacity = 0.85,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };

        var syncBtn = new Button
        {
            Content = "Sync",
            Padding = new Thickness(12, 4, 12, 4),
            Background = Brush("Accent", Color.FromRgb(0, 180, 255)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            IsEnabled = !_policy.IsAssessmentModeActive
        };
        syncBtn.Click += (_, _) => _ = SyncCourseAsync(course);
        buttons.Children.Add(syncBtn);

        var deleteBtn = new Button
        {
            Content = "Delete",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(12, 4, 12, 4),
            Background = Brushes.Transparent,
            Foreground = Brush("SecondaryText", Colors.Gray),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        deleteBtn.Click += (_, _) => _ = DeleteCourseAsync(course);
        buttons.Children.Add(deleteBtn);

        stack.Children.Add(buttons);
        card.Child = stack;
        return card;
    }

    private static string DescribeHealth(CourseIndexHealth h, DateTimeOffset? lastSynced)
    {
        var sb = new StringBuilder();
        sb.Append(h.Status).Append("  •  ");
        sb.Append($"{h.IndexedImages} indexed");
        if (h.LowConfidenceItems > 0) sb.Append($", {h.LowConfidenceItems} low-confidence");
        if (h.UnavailableSourceItems > 0) sb.Append($", {h.UnavailableSourceItems} failed");
        sb.Append($"  •  {h.TotalPages} pages, {h.TotalWeeks} weeks");
        sb.Append(lastSynced is { } t ? $"  •  last sync {t.LocalDateTime:g}" : "  •  never synced");
        return sb.ToString();
    }

    private async void OnSaveToken(object sender, RoutedEventArgs e)
    {
        var token = TokenBox.Password;
        if (string.IsNullOrWhiteSpace(token))
        {
            SetStatus("Enter a token first.");
            return;
        }

        try
        {
            await _notes.StoreTokenAsync(token);
            TokenBox.Clear();
            SetStatus("Notion token saved (encrypted on this device).");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            SetStatus("Could not save token: " + ex.Message);
            _logger.LogWarning(ex, "Saving Notion token failed.");
        }
    }

    private async void OnTestConnection(object sender, RoutedEventArgs e)
    {
        SetStatus("Testing connection…");
        try
        {
            var ok = await _notes.TestConnectionAsync();
            SetStatus(ok
                ? "Connected to Notion."
                : _policy.IsAssessmentModeActive
                    ? "Notion is blocked while Assessment Mode is active."
                    : "Could not connect — check the token and that pages are shared with the integration.");
        }
        catch (Exception ex)
        {
            SetStatus("Connection test failed: " + ex.Message);
        }
    }

    private async void OnAddCourse(object sender, RoutedEventArgs e)
    {
        var name = CourseNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            SetStatus("Enter a course name.");
            return;
        }

        var course = new Course
        {
            CourseId = $"{Slug(name)}-{Guid.NewGuid().ToString("N")[..6]}",
            Name = name,
            NotionRootPageId = Nullify(CourseRootBox.Text),
            Description = Nullify(CourseDescBox.Text)
        };

        try
        {
            await _courses.UpsertAsync(course);
            CourseNameBox.Clear();
            CourseRootBox.Clear();
            CourseDescBox.Clear();
            SetStatus($"Added course “{name}”.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            SetStatus("Could not add course: " + ex.Message);
            _logger.LogWarning(ex, "Adding course failed.");
        }
    }

    private async void OnDiscoverPages(object sender, RoutedEventArgs e)
    {
        if (_policy.IsAssessmentModeActive)
        {
            SetStatus("Page discovery is disabled in Assessment Mode.");
            return;
        }

        SetStatus("Discovering pages shared with your integration…");
        try
        {
            var pages = await _notes.DiscoverPagesAsync();
            DiscoveredStack.Children.Clear();

            if (pages.Count == 0)
            {
                DiscoveredStack.Children.Add(new TextBlock
                {
                    Text = "No pages found. Save your token first, and make sure you've shared the "
                         + "pages with your integration in Notion (••• → Connections).",
                    Opacity = 0.6,
                    TextWrapping = TextWrapping.Wrap
                });
                SetStatus("No shared pages found.");
                return;
            }

            foreach (var p in pages)
                DiscoveredStack.Children.Add(BuildDiscoveredRow(p));

            SetStatus($"Found {pages.Count} shared page(s). Click “Add as course” on the ones you want.");
        }
        catch (Exception ex)
        {
            SetStatus("Discovery failed: " + ex.Message);
            _logger.LogWarning(ex, "Notion page discovery failed.");
        }
    }

    private UIElement BuildDiscoveredRow(DiscoveredPage page)
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

        var addBtn = new Button
        {
            Content = "Add as course",
            Padding = new Thickness(12, 4, 12, 4),
            Background = Brush("Accent", Color.FromRgb(0, 180, 255)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            IsEnabled = !_policy.IsAssessmentModeActive
        };
        addBtn.Click += (_, _) => _ = AddDiscoveredAsync(page);
        DockPanel.SetDock(addBtn, Dock.Right);
        dock.Children.Add(addBtn);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = page.Title, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = page.Id,
            Opacity = 0.5,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        });
        dock.Children.Add(text);

        border.Child = dock;
        return border;
    }

    private async Task AddDiscoveredAsync(DiscoveredPage page)
    {
        var course = new Course
        {
            CourseId = $"{Slug(page.Title)}-{Guid.NewGuid().ToString("N")[..6]}",
            Name = page.Title,
            NotionRootPageId = page.Id
        };

        try
        {
            await _courses.UpsertAsync(course);
            SetStatus($"Added “{page.Title}”. Click Sync on it to index its notes.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Could not add “{page.Title}”: {ex.Message}");
            _logger.LogWarning(ex, "Adding discovered course failed.");
        }
    }

    private async Task SyncCourseAsync(Course course)
    {
        if (_policy.IsAssessmentModeActive)
        {
            SetStatus("Sync is disabled in Assessment Mode.");
            return;
        }
        if (string.IsNullOrWhiteSpace(course.NotionRootPageId))
        {
            SetStatus($"“{course.Name}” has no Notion root page id set.");
            return;
        }

        SetStatus($"Syncing “{course.Name}”…");
        try
        {
            await _notes.SyncCourseAsync(course.CourseId);
            SetStatus($"Synced “{course.Name}”.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Sync failed for “{course.Name}”: {ex.Message}");
            _logger.LogWarning(ex, "Course sync failed.");
        }
    }

    private async Task DeleteCourseAsync(Course course)
    {
        var confirm = MessageBox.Show(
            $"Delete “{course.Name}” and all its indexed notes? This cannot be undone.",
            "Delete course", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            await _courses.DeleteAsync(course.CourseId);
            SetStatus($"Deleted “{course.Name}”.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Could not delete “{course.Name}”: {ex.Message}");
            _logger.LogWarning(ex, "Course delete failed.");
        }
    }

    private void SetStatus(string message) => StatusLabel.Text = message;

    private Brush Brush(string token, Color fallback)
        => TryFindResource(token) as Brush ?? new SolidColorBrush(fallback);

    private static string? Nullify(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string Slug(string name)
    {
        var chars = name.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return string.IsNullOrEmpty(slug) ? "course" : slug;
    }
}
