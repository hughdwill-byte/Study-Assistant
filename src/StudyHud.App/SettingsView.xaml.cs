using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Models;
using StudyHud.Core.Services;
using StudyHud.Windows.Services;

namespace StudyHud.App;

/// <summary>
/// Editor for <see cref="StudyHudSettings"/> (spec §3.2). Loads the current settings into
/// controls, writes them back via <see cref="ISettingsStore"/>, and applies theme +
/// Hold-to-Interact immediately so the user sees the effect without restarting.
///
/// Deliberately code-behind + named controls (no complex data binding) to keep the wiring
/// simple and predictable. Fields not represented here (hotkeys, workspace→profile map,
/// session context) are preserved via the record's <c>with</c> copy.
/// </summary>
public partial class SettingsView : UserControl
{
    private readonly ISettingsStore _store;
    private readonly IThemeService _theme;
    private readonly HoldToInteractService _hold;
    private readonly ILogger<SettingsView> _logger;
    private bool _loading;

    private sealed record Option(string Name, int Value);

    // Presets that avoid clobbering normal keys; Caps Lock kept as the documented default.
    private static readonly Option[] KeyPresets =
    {
        new("Caps Lock", 0x14),
        new("Scroll Lock", 0x91),
        new("Pause/Break", 0x13),
        new("F13", 0x7C),
        new("F14", 0x7D),
        new("Right Ctrl", 0xA3),
        new("Right Shift", 0xA1),
        new("Right Alt", 0xA5),
    };

    public SettingsView(
        ISettingsStore store,
        IThemeService theme,
        HoldToInteractService hold,
        ILogger<SettingsView> logger)
    {
        _store = store;
        _theme = theme;
        _hold = hold;
        _logger = logger;

        InitializeComponent();

        SnapSlider.ValueChanged += (_, _) =>
            SnapValue.Text = ((int)Math.Round(SnapSlider.Value)).ToString();

        Loaded += (_, _) => LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        _loading = true;
        try
        {
            var s = _store.Current;

            // Theme
            ThemeCombo.Items.Clear();
            foreach (var id in _theme.AvailableThemeIds) ThemeCombo.Items.Add(id);
            ThemeCombo.SelectedItem = _theme.AvailableThemeIds.Contains(s.ThemeId)
                ? s.ThemeId : _theme.CurrentThemeId;

            SnapSlider.Value = s.SnapDistancePixels;
            SnapValue.Text = ((int)Math.Round(s.SnapDistancePixels)).ToString();
            ShowCapsuleCheck.IsChecked = s.ShowControlCapsule;

            // Hold-to-Interact
            TriggerTypeCombo.Items.Clear();
            TriggerTypeCombo.Items.Add("Keyboard key");
            TriggerTypeCombo.Items.Add("Mouse side button");
            TriggerTypeCombo.SelectedIndex = s.HoldToInteract.Type == HoldTriggerType.MouseButton ? 1 : 0;

            TriggerKeyCombo.DisplayMemberPath = "Name";
            TriggerKeyCombo.Items.Clear();
            foreach (var k in KeyPresets) TriggerKeyCombo.Items.Add(k);
            var match = KeyPresets.FirstOrDefault(k => k.Value == s.HoldToInteract.VirtualKey);
            if (match is null)
            {
                match = new Option($"Custom (0x{s.HoldToInteract.VirtualKey:X})", s.HoldToInteract.VirtualKey);
                TriggerKeyCombo.Items.Add(match);
            }
            TriggerKeyCombo.SelectedItem = match;

            MouseButtonCombo.DisplayMemberPath = "Name";
            MouseButtonCombo.Items.Clear();
            MouseButtonCombo.Items.Add(new Option("Mouse 4 (back)", 4));
            MouseButtonCombo.Items.Add(new Option("Mouse 5 (forward)", 5));
            MouseButtonCombo.SelectedIndex = s.HoldToInteract.MouseButton == 4 ? 0 : 1;

            UpdateTriggerRowVisibility();

            // Coordination / background / exclusions
            AutoSwitchProfileCheck.IsChecked = s.AutoSwitchMacroProfile;
            HideInFullscreenCheck.IsChecked = s.HideHudInFullscreen;
            PauseOnBatteryCheck.IsChecked = s.PauseHeavyIndexingOnBattery;
            ExclusionsBox.Text = string.Join(Environment.NewLine, s.ExcludedApplications);

            AssessmentStartupCheck.IsChecked = s.AssessmentModeActive;

            SavedLabel.Text = string.Empty;
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnTriggerTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        UpdateTriggerRowVisibility();
    }

    private void UpdateTriggerRowVisibility()
    {
        if (KeyRow is null || MouseRow is null) return;
        bool keyboard = TriggerTypeCombo.SelectedIndex == 0;
        KeyRow.Visibility = keyboard ? Visibility.Visible : Visibility.Collapsed;
        MouseRow.Visibility = keyboard ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            var current = _store.Current;

            bool keyboard = TriggerTypeCombo.SelectedIndex == 0;
            int vk = (TriggerKeyCombo.SelectedItem as Option)?.Value ?? current.HoldToInteract.VirtualKey;
            int mouse = (MouseButtonCombo.SelectedItem as Option)?.Value ?? current.HoldToInteract.MouseButton;

            var updated = current with
            {
                ThemeId = ThemeCombo.SelectedItem as string ?? current.ThemeId,
                SnapDistancePixels = Math.Round(SnapSlider.Value),
                ShowControlCapsule = ShowCapsuleCheck.IsChecked == true,
                HoldToInteract = new HoldTriggerSettings
                {
                    Type = keyboard ? HoldTriggerType.KeyboardKey : HoldTriggerType.MouseButton,
                    VirtualKey = vk,
                    MouseButton = mouse
                },
                AutoSwitchMacroProfile = AutoSwitchProfileCheck.IsChecked == true,
                HideHudInFullscreen = HideInFullscreenCheck.IsChecked == true,
                PauseHeavyIndexingOnBattery = PauseOnBatteryCheck.IsChecked == true,
                AssessmentModeActive = AssessmentStartupCheck.IsChecked == true,
                ExcludedApplications = ParseExclusions(ExclusionsBox.Text)
            };

            await _store.SaveAsync(updated);

            // Apply the live-effect settings immediately.
            _theme.ApplyTheme(updated.ThemeId);
            _hold.ApplySettings(updated);

            SavedLabel.Text = $"Saved at {DateTime.Now:HH:mm:ss}";
            _logger.LogInformation("Settings saved from the settings window.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings.");
            SavedLabel.Text = "Save failed — see logs.";
        }
    }

    private void OnRevert(object sender, RoutedEventArgs e) => LoadFromSettings();

    private static List<string> ParseExclusions(string text)
        => text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line => line.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? line[..^4] : line)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
