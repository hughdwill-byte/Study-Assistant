using Microsoft.Extensions.Logging;
using StudyHud.Core.Services;

namespace StudyHud.Search;

/// <summary>
/// Builds and searches the <see cref="IWordbank"/> from the local index (spec §54, §71). Fully
/// deterministic and local: it harvests the user's own wording — page titles, headings, note text and
/// the OCR'd text on their images — and never invents a term or calls a remote/generative service.
///
/// Terms found in a page title or heading are flagged as <em>key terms</em> (concepts the user named)
/// and always float to the top; everything else is ranked by how often it recurs.
/// </summary>
public sealed class Wordbank : IWordbank
{
    // Non-key terms must recur at least this many times to make the curated glossary, keeping it
    // signal not noise. Every harvested term (including one-offs) is still findable via LookupAsync.
    private const int MinNonKeyFrequency = 2;
    private const int MaxCuratedEntries = 750;

    private readonly ISearchIndex _index;
    private readonly ILogger<Wordbank> _logger;

    public Wordbank(ISearchIndex index, ILogger<Wordbank> logger)
    {
        _index = index;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WordbankEntry>> BuildAsync(
        string courseId, CancellationToken ct = default)
    {
        var all = await HarvestAsync(courseId, ct).ConfigureAwait(false);
        return all
            .Where(e => e.IsKeyTerm || e.Frequency >= MinNonKeyFrequency)
            .Take(MaxCuratedEntries)
            .ToList();
    }

    public async Task<IReadOnlyList<WordbankEntry>> LookupAsync(
        string courseId, string query, int maxResults = 25, CancellationToken ct = default)
    {
        var q = (query ?? string.Empty).Trim().ToLowerInvariant();
        if (q.Length == 0) return [];

        var all = await HarvestAsync(courseId, ct).ConfigureAwait(false);

        // Rank matches: exact, then prefix, then substring; ties broken by the harvest order (key
        // terms and frequency). This keeps "the exact wording I need" at the top.
        int Rank(WordbankEntry e)
        {
            var t = e.Term.ToLowerInvariant();
            if (t == q) return 0;
            if (t.StartsWith(q, StringComparison.Ordinal)) return 1;
            if (t.Contains(q, StringComparison.Ordinal)) return 2;
            return 3;
        }

        return all
            .Select((e, i) => (e, r: Rank(e), i))
            .Where(x => x.r < 3)
            .OrderBy(x => x.r).ThenBy(x => x.i)
            .Take(maxResults)
            .Select(x => x.e)
            .ToList();
    }

    /// <summary>
    /// Harvests the full, ordered term aggregate for a course. Key terms first, then by frequency,
    /// then alphabetically — a stable order so the UI and lookups are predictable.
    /// </summary>
    private async Task<IReadOnlyList<WordbankEntry>> HarvestAsync(string courseId, CancellationToken ct)
    {
        var corpus = await _index.GetCourseCorpusAsync(courseId, ct).ConfigureAwait(false);
        var agg = new Dictionary<string, Aggregate>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in corpus)
        {
            ct.ThrowIfCancellationRequested();

            // Text the user explicitly named the concept with — titles and headings.
            var keyText = string.Join(' ', new[] { row.PageName, row.HeadingText, row.HeadingPath }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            var keyTerms = new HashSet<string>(
                KeyPhraseExtractor.SignificantWords(keyText)
                    .Concat(KeyPhraseExtractor.Phrases(keyText)),
                StringComparer.OrdinalIgnoreCase);

            // Every candidate term across the note's key text + body, with occurrence counts.
            var body = row.Normalised ?? string.Empty;
            var combined = keyText + " " + body;

            foreach (var (term, count) in CountTerms(combined))
            {
                if (!agg.TryGetValue(term, out var a))
                {
                    a = new Aggregate { Display = term };
                    agg[term] = a;
                }
                a.Frequency += count;
                if (keyTerms.Contains(term)) a.IsKeyTerm = true;
                if (a.SeenNotes.Add(row.NoteItemId))
                    a.Locations.Add(new WordbankLocation
                    {
                        NoteItemId = row.NoteItemId,
                        PageName = row.PageName,
                        WeekLabel = row.WeekLabel,
                        HeadingPath = row.HeadingPath,
                        NotionPageUrl = row.NotionPageUrl,
                        NotionBlockId = row.NotionBlockId
                    });
            }
        }

        var entries = agg.Values
            .Select(a => new WordbankEntry
            {
                Term = a.Display,
                Frequency = a.Frequency,
                IsKeyTerm = a.IsKeyTerm,
                // Notes where the concept was named (a heading/title hit) come first.
                Locations = a.Locations
            })
            .OrderByDescending(e => e.IsKeyTerm)
            .ThenByDescending(e => e.Frequency)
            .ThenBy(e => e.Term, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogDebug("Wordbank for {Course}: {Terms} terms from {Notes} notes.",
            courseId, entries.Count, corpus.Count);
        return entries;
    }

    /// <summary>Occurrence counts for every significant unigram and key phrase in the text.</summary>
    private static IEnumerable<(string Term, int Count)> CountTerms(string text)
    {
        var tokens = KeyPhraseExtractor.Tokenize(text);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Unigrams
        foreach (var t in tokens)
            if (KeyPhraseExtractor.IsSignificant(t))
                counts[t] = counts.GetValueOrDefault(t) + 1;

        // Phrases (adjacent significant pairs)
        for (int i = 0; i + 1 < tokens.Count; i++)
        {
            var a = tokens[i];
            var b = tokens[i + 1];
            if (a.Length >= 3 && b.Length >= 3
                && !KeyPhraseExtractor.StopWords.Contains(a) && !KeyPhraseExtractor.StopWords.Contains(b))
            {
                var phrase = a + " " + b;
                counts[phrase] = counts.GetValueOrDefault(phrase) + 1;
            }
        }

        return counts.Select(kv => (kv.Key, kv.Value));
    }

    private sealed class Aggregate
    {
        public required string Display { get; init; }
        public int Frequency { get; set; }
        public bool IsKeyTerm { get; set; }
        public HashSet<string> SeenNotes { get; } = new(StringComparer.Ordinal);
        public List<WordbankLocation> Locations { get; } = new();
    }
}
