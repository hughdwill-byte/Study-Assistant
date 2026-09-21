namespace StudyHud.Core.Services;

/// <summary>
/// Types text into whatever window currently has focus. Used by the symbol palette to "enter" a symbol
/// straight where the user is typing — the HUD overlay is a no-activate window, so it never steals
/// focus and the characters land in the underlying app.
/// </summary>
public interface ITextInputService
{
    /// <summary>Sends the given text to the focused window as Unicode keystrokes.</summary>
    void InsertText(string text);
}
