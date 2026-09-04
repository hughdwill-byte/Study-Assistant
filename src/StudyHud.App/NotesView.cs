using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using StudyHud.Macros.Services;

namespace StudyHud.App;

/// <summary>
/// Captured-notes page (spec §31–34): lists the screenshots saved to %LOCALAPPDATA%\StudyHud\Notes,
/// with a preview and open/delete, plus a capture button and a link to the folder. Built in code.
/// </summary>
public sealed class NotesView : UserControl
{
    private readonly MacroEngine _engine;
    private readonly ILogger<NotesView> _logger;
    private readonly StackPanel _list;
    private readonly TextBlock _status;

    private static string NotesDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudyHud", "Notes");

    public NotesView(MacroEngine engine, ILogger<NotesView> logger)
    {
        _engine = engine;
        _logger = logger;

        var root = new StackPanel { Margin = new Thickness(4) };

        root.Children.Add(new TextBlock
        {
            Text = "Notes",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("PrimaryText", Colors.White),
            Margin = new Thickness(0, 0, 0, 8)
        });

        root.Children.Add(new TextBlock
        {
            Text = "Screenshots you capture (Home → Capture Note, or mouse button 4) are saved here and "
                 + "copied to your clipboard.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(0, 0, 0, 12)
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };

        var captureBtn = MakeButton("Capture Note", accent: true);
        captureBtn.Click += async (_, _) => { await _engine.CaptureNoteAsync(); Refresh(); };
        buttons.Children.Add(captureBtn);

        var refreshBtn = MakeButton("Refresh", accent: false);
        refreshBtn.Margin = new Thickness(8, 0, 0, 0);
        refreshBtn.Click += (_, _) => Refresh();
        buttons.Children.Add(refreshBtn);

        var folderBtn = MakeButton("Open folder", accent: false);
        folderBtn.Margin = new Thickness(8, 0, 0, 0);
        folderBtn.Click += (_, _) => OpenPath(NotesDir);
        buttons.Children.Add(folderBtn);

        root.Children.Add(buttons);

        _list = new StackPanel();
        root.Children.Add(_list);

        _status = new TextBlock
        {
            Opacity = 0.7,
            Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(_status);

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = root
        };

        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        _list.Children.Clear();
        try
        {
            if (!Directory.Exists(NotesDir))
            {
                SetStatus("No notes yet — capture one to get started.");
                return;
            }

            var files = new DirectoryInfo(NotesDir)
                .GetFiles("*.png")
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();

            if (files.Count == 0)
            {
                SetStatus("No notes yet — capture one to get started.");
                return;
            }

            foreach (var file in files)
                _list.Children.Add(BuildNoteCard(file));

            SetStatus($"{files.Count} note{(files.Count == 1 ? "" : "s")}.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not read notes: " + ex.Message);
            _logger.LogWarning(ex, "Listing notes failed.");
        }
    }

    private UIElement BuildNoteCard(FileInfo file)
    {
        var border = new Border
        {
            Background = Brush("SecondaryBackground", Color.FromArgb(180, 40, 40, 48)),
            BorderBrush = Brush("PanelBorder", Color.FromRgb(60, 60, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(10)
        };

        var dock = new DockPanel();

        var thumb = new Image
        {
            Width = 140,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            Source = LoadThumbnail(file.FullName)
        };
        DockPanel.SetDock(thumb, Dock.Left);
        thumb.Margin = new Thickness(0, 0, 12, 0);
        dock.Children.Add(thumb);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var openBtn = MakeButton("Open", accent: true);
        openBtn.Click += (_, _) => OpenPath(file.FullName);
        actions.Children.Add(openBtn);
        var delBtn = MakeButton("Delete", accent: false);
        delBtn.Margin = new Thickness(8, 0, 0, 0);
        delBtn.Click += (_, _) => DeleteNote(file);
        actions.Children.Add(delBtn);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = file.Name,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("PrimaryText", Colors.White),
            TextWrapping = TextWrapping.Wrap
        });
        info.Children.Add(new TextBlock
        {
            Text = $"{file.LastWriteTime:g}  •  {file.Length / 1024:N0} KB",
            Opacity = 0.6,
            FontSize = 11,
            Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(0, 2, 0, 0)
        });
        info.Children.Add(actions);
        dock.Children.Add(info);

        border.Child = dock;
        return border;
    }

    private ImageSource? LoadThumbnail(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;      // don't keep the file locked
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.DecodePixelWidth = 280;                       // small decode for a thumbnail
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load note thumbnail {Path}.", path);
            return null;
        }
    }

    private void DeleteNote(FileInfo file)
    {
        var confirm = MessageBox.Show($"Delete “{file.Name}”? This cannot be undone.",
            "Delete note", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            file.Delete();
            Refresh();
        }
        catch (Exception ex)
        {
            SetStatus($"Could not delete “{file.Name}”: {ex.Message}");
            _logger.LogWarning(ex, "Deleting note failed.");
        }
    }

    private void OpenPath(string path)
    {
        try
        {
            if (path == NotesDir) Directory.CreateDirectory(NotesDir);
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus("Could not open: " + ex.Message);
            _logger.LogWarning(ex, "Opening path {Path} failed.", path);
        }
    }

    private void SetStatus(string msg) => _status.Text = msg;

    private Button MakeButton(string content, bool accent)
        => new()
        {
            Content = content,
            Padding = new Thickness(12, 4, 12, 4),
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0),
            Foreground = accent ? Brushes.White : Brush("SecondaryText", Colors.Gray),
            Background = accent ? Brush("Accent", Color.FromRgb(0, 180, 255)) : Brushes.Transparent
        };

    private Brush Brush(string token, Color fallback)
        => TryFindResource(token) as Brush ?? new SolidColorBrush(fallback);
}
