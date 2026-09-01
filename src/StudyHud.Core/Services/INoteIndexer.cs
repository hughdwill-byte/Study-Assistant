namespace StudyHud.Core.Services;

/// <summary>
/// The pre-indexing pipeline (spec §50): turns raw note sources (images or text blocks) into
/// searchable index rows by OCR'ing once, normalising, extracting deterministic features, and
/// writing them through <see cref="ISearchIndex"/>. Unchanged content is skipped by content hash
/// so expensive OCR is not repeated on every sync (spec §47).
///
/// Everything here is local and non-generative (spec §39, §71): OCR is a local engine, ranking is
/// deterministic, and nothing is uploaded.
/// </summary>
public interface INoteIndexer
{
    Task<IndexingSummary> IndexCourseSourcesAsync(
        string courseId,
        string courseName,
        IReadOnlyList<RawNoteSource> sources,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default);
}

/// <summary>
/// A single note source to be indexed. Carries either an image (to be OCR'd) or already-extracted
/// text, plus the metadata needed to locate it back in Notion (spec §48).
/// </summary>
public record RawNoteSource
{
    public required string Id { get; init; }
    public required string PageId { get; init; }
    public required string PageName { get; init; }
    public string HeadingPath { get; init; } = "";
    public string? HeadingText { get; init; }
    public string? WeekId { get; init; }
    public string? WeekLabel { get; init; }
    public required string NotionPageUrl { get; init; }
    public string? NotionBlockId { get; init; }
    public string? LocalCacheId { get; init; }

    /// <summary>Image bytes to OCR. Mutually exclusive with <see cref="Text"/>.</summary>
    public byte[]? ImageBytes { get; init; }

    /// <summary>Already-available text (e.g. a Notion text block). Used verbatim when set.</summary>
    public string? Text { get; init; }
}

/// <summary>Outcome counts for an indexing run (spec §60 library states).</summary>
public record IndexingSummary
{
    public int TotalSources { get; init; }
    public int Indexed { get; init; }
    public int Skipped { get; init; }
    public int LowConfidence { get; init; }
    public int Failed { get; init; }
}
