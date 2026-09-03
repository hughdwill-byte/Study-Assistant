using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Services;

namespace StudyHud.Search;

/// <summary>
/// Implements the pre-indexing pipeline (spec §50): NOTION IMAGE/TEXT → OCR once → normalise →
/// index. Skips unchanged content by hash so OCR is not repeated every sync (spec §47), flags
/// low-confidence and failed OCR for the library UI (spec §59, §60), and writes everything through
/// the single controlled writer as one batch (spec §49).
///
/// Local + non-generative throughout (spec §39, §71): a local OCR engine, deterministic ranking,
/// no uploads. OCR runs sequentially (concurrency = 1), within the §68 cap for background work.
/// </summary>
public sealed class NoteIndexer : INoteIndexer
{
    private const float LowConfidenceThreshold = 0.6f;

    private readonly IOcrService _ocr;
    private readonly ISearchIndex _index;
    private readonly ILogger<NoteIndexer> _logger;

    public NoteIndexer(IOcrService ocr, ISearchIndex index, ILogger<NoteIndexer> logger)
    {
        _ocr = ocr;
        _index = index;
        _logger = logger;
    }

    public async Task<IndexingSummary> IndexCourseSourcesAsync(
        string courseId,
        string courseName,
        IReadOnlyList<RawNoteSource> sources,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default)
    {
        await _index.UpsertCourseAsync(courseId, courseName, ct).ConfigureAwait(false);

        // Existing hashes let us skip content that has not changed since the last index (spec §47).
        var existing = await _index.GetContentHashesAsync(courseId, ct).ConfigureAwait(false);

        var toWrite = new List<IndexableNoteItem>();
        int indexed = 0, skipped = 0, lowConfidence = 0, failed = 0, done = 0;

        foreach (var src in sources)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var hash = ComputeHash(src);
                if (existing.TryGetValue(src.Id, out var prev) && prev == hash)
                {
                    skipped++;
                    Report(progress, "Skipping unchanged", ++done, sources.Count);
                    continue;
                }

                var (rawText, normalised, confidence) = await ExtractAsync(src, ct).ConfigureAwait(false);

                string state;
                if (string.IsNullOrWhiteSpace(normalised)) { state = "failed"; failed++; }
                else if (confidence < LowConfidenceThreshold) { state = "low_confidence"; lowConfidence++; }
                else { state = "indexed"; indexed++; }

                toWrite.Add(new IndexableNoteItem
                {
                    Id = src.Id,
                    CourseId = courseId,
                    WeekId = src.WeekId,
                    WeekLabel = src.WeekLabel,
                    PageId = src.PageId,
                    PageName = src.PageName,
                    HeadingPath = src.HeadingPath,
                    HeadingText = src.HeadingText,
                    SourceType = src.ImageBytes is not null ? "image" : "text",
                    NotionPageUrl = src.NotionPageUrl,
                    NotionBlockId = src.NotionBlockId,
                    LocalCacheId = src.LocalCacheId,
                    ContentHash = hash,
                    OcrRawText = rawText,
                    OcrNormalised = normalised,
                    OcrConfidence = confidence,
                    OcrState = state
                });

                Report(progress, "Indexing", ++done, sources.Count);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex, "Failed to index note source {Id}.", src.Id);
                Report(progress, "Indexing", ++done, sources.Count);
            }
        }

        if (toWrite.Count > 0)
            await _index.IndexItemsAsync(toWrite, ct).ConfigureAwait(false);

        var summary = new IndexingSummary
        {
            TotalSources = sources.Count,
            Indexed = indexed,
            Skipped = skipped,
            LowConfidence = lowConfidence,
            Failed = failed
        };
        _logger.LogInformation(
            "Indexed course {Course}: {Indexed} indexed, {Low} low-confidence, {Skipped} skipped, {Failed} failed.",
            courseId, indexed, lowConfidence, skipped, failed);
        return summary;
    }

    private async Task<(string raw, string normalised, float confidence)> ExtractAsync(
        RawNoteSource src, CancellationToken ct)
    {
        // A text block (e.g. plain Notion text) needs no OCR — it is already text.
        if (src.Text is not null)
            return (src.Text, src.Text, 1.0f);

        if (src.ImageBytes is { Length: > 0 } bytes && _ocr.IsAvailable)
        {
            var result = await _ocr.RecogniseAsync(bytes, ct).ConfigureAwait(false);
            return (result.RawText, result.NormalisedText, result.Confidence);
        }

        // No text and no usable image / OCR engine → treated as a failed source (spec §59).
        return (string.Empty, string.Empty, 0f);
    }

    private static string ComputeHash(RawNoteSource src)
    {
        var bytes = src.ImageBytes ?? Encoding.UTF8.GetBytes(src.Text ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static void Report(IProgress<SyncProgress>? progress, string phase, int done, int total)
        => progress?.Report(new SyncProgress { Phase = phase, CompletedItems = done, TotalItems = total });
}
