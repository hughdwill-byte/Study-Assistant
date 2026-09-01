using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Services;
using MatchType = StudyHud.Core.Services.MatchType;

namespace StudyHud.Search;

/// <summary>
/// Deterministic local search using SQLite FTS5 + BM25 ranking (spec §49, §54, §55).
/// No generative AI, embeddings, or network calls involved.
/// All results are explainable from the deterministic scoring model.
/// </summary>
public sealed class LocalSearchIndex : ISearchIndex
{
    private readonly string _dbPath;
    private readonly ILogger<LocalSearchIndex> _logger;

    public LocalSearchIndex(string dbPath, ILogger<LocalSearchIndex> logger)
    {
        _dbPath = dbPath;
        _logger = logger;
    }

    public async Task<bool> IsReadyAsync(string courseId, CancellationToken ct = default)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
            await conn.OpenAsync(ct);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(*) FROM note_items 
                WHERE course_id = @courseId AND ocr_state = 'indexed'
                """;
            cmd.Parameters.AddWithValue("@courseId", courseId);

            var count = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
            return count > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        SearchQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.RawText) && query.Features.Words.Count == 0)
            return [];

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
            await conn.OpenAsync(ct);

            // Enable WAL for concurrent reads (spec §49)
            using (var pragma = conn.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=WAL;";
                await pragma.ExecuteNonQueryAsync(ct);
            }

            var rawResults = await ExecuteFtsSearchAsync(conn, query, ct);
            var scored = ScoreAndRank(rawResults, query);
            var top = scored.Take(query.MaxResults).ToList();

            _logger.LogDebug("Search completed in {Ms}ms: {Count} results for '{Text}'.",
                sw.ElapsedMilliseconds, top.Count, query.RawText);

            return top;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for query '{Text}'.", query.RawText);
            return [];
        }
    }

    private async Task<List<RawSearchResult>> ExecuteFtsSearchAsync(
        SqliteConnection conn, SearchQuery query, CancellationToken ct)
    {
        // Build FTS5 query from features
        var ftsTerms = BuildFtsQuery(query);
        if (string.IsNullOrEmpty(ftsTerms)) return [];

        var sql = """
            SELECT
                ni.id,
                ni.course_id,
                c.name AS course_name,
                ni.week_label,
                ni.page_name,
                ni.heading_path,
                ni.notion_page_url,
                ni.notion_block_id,
                ni.ocr_confidence,
                ni.heading_text,
                bm25(note_fts) AS bm25_score,
                snippet(note_fts, 0, '[', ']', '...', 20) AS snippet
            FROM note_fts
            JOIN note_items ni ON ni.id = note_fts.rowid
            JOIN courses c ON c.id = ni.course_id
            WHERE note_fts MATCH @query
            """;

        if (!string.IsNullOrEmpty(query.CourseId))
            sql += " AND ni.course_id = @courseId";

        if (!string.IsNullOrEmpty(query.WeekId))
            sql += " AND ni.week_id = @weekId";

        sql += " ORDER BY bm25_score LIMIT 50";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@query", ftsTerms);

        if (!string.IsNullOrEmpty(query.CourseId))
            cmd.Parameters.AddWithValue("@courseId", query.CourseId);
        if (!string.IsNullOrEmpty(query.WeekId))
            cmd.Parameters.AddWithValue("@weekId", query.WeekId);

        var results = new List<RawSearchResult>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new RawSearchResult
            {
                Id = reader.GetString(0),
                CourseId = reader.GetString(1),
                CourseName = reader.GetString(2),
                WeekLabel = reader.IsDBNull(3) ? null : reader.GetString(3),
                PageName = reader.GetString(4),
                HeadingPath = reader.GetString(5),
                NotionPageUrl = reader.GetString(6),
                NotionBlockId = reader.IsDBNull(7) ? null : reader.GetString(7),
                OcrConfidence = reader.IsDBNull(8) ? 1.0f : reader.GetFloat(8),
                HeadingText = reader.IsDBNull(9) ? null : reader.GetString(9),
                Bm25Score = reader.GetDouble(10),
                Snippet = reader.IsDBNull(11) ? null : reader.GetString(11)
            });
        }

        return results;
    }

    private static string BuildFtsQuery(SearchQuery query)
    {
        // Combine all feature types into an FTS5 query
        // Words get standard term search; variables/expressions get exact match
        var terms = new List<string>();

        foreach (var word in query.Features.Words.Take(10))
        {
            if (word.Length >= 3)
                terms.Add(EscapeFtsTerm(word));
        }

        foreach (var variable in query.Features.Variables)
            terms.Add($"\"{EscapeFtsTerm(variable)}\""); // exact match

        foreach (var unit in query.Features.Units)
            terms.Add($"\"{EscapeFtsTerm(unit)}\"");

        if (!terms.Any()) return string.Empty;

        return string.Join(" OR ", terms);
    }

    private static string EscapeFtsTerm(string term) =>
        term.Replace("\"", "\"\"").Replace("*", "");

    // ── Deterministic scoring (spec §55) ────────────────────────────────────

    private List<SearchResult> ScoreAndRank(List<RawSearchResult> raw, SearchQuery query)
    {
        var results = new List<SearchResult>();

        foreach (var r in raw)
        {
            var explanations = new List<MatchExplanation>();
            double score = 0;

            // Base BM25 (negated — lower = better in SQLite FTS5)
            double bm25Base = Math.Max(0, -r.Bm25Score * 10);
            score += bm25Base;

            // Heading match bonus
            if (!string.IsNullOrEmpty(r.HeadingText))
            {
                foreach (var word in query.Features.Words)
                {
                    if (r.HeadingText.Contains(word, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 15;
                        explanations.Add(new MatchExplanation
                        {
                            Type = MatchType.Heading,
                            Value = word
                        });
                    }
                }
            }

            // Variable exact overlap (high weight for engineering)
            foreach (var variable in query.Features.Variables)
            {
                if (r.Snippet?.Contains(variable) == true ||
                    r.HeadingText?.Contains(variable) == true)
                {
                    score += 20;
                    explanations.Add(new MatchExplanation
                    {
                        Type = MatchType.Variable,
                        Value = variable
                    });
                }
            }

            // Unit match
            foreach (var unit in query.Features.Units)
            {
                if (r.Snippet?.Contains(unit) == true)
                {
                    score += 8;
                    explanations.Add(new MatchExplanation
                    {
                        Type = MatchType.Symbol,
                        Value = unit
                    });
                }
            }

            // Word matches
            foreach (var word in query.Features.Words)
            {
                if (r.Snippet?.Contains(word, StringComparison.OrdinalIgnoreCase) == true)
                {
                    explanations.Add(new MatchExplanation
                    {
                        Type = MatchType.Word,
                        Value = word
                    });
                }
            }

            // OCR confidence weight (spec §55)
            score *= (0.5 + r.OcrConfidence * 0.5);

            // Normalise to 0–100
            double normalisedScore = Math.Min(100, Math.Max(0, score));

            results.Add(new SearchResult
            {
                NoteItemId = r.Id,
                CourseId = r.CourseId,
                CourseName = r.CourseName,
                WeekLabel = r.WeekLabel,
                PageName = r.PageName,
                HeadingPath = r.HeadingPath,
                NotionPageUrl = r.NotionPageUrl,
                NotionBlockId = r.NotionBlockId,
                MatchScore = normalisedScore,
                Explanations = explanations
            });
        }

        return results
            .Where(r => r.MatchScore > 0)
            .OrderByDescending(r => r.MatchScore)
            .ToList();
    }

    private record RawSearchResult
    {
        public required string Id { get; init; }
        public required string CourseId { get; init; }
        public required string CourseName { get; init; }
        public string? WeekLabel { get; init; }
        public required string PageName { get; init; }
        public required string HeadingPath { get; init; }
        public required string NotionPageUrl { get; init; }
        public string? NotionBlockId { get; init; }
        public required float OcrConfidence { get; init; }
        public string? HeadingText { get; init; }
        public required double Bm25Score { get; init; }
        public string? Snippet { get; init; }
    }
}
