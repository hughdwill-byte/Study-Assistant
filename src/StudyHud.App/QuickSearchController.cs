using Microsoft.Extensions.Logging;
using StudyHud.Core.Services;

namespace StudyHud.App;

/// <summary>
/// Owns the Quick-Search palette and its global hotkey (Ctrl + Shift + Space): shows/hides the
/// <see cref="QuickSearchWindow"/> from anywhere. Created once at startup.
/// </summary>
public sealed class QuickSearchController
{
    private const int HotkeyId = 3000;
    private const int VkSpace = 0x20;

    private readonly IGlobalInputService _input;
    private readonly IQuestionFinder _finder;
    private readonly ILogger<QuickSearchController> _logger;

    private QuickSearchWindow? _window;

    public QuickSearchController(
        IGlobalInputService input, IQuestionFinder finder, ILogger<QuickSearchController> logger)
    {
        _input = input;
        _finder = finder;
        _logger = logger;
    }

    public void Start()
    {
        _input.RegisterHotKey(HotkeyId, ModifierKeys.Control | ModifierKeys.Shift, VkSpace);
        _input.InputReceived += OnInput;
        _logger.LogInformation("Quick-Search palette ready (Ctrl+Shift+Space).");
    }

    private void OnInput(object? sender, GlobalInputEventArgs e)
    {
        if (e.EventType != GlobalInputEventType.HotKey || e.HotKeyId != HotkeyId) return;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(Toggle);
    }

    private void Toggle()
    {
        _window ??= new QuickSearchWindow(_finder, _logger);
        if (_window.IsVisible) _window.Hide();
        else _window.ShowPalette();
    }
}
