using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StudyHud.Core.Models;
using StudyHud.Core.Services;

namespace StudyHud.Overlay.Controls;

/// <summary>
/// Question Finder workspace panel (spec §38, §57).
/// Before capture: shows course selector + Draw Question button.
/// After capture: shows ranked results with match explanations.
/// </summary>
public sealed class QuestionFinderPanel : HudPanelBase
{
    private readonly IApplicationStateService _appState;
    private readonly ISearchIndex _searchIndex;
    private Grid _preCapture = null!;
    private Grid _postCapture = null!;
    private ComboBox _courseCombo = null!;
    private StackPanel _resultsStack = null!;
    private TextBlock _statusLabel = null!;

    public QuestionFinderPanel(
        IApplicationStateService appState,
        IThemeService theme,
        ISearchIndex searchIndex)
        : base("question-finder-panel", appState, theme)
    {
        _appState = appState;
        _searchIndex = searchIndex;
        MinWidth = 200;
        MinHeight = 120;
        Width = 320;
        Height = 380;
    }

    protected override string PanelTitle => "Question Finder";

    protected override void PopulateContent(Grid contentGrid)
    {
        var root = new DockPanel { Margin = new Thickness(10, 8, 10, 8) };

        // ── Status bar at bottom ─────────────────────────────────────────
        _statusLabel = new TextBlock
        {
            FontSize = 10,
            Opacity = 0.6,
            Foreground = Application.Current.TryFindResource("SecondaryText") as Brush ?? Brushes.Gray,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        DockPanel.SetDock(_statusLabel, Dock.Bottom);
        root.Children.Add(_statusLabel);

        // ── Pre-capture view ─────────────────────────────────────────────
        _preCapture = new Grid();
        _preCapture.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _preCapture.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _preCapture.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Course label + combo
        var courseLabel = new TextBlock
        {
            Text = "COURSE",
            FontSize = 9,
            Opacity = 0.5,
            Foreground = Application.Current.TryFindResource("SecondaryText") as Brush ?? Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(courseLabel, 0);

        _courseCombo = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 12),
            FontSize = 12,
            IsEditable = false
        };
        _courseCombo.Items.Add("All Courses");
        _courseCombo.Items.Add("Engineering Mathematics");
        _courseCombo.Items.Add("Systems Engineering");
        _courseCombo.Items.Add("Materials");
        _courseCombo.Items.Add("Mechanics");
        _courseCombo.SelectedIndex = 0;
        Grid.SetRow(_courseCombo, 1);

        // Draw Question button
        var drawBtn = new Button
        {
            Content = "⬛  Draw Question",
            FontSize = 14,
            Padding = new Thickness(16, 12, 16, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Application.Current.TryFindResource("Accent") as Brush
                         ?? new SolidColorBrush(Color.FromRgb(0, 180, 255)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        drawBtn.Click += OnDrawQuestion;
        Grid.SetRow(drawBtn, 2);

        _preCapture.Children.Add(courseLabel);
        _preCapture.Children.Add(_courseCombo);
        _preCapture.Children.Add(drawBtn);

        // ── Post-capture results view ─────────────────────────────────────
        _postCapture = new Grid { Visibility = Visibility.Collapsed };
        _postCapture.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _postCapture.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var resultsHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var resultsTitle = new TextBlock
        {
            Text = "RESULTS",
            FontSize = 9,
            Opacity = 0.5,
            Foreground = Application.Current.TryFindResource("SecondaryText") as Brush ?? Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center
        };
        var newSearchBtn = new Button
        {
            Content = "← New",
            FontSize = 10,
            Padding = new Thickness(6, 2, 6, 2),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = Application.Current.TryFindResource("Accent") as Brush
                         ?? new SolidColorBrush(Color.FromRgb(0, 180, 255)),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        newSearchBtn.Click += (_, _) => ShowPreCapture();
        DockPanel.SetDock(newSearchBtn, Dock.Right);
        resultsHeader.Children.Add(newSearchBtn);
        resultsHeader.Children.Add(resultsTitle);
        Grid.SetRow(resultsHeader, 0);

        var resultsScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _resultsStack = new StackPanel { Orientation = Orientation.Vertical };
        resultsScroll.Content = _resultsStack;
        Grid.SetRow(resultsScroll, 1);

        _postCapture.Children.Add(resultsHeader);
        _postCapture.Children.Add(resultsScroll);

        // Add both views
        DockPanel.SetDock(_preCapture, Dock.Top);
        DockPanel.SetDock(_postCapture, Dock.Top);
        root.Children.Add(_preCapture);
        root.Children.Add(_postCapture);

        contentGrid.Children.Add(root);
        SetStatus("Ready — select a course and draw a question.");
    }

    private async void OnDrawQuestion(object sender, RoutedEventArgs e)
    {
        // Phase 10 will wire this to the full CaptureService → OCR → Search pipeline.
        // For Phase 2 we show the flow works end-to-end with a placeholder.
        SetStatus("Drawing region… (capture + OCR in Phase 10)");

        await Task.Delay(500); // Simulate async work

        // Show placeholder results
        ShowResults([
            new SearchResult
            {
                NoteItemId = "demo-1",
                CourseId = "eng-maths",
                CourseName = "Engineering Mathematics",
                WeekLabel = "Week 6",
                PageName = "Bending and Flexure",
                HeadingPath = "Flexure Formula",
                NotionPageUrl = "https://notion.so",
                NotionBlockId = null,
                MatchScore = 92,
                Explanations =
                [
                    new MatchExplanation { Type = MatchType.Variable, Value = "M" },
                    new MatchExplanation { Type = MatchType.Variable, Value = "I" },
                    new MatchExplanation { Type = MatchType.Word, Value = "bending" },
                    new MatchExplanation { Type = MatchType.Word, Value = "stress" }
                ]
            }
        ]);
    }

    private void ShowResults(IReadOnlyList<SearchResult> results)
    {
        _resultsStack.Children.Clear();

        if (results.Count == 0)
        {
            _resultsStack.Children.Add(new TextBlock
            {
                Text = "No strong matches found.",
                Opacity = 0.6,
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = Application.Current.TryFindResource("SecondaryText") as Brush ?? Brushes.Gray
            });
        }
        else
        {
            foreach (var r in results.Take(3))
                _resultsStack.Children.Add(BuildResultCard(r));
        }

        _preCapture.Visibility = Visibility.Collapsed;
        _postCapture.Visibility = Visibility.Visible;
        SetStatus($"{results.Count} result(s) found.");
    }

    private UIElement BuildResultCard(SearchResult result)
    {
        var card = new Border
        {
            Background = Application.Current.TryFindResource("SecondaryBackground") as Brush
                         ?? new SolidColorBrush(Color.FromArgb(180, 40, 40, 48)),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(10, 8, 10, 8)
        };

        var stack = new StackPanel();

        // Week + heading
        var headerRow = new DockPanel();
        var weekLabel = new TextBlock
        {
            Text = result.WeekLabel ?? "",
            FontSize = 9,
            Opacity = 0.5,
            Foreground = Application.Current.TryFindResource("SecondaryText") as Brush ?? Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center
        };
        var scoreLabel = new TextBlock
        {
            Text = $"{result.MatchScore:F0}% match",
            FontSize = 9,
            Foreground = Application.Current.TryFindResource("Accent") as Brush
                         ?? new SolidColorBrush(Color.FromRgb(0, 180, 255)),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(scoreLabel, Dock.Right);
        headerRow.Children.Add(scoreLabel);
        headerRow.Children.Add(weekLabel);

        var heading = new TextBlock
        {
            Text = result.HeadingPath,
            FontSize = 12,
            Foreground = Application.Current.TryFindResource("PrimaryText") as Brush ?? Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 0, 4),
            TextWrapping = TextWrapping.Wrap
        };

        // Matched terms
        var matchedText = string.Join("  •  ", result.Explanations.Take(4).Select(x => x.Value));
        var matched = new TextBlock
        {
            Text = "Matched: " + matchedText,
            FontSize = 10,
            Opacity = 0.7,
            Foreground = Application.Current.TryFindResource("SecondaryText") as Brush ?? Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 6),
            TextWrapping = TextWrapping.Wrap
        };

        var openBtn = new Button
        {
            Content = "OPEN IN NOTION →",
            FontSize = 10,
            Padding = new Thickness(8, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = Application.Current.TryFindResource("Accent") as Brush
                         ?? new SolidColorBrush(Color.FromRgb(0, 180, 255)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        openBtn.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = result.NotionPageUrl, UseShellExecute = true }); }
            catch { /* Graceful fail */ }
        };

        stack.Children.Add(headerRow);
        stack.Children.Add(heading);
        stack.Children.Add(matched);
        stack.Children.Add(openBtn);
        card.Child = stack;
        return card;
    }

    private void ShowPreCapture()
    {
        _preCapture.Visibility = Visibility.Visible;
        _postCapture.Visibility = Visibility.Collapsed;
        _resultsStack.Children.Clear();
        SetStatus("Ready — select a course and draw a question.");
    }

    private void SetStatus(string msg) => _statusLabel.Text = msg;
}
