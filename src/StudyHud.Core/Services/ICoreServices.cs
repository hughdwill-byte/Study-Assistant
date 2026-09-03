using StudyHud.Core.Models;

namespace StudyHud.Core.Services;

/// <summary>
/// Abstracts global keyboard and mouse input handling (spec §36, §168, §170).
/// Implementations must keep hook callbacks extremely lightweight.
/// </summary>
public interface IGlobalInputService : IDisposable
{
    /// <summary>Raised on the worker thread when a trigger event occurs. NEVER block this.</summary>
    event EventHandler<GlobalInputEventArgs> InputReceived;

    void RegisterHotKey(int id, ModifierKeys modifiers, int virtualKey);
    void UnregisterHotKey(int id);

    /// <summary>
    /// Starts reporting down/up transitions for this virtual key via <see cref="InputReceived"/>
    /// (used for keyboard Hold-to-Interact triggers — spec §6). Keys that are not watched are
    /// never observed or enqueued, so ordinary typing does not flow through the service.
    /// </summary>
    void WatchKey(int virtualKey);

    /// <summary>Stops reporting the given virtual key.</summary>
    void UnwatchKey(int virtualKey);

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

[Flags]
public enum ModifierKeys
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8
}

public class GlobalInputEventArgs : EventArgs
{
    public required GlobalInputEventType EventType { get; init; }
    public int HotKeyId { get; init; }
    public int VirtualKey { get; init; }
    public ModifierKeys Modifiers { get; init; }
    public bool IsMouseButton { get; init; }
    public int MouseButton { get; init; }
    public bool IsDown { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public enum GlobalInputEventType
{
    HotKey,
    MouseButton,
    KeyboardKey
}

// ---------------------------------------------------------------------------

/// <summary>
/// Manages HUD panel layout persistence and runtime layout operations (spec §19, §21).
/// </summary>
public interface ILayoutService
{
    Task<IReadOnlyList<PanelLayout>> LoadLayoutAsync(string layoutId, CancellationToken ct = default);
    Task SaveLayoutAsync(string layoutId, IEnumerable<PanelLayout> panels, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetSavedLayoutNamesAsync(CancellationToken ct = default);
    Task DeleteLayoutAsync(string layoutId, CancellationToken ct = default);

    /// <summary>
    /// Recovers panels whose stored monitor is no longer present (spec §160).
    /// Moves them to the nearest valid monitor.
    /// </summary>
    IReadOnlyList<PanelLayout> RecoverPanelsForCurrentMonitors(
        IEnumerable<PanelLayout> panels,
        IEnumerable<MonitorInfo> availableMonitors);
}

// ---------------------------------------------------------------------------

/// <summary>
/// Enforces Study HUD network/AI/generative policy during Assessment Mode (spec §41, §182).
/// Every network-capable Study HUD component must check this before outbound requests.
/// </summary>
public interface IAssessmentPolicyService
{
    bool IsAssessmentModeActive { get; }

    /// <summary>True if the requested operation is allowed under the current policy.</summary>
    bool IsOperationAllowed(PolicyOperation operation);

    /// <summary>Returns an explanation of why an operation was blocked.</summary>
    string GetBlockReason(PolicyOperation operation);

    event EventHandler<PolicyChangedEventArgs> PolicyChanged;
}

public enum PolicyOperation
{
    NotionSync,
    CapturedQuestionUpload,
    LlmRequest,
    EmbeddingRequest,
    CloudOcrRequest,
    WebSearch,
    UpdateCheck,
    LocalOcr,
    LocalSearch,
    LocalIndex
}

public class PolicyChangedEventArgs : EventArgs
{
    public required bool AssessmentModeActive { get; init; }
}

// ---------------------------------------------------------------------------

/// <summary>
/// Manages foreground window tracking (spec §167).
/// Uses SetWinEventHook internally; never polls GetForegroundWindow.
/// </summary>
public interface IForegroundWindowService : IDisposable
{
    /// <summary>Current cached foreground context. Safe to read from any thread.</summary>
    ForegroundContext Current { get; }

    event EventHandler<ForegroundContextChangedEventArgs> ContextChanged;

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}

public class ForegroundContextChangedEventArgs : EventArgs
{
    public required ForegroundContext Previous { get; init; }
    public required ForegroundContext Current { get; init; }
}
