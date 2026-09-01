using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Models;
using StudyHud.Core.Services;
using StudyHud.Windows.Native;

namespace StudyHud.Overlay;

/// <summary>
/// One transparent, always-on-top WPF window per physical monitor.
/// In Ghost mode: WS_EX_TRANSPARENT makes clicks pass through.
/// In Active/Edit mode: transparent style is removed so controls can be clicked.
/// This is the passive rendering surface (spec §4, §171).
/// Now hosts a PanelHost canvas with the actual HUD panels.
/// </summary>
public sealed class MonitorOverlayWindow : Window
{
    private readonly MonitorInfo _monitor;
    private readonly IApplicationStateService _appState;
    private readonly ILogger _logger;
    private IntPtr _hwnd;
    private bool _isTransparent = true;

    private const int WM_DISPLAYCHANGE = 0x007E;

    public MonitorInfo Monitor => _monitor;

    // The panel canvas — wired up by OverlayManager after construction
    public PanelHost? PanelHost { get; private set; }

    public MonitorOverlayWindow(
        MonitorInfo monitor,
        IApplicationStateService appState,
        ILogger logger)
    {
        _monitor = monitor;
        _appState = appState;
        _logger = logger;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;

        ApplyMonitorBounds();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;

        _appState.StateChanged += OnAppStateChanged;
    }

    public void SetPanelHost(PanelHost host)
    {
        PanelHost = host;
        Content = host;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var source = (HwndSource)PresentationSource.FromVisual(this);
        _hwnd = source.Handle;
        source.AddHook(WndProc);
        ApplyOverlayWindowStyles();
        _logger.LogDebug("Overlay HWND created for monitor {MonitorId}.", _monitor.MonitorId);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ApplyMonitorBounds();
        ApplyOverlayWindowStyles();
    }

    private void ApplyOverlayWindowStyles()
    {
        if (_hwnd == IntPtr.Zero) return;

        var exStyle = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
        exStyle |= NativeMethods.WS_EX_TOPMOST
                 | NativeMethods.WS_EX_TOOLWINDOW
                 | NativeMethods.WS_EX_LAYERED
                 | NativeMethods.WS_EX_NOACTIVATE;

        if (_isTransparent)
            exStyle |= NativeMethods.WS_EX_TRANSPARENT;
        else
            exStyle &= ~NativeMethods.WS_EX_TRANSPARENT;

        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
        NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    public void SetGhostMode(bool ghost)
    {
        if (_isTransparent == ghost) return;
        _isTransparent = ghost;
        ApplyOverlayWindowStyles();
    }

    private void OnAppStateChanged(object? sender, ApplicationStateChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!e.Current.HudVisible)
            {
                Visibility = Visibility.Collapsed;
                return;
            }
            Visibility = Visibility.Visible;
            bool shouldBeGhost = e.Current.HudInteractionState == HudInteractionState.Ghost
                                 && !e.Current.IsCaptureModeActive;
            SetGhostMode(shouldBeGhost);
        });
    }

    public void ApplyMonitorBounds()
    {
        double scale = _monitor.ScaleFactor;
        var wa = _monitor.WorkArea;
        Left = wa.Left / scale;
        Top = wa.Top / scale;
        Width = wa.Width / scale;
        Height = wa.Height / scale;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DISPLAYCHANGE) handled = false;
        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        _appState.StateChanged -= OnAppStateChanged;
        base.OnClosed(e);
    }
}
