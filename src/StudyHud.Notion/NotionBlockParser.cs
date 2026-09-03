using System.Text;
using System.Text.Json;

namespace StudyHud.Notion;

/// <summary>
/// A single indexable block extracted from a Notion page (spec §44): an image (URL to download) or
/// a text block, tagged with the heading hierarchy it appears under so results carry a breadcrumb.
/// </summary>
public record ParsedNoteBlock
{
    public required string BlockId { get; init; }
    public string HeadingPath { get; init; } = "";
    public string? HeadingText { get; init; }
    public string? Text { get; init; }
    public string? ImageUrl { get; init; }
    public bool IsImage => ImageUrl is not null;
}

/// <summary>
/// Deterministically converts a Notion page's block children (as returned by
/// <c>GET /v1/blocks/{id}/children</c>) into <see cref="ParsedNoteBlock"/>s (spec §44, §45). Pure and
/// side-effect-free: no network, no generative AI. Heading blocks update a running breadcrumb so each
/// image/text block records the section it lives under (spec §48, §58).
/// </summary>
public static class NotionBlockParser
{
    private static readonly HashSet<string> TextTypes = new(StringComparer.Ordinal)
    {
        "paragraph", "bulleted_list_item", "numbered_list_item",
        "quote", "callout", "toggle", "to_do"
    };

    public static IReadOnlyList<ParsedNoteBlock> ParsePage(IEnumerable<JsonElement> blocks)
    {
        var result = new List<ParsedNoteBlock>();
        string? h1 = null, h2 = null, h3 = null;

        foreach (var block in blocks)
        {
            if (block.ValueKind != JsonValueKind.Object) continue;
            if (!block.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
                continue;

            var type = typeEl.GetString()!;
            var id = block.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString()! : string.Empty;

            switch (type)
            {
                case "heading_1":
                    h1 = RichTextOf(block, type); h2 = null; h3 = null;
                    break;
                case "heading_2":
                    h2 = RichTextOf(block, type); h3 = null;
                    break;
                case "heading_3":
                    h3 = RichTextOf(block, type);
                    break;

                case "image":
                    var url = ImageUrlOf(block);
                    if (!string.IsNullOrEmpty(url))
                        result.Add(new ParsedNoteBlock
                        {
                            BlockId = id,
                            ImageUrl = url,
                            HeadingPath = BuildPath(h1, h2, h3),
                            HeadingText = Deepest(h1, h2, h3)
                        });
                    break;

                default:
                    if (TextTypes.Contains(type))
                    {
                        var text = RichTextOf(block, type);
                        if (!string.IsNullOrWhiteSpace(text))
                            result.Add(new ParsedNoteBlock
                            {
                                BlockId = id,
                                Text = text,
                                HeadingPath = BuildPath(h1, h2, h3),
                                HeadingText = Deepest(h1, h2, h3)
                            });
                    }
                    break;
            }
        }

        return result;
    }

    /// <summary>Concatenates the <c>plain_text</c> spans of a block's <c>rich_text</c> array.</summary>
    private static string RichTextOf(JsonElement block, string type)
    {
        if (!block.TryGetProperty(type, out var body) || body.ValueKind != JsonValueKind.Object)
            return string.Empty;
        if (!body.TryGetProperty("rich_text", out var richText) || richText.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var span in richText.EnumerateArray())
            if (span.TryGetProperty("plain_text", out var pt) && pt.ValueKind == JsonValueKind.String)
                sb.Append(pt.GetString());
        return sb.ToString().Trim();
    }

    /// <summary>Resolves an image block's URL — external links directly, uploaded files via their
    /// (temporary) signed URL, which the caller must download promptly before it expires (spec §45).</summary>
    private static string? ImageUrlOf(JsonElement block)
    {
        if (!block.TryGetProperty("image", out var img) || img.ValueKind != JsonValueKind.Object)
            return null;

        var kind = img.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString() : null;

        if (kind == "external" && img.TryGetProperty("external", out var ext)
            && ext.TryGetProperty("url", out var u1) && u1.ValueKind == JsonValueKind.String)
            return u1.GetString();

        if (kind == "file" && img.TryGetProperty("file", out var file)
            && file.TryGetProperty("url", out var u2) && u2.ValueKind == JsonValueKind.String)
            return u2.GetString();

        return null;
    }

    private static string BuildPath(string? h1, string? h2, string? h3)
        => string.Join(" > ", new[] { h1, h2, h3 }.Where(s => !string.IsNullOrWhiteSpace(s)));

    private static string? Deepest(string? h1, string? h2, string? h3)
    {
        if (!string.IsNullOrWhiteSpace(h3)) return h3;
        if (!string.IsNullOrWhiteSpace(h2)) return h2;
        return string.IsNullOrWhiteSpace(h1) ? null : h1;
    }
}
