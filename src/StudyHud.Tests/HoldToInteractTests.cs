using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StudyHud.Core.Models;
using StudyHud.Core.Services;
using StudyHud.Windows.Services;
using Xunit;

namespace StudyHud.Tests;

// ─── Hold-to-Interact trigger tests (spec §6, §127) ──────────────────────────

public sealed class HoldToInteractTests
{
    /// <summary>In-memory input service: records watched keys and lets tests raise input events.</summary>
    private sealed class FakeInputService : IGlobalInputService
    {
        public readonly HashSet<int> WatchedKeys = new();
        public event EventHandler<GlobalInputEventArgs>? InputReceived;

        public void WatchKey(int virtualKey) => WatchedKeys.Add(virtualKey);
        public void UnwatchKey(int virtualKey) => WatchedKeys.Remove(virtualKey);
        public void RegisterHotKey(int id, ModifierKeys modifiers, int virtualKey) { }
        public void UnregisterHotKey(int id) { }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }

        public void Raise(GlobalInputEventArgs e) => InputReceived?.Invoke(this, e);
    }

    private static (HoldToInteractService svc, FakeInputService input, ApplicationStateService state) Build()
    {
        var input = new FakeInputService();
        var state = new ApplicationStateService(Mock.Of<ILogger<ApplicationStateService>>());
        var svc = new HoldToInteractService(input, state, Mock.Of<ILogger<HoldToInteractService>>());
        return (svc, input, state);
    }

    [Fact]
    public void Start_WithKeyboardTrigger_WatchesTheTriggerKey()
    {
        var (svc, input, _) = Build();
        svc.Start(); // default trigger = Caps Lock (0x14)

        input.WatchedKeys.Should().Contain(0x14);
    }

    [Fact]
    public void KeyboardTrigger_HeldThenReleased_TogglesActiveThenGhost()
    {
        var (svc, input, state) = Build();
        svc.Start();

        input.Raise(new GlobalInputEventArgs
        {
            EventType = GlobalInputEventType.KeyboardKey, VirtualKey = 0x14, IsDown = true
        });
        state.Current.HudInteractionState.Should().Be(HudInteractionState.Active);

        input.Raise(new GlobalInputEventArgs
        {
            EventType = GlobalInputEventType.KeyboardKey, VirtualKey = 0x14, IsDown = false
        });
        state.Current.HudInteractionState.Should().Be(HudInteractionState.Ghost);
    }

    [Fact]
    public void KeyboardTrigger_UnrelatedKey_IsIgnored()
    {
        var (svc, input, state) = Build();
        svc.Start();

        input.Raise(new GlobalInputEventArgs
        {
            EventType = GlobalInputEventType.KeyboardKey, VirtualKey = 0x41 /* A */, IsDown = true
        });

        state.Current.HudInteractionState.Should().Be(HudInteractionState.Ghost,
            "only the configured trigger key drives Hold-to-Interact");
    }

    [Fact]
    public void HoldRelease_DoesNotExitEditMode()
    {
        var (svc, input, state) = Build();
        svc.Start();
        state.SetHudInteractionState(HudInteractionState.Edit);

        // Press then release the trigger while in Edit mode.
        input.Raise(new GlobalInputEventArgs
        {
            EventType = GlobalInputEventType.KeyboardKey, VirtualKey = 0x14, IsDown = true
        });
        input.Raise(new GlobalInputEventArgs
        {
            EventType = GlobalInputEventType.KeyboardKey, VirtualKey = 0x14, IsDown = false
        });

        state.Current.HudInteractionState.Should().Be(HudInteractionState.Edit,
            "Edit mode is sticky — releasing Hold-to-Interact must not exit it (spec §126)");
    }

    [Fact]
    public void ApplySettings_SwitchingToMouseTrigger_UnwatchesKeyboardKey()
    {
        var (svc, input, state) = Build();
        svc.Start();
        input.WatchedKeys.Should().Contain(0x14);

        svc.ApplySettings(new StudyHudSettings
        {
            HoldToInteract = new HoldTriggerSettings { Type = HoldTriggerType.MouseButton, MouseButton = 5 }
        });
        input.WatchedKeys.Should().NotContain(0x14, "a mouse trigger should release the keyboard watch");

        // Mouse-button 5 now drives the trigger.
        input.Raise(new GlobalInputEventArgs
        {
            EventType = GlobalInputEventType.MouseButton, IsMouseButton = true, MouseButton = 5, IsDown = true
        });
        state.Current.HudInteractionState.Should().Be(HudInteractionState.Active);
    }
}
