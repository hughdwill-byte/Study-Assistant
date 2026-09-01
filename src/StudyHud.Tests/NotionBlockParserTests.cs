using System.Text.Json;
using FluentAssertions;
using StudyHud.Notion;
using Xunit;

namespace StudyHud.Tests;

// ─── Notion block parsing (spec §44, §45, §48) ───────────────────────────────

public class NotionBlockParserTests
{
    private static IReadOnlyList<ParsedNoteBlock> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        // Clone so the elements outlive the JsonDocument (mirrors how the connector buffers pages).
        var blocks = doc.RootElement.EnumerateArray().Select(e => e.Clone());
        return NotionBlockParser.ParsePage(blocks);
    }

    private const string Page = """
    [
      { "type": "heading_1", "id": "h1", "heading_1": { "rich_text": [ { "plain_text": "Bending" } ] } },
      { "type": "heading_2", "id": "h2", "heading_2": { "rich_text": [ { "plain_text": "Flexure Formula" } ] } },
      { "type": "paragraph", "id": "p1", "paragraph": { "rich_text": [ { "plain_text": "The flexure formula is " }, { "plain_text": "sigma = My/I" } ] } },
      { "type": "image", "id": "img1", "image": { "type": "file", "file": { "url": "https://files.notion.so/note1.png" } } },
      { "type": "heading_1", "id": "h1b", "heading_1": { "rich_text": [ { "plain_text": "Torsion" } ] } },
      { "type": "image", "id": "img2", "image": { "type": "external", "external": { "url": "https://ext.example/note2.png" } } },
      { "type": "divider", "id": "d1", "divider": {} }
    ]
    """;

    [Fact]
    public void ParsePage_ExtractsImagesTextAndHeadingBreadcrumbs()
    {
        var blocks = Parse(Page);

        // paragraph under Bending > Flexure Formula, with both rich_text spans concatenated
        var para = blocks.Single(b => b.BlockId == "p1");
        para.Text.Should().Be("The flexure formula is sigma = My/I");
        para.HeadingPath.Should().Be("Bending > Flexure Formula");
        para.HeadingText.Should().Be("Flexure Formula");

        // file image inherits the same breadcrumb and resolves its signed URL
        var img1 = blocks.Single(b => b.BlockId == "img1");
        img1.IsImage.Should().BeTrue();
        img1.ImageUrl.Should().Be("https://files.notion.so/note1.png");
        img1.HeadingPath.Should().Be("Bending > Flexure Formula");
    }

    [Fact]
    public void ParsePage_NewHeading1_ResetsDeeperHeadings()
    {
        var blocks = Parse(Page);

        // img2 comes after a fresh heading_1 "Torsion"; the old heading_2 must be cleared.
        var img2 = blocks.Single(b => b.BlockId == "img2");
        img2.ImageUrl.Should().Be("https://ext.example/note2.png");
        img2.HeadingPath.Should().Be("Torsion");
        img2.HeadingText.Should().Be("Torsion");
    }

    [Fact]
    public void ParsePage_IgnoresDividersAndEmptyText()
    {
        var blocks = Parse(Page);

        blocks.Should().NotContain(b => b.BlockId == "d1");
        blocks.Should().OnlyContain(b => b.IsImage || !string.IsNullOrWhiteSpace(b.Text));
    }

    [Fact]
    public void ParsePage_EmptyArray_ReturnsEmpty()
    {
        Parse("[]").Should().BeEmpty();
    }
}
