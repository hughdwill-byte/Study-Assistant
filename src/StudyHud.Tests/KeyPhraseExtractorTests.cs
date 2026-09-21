using FluentAssertions;
using StudyHud.Search;
using Xunit;

namespace StudyHud.Tests;

// ─── Deterministic key-phrase / term extraction (spec §51, §54) ──────────────

public sealed class KeyPhraseExtractorTests
{
    [Fact]
    public void Phrases_ExtractsAdjacentSignificantPairs()
    {
        var phrases = KeyPhraseExtractor.Phrases("Maximum bending stress in the beam");

        phrases.Should().Contain("bending stress");
        phrases.Should().Contain("maximum bending");
    }

    [Fact]
    public void Phrases_DoesNotBridgeStopWords()
    {
        // "stress" and "beam" are separated by the stop word "in the" — never glued into a phrase.
        var phrases = KeyPhraseExtractor.Phrases("bending stress in the beam");

        phrases.Should().NotContain("stress beam");
        phrases.Should().NotContain("stress in");
    }

    [Fact]
    public void Phrases_AreDistinctAndLowercased()
    {
        var phrases = KeyPhraseExtractor.Phrases("Shear Force shear force SHEAR FORCE");

        // Case-folded and de-duplicated: "shear force" appears once regardless of the original casing.
        phrases.Should().OnlyHaveUniqueItems();
        phrases.Should().OnlyContain(p => p == p.ToLowerInvariant());
        phrases.Should().Contain("shear force");
        phrases.Count(p => p == "shear force").Should().Be(1);
    }

    [Fact]
    public void SignificantWords_DropsStopWordsAndShortTokens()
    {
        var words = KeyPhraseExtractor.SignificantWords("the beam is in torsion at a node");

        words.Should().Contain("beam");
        words.Should().Contain("torsion");
        words.Should().Contain("node");
        words.Should().NotContain("the");
        words.Should().NotContain("is");
        words.Should().NotContain("at");   // stop word
    }

    [Fact]
    public void Tokenize_KeepsGreekAndAlphanumericWords()
    {
        var tokens = KeyPhraseExtractor.Tokenize("σ = My/I for co2 uptake");

        tokens.Should().Contain("σ");
        tokens.Should().Contain("my");
        tokens.Should().Contain("co2");   // letter-initiated alphanumeric word is kept
    }

    [Fact]
    public void Phrases_EmptyOrNull_ReturnsEmpty()
    {
        KeyPhraseExtractor.Phrases("").Should().BeEmpty();
        KeyPhraseExtractor.Phrases("   ").Should().BeEmpty();
    }
}
