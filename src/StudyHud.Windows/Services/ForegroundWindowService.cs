using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Models;
using StudyHud.Core.Services;
using StudyHud.Windows.Native;

namespace StudyHud.Windows.Services;

/// <summary>
/// Tracks the foreground application using SetWinEventHook EVENT_SYSTEM_FOREGROUND.
/// Never polls GetForegroundWindow at high frequency (spec §167, §186).
/// Hook callback enqueues only an HWND; all expensive work is done on the worker.
/// </summary>
public sealed class ForegroundWindowService : IForegroundWindowService
{
    private readonly ILogger<ForegroundWindowService> _logger;
    private readonly IAssessmentPolicyService _policy;
    private readonly HashSet<string> _globalExclusions;

    // Lightweight, bounded channel from hook → worker (spec §168)
    private readonly Channel<IntPtr> _channel =
        Channel.CreateBounded<IntPtr>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    private NativeMethods.WinEventProc? _hookDelegate; // keep alive
    private IntPtr _hookHandle;
    private Task? _workerTask;
    private CancellationTokenSource? _cts;

    private ForegroundContext _current = ForegroundContext.Unknown;
    private readonly object _lock = new();

    public ForegroundWindowService(
        ILogger<ForegroundWindowService> logger,
        IAssessmentPolicyService policy)
    {
        _logger = logger;
        _policy = policy;
        _globalExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public ForegroundContext Current
    {
        get { lock (_lock) { return _current; } }
        private set { lock (_lock) { _current = value; } }
    }

    public event EventHandler<ForegroundContextChangedEventArgs>? ContextChanged;

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Install hook on the calling thread (must be STA/message loop thread)
        _hookDelegate = WinEventCallback; // prevent GC collection
        _hookHandle = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _hookDelegate,
            0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        if (_hookHandle == IntPtr.Zero)
            _logger.LogWarning("SetWinEventHook returned null — foreground tracking unavailable.");
        else
            _logger.LogInformation("ForegroundWindowService started.");

        _workerTask = Task.Run(() => WorkerLoopAsync(_cts.Token), _cts.Token);

        // Seed with the current foreground
        var currentHwnd = NativeMethods.GetForegroundWindow();
        if (currentHwnd != IntPtr.Zero)
            _channel.Writer.TryWrite(currentHwnd);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        _channel.Writer.TryComplete();
        _cts?.Cancel();

        if (_workerTask != null)
            await _workerTask.ConfigureAwait(false);

        _logger.LogInformation("ForegroundWindowService stopped.");
    }

    // ── Hook callback — must be minimal (spec §168) ─────────────────────────
    private void WinEventCallback(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        // Only enqueue — no expensive work here
        _channel.Writer.TryWrite(hwnd);
    }

    // ── Worker processes HWNDs off the hot path ──────────────────────────────
    private async Task WorkerLoopAsync(CancellationToken ct)
    {
        await foreach (var hwnd in _channel.Reader.ReadAllAsync(ct))
        {
            try
            {
                var newContext = BuildContext(hwnd);
                var previous = Current;
                Current = newContext;

                if (previous.WindowHandle != newContext.WindowHandle ||
                    previous.ExecutableName != newContext.ExecutableName)
                {
                    ContextChanged?.Invoke(this, new ForegroundContextChangedEventArgs
                    {
                        Previous = previous,
                        Current = newContext
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error resolving foreground context.");
            }
        }
    }

    private ForegroundContext BuildContext(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return ForegroundContext.Unknown;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return ForegroundContext.Unknown;

        string execName = ResolveExecutableName(pid, out string? execPath);
        bool isStudyHudOwned = execName.Equals("StudyHud", StringComparison.OrdinalIgnoreCase);
        bool isExcluded = _globalExclusions.Contains(execName);

        // Fullscreen detection: window bounds match monitor bounds
        bool isFullscreen = IsWindowFullscreen(hwnd);

        return new ForegroundContext
        {
            ExecutableName = execName,
            ExecutablePath = execPath,
            ProcessId = (int)pid,
            WindowHandle = hwnd,
            IsElevated = false, // elevation check omitted for V1 — would need OpenProcessToken
            IsFullscreen = isFullscreen,
            IsStudyHudOwned = isStudyHudOwned,
            MacrosAllowed = !isExcluded && !isStudyHudOwned,
            CaptureAllowed = !isExcluded,
            HudAllowed = !isExcluded,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private static string ResolveExecutableName(uint pid, out string? fullPath)
    {
        fullPath = null;
        var hProcess = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess == IntPtr.Zero) return string.Empty;

        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            if (NativeMethods.QueryFullProcessImageNameW(hProcess, 0, sb, ref size))
            {
                fullPath = sb.ToString(0, (int)size);
                return Path.GetFileNameWithoutExtension(fullPath) ?? string.Empty;
            }
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }

        return string.Empty;
    }

    private static bool IsWindowFullscreen(IntPtr hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var wr)) return false;

        // Compare against virtual desktop size; a window matching any monitor's full bounds is fullscreen
        var hMonitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero) return false;

        var mi = new NativeMethods.MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        if (!NativeMethods.GetMonitorInfoW(hMonitor, ref mi)) return false;

        return wr.Left <= mi.rcMonitor.Left && wr.Top <= mi.rcMonitor.Top &&
               wr.Right >= mi.rcMonitor.Right && wr.Bottom >= mi.rcMonitor.Bottom;
    }

    public void AddExclusion(string executableName) => _globalExclusions.Add(executableName);
    public void RemoveExclusion(string executableName) => _globalExclusions.Remove(executableName);

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
