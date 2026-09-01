using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Services;
using StudyHud.Windows.Native;

namespace StudyHud.Windows.Services;

/// <summary>
/// Global input service (spec §36, §168, §170).
/// Uses RegisterHotKey for ordinary shortcuts and a low-level mouse hook for
/// side-button hold/press/release semantics.
///
/// Hook callback rule: enqueue only — no OCR, no DB, no macro execution, no WPF.
/// Fail-open: if the queue is full, pass the input through.
/// </summary>
public sealed class GlobalInputService : IGlobalInputService
{
    private readonly ILogger<GlobalInputService> _logger;

    // Message-only window for WM_HOTKEY
    private HwndSource? _hwndSource;
    private readonly Dictionary<int, (ModifierKeys Mods, int Vk)> _hotkeys = new();

    // Low-level mouse hook
    private IntPtr _mouseHookHandle;
    private LowLevelMouseProc? _mouseHookDelegate; // GC pin

    // Channel: hook callback → input worker (bounded, fail-open)
    private readonly Channel<GlobalInputEventArgs> _inputChannel =
        Channel.CreateBounded<GlobalInputEventArgs>(
            new BoundedChannelOptions(64)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });

    private CancellationTokenSource? _cts;
    private Task? _workerTask;

    public event EventHandler<GlobalInputEventArgs>? InputReceived;

    private const int WM_HOTKEY = 0x0312;
    private const int WH_MOUSE_LL = 14;

    // Mouse messages
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;
    private const int XBUTTON1 = 0x0001; // Mouse 4
    private const int XBUTTON2 = 0x0002; // Mouse 5

    public GlobalInputService(ILogger<GlobalInputService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Create a message-only window on the UI thread for WM_HOTKEY delivery
        System.Windows.Application.Current.Dispatcher.Invoke(CreateMessageWindow);

        // Install low-level mouse hook (must be on a thread with a message loop)
        InstallMouseHook();

        _workerTask = Task.Run(() => InputWorkerAsync(_cts.Token), _cts.Token);

        _logger.LogInformation("GlobalInputService started.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _cts?.Cancel();

        // Unregister all hotkeys
        foreach (var id in _hotkeys.Keys.ToList())
            UnregisterHotKeyInternal(id);

        // Remove mouse hook
        if (_mouseHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
        }

        _hwndSource?.Dispose();
        _inputChannel.Writer.TryComplete();

        _logger.LogInformation("GlobalInputService stopped.");
        return Task.CompletedTask;
    }

    // ── Hotkey registration ─────────────────────────────────────────────────

    public void RegisterHotKey(int id, ModifierKeys modifiers, int virtualKey)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_hwndSource == null) return;
            uint mods = (uint)modifiers;
            if (NativeMethods.RegisterHotKey(_hwndSource.Handle, id, mods, (uint)virtualKey))
            {
                _hotkeys[id] = (modifiers, virtualKey);
                _logger.LogDebug("Hotkey {Id} registered (mods={Mods}, vk={Vk}).", id, modifiers, virtualKey);
            }
            else
            {
                _logger.LogWarning("Failed to register hotkey {Id} — may conflict with another app.", id);
            }
        });
    }

    public void UnregisterHotKey(int id)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => UnregisterHotKeyInternal(id));
    }

    private void UnregisterHotKeyInternal(int id)
    {
        if (_hwndSource != null)
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, id);
        _hotkeys.Remove(id);
    }

    // ── Message window for WM_HOTKEY ────────────────────────────────────────

    private void CreateMessageWindow()
    {
        var params_ = new HwndSourceParameters("StudyHud.HotkeyWindow")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            PositionX = 0, PositionY = 0,
            Width = 0, Height = 0,
            ParentWindow = new IntPtr(-3) // HWND_MESSAGE
        };
        _hwndSource = new HwndSource(params_);
        _hwndSource.AddHook(HotkeyWndProc);
    }

    private IntPtr HotkeyWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            var ev = new GlobalInputEventArgs
            {
                EventType = GlobalInputEventType.HotKey,
                HotKeyId = id,
                IsDown = true,
                Timestamp = DateTimeOffset.UtcNow
            };
            // Fail-open: try to enqueue, ignore if full
            _inputChannel.Writer.TryWrite(ev);
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ── Low-level mouse hook ────────────────────────────────────────────────

    private void InstallMouseHook()
    {
        _mouseHookDelegate = MouseHookCallback;
        _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookDelegate,
            IntPtr.Zero, 0);

        if (_mouseHookHandle == IntPtr.Zero)
            _logger.LogWarning("Low-level mouse hook could not be installed.");
        else
            _logger.LogDebug("Low-level mouse hook installed.");
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Latency-critical path: read cached state, enqueue, return immediately.
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == WM_XBUTTONDOWN || msg == WM_XBUTTONUP)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int xButton = (int)((data.mouseData >> 16) & 0xFFFF);
                bool isDown = msg == WM_XBUTTONDOWN;

                var ev = new GlobalInputEventArgs
                {
                    EventType = GlobalInputEventType.MouseButton,
                    IsMouseButton = true,
                    MouseButton = xButton == XBUTTON1 ? 4 : 5,
                    IsDown = isDown,
                    Timestamp = DateTimeOffset.UtcNow
                };

                // Fail-open: never block the hook callback
                _inputChannel.Writer.TryWrite(ev);
            }
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    // ── Input worker ────────────────────────────────────────────────────────

    private async Task InputWorkerAsync(CancellationToken ct)
    {
        await foreach (var ev in _inputChannel.Reader.ReadAllAsync(ct))
        {
            try
            {
                InputReceived?.Invoke(this, ev);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "InputReceived handler threw.");
            }
        }
    }

    // ── P/Invoke for hooks ──────────────────────────────────────────────────

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn,
        IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
        IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public NativeMethods.POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _cts?.Dispose();
    }
}
