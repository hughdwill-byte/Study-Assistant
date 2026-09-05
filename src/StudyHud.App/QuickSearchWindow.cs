using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Services;

namespace StudyHud.App;

/// <summary>
/// Spotlight-style quick-search palette (Focus Mode design, artboard 3): a floating search bar that
/// queries the local note index and opens a match in Notion. Toggled by a global hotkey. It searches
/// the user's own notes and never answers the question (spec §38, §39).
/// </summary>
public sealed class QuickSearchWindow : Window
{
    private readonly IQuestionFinder _finder;
    private readonly ILogger _logger;

    private readonly TextBox _input;
    private readonly ListBox _results;
    private readonly TextBlock _status;
    private readonly DispatcherTimer _debounce;

    public QuickSearchWindow(IQuestionFinder finder, ILogger logger)
    {
        _finder = finder;
        _logger = logger;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 620;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var shell = new Border
        {
            Background = Brush("SurfaceBackground", Color.FromArgb(245, 32, 32, 38)),
            BorderBrush = Brush("PanelBorder", Color.FromRgb(60, 60, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = TryFindResource("CornerRadius") is CornerRadius cr ? cr : new CornerRadius(10),
            Padding = new Thickness(14)
        };
        var stack = new StackPanel();

        _input = new TextBox
        {
            FontSize = 18,
            Background = Brushes.Transparent,
            Foreground = Brush("PrimaryText", Colors.White),
            BorderThickness = new Thickness(0),
            CaretBrush = Brush("Accent", Color.FromRgb(0, 180, 255)),
            Padding = new Thickness(2, 4, 2, 6)
        };
        _input.TextChanged += (_, _) => _debounce.Stop();
        _input.TextChanged += (_, _) => _debounce.Start();
        _input.PreviewKeyDown += OnKey;

        // Prompt glyph "❯" to the left of the input (additive; amber under Retro via the Accent token).
        var inputRow = new DockPanel();
        var prompt = new TextBlock
        {
            Text = "❯",
            FontSize = 18,
            Margin = new Thickness(2, 4, 10, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("Accent", Color.FromRgb(0, 180, 255))
        };
        DockPanel.SetDock(prompt, Dock.Left);
        inputRow.Children.Add(prompt);
        inputRow.Children.Add(_input);
        stack.Children.Add(inputRow);

        stack.Children.Add(new Border
        {
            Height = 1, Background = Brush("PanelBorder", Color.FromRgb(60, 60, 70)),
            Margin = new Thickness(0, 4, 0, 6)
        });

        _results = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            MaxHeight = 360,
            Foreground = Brush("PrimaryText", Colors.White)
        };
        _results.MouseDoubleClick += (_, _) => OpenSelected();
        stack.Children.Add(_results);

        _status = new TextBlock
        {
            Text = "Type to search your notes…  ↵ open   ·   Esc close",
            Opacity = 0.55, FontSize = 11,
            Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(2, 6, 0, 0)
        };
        stack.Children.Add(_status);

        shell.Child = stack;
        Content = shell;

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await RunSearchAsync(); };

        Deactivated += (_, _) => Hide();
        PreviewKeyDown += OnKey;
    }

    public void ShowPalette()
    {
        // Centre near the top of the primary work area.
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = SystemParameters.WorkArea.Top + SystemParameters.PrimaryScreenHeight * 0.18;

        _input.Text = string.Empty;
        _results.Items.Clear();
        Show();
        Activate();
        _input.Focus();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
            case Key.Enter:
                OpenSelected();
                e.Handled = true;
                break;
            case Key.Down:
                Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;
        }
    }

    private void Move(int delta)
    {
        if (_results.Items.Count == 0) return;
        int i = _results.SelectedIndex + delta;
        _results.SelectedIndex = Math.Clamp(i, 0, _results.Items.Count - 1);
        _results.ScrollIntoView(_results.SelectedItem);
    }

    private async Task RunSearchAsync()
    {
        var query = _input.Text.Trim();
        _results.Items.Clear();
        if (query.Length < 2)
        {
            _status.Text = "Type to search your notes…  ↵ open   ·   Esc close";
            return;
        }

        try
        {
            var result = await _finder.FindFromTextAsync(query, maxResults: 8);
            if (result.Results.Count == 0)
            {
                _status.Text = $"No matches for “{query}”.";
                return;
            }

            foreach (var r in result.Results)
                _results.Items.Add(BuildItem(r));
            if (_results.Items.Count > 0) _results.SelectedIndex = 0;
            _status.Text = $"{result.Results.Count} match(es)   ·   ↵ open in Notion";
        }
        catch (Exception ex)
        {
            _status.Text = "Search failed.";
            _logger.LogWarning(ex, "Quick search failed for '{Query}'.", query);
        }
    }

    private ListBoxItem BuildItem(SearchResult r)
    {
        var panel = new StackPanel { Margin = new Thickness(2) };
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(r.HeadingPath) ? r.PageName : r.HeadingPath,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("PrimaryText", Colors.White),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var sub = $"{r.CourseName}"
                + (string.IsNullOrEmpty(r.WeekLabel) ? "" : $"  ·  {r.WeekLabel}")
                + $"  ·  {r.MatchScore:F0}% match";
        panel.Children.Add(new TextBlock
        {
            Text = sub, Opacity = 0.6, FontSize = 11,
            Foreground = Brush("SecondaryText", Colors.Gray)
        });
        return new ListBoxItem { Content = panel, Tag = r };
    }

    private void OpenSelected()
    {
        if (_results.SelectedItem is not ListBoxItem { Tag: SearchResult r }) return;
        OpenInNotion(r.NotionPageUrl);
        Hide();
    }

    private static void OpenInNotion(string httpsUrl)
    {
        if (string.IsNullOrWhiteSpace(httpsUrl)) return;
        var appUri = httpsUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? "notion://" + httpsUrl["https://".Length..]
            : httpsUrl;
        if (TryStart(appUri)) return;
        TryStart(httpsUrl);
    }

    private static bool TryStart(string uri)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo { FileName = uri, UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    // Never actually close (Alt+F4 / hotkey toggles visibility) — hide instead so it can be reshown.
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private Brush Brush(string token, Color fallback)
        => TryFindResource(token) as Brush
           ?? (Application.Current?.TryFindResource(token) as Brush)
           ?? new SolidColorBrush(fallback);
}
