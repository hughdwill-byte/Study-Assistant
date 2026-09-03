using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Models;
using StudyHud.Core.Services;
using StudyHud.Windows.Native;

namespace StudyHud.Capture;

/// <summary>
/// One-gesture region capture (spec §32, §33, §139).
///
/// The screen is snapshotted to an off-screen bitmap BEFORE the selection overlay is shown, and the
/// selected region is cropped from that clean snapshot. This is deliberate: the overlay dims the
/// screen and draws a selection rectangle, so grabbing pixels from the live screen after it appears
/// would bake the dimming and the selection box into the captured image and ruin OCR.
/// </summary>
public sealed class CaptureService : ICaptureService, IDisposable
{
    private readonly IMonitorService _monitors;
    private readonly IApplicationStateService _appState;
    private readonly ILogger<CaptureService> _logger;

    public CaptureService(
        IMonitorService monitors,
        IApplicationStateService appState,
        ILogger<CaptureService> logger)
    {
        _monitors = monitors;
        _appState = appState;
        _logger = logger;
    }

    public async Task<CaptureResult?> CaptureRegionAsync(CancellationToken ct = default)
    {
        _appState.SetCaptureModeActive(true);
        try
        {
            return await ShowCaptureOverlayAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Region capture failed.");
            return null;
        }
        finally
        {
            _appState.SetCaptureModeActive(false);
        }
    }

