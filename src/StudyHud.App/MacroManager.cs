using Microsoft.Extensions.Logging;
using StudyHud.Core.Services;
using StudyHud.Macros;
using StudyHud.Macros.Models;
using StudyHud.Macros.Services;

namespace StudyHud.App;

/// <summary>
/// Applies user macro specs to the running system (spec §30): loads them into the <see cref="MacroEngine"/>,
/// registers global hotkeys for enabled keyboard macros, and routes global input to the engine. Also the
/// single source of truth the Macros editor reads/writes through.
/// </summary>
public sealed class MacroManager
{
    private const int HotkeyBase = 2000;

    private readonly MacroEngine _engine;
    private readonly IGlobalInputService _input;
    private readonly MacroStore _store;
    private readonly ILogger<MacroManager> _logger;

    private readonly Dictionary<int, MacroDefinition> _hotkeyMap = new();
    private readonly List<int> _registeredHotkeyIds = new();
    private bool _attached;

    public MacroManager(
        MacroEngine engine, IGlobalInputService input, MacroStore store, ILogger<MacroManager> logger)
    {
        _engine = engine;
        _input = input;
        _store = store;
        _logger = logger;
    }

    /// <summary>The current specs (from the last load/save). Read by the editor.</summary>
    public IReadOnlyList<MacroSpec> Specs { get; private set; } = Array.Empty<MacroSpec>();

    /// <summary>Subscribes to global input once. Call after the input service has started.</summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _input.InputReceived += OnInput;
    }

    public void LoadAndApply() => Apply(_store.Load());

    public void SaveAndApply(IReadOnlyList<MacroSpec> specs)
    {
        _store.Save(specs);
        Apply(specs);
    }

    private void OnInput(object? sender, GlobalInputEventArgs e)
    {
        // Hotkey events run the mapped macro directly; mouse/other events go through trigger matching.
        if (e.EventType == GlobalInputEventType.HotKey)
        {
            if (_hotkeyMap.TryGetValue(e.HotKeyId, out var def))
                _engine.Enqueue(def);
        }
        else
        {
            _engine.EvaluateTrigger(e);
        }
    }

    private void Apply(IReadOnlyList<MacroSpec> specs)
    {
        Specs = specs;

        // Drop the previous hotkey registrations before adding the new set.
        foreach (var id in _registeredHotkeyIds)
            _input.UnregisterHotKey(id);
        _registeredHotkeyIds.Clear();
        _hotkeyMap.Clear();

        var defs = specs.Select(s => s.ToDefinition()).ToList();
        _engine.LoadMacros(defs);
        _engine.LoadProfiles(new[]
        {
            new MacroProfile { ProfileId = "user", Name = "User", MacroIds = defs.Select(d => d.Id).ToList() }
        });
        _engine.SetActiveProfile("user");

        int hotkeyId = HotkeyBase;
        foreach (var spec in specs)
        {
            if (!spec.Enabled || !spec.IsKeyboard || spec.VirtualKey == 0) continue;
            var def = defs.First(d => d.Id == spec.Id);
            _hotkeyMap[hotkeyId] = def;
            _input.RegisterHotKey(hotkeyId, (ModifierKeys)spec.Modifiers, spec.VirtualKey);
            _registeredHotkeyIds.Add(hotkeyId);
            hotkeyId++;
        }

        _logger.LogInformation(
            "Applied {Count} macro(s); {Hotkeys} global hotkey(s).", specs.Count, _registeredHotkeyIds.Count);
    }
}
