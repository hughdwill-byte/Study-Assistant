using System.IO;
using System.Runtime.InteropServices;
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
/// The same mouse-button press that triggers capture also begins the drag selection.
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
        CaptureResult? result = null;

        try
        {
            result = await ShowCaptureOverlayAsync(ct);
        }
        finally
        {
            _appState.SetCaptureModeActive(false);
        }

        return result;
    }

    private async Task<CaptureResult?> ShowCaptureOverlayAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<CaptureResult?>();
        using var reg = ct.Register(() => tcs.TrySetResult(null));

        CaptureOverlayWindow? overlay = null;
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            overlay = new CaptureOverlayWindow(_monitors, _logger);
            overlay.CaptureCompleted += (_, args) => tcs.TrySetResult(args);
            overlay.CaptureCancelled += (_, _) => tcs.TrySetResult(null);
            overlay.Show();
        });

        var captureResult = await tcs.Task;

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            overlay?.Close();
        });

        if (captureResult == null || captureResult.WasCancelled)
        {
            _logger.LogDebug("Capture cancelled.");
            return null;
        }

        _logger.LogDebug("Capture completed: {Rect}.", captureResult.PhysicalRect);

        // Copy to clipboard (spec §34)
        await CopyToClipboardAsync(captureResult);

        return captureResult;
    }

    private async Task CopyToClipboardAsync(CaptureResult result)
    {
        try
        {
            var bmp = CapturePhysicalRegion(result.PhysicalRect);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                System.Windows.Clipboard.SetImage(bmp));

            _logger.LogInformation("Screenshot captured and copied to clipboard.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy capture to clipboard.");
        }
    }

    private static System.Windows.Media.Imaging.BitmapSource CapturePhysicalRegion(ScreenRect rect)
    {
        int w = rect.Width;
        int h = rect.Height;

        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        var memDc = NativeMethods.CreateCompatibleDC(screenDc);
        var hBitmap = NativeMethods.CreateCompatibleBitmap(screenDc, w, h);
        var oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

        NativeMethods.BitBlt(memDc, 0, 0, w, h, screenDc, rect.Left, rect.Top, NativeMethods.SRCCOPY);

        NativeMethods.SelectObject(memDc, oldBitmap);
        NativeMethods.DeleteDC(memDc);
        NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);

        // Convert HBITMAP to WPF BitmapSource
        var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
            hBitmap,
            IntPtr.Zero,
            System.Windows.Int32Rect.Empty,
            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

        NativeMethods.DeleteObject(hBitmap);
        bitmapSource.Freeze();
        return bitmapSource;
    }

    public void Dispose() { }
}

// ─── Capture Overlay Window ─────────────────────────────────────────────────

/// <summary>
/// Full-virtual-desktop transparent overlay for selection drawing (spec §33).
/// Covers all monitors. Dims non-selected area; shows clear selection boundary.
/// </summary>
internal sealed class CaptureOverlayWindow : Window
{
    private readonly IMonitorService _monitors;
    private readonly ILogger _logger;

    private System.Windows.Point _startPoint;
    private System.Windows.Point _currentPoint;
    private bool _isDragging;
    private bool _hasResult;

    // Visual elements
    private readonly Canvas _canvas;
    private readonly Rectangle _dimRect;
    private readonly Rectangle _selectionRect;
    private readonly System.Windows.Controls.TextBlock _dimensionsLabel;

    public event EventHandler<CaptureResult>? CaptureCompleted;
    public event EventHandler? CaptureCancelled;