    private async Task<CaptureResult?> ShowCaptureOverlayAsync(CancellationToken ct)
    {
        // 1) Snapshot the whole virtual desktop in physical pixels, before any overlay is shown.
        using var snapshot = CaptureVirtualDesktop(out int physLeft, out int physTop);
        if (snapshot is null)
        {
            _logger.LogError("Could not snapshot the desktop for capture.");
            return null;
        }

        // 2) Show the transparent selection overlay and wait for the user's rectangle.
        var tcs = new TaskCompletionSource<ScreenRect?>();
        using var reg = ct.Register(() => tcs.TrySetResult(null));

        CaptureOverlayWindow? overlay = null;
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            overlay = new CaptureOverlayWindow(_monitors, _logger);
            overlay.CaptureCompleted += (_, rect) => tcs.TrySetResult(rect);
            overlay.CaptureCancelled += (_, _) => tcs.TrySetResult(null);
            overlay.Show();
        });

        var physicalRect = await tcs.Task;

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => overlay?.Close());

        if (physicalRect is not { } rect || rect.Width < 5 || rect.Height < 5)
        {
            _logger.LogDebug("Capture cancelled or region too small.");
            return null;
        }

        // 3) Crop the selected region out of the clean snapshot.
        var imageBytes = CropToPng(snapshot, physLeft, physTop, rect);
        if (imageBytes.Length == 0)
        {
            _logger.LogWarning("Captured region produced no image bytes.");
            return null;
        }

        var monitor = _monitors.GetMonitorAtPoint(
            new ScreenPoint(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2));

        await CopyToClipboardAsync(imageBytes);
        _logger.LogInformation("Screenshot captured: {W}×{H}px.", rect.Width, rect.Height);

        return new CaptureResult
        {
            ImageBytes = imageBytes,
            PhysicalRect = rect,
            MonitorId = monitor?.MonitorId ?? string.Empty,
            WasCancelled = false
        };
    }

    /// <summary>
    /// Grabs the full virtual desktop (all monitors) as a GDI bitmap in physical pixels.
    /// <paramref name="physLeft"/>/<paramref name="physTop"/> are the physical coordinates of the
    /// snapshot's top-left, used to translate absolute screen coordinates into snapshot offsets.
    /// </summary>
    private System.Drawing.Bitmap? CaptureVirtualDesktop(out int physLeft, out int physTop)
    {
        physLeft = 0;
        physTop = 0;

        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (var m in _monitors.Monitors)
        {
            left = Math.Min(left, m.Bounds.Left);
            top = Math.Min(top, m.Bounds.Top);
            right = Math.Max(right, m.Bounds.Right);
            bottom = Math.Max(bottom, m.Bounds.Bottom);
        }

        if (left == int.MaxValue) // no monitors reported — fall back to the primary DC size
        {
            var p = _monitors.PrimaryMonitor;
            if (p is null) return null;
            left = p.Bounds.Left; top = p.Bounds.Top; right = p.Bounds.Right; bottom = p.Bounds.Bottom;
        }

        physLeft = left;
        physTop = top;
        int w = Math.Max(1, right - left);
        int h = Math.Max(1, bottom - top);

        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        var memDc = NativeMethods.CreateCompatibleDC(screenDc);
        var hBitmap = NativeMethods.CreateCompatibleBitmap(screenDc, w, h);
        var oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

        NativeMethods.BitBlt(memDc, 0, 0, w, h, screenDc, left, top, NativeMethods.SRCCOPY);

        NativeMethods.SelectObject(memDc, oldBitmap);
        NativeMethods.DeleteDC(memDc);
        NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);

        try
        {
            // FromHbitmap copies the pixels; the HBITMAP can be freed straight after.
            return System.Drawing.Image.FromHbitmap(hBitmap);
        }
        finally
        {
            NativeMethods.DeleteObject(hBitmap);
        }
    }

    private byte[] CropToPng(System.Drawing.Bitmap snapshot, int physLeft, int physTop, ScreenRect rect)
    {
        try
        {
            int offX = rect.Left - physLeft;
            int offY = rect.Top - physTop;
            int w = rect.Width;
            int h = rect.Height;

            // Clamp to the snapshot so a selection that runs off the captured area can't throw.
            offX = Math.Clamp(offX, 0, Math.Max(0, snapshot.Width - 1));
            offY = Math.Clamp(offY, 0, Math.Max(0, snapshot.Height - 1));
            w = Math.Clamp(w, 1, snapshot.Width - offX);
            h = Math.Clamp(h, 1, snapshot.Height - offY);

            using var cropped = snapshot.Clone(
                new System.Drawing.Rectangle(offX, offY, w, h), snapshot.PixelFormat);
            using var ms = new MemoryStream();
            cropped.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cropping the captured region failed.");
            return Array.Empty<byte>();
        }
    }

    private async Task CopyToClipboardAsync(byte[] pngBytes)
    {
        try
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var image = new System.Windows.Media.Imaging.BitmapImage();
                using var ms = new MemoryStream(pngBytes);
                image.BeginInit();
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze();
                System.Windows.Clipboard.SetImage(image);
            });
        }
        catch (Exception ex)
        {
            // Clipboard copy is a convenience (spec §34) — never fail the capture over it.
            _logger.LogWarning(ex, "Could not copy capture to clipboard.");
        }
    }

    public void Dispose() { }
}

// ─── Capture Overlay Window ─────────────────────────────────────────────────

/// <summary>
/// Full-virtual-desktop transparent overlay for selection drawing (spec §33).
/// Covers all monitors, dims the screen, and reports the selected region in physical pixels.
/// It does NOT capture pixels itself — <see cref="CaptureService"/> crops a pre-taken snapshot.
/// </summary>
internal sealed class CaptureOverlayWindow : Window
{
    private readonly IMonitorService _monitors;
    private readonly ILogger _logger;

    private System.Windows.Point _startPoint;
    private System.Windows.Point _currentPoint;
    private bool _isDragging;

    private readonly Canvas _canvas;
    private readonly Rectangle _selectionRect;
    private readonly System.Windows.Controls.TextBlock _dimensionsLabel;

    /// <summary>Raised with the selected region in physical (device) pixels.</summary>
    public event EventHandler<ScreenRect>? CaptureCompleted;
    public event EventHandler? CaptureCancelled;

