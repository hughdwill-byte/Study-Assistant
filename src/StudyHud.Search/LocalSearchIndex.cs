using System.Text.RegularExpressions;
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

            // journal_mode (WAL) is a persistent database property set once by the migrator; a
            // read-only connection must not (and cannot) set it, so we just read (spec §49).
            var terms = BuildFtsQuery(query);
            var rawResults = terms.Length == 0
                ? new List<RawSearchResult>()
                : await ExecuteFtsSearchAsync(conn, terms, query, ct);

            // If the feature-based query found nothing, retry with a broad query built straight from
            // the raw OCR tokens — feature extraction can over-filter short/edge-case captures.
            if (rawResults.Count == 0)
            {
                var fallback = BuildRawFallbackQuery(query.RawText);
                if (fallback.Length > 0 && fallback != terms)
                    rawResults = await ExecuteFtsSearchAsync(conn, fallback, query, ct);
            }

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
        SqliteConnection conn, string ftsTerms, SearchQuery query, CancellationToken ct)
    {
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
            JOIN note_items ni ON ni.id = note_fts.note_item_id
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

    private static readonly Regex TokenPattern = new(@"[\p{L}\p{Nd}]+", RegexOptions.Compiled);

    private static string BuildFtsQuery(SearchQuery query)
    {
        // Every term is wrapped as a quoted FTS5 phrase so that words which collide with FTS
        // operators (AND, OR, NOT, NEAR) or contain punctuation can never corrupt the query.
        var terms = new List<string>();

        foreach (var word in query.Features.Words.Take(12))
            if (word.Length >= 3) terms.Add(QuoteFts(word));

        foreach (var variable in query.Features.Variables)
            terms.Add(QuoteFts(variable));

        foreach (var unit in query.Features.Units)
            terms.Add(QuoteFts(unit));

        var distinct = terms.Where(t => t.Length > 2).Distinct().ToList();
        return distinct.Count == 0 ? string.Empty : string.Join(" OR ", distinct);
    }

    /// <summary>Broad fallback: quote the raw OCR tokens directly and OR them together.</summary>
    private static string BuildRawFallbackQuery(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return string.Empty;

        var tokens = TokenPattern.Matches(rawText)
            .Select(m => m.Value.ToLowerInvariant())
            .Where(t => t.Length >= 3)
            .Distinct()
            .Take(16)
            .Select(QuoteFts)
            .ToList();

        return tokens.Count == 0 ? string.Empty : string.Join(" OR ", tokens);
    }

    private static string QuoteFts(string term) =>
        "\"" + term.Replace("\"", "\"\"") + "\"";

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

    // ── Write path (spec §49, §50) ──────────────────────────────────────────

    public async Task UpsertCourseAsync(string courseId, string courseName, CancellationToken ct = default)
    {
        await WithWriteConnectionAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO courses (id, name, created_at)
                VALUES (@id, @name, @now)
                ON CONFLICT(id) DO UPDATE SET name = excluded.name
                """;
            cmd.Parameters.AddWithValue("@id", courseId);
            cmd.Parameters.AddWithValue("@name", courseName);
            cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);
        }, ct);
    }

    public async Task IndexItemsAsync(IReadOnlyList<IndexableNoteItem> items, CancellationToken ct = default)
    {
        if (items.Count == 0) return;

        await WithWriteConnectionAsync(async conn =>
        {
            // One batched transaction for the whole set — never held open across OCR/network
            // work because callers pass already-OCR'd items (spec §49).
            using var tx = conn.BeginTransaction();

            using var upsert = conn.CreateCommand();
            upsert.Transaction = tx;
            upsert.CommandText = """
                INSERT INTO note_items
                    (id, course_id, week_id, week_label, page_id, page_name, heading_path,
                     heading_text, source_type, notion_page_url, notion_block_id, local_cache_id,
                     image_hash, ocr_raw_text, ocr_normalised, ocr_confidence, ocr_state, last_indexed_at)
                VALUES
                    (@id, @course, @weekId, @weekLabel, @pageId, @pageName, @headingPath,
                     @headingText, @sourceType, @url, @blockId, @cacheId,
                     @hash, @raw, @norm, @conf, @state, @now)
                ON CONFLICT(id) DO UPDATE SET
                    course_id=excluded.course_id, week_id=excluded.week_id, week_label=excluded.week_label,
                    page_id=excluded.page_id, page_name=excluded.page_name, heading_path=excluded.heading_path,
                    heading_text=excluded.heading_text, source_type=excluded.source_type,
                    notion_page_url=excluded.notion_page_url, notion_block_id=excluded.notion_block_id,
                    local_cache_id=excluded.local_cache_id, image_hash=excluded.image_hash,
                    ocr_raw_text=excluded.ocr_raw_text, ocr_normalised=excluded.ocr_normalised,
                    ocr_confidence=excluded.ocr_confidence, ocr_state=excluded.ocr_state,
                    last_indexed_at=excluded.last_indexed_at
                """;
            DeclareItemParameters(upsert);

            using var ftsDelete = conn.CreateCommand();
            ftsDelete.Transaction = tx;
            ftsDelete.CommandText = "DELETE FROM note_fts WHERE note_item_id = @id";
            var ftsDeleteId = ftsDelete.Parameters.Add("@id", SqliteType.Text);

            using var ftsInsert = conn.CreateCommand();
            ftsInsert.Transaction = tx;
            ftsInsert.CommandText = """
                INSERT INTO note_fts (ocr_normalised, heading_path, page_name, note_item_id)
                VALUES (@norm, @headingPath, @pageName, @id)
                """;
            var ftsNorm = ftsInsert.Parameters.Add("@norm", SqliteType.Text);
            var ftsHeading = ftsInsert.Parameters.Add("@headingPath", SqliteType.Text);
            var ftsPage = ftsInsert.Parameters.Add("@pageName", SqliteType.Text);
            var ftsId = ftsInsert.Parameters.Add("@id", SqliteType.Text);

            var now = DateTimeOffset.UtcNow.ToString("O");
            foreach (var item in items)
            {
                BindItemParameters(upsert, item, now);
                await upsert.ExecuteNonQueryAsync(ct);

                // Rebuild the FTS row for this item (delete-then-insert keeps re-indexing idempotent).
                ftsDeleteId.Value = item.Id;
                await ftsDelete.ExecuteNonQueryAsync(ct);

                ftsNorm.Value = item.OcrNormalised ?? string.Empty;
                ftsHeading.Value = item.HeadingPath ?? string.Empty;
                ftsPage.Value = item.PageName ?? string.Empty;
                ftsId.Value = item.Id;
                await ftsInsert.ExecuteNonQueryAsync(ct);
            }

            tx.Commit();
            _logger.LogDebug("Indexed {Count} note items.", items.Count);
        }, ct);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetContentHashesAsync(
        string courseId, CancellationToken ct = default)
    {
        var map = new Dictionary<string, string>();
        using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, image_hash FROM note_items WHERE course_id = @c AND image_hash IS NOT NULL";
        cmd.Parameters.AddWithValue("@c", courseId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            map[reader.GetString(0)] = reader.GetString(1);
        return map;
    }

    public async Task DeleteCourseAsync(string courseId, CancellationToken ct = default)
    {
        await WithWriteConnectionAsync(async conn =>
        {
            using var tx = conn.BeginTransaction();

            using (var delFts = conn.CreateCommand())
            {
                delFts.Transaction = tx;
                delFts.CommandText = """
                    DELETE FROM note_fts
                    WHERE note_item_id IN (SELECT id FROM note_items WHERE course_id = @c)
                    """;
                delFts.Parameters.AddWithValue("@c", courseId);
                await delFts.ExecuteNonQueryAsync(ct);
            }
            using (var delItems = conn.CreateCommand())
            {
                delItems.Transaction = tx;
                delItems.CommandText = "DELETE FROM note_items WHERE course_id = @c";
                delItems.Parameters.AddWithValue("@c", courseId);
                await delItems.ExecuteNonQueryAsync(ct);
            }

            tx.Commit();
        }, ct);
    }

    public async Task<int> GetIndexedCountAsync(string courseId, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM note_items WHERE course_id = @c AND ocr_state = 'indexed'";
        cmd.Parameters.AddWithValue("@c", courseId);
        return (int)(long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
    }

    private static void DeclareItemParameters(SqliteCommand cmd)
    {
        foreach (var name in new[]
        {
            "@id", "@course", "@weekId", "@weekLabel", "@pageId", "@pageName", "@headingPath",
            "@headingText", "@sourceType", "@url", "@blockId", "@cacheId", "@hash", "@raw",
            "@norm", "@conf", "@state", "@now"
        })
        {
            cmd.Parameters.Add(name, name is "@conf" ? SqliteType.Real : SqliteType.Text);
        }
    }

    private static void BindItemParameters(SqliteCommand cmd, IndexableNoteItem item, string now)
    {
        cmd.Parameters["@id"].Value = item.Id;
        cmd.Parameters["@course"].Value = item.CourseId;
        cmd.Parameters["@weekId"].Value = (object?)item.WeekId ?? DBNull.Value;
        cmd.Parameters["@weekLabel"].Value = (object?)item.WeekLabel ?? DBNull.Value;
        cmd.Parameters["@pageId"].Value = item.PageId;
        cmd.Parameters["@pageName"].Value = item.PageName;
        cmd.Parameters["@headingPath"].Value = item.HeadingPath ?? string.Empty;
        cmd.Parameters["@headingText"].Value = (object?)item.HeadingText ?? DBNull.Value;
        cmd.Parameters["@sourceType"].Value = item.SourceType;
        cmd.Parameters["@url"].Value = item.NotionPageUrl;
        cmd.Parameters["@blockId"].Value = (object?)item.NotionBlockId ?? DBNull.Value;
        cmd.Parameters["@cacheId"].Value = (object?)item.LocalCacheId ?? DBNull.Value;
        cmd.Parameters["@hash"].Value = (object?)item.ContentHash ?? DBNull.Value;
        cmd.Parameters["@raw"].Value = (object?)item.OcrRawText ?? DBNull.Value;
        cmd.Parameters["@norm"].Value = item.OcrNormalised ?? string.Empty;
        cmd.Parameters["@conf"].Value = item.OcrConfidence;
        cmd.Parameters["@state"].Value = item.OcrState;
        cmd.Parameters["@now"].Value = now;
    }

    /// <summary>
    /// Runs a write action against a single ReadWrite connection with foreign keys on and a busy
    /// timeout, retrying a bounded number of times on SQLITE_BUSY/LOCKED rather than freezing (spec §49).
    /// </summary>
    private async Task WithWriteConnectionAsync(Func<SqliteConnection, Task> action, CancellationToken ct)
    {
        const int maxAttempts = 5;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadWrite;Cache=Shared");
                await conn.OpenAsync(ct);
                using (var pragma = conn.CreateCommand())
                {
                    pragma.CommandText = "PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
                    await pragma.ExecuteNonQueryAsync(ct);
                }
                await action(conn);
                return;
            }
            catch (SqliteException ex) when (
                (ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6) && attempt < maxAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(50 * Math.Pow(2, attempt - 1));
                _logger.LogWarning("SQLite busy (attempt {Attempt}); retrying in {Ms}ms.",
                    attempt, delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
        }
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
