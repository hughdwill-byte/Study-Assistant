namespace StudyHud.Core.Models;

/// <summary>
/// The kind of a rendered Cheat Sheet block, mapped from Notion block types. Presentation-neutral so
/// the Core layer stays free of WPF — the HUD panel decides how each kind is drawn.
/// </summary>
public enum CheatBlockKind
{
    Heading1,
    Heading2,
    Heading3,
    Paragraph,
    Bulleted,
    Numbered,
    ToDo,
    Quote,
    Callout,
    Code,
    Divider,
    Image,
    ChildPageLink
}

/// <summary>
/// A run of text with Notion's inline annotations preserved, so the Cheat Sheet can render bold,
/// italic, underline, strikethrough, inline code and coloured text exactly as written.
/// </summary>
public sealed record CheatInline
{
    public required string Text { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Strikethrough { get; init; }
    public bool Code { get; init; }

    /// <summary>Foreground colour as "#RRGGBB", or null to use the theme's default text colour.</summary>
    public string? ColorHex { get; init; }
}

/// <summary>
/// One rendered block of a Cheat Sheet page: a heading, a list item, a quote, an image, etc. Carries
/// exactly what the panel needs to draw it faithfully — the formatted inline runs, nesting depth,
/// list numbering / checkbox state, and image bytes (already downloaded).
/// </summary>
public sealed record CheatBlock
{
    public required CheatBlockKind Kind { get; init; }

    /// <summary>Nesting depth (0 = top level). Drives left indentation of lists and nested content.</summary>
    public int IndentLevel { get; init; }

    public IReadOnlyList<CheatInline> Inlines { get; init; } = Array.Empty<CheatInline>();

    /// <summary>Decoded image bytes for <see cref="CheatBlockKind.Image"/> blocks.</summary>
    public byte[]? ImageBytes { get; init; }

    /// <summary>Optional caption text shown under an image.</summary>
    public string? ImageCaption { get; init; }

    /// <summary>Whether a to-do item is checked.</summary>
    public bool Checked { get; init; }

    /// <summary>1-based item number for <see cref="CheatBlockKind.Numbered"/> blocks.</summary>
    public int? Number { get; init; }

    /// <summary>The callout's leading emoji, if any.</summary>
    public string? Emoji { get; init; }
}

/// <summary>
/// A whole Notion page rendered for the Cheat Sheet panel: just the notes — heading, text, lists,
/// quotes, callouts, code and images — with all of Notion's chrome stripped out.
/// </summary>
public sealed record CheatSheetDocument
{
    public required string Title { get; init; }
    public IReadOnlyList<CheatBlock> Blocks { get; init; } = Array.Empty<CheatBlock>();
}