    public CaptureOverlayWindow(IMonitorService monitors, ILogger logger)
    {
        _monitors = monitors;
        _logger = logger;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Cursor = Cursors.Cross;

        var (left, top, right, bottom) = GetVirtualDesktopBounds();
        Left = left;
        Top = top;
        Width = right - left;
        Height = bottom - top;

        _canvas = new Canvas();
        Content = _canvas;

        _selectionRect = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0xB4, 0xFF)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(20, 0, 180, 255)),
            Visibility = Visibility.Collapsed
        };

        _dimensionsLabel = new System.Windows.Controls.TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 11,
            Visibility = Visibility.Collapsed
        };

        _canvas.Children.Add(_selectionRect);
        _canvas.Children.Add(_dimensionsLabel);

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        KeyDown += OnKeyDown;
        Focusable = true;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        Activate();
        Focus();
        CaptureMouse();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(this);
        _currentPoint = _startPoint;
        _isDragging = true;
        _selectionRect.Visibility = Visibility.Visible;
        _dimensionsLabel.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        _currentPoint = e.GetPosition(this);
        UpdateSelectionVisuals();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        _currentPoint = e.GetPosition(this);
        ReleaseMouseCapture();

        var (x, y, w, h) = GetNormalisedRect();
        if (w < 5 || h < 5)
        {
            CaptureCancelled?.Invoke(this, EventArgs.Empty);
            return;
        }

        CaptureCompleted?.Invoke(this, WpfToPhysical(x, y, w, h));
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _isDragging = false;
            ReleaseMouseCapture();
            CaptureCancelled?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateSelectionVisuals()
    {
        var (x, y, w, h) = GetNormalisedRect();
        Canvas.SetLeft(_selectionRect, x);
        Canvas.SetTop(_selectionRect, y);
        _selectionRect.Width = Math.Max(1, w);
        _selectionRect.Height = Math.Max(1, h);

        var physical = WpfToPhysical(x, y, w, h);
        _dimensionsLabel.Text = $"{physical.Width} × {physical.Height}";
        Canvas.SetLeft(_dimensionsLabel, x + 4);
        Canvas.SetTop(_dimensionsLabel, y - 18);
    }

    private (double x, double y, double w, double h) GetNormalisedRect()
    {
        double x = Math.Min(_startPoint.X, _currentPoint.X);
        double y = Math.Min(_startPoint.Y, _currentPoint.Y);
        double w = Math.Abs(_currentPoint.X - _startPoint.X);
        double h = Math.Abs(_currentPoint.Y - _startPoint.Y);
        return (x, y, w, h);
    }

    private ScreenRect WpfToPhysical(double x, double y, double w, double h)
    {
        // The window's Left/Top are DIPs on the virtual desktop; convert the selection back to
        // physical pixels using the primary monitor scale (accurate for single-/same-DPI setups).
        var primary = _monitors.PrimaryMonitor;
        double scale = primary?.ScaleFactor ?? 1.0;

        int physLeft = (int)Math.Round((Left + x) * scale);
        int physTop = (int)Math.Round((Top + y) * scale);
        int physWidth = (int)Math.Round(w * scale);
        int physHeight = (int)Math.Round(h * scale);

        return new ScreenRect(physLeft, physTop, physLeft + physWidth, physTop + physHeight);
    }

    private (double left, double top, double right, double bottom) GetVirtualDesktopBounds()
    {
        double left = double.MaxValue, top = double.MaxValue;
        double right = double.MinValue, bottom = double.MinValue;

        foreach (var m in _monitors.Monitors)
        {
            double scale = m.ScaleFactor <= 0 ? 1.0 : m.ScaleFactor;
            left = Math.Min(left, m.Bounds.Left / scale);
            top = Math.Min(top, m.Bounds.Top / scale);
            right = Math.Max(right, m.Bounds.Right / scale);
            bottom = Math.Max(bottom, m.Bounds.Bottom / scale);
        }

        if (left == double.MaxValue)
            return (0, 0, 1920, 1080);

        return (left, top, right, bottom);
    }
}
