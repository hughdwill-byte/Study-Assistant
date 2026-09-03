using System.Text.RegularExpressions;
using StudyHud.Core.Services;

namespace StudyHud.Search;

/// <summary>
/// Deterministic feature extraction from OCR text (spec §51, §146).
/// Separates words, variables, numbers, units, symbols, and expressions.
/// No LLMs, embeddings, or generative AI involved.
/// </summary>
public static class FeatureExtractor
{
    // Engineering units (case-sensitive after normalisation)
    private static readonly string[] KnownUnits = [
        "kNm", "kN", "MPa", "GPa", "kPa", "Pa", "kJ", "J",
        "kg", "g", "mg", "mm", "cm", "m", "km",
        "N", "W", "kW", "MW", "V", "A", "Hz", "kHz", "MHz",
        "rad", "deg", "°", "°C", "°F", "K",
        "s", "ms", "μs", "min", "hr", "h"
    ];

    // Common engineering/maths variables (single letters used as symbols)
    private static readonly HashSet<string> CommonVariables = new(StringComparer.Ordinal)
    {
        "σ", "ε", "τ", "ρ", "μ", "λ", "α", "β", "γ", "δ", "θ", "φ", "ω", "Δ",
        "M", "I", "E", "F", "A", "V", "P", "Q", "R", "T", "S", "G", "J",
        "y", "x", "z", "n", "k", "c", "d", "h", "b", "L", "W", "H"
    };

    // Expression patterns (conservative, won't over-match)
    private static readonly Regex ExpressionPattern = new(
        @"[A-Za-zσεταβγδθφωρμλΔ]\s*[=≈<>≤≥]\s*[\w\.σεταβγδθφω/\(\)\*\+\-\^]+",
        RegexOptions.Compiled);

    private static readonly Regex NumberPattern = new(
        @"\b\d+\.?\d*(?:[eE][+-]?\d+)?\b",
        RegexOptions.Compiled);

    private static readonly Regex VariablePattern = new(
        @"(?<![A-Za-z])([σεταβγδθφωρμλΔA-Z])(?![A-Za-z])",
        RegexOptions.Compiled);

    private static readonly Regex StopWordPattern = new(
        @"\b(the|a|an|is|are|was|were|be|been|being|have|has|had|do|does|did|" +
        @"will|would|could|should|may|might|shall|can|to|of|in|on|at|by|for|" +
        @"with|from|into|through|during|before|after|above|below|and|or|but|" +
        @"if|then|so|yet|nor|both|either|neither|not|only|also|just|this|that|" +
        @"when|where|which|who|whom|whose|what|why|how|" +
        @"these|those|its|it|he|she|they|we|you|i)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // A run of 2+ letters (Latin or Greek) — a candidate compound expression such as "My" in
    // "My/I". Only decomposed into individual variables when it sits in a math context (spec §51).
    private static readonly Regex LetterClusterPattern = new(
        @"[A-Za-zσεταβγδθφωρμλΔ]{2,}",
        RegexOptions.Compiled);

    // Characters that mark an expression context around a letter cluster.
    private static readonly char[] MathContextChars =
        ['=', '/', '*', '+', '-', '^', '(', ')', '·', '×', '≈', '<', '>', '≤', '≥'];

    public static ExtractedFeatures Extract(string normalisedText)
    {
        if (string.IsNullOrWhiteSpace(normalisedText))
            return new ExtractedFeatures
            {
                Words = [], Variables = [], Numbers = [],
                Units = [], Expressions = [], Symbols = []
            };

        var expressions = ExtractExpressions(normalisedText);
        var numbers = ExtractNumbers(normalisedText);
        var units = ExtractUnits(normalisedText);
        var variables = ExtractVariables(normalisedText);
        var words = ExtractWords(normalisedText, variables, numbers, units);

        return new ExtractedFeatures
        {
            Words = words,
            Variables = variables,
            Numbers = numbers,
            Units = units,
            Expressions = expressions,
            Symbols = variables.Concat(ExtractUnicodeSymbols(normalisedText))
                               .Distinct().ToList()
        };
    }

    private static IReadOnlyList<string> ExtractExpressions(string text)
    {
        return ExpressionPattern.Matches(text)
            .Select(m => m.Value.Trim())
            .Distinct()
            .ToList();
    }

    private static IReadOnlyList<string> ExtractNumbers(string text)
    {
        return NumberPattern.Matches(text)
            .Select(m => m.Value)
            .Distinct()
            .ToList();
    }

    private static IReadOnlyList<string> ExtractUnits(string text)
    {
        var found = new HashSet<string>();
        foreach (var unit in KnownUnits)
        {
            // Word-boundary check for unit
            if (Regex.IsMatch(text, $@"(?<![A-Za-z]){Regex.Escape(unit)}(?![A-Za-z])"))
                found.Add(unit);
        }
        return found.ToList();
    }

    private static IReadOnlyList<string> ExtractVariables(string text)
    {
        var found = new HashSet<string>();

        // Pass 1: isolated single-letter variables (e.g. "M = 3.2").
        foreach (Match m in VariablePattern.Matches(text))
        {
            if (CommonVariables.Contains(m.Value))
                found.Add(m.Value);
        }

        // Pass 2: compound clusters in a math context (e.g. "My/I" → M, y, I). A cluster is only
        // decomposed when it touches a math operator AND every character is a known variable, so
        // ordinary prose words are never split (spec §51, §52).
        foreach (Match m in LetterClusterPattern.Matches(text))
        {
            int start = m.Index, end = m.Index + m.Length;
            char before = start > 0 ? text[start - 1] : ' ';
            char after = end < text.Length ? text[end] : ' ';
            bool inMathContext = Array.IndexOf(MathContextChars, before) >= 0
                              || Array.IndexOf(MathContextChars, after) >= 0;
            if (!inMathContext) continue;

            if (m.Value.All(ch => CommonVariables.Contains(ch.ToString())))
            {
                foreach (char ch in m.Value)
                    found.Add(ch.ToString());
            }
        }

        return found.ToList();
    }

    private static IReadOnlyList<string> ExtractWords(
        string text,
        IReadOnlyList<string> variables,
        IReadOnlyList<string> numbers,
        IReadOnlyList<string> units)
    {
        // Tokenise, strip stop words and already-categorised tokens
        var tokens = Regex.Split(text, @"[\s\p{P}]+")
            .Where(t => t.Length >= 3)
            .Select(t => t.ToLowerInvariant())
            .Where(t => !StopWordPattern.IsMatch(t))
            .Where(t => !numbers.Contains(t))
            .Where(t => !units.Any(u => t.Equals(u, StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .ToList();

        return tokens;
    }

    private static IReadOnlyList<string> ExtractUnicodeSymbols(string text)
    {
        return text
            .Where(c => c > 127 && char.IsLetter(c))
            .Select(c => c.ToString())
            .Distinct()
            .ToList();
    }
}
