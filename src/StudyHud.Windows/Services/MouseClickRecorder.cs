using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Services;

namespace StudyHud.Windows.Services;

/// <summary>
/// Records mouse clicks (position + timing) with a low-level mouse hook, stopping when Escape is pressed
/// (watched by a low-level keyboard hook). Clicks are observed, never suppressed (spec §36). Both hooks
/// are installed on the calling thread — call it from the UI thread, whose message loop drives the
/// callbacks — and torn down when recording ends.
/// </summary>
public sealed class MouseClickRecorder : IMouseClickRecorder
{
    private const int WH_MOUSE_LL = 14;
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_ESCAPE = 0x1B;

    private readonly ILogger<MouseClickRecorder> _logger;

    private HookProc? _mouseProc;
    private HookProc? _keyProc;
    private IntPtr _mouseHook = IntPtr.Zero;
    private IntPtr _keyHook = IntPtr.Zero;

    private readonly List<MouseClickSample> _samples = new();
    private long _lastTicks;
    private TaskCompletionSource<IReadOnlyList<MouseClickSample>>? _tcs;

    public MouseClickRecorder(ILogger<MouseClickRecorder> logger) => _logger = logger;

    public bool IsRecording { get; private set; }

    public Task<IReadOnlyList<MouseClickSample>> RecordUntilEscapeAsync()
    {
        if (IsRecording) return _tcs!.Task;

        _samples.Clear();
        _lastTicks = Environment.TickCount64;
        _tcs = new TaskCompletionSource<IReadOnlyList<MouseClickSample>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _mouseProc = MouseCallback;
        _keyProc = KeyCallback;
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, IntPtr.Zero, 0);
        _keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyProc, IntPtr.Zero, 0);

        if (_mouseHook == IntPtr.Zero || _keyHook == IntPtr.Zero)
        {
            _logger.LogWarning("Mouse-click recorder could not install its hooks.");
            Teardown();
            _tcs.TrySetResult(Array.Empty<MouseClickSample>());
            return _tcs.Task;
        }

        IsRecording = true;
        _logger.LogInformation("Mouse-click recording started (Esc to finish).");
        return _tcs.Task;
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsRecording)
        {
            int msg = wParam.ToInt32();
            if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                long now = Environment.TickCount64;
                int delay = _samples.Count == 0 ? 0 : (int)Math.Clamp(now - _lastTicks, 0, 60000);
                _lastTicks = now;
                _samples.Add(new MouseClickSample(data.pt.x, data.pt.y, msg == WM_RBUTTONDOWN ? 2 : 1, delay));
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsRecording)
        {
            int msg = wParam.ToInt32();
            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                int vk = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam).vkCode;
                if (vk == VK_ESCAPE)
                {
                    var result = _samples.ToList();
                    Teardown();
                    _tcs?.TrySetResult(result);
                    _logger.LogInformation("Mouse-click recording finished: {Count} click(s).", result.Count);
                }
            }
        }
        return CallNextHookEx(_keyHook, nCode, wParam, lParam);
    }

    private void Teardown()
    {
        IsRecording = false;
        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        if (_keyHook != IntPtr.Zero) { UnhookWindowsHookEx(_keyHook); _keyHook = IntPtr.Zero; }
        _mouseProc = null;
        _keyProc = null;
    }

    // ── P/Invoke ────────────────────────────────────────────────────────────

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
}