    public CaptureOverlayWindow(IMonitorService monitors, ILogger logger)
    {
        _monitors = monitors;
        _logger = logger;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)); // subtle dim
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Cursor = Cursors.Cross;

        // Span all monitors
        var (left, top, right, bottom) = GetVirtualDesktopBounds();
        Left = left;
        Top = top;
        Width = right - left;
        Height = bottom - top;

        // Build visual tree
        _canvas = new Canvas();
        Content = _canvas;

        // Selection rectangle
        _selectionRect = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0xB4, 0xFF)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(20, 0, 180, 255)),
            Visibility = Visibility.Collapsed
        };

        // Dim overlay (will be clipped to non-selected area in a real implementation)
        _dimRect = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
            Width = Width, Height = Height
        };
        Canvas.SetLeft(_dimRect, 0);
        Canvas.SetTop(_dimRect, 0);

        _dimensionsLabel = new System.Windows.Controls.TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 11,
            Visibility = Visibility.Collapsed
        };

        _canvas.Children.Add(_dimRect);
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
        FinaliseCapture();
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _isDragging = false;
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

        // Dimensions label
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

    private void FinaliseCapture()
    {
        var (x, y, w, h) = GetNormalisedRect();

        // Minimum capture size guard (spec §140)
        if (w < 5 || h < 5)
        {
            CaptureCancelled?.Invoke(this, EventArgs.Empty);
            return;
        }

        var physicalRect = WpfToPhysical(x, y, w, h);
        var monitor = _monitors.GetMonitorAtPoint(
            new ScreenPoint(physicalRect.Left + physicalRect.Width / 2,
                            physicalRect.Top + physicalRect.Height / 2));

        // Capture the screen pixels BEFORE closing the overlay
        byte[] imageBytes = CaptureToBytes(physicalRect);

        _hasResult = true;
        CaptureCompleted?.Invoke(this, new CaptureResult
        {
            ImageBytes = imageBytes,
            PhysicalRect = physicalRect,
            MonitorId = monitor?.MonitorId ?? string.Empty,
            WasCancelled = false
        });
    }

    private ScreenRect WpfToPhysical(double x, double y, double w, double h)
    {
        // Convert WPF DIPs (relative to this window) back to virtual desktop physical pixels
        // This window's Left/Top are in DIPs, so we add them back
        // then multiply by the primary monitor DPI scale as approximation
        // A full implementation uses per-monitor DPI at the centre point
        var primary = _monitors.PrimaryMonitor;
        double scale = primary?.ScaleFactor ?? 1.0;

        int physLeft = (int)Math.Round((Left + x) * scale);
        int physTop = (int)Math.Round((Top + y) * scale);
        int physWidth = (int)Math.Round(w * scale);
        int physHeight = (int)Math.Round(h * scale);

        return new ScreenRect(physLeft, physTop, physLeft + physWidth, physTop + physHeight);
    }

    private static byte[] CaptureToBytes(ScreenRect rect)
    {
        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        var memDc = NativeMethods.CreateCompatibleDC(screenDc);
        var hBitmap = NativeMethods.CreateCompatibleBitmap(screenDc, rect.Width, rect.Height);
        var oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

        NativeMethods.BitBlt(memDc, 0, 0, rect.Width, rect.Height,
            screenDc, rect.Left, rect.Top, NativeMethods.SRCCOPY);

        NativeMethods.SelectObject(memDc, oldBitmap);
        NativeMethods.DeleteDC(memDc);
        NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);

        // Convert to PNG bytes using System.Drawing
        using var bmp = System.Drawing.Image.FromHbitmap(hBitmap);
        NativeMethods.DeleteObject(hBitmap);

        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    private (double left, double top, double right, double bottom) GetVirtualDesktopBounds()
    {
        double left = double.MaxValue, top = double.MaxValue;
        double right = double.MinValue, bottom = double.MinValue;

        foreach (var m in _monitors.Monitors)
        {
            double scale = m.ScaleFactor;
            left = Math.Min(left, m.Bounds.Left / scale);
            top = Math.Min(top, m.Bounds.Top / scale);
            right = Math.Max(right, m.Bounds.Right / scale);
            bottom = Math.Max(bottom, m.Bounds.Bottom / scale);
        }

        return (left, top, right, bottom);
    }
}
