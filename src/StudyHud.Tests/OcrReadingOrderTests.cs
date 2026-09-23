using FluentAssertions;
using StudyHud.Core.Models;
using StudyHud.Core.Services;
using StudyHud.Ocr;
using Xunit;

namespace StudyHud.Tests;

// ─── Column-aware OCR reading-order reconstruction (spec §51) ─────────────────

public sealed class OcrReadingOrderTests
{
    private static OcrWord W(string text, int left, int top, int right, int bottom) => new()
    {
        Text = text,
        Confidence = 0.9f,
        BoundingBox = new ScreenRect(left, top, right, bottom)
    };

    [Fact]
    public void Reconstruct_TwoColumns_ReadsEachColumnFullyBeforeTheNext()
    {
        // Two columns, supplied interleaved by row (as the engine would emit them).
        var words = new[]
        {
            W("alpha", 10, 0, 90, 20),
            W("gamma", 400, 0, 480, 20),
            W("beta", 10, 25, 90, 45),
            W("delta", 400, 25, 480, 45),
        };

        var text = OcrReadingOrder.Reconstruct(words);

        // Left column ("alpha", "beta") is read completely before the right column ("gamma", "delta").
        text.IndexOf("alpha").Should().BeLessThan(text.IndexOf("beta"));
        text.IndexOf("beta").Should().BeLessThan(text.IndexOf("gamma"));
        text.IndexOf("gamma").Should().BeLessThan(text.IndexOf("delta"));
    }

    [Fact]
    public void Reconstruct_SingleColumn_ReadsTopToBottom()
    {
        var words = new[]
        {
            W("second", 10, 30, 120, 50),
            W("first", 10, 0, 120, 20),
            W("third", 10, 60, 120, 80),
        };

        var text = OcrReadingOrder.Reconstruct(words);

        text.IndexOf("first").Should().BeLessThan(text.IndexOf("second"));
        text.IndexOf("second").Should().BeLessThan(text.IndexOf("third"));
    }

    [Fact]
    public void Reconstruct_KeepsInColumnWordsAdjacent_ForKeyPhrases()
    {
        // "bending stress" sit side by side in the LEFT column; a right-column word shares their row.
        var words = new[]
        {
            W("bending", 10, 0, 80, 20),
            W("stress", 90, 0, 150, 20),
            W("entropy", 400, 0, 470, 20),
        };

        var text = OcrReadingOrder.Reconstruct(words);
        var phrases = StudyHud.Search.KeyPhraseExtractor.Phrases(text);

        phrases.Should().Contain("bending stress");
    }

    [Fact]
    public void Reconstruct_EmptyOrSingle_IsSafe()
    {
        OcrReadingOrder.Reconstruct(Array.Empty<OcrWord>()).Should().BeEmpty();
        OcrReadingOrder.Reconstruct(new[] { W("solo", 0, 0, 40, 20) }).Should().Be("solo");
    }
}
