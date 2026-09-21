using System.Text.RegularExpressions;

namespace StudyHud.Search;

/// <summary>
/// Deterministic extraction of significant terms and key phrases from text (spec §51, §54). No LLMs,
/// embeddings, or generative AI — just tokenising, stop-word removal, and adjacent-word pairing.
///
/// A "key phrase" is a pair of adjacent significant words (e.g. "bending stress", "shear force"). These
/// let the search and the Wordbank match the <em>exact wording</em> of a concept, which is far more
/// precise than matching its individual words, without ever inventing wording that isn't in the notes.
/// </summary>
public static class KeyPhraseExtractor
{
    /// <summary>Words too common to carry meaning; dropped from terms and never allowed in a phrase.</summary>
    public static readonly IReadOnlySet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "is", "are", "was", "were", "be", "been", "being", "have", "has", "had",
        "do", "does", "did", "will", "would", "could", "should", "may", "might", "shall", "can",
        "to", "of", "in", "on", "at", "by", "for", "with", "from", "into", "through", "during",
        "before", "after", "above", "below", "and", "or", "but", "if", "then", "so", "yet", "nor",
        "both", "either", "neither", "not", "only", "also", "just", "this", "that", "when", "where",
        "which", "who", "whom", "whose", "what", "why", "how", "these", "those", "its", "it", "he",
        "she", "they", "we", "you", "i", "as", "such", "than", "each", "any", "all", "one", "two",
        "use", "used", "using", "given", "find", "determine", "calculate", "shown", "figure", "using",
    };

    private static readonly Regex WordPattern = new(@"[\p{L}][\p{L}\p{Nd}\-]*", RegexOptions.Compiled);

    /// <summary>Lower-cased word tokens in document order (Greek/Latin letters, digits and hyphens).</summary>
    public static IReadOnlyList<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var result = new List<string>();
        foreach (Match m in WordPattern.Matches(text))
            result.Add(m.Value.ToLowerInvariant());
        return result;
    }

    /// <summary>True if a token is worth keeping as a standalone term (long enough and not a stop word).</summary>
    public static bool IsSignificant(string token) =>
        token.Length >= 4 && !StopWords.Contains(token) && token.Any(char.IsLetter);

    /// <summary>Distinct significant single-word terms in first-seen order.</summary>
    public static IReadOnlyList<string> SignificantWords(string text)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var t in Tokenize(text))
            if (IsSignificant(t) && seen.Add(t))
                result.Add(t);
        return result;
    }

    /// <summary>
    /// Distinct adjacent key phrases (bigrams) in first-seen order. A phrase is two neighbouring tokens
    /// that are each at least three letters long and neither a stop word, so ordinary connective words
    /// never glue two unrelated terms together.
    /// </summary>
    public static IReadOnlyList<string> Phrases(string text)
    {
        var tokens = Tokenize(text);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        for (int i = 0; i + 1 < tokens.Count; i++)
        {
            var a = tokens[i];
            var b = tokens[i + 1];
            if (!PhraseWord(a) || !PhraseWord(b)) continue;
            var phrase = a + " " + b;
            if (seen.Add(phrase)) result.Add(phrase);
        }
        return result;
    }

    // A phrase member must be a real word (3+ letters, at least one letter) and not a stop word.
    private static bool PhraseWord(string token) =>
        token.Length >= 3 && !StopWords.Contains(token) && token.Any(char.IsLetter);
}
