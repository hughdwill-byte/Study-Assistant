using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Services;
using StudyHud.Windows.Native;

namespace StudyHud.Windows.Services;

/// <summary>
/// Types Unicode text into the focused window via SendInput (KEYEVENTF_UNICODE). Each UTF-16 code unit
/// is sent as a scan code, so characters outside the BMP (surrogate pairs) come through correctly.
/// </summary>
public sealed class TextInputService : ITextInputService
{
    private readonly ILogger<TextInputService> _logger;

    public TextInputService(ILogger<TextInputService> logger) => _logger = logger;

    public void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            var inputs = new List<NativeMethods.INPUT>(text.Length * 2);
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
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send symbol text to the focused window.");
        }
    }
}
