using Microsoft.Extensions.Logging;
using StudyHud.Core.Models;
using StudyHud.Core.Services;

namespace StudyHud.Windows.Services;

/// <summary>
/// Manages the Hold-to-Interact trigger (spec §6, §127).
/// When the configured trigger is held: HUD → Active.
/// When released: HUD → Ghost.
/// Does NOT exit Edit Mode on release (spec §126).
///
/// Also handles the Panic Hide shortcut (spec §7).
/// </summary>
public sealed class HoldToInteractService : IDisposable
{
    private readonly IGlobalInputService _input;
    private readonly IApplicationStateService _appState;
    private readonly ILogger<HoldToInteractService> _logger;

    // Configurable trigger (default: Caps Lock = vk 0x14, or Mouse5 = button 5)
    // The trigger type is stored so we can distinguish keyboard vs mouse hold
    private HoldTriggerConfig _trigger = new()
    {
        Type = TriggerKind.KeyboardKey,
        VirtualKey = 0x14 // Caps Lock default — user can change in Settings
    };

    private bool _isHeld;

    // Hotkey IDs
    private const int HotkeyIdPanicHide = 1001;
    private const int HotkeyIdToggleEditMode = 1002;

    public HoldToInteractService(
        IGlobalInputService input,
        IApplicationStateService appState,
        ILogger<HoldToInteractService> logger)
    {
        _input = input;
        _appState = appState;
        _logger = logger;
    }

    public void Start()
    {
        _input.InputReceived += OnInputReceived;

        // Register Panic Hide: Ctrl+Shift+H (configurable in Settings)
        _input.RegisterHotKey(HotkeyIdPanicHide, ModifierKeys.Control | ModifierKeys.Shift, 0x48); // H

        // Register Edit Mode toggle: Ctrl+Shift+E
        _input.RegisterHotKey(HotkeyIdToggleEditMode, ModifierKeys.Control | ModifierKeys.Shift, 0x45); // E

        _logger.LogInformation("HoldToInteractService started. Trigger: {Trigger}.", _trigger);
    }

    public void Stop()
    {
        _input.InputReceived -= OnInputReceived;
        _input.UnregisterHotKey(HotkeyIdPanicHide);
        _input.UnregisterHotKey(HotkeyIdToggleEditMode);
    }

    public void SetTrigger(HoldTriggerConfig trigger)
    {
        _trigger = trigger;
        _logger.LogInformation("Hold-to-Interact trigger changed to {Trigger}.", trigger);
    }

    private void OnInputReceived(object? sender, GlobalInputEventArgs e)
    {
        var state = _appState.Current;

        // ── Hotkeys ───────────────────────────────────────────────────────
        if (e.EventType == GlobalInputEventType.HotKey)
        {
            if (e.HotKeyId == HotkeyIdPanicHide)
            {
                _appState.SetHudVisible(!state.HudVisible);
                _logger.LogDebug("Panic Hide toggled → visible={V}.", !state.HudVisible);
                return;
            }

            if (e.HotKeyId == HotkeyIdToggleEditMode)
            {
                if (state.HudInteractionState == HudInteractionState.Edit)
                    _appState.SetHudInteractionState(HudInteractionState.Ghost);
                else
                    _appState.SetHudInteractionState(HudInteractionState.Edit);
                return;
            }
        }

        // ── Mouse side-button trigger ─────────────────────────────────────
        if (_trigger.Type == TriggerKind.MouseButton
            && e.IsMouseButton && e.MouseButton == _trigger.MouseButton)
        {
            HandleTriggerStateChange(e.IsDown);
        }
    }

    /// <summary>Called by the keyboard hook adapter when the trigger key changes state.</summary>
    public void HandleTriggerKeyStateChange(bool isDown) => HandleTriggerStateChange(isDown);

    private void HandleTriggerStateChange(bool isDown)
    {
        if (isDown && !_isHeld)
        {
            _isHeld = true;
            // Only transition to Active if we're currently Ghost (not Edit)
            if (_appState.Current.HudInteractionState == HudInteractionState.Ghost)
                _appState.SetHudInteractionState(HudInteractionState.Active);
        }
        else if (!isDown && _isHeld)
        {
            _isHeld = false;
            // Only return to Ghost if currently Active (not Edit — Edit is sticky)
            if (_appState.Current.HudInteractionState == HudInteractionState.Active)
                _appState.SetHudInteractionState(HudInteractionState.Ghost);
        }
    }

    public void Dispose() => Stop();
}

public enum TriggerKind { KeyboardKey, MouseButton }

public record HoldTriggerConfig
{
    public TriggerKind Type { get; init; } = TriggerKind.KeyboardKey;
    public int VirtualKey { get; init; } = 0x14; // Caps Lock
    public int MouseButton { get; init; } = 5;
}
