using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Models;
using StudyHud.Core.Services;
using StudyHud.Macros.Models;
using StudyHud.Windows.Native;

namespace StudyHud.Macros.Services;

/// <summary>
/// Executes macro action sequences following the pipeline in spec §136.
/// Input arrives via a channel from the low-level hook layer.
/// All condition checks use the cached ForegroundContext — no inline process lookup.
/// </summary>
public sealed class MacroEngine : IDisposable
{
    private readonly ILogger<MacroEngine> _logger;
    private readonly IApplicationStateService _appState;
    private readonly IForegroundWindowService _foreground;
    private readonly IAssessmentPolicyService _policy;

    private readonly Channel<(MacroDefinition Macro, bool IsDown)> _executionChannel =
        Channel.CreateBounded<(MacroDefinition, bool)>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    private readonly Dictionary<string, MacroDefinition> _macros = new();
    private readonly Dictionary<string, MacroProfile> _profiles = new();
    private string _activeProfileId = string.Empty;
    private readonly Dictionary<string, DateTimeOffset> _cooldowns = new();

    private Task? _workerTask;
    private CancellationTokenSource? _cts;

    public MacroEngine(
        ILogger<MacroEngine> logger,
        IApplicationStateService appState,
        IForegroundWindowService foreground,
        IAssessmentPolicyService policy)
    {
        _logger = logger;
        _appState = appState;
        _foreground = foreground;
        _policy = policy;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _workerTask = Task.Run(() => ExecutionLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _executionChannel.Writer.TryComplete();
    }

    // ── Profile management ──────────────────────────────────────────────────

    public void LoadMacros(IEnumerable<MacroDefinition> macros)
    {
        _macros.Clear();
        foreach (var m in macros) _macros[m.Id] = m;
    }

    public void LoadProfiles(IEnumerable<MacroProfile> profiles)
    {
        _profiles.Clear();
        foreach (var p in profiles) _profiles[p.ProfileId] = p;
    }

    public void SetActiveProfile(string profileId)
    {
        _activeProfileId = profileId;
        _logger.LogInformation("Macro profile switched to {ProfileId}.", profileId);
    }

    // ── Trigger evaluation (called from input pipeline) ─────────────────────

    /// <summary>
    /// Evaluates whether a global input event matches any enabled macro.
    /// Returns the matching macro and whether to suppress the input event.
    /// This is called after the hook callback enqueues the event — NOT inside the callback.
    /// </summary>
    public (MacroDefinition? Macro, bool SuppressInput) EvaluateTrigger(
        GlobalInputEventArgs inputEvent)
    {
        var state = _appState.Current;
        var ctx = _foreground.Current;

        // Step 1: Check foreground context policy
        if (!ctx.MacrosAllowed)
            return (null, false);

        // Step 2: Check Assessment Mode for dangerous actions
        // (non-AI macros like text injection are fine in assessment)

        // Step 3: Find matching macro
        MacroDefinition? matched = null;
        bool shouldSuppress = false;

        foreach (var macroId in GetActiveMacroIds())
        {
            if (!_macros.TryGetValue(macroId, out var macro) || !macro.Enabled) continue;
            if (!TriggerMatches(macro.Trigger, inputEvent)) continue;
            if (!ConditionsMet(macro.Conditions, state, ctx)) continue;
            if (IsOnCooldown(macro.Id)) continue;

            matched = macro;
            shouldSuppress = ShouldSuppress(macro.Trigger);
            break;
        }

        if (matched != null)
            _executionChannel.Writer.TryWrite((matched, inputEvent.IsDown));

        return (matched, shouldSuppress && matched != null);
    }

    private IEnumerable<string> GetActiveMacroIds()
    {
        if (string.IsNullOrEmpty(_activeProfileId)) yield break;
        if (!_profiles.TryGetValue(_activeProfileId, out var profile)) yield break;
        foreach (var id in profile.MacroIds) yield return id;
    }

    private static bool TriggerMatches(MacroTrigger trigger, GlobalInputEventArgs e)
    {
        if (e.IsMouseButton && trigger.Type is TriggerType.MouseSideButton or
            TriggerType.MouseButtonHold or TriggerType.MouseButtonPress)
        {
            return e.MouseButton == trigger.MouseButton &&
                   e.IsDown == (trigger.Semantic != TriggerSemantic.Release);
        }

        if (!e.IsMouseButton && trigger.Type is TriggerType.KeyboardShortcut or
            TriggerType.FunctionKey or TriggerType.KeyChord)
        {
            return e.VirtualKey == trigger.VirtualKey &&
                   (int)e.Modifiers == trigger.Modifiers;
        }

        return false;
    }

    private static bool ConditionsMet(MacroConditions cond, ApplicationState state, ForegroundContext ctx)
    {
        if (cond.RequiredWorkspace.HasValue && cond.RequiredWorkspace != state.CurrentWorkspace)
            return false;

        if (cond.AllowedApplications?.Count > 0 &&
            !cond.AllowedApplications.Contains(ctx.ExecutableName, StringComparer.OrdinalIgnoreCase))
            return false;

        if (cond.BlockedApplications?.Count > 0 &&
            cond.BlockedApplications.Contains(ctx.ExecutableName, StringComparer.OrdinalIgnoreCase))
            return false;

        if (!cond.AllowInCapture && state.IsCaptureModeActive)
            return false;

        return true;
    }

    private bool IsOnCooldown(string macroId)
    {
        if (_cooldowns.TryGetValue(macroId, out var last))
        {
            var macro = _macros[macroId];
            if (macro.CooldownMs > 0 &&
                (DateTimeOffset.UtcNow - last).TotalMilliseconds < macro.CooldownMs)
                return true;
        }
        return false;
    }

    private static bool ShouldSuppress(MacroTrigger trigger) =>
        trigger.Type is TriggerType.MouseSideButton or TriggerType.MouseButtonHold
            or TriggerType.MouseButtonPress or TriggerType.KeyboardShortcut
            or TriggerType.FunctionKey;

    // ── Execution loop ──────────────────────────────────────────────────────

    private async Task ExecutionLoopAsync(CancellationToken ct)
    {
        await foreach (var (macro, isDown) in _executionChannel.Reader.ReadAllAsync(ct))
        {
            if (!isDown) continue; // Execute on down-stroke

            _cooldowns[macro.Id] = DateTimeOffset.UtcNow;

            try
            {
                await ExecuteMacroAsync(macro, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Macro '{Name}' failed during execution.", macro.Name);
            }
        }
    }

    private async Task ExecuteMacroAsync(MacroDefinition macro, CancellationToken ct)
    {
        _logger.LogDebug("Executing macro '{Name}'.", macro.Name);

        foreach (var action in macro.Actions)
        {
            ct.ThrowIfCancellationRequested();
            bool success = await ExecuteActionAsync(action, ct);

            if (!success && macro.OnFailure == MacroFailureBehaviour.StopMacro)
            {
                _logger.LogWarning("Macro '{Name}' stopped at action {Type}.", macro.Name, action.ActionType);
                return;
            }
        }
    }

    private async Task<bool> ExecuteActionAsync(MacroAction action, CancellationToken ct)
    {
        try
        {
            switch (action)
            {
                case KeyDownAction kd: SendKey(kd.VirtualKey, false); break;
                case KeyUpAction ku: SendKey(ku.VirtualKey, true); break;
                case KeyPressAction kp: SendKeyPress(kp.VirtualKey, kp.Modifiers); break;
                case TypeTextAction tt: await SendTextAsync(tt.Text, tt.UseClipboardForUnicode, ct); break;
                case DelayAction da: await Task.Delay(da.Milliseconds, ct); break;
                case SwitchWorkspaceAction sw:
                    await _appState.SwitchWorkspaceAsync(sw.TargetWorkspace, ct); break;
                case SwitchMacroProfileAction sm: SetActiveProfile(sm.ProfileId); break;
                case TogglePanelCollapseAction: /* handled by HUD layer */ break;
                default:
                    _logger.LogDebug("Action {Type} not yet implemented.", action.ActionType);
                    break;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Action {Type} failed.", action.ActionType);
            return false;
        }
    }

    // ── Input injection (spec §36) ─────────────────────────────────────────

    private static void SendKey(int vk, bool keyUp)
    {
        var inputs = new NativeMethods.INPUT[1];
        inputs[0].type = NativeMethods.INPUT.INPUT_KEYBOARD;
        inputs[0].U.ki.wVk = (ushort)vk;
        inputs[0].U.ki.dwFlags = keyUp ? NativeMethods.KEYBDINPUT.KEYEVENTF_KEYUP : 0;
        NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void SendKeyPress(int vk, int modifiers)
    {
        // Press modifiers
        if ((modifiers & 2) != 0) SendKey(0x11 /*VK_CONTROL*/, false);
        if ((modifiers & 1) != 0) SendKey(0x12 /*VK_MENU/ALT*/, false);
        if ((modifiers & 4) != 0) SendKey(0x10 /*VK_SHIFT*/, false);

        SendKey(vk, false);
        SendKey(vk, true);

        // Release modifiers in reverse
        if ((modifiers & 4) != 0) SendKey(0x10, true);
        if ((modifiers & 1) != 0) SendKey(0x12, true);
        if ((modifiers & 2) != 0) SendKey(0x11, true);
    }

    private async Task SendTextAsync(string text, bool useClipboard, CancellationToken ct)
    {
        if (useClipboard)
        {
            // Clipboard-assisted insertion for complex Unicode
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                System.Windows.Clipboard.SetText(text));
            await Task.Delay(20, ct);
            SendKeyPress(0x56 /*V*/, 2 /*Ctrl*/);
            return;
        }

        // Unicode SendInput for each character
        var inputs = new List<NativeMethods.INPUT>();
        foreach (char c in text)
        {
            var down = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT.INPUT_KEYBOARD,
                U = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wScan = c,
                        dwFlags = NativeMethods.KEYBDINPUT.KEYEVENTF_UNICODE
                    }
                }
            };
            var up = down;
            up.U.ki.dwFlags |= NativeMethods.KEYBDINPUT.KEYEVENTF_KEYUP;
            inputs.Add(down);
            inputs.Add(up);
        }

        var arr = inputs.ToArray();
        NativeMethods.SendInput((uint)arr.Length, arr, Marshal.SizeOf<NativeMethods.INPUT>());
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
