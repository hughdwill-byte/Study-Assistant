using StudyHud.Core.Models;
using System.Drawing;

namespace StudyHud.Core.Services;

// ─── OCR ────────────────────────────────────────────────────────────────────

/// <summary>
/// Converts an image to text locally (spec §40, §82).
/// Implementations may use Windows.Media.Ocr or Tesseract.
/// </summary>
public interface IOcrService
{
    /// <summary>True if this OCR engine is available on the current machine.</summary>
    bool IsAvailable { get; }

    string EngineName { get; }

    /// <summary>
    /// Performs OCR on the given image bytes (PNG/BMP).
    /// Returns raw text and confidence (0–1). Never calls generative AI.
    /// </summary>
    Task<OcrResult> RecogniseAsync(byte[] imageBytes, CancellationToken ct = default);
}

public record OcrResult
{
    public required string RawText { get; init; }
    public required string NormalisedText { get; init; }
    public required float Confidence { get; init; }
    public required IReadOnlyList<OcrWord> Words { get; init; }
    public bool IsLowConfidence => Confidence < 0.6f;
    public string EngineName { get; init; } = string.Empty;
}

public record OcrWord
{
    public required string Text { get; init; }
    public required float Confidence { get; init; }
    public required ScreenRect BoundingBox { get; init; }
}

// ─── SEARCH ─────────────────────────────────────────────────────────────────

/// <summary>
/// Deterministic local note search (spec §54, §82).
/// No generative AI, embeddings, or remote services.
/// </summary>
public interface ISearchIndex
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        SearchQuery query, CancellationToken ct = default);

    Task<bool> IsReadyAsync(string courseId, CancellationToken ct = default);

    // ── Write path (spec §49, §50) ───────────────────────────────────────────

    /// <summary>
    /// Inserts or replaces note items and their full-text rows through the single controlled
    /// writer, batched in one transaction (spec §49). Safe to call repeatedly; an item with an
    /// existing id is replaced. Never held open across OCR/network work — callers pass already
    /// OCR'd items.
    /// </summary>
    Task IndexItemsAsync(IReadOnlyList<IndexableNoteItem> items, CancellationToken ct = default);

    /// <summary>Ensures a course row exists so note items can reference it (FK target).</summary>
    Task UpsertCourseAsync(string courseId, string courseName, CancellationToken ct = default);

    /// <summary>
    /// Returns note-item id → content hash for a course, so a sync layer can skip unchanged
    /// content and only re-OCR/re-index what changed (spec §47).
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetContentHashesAsync(
        string courseId, CancellationToken ct = default);

    /// <summary>Removes all note items (and their full-text rows) for a course.</summary>
    Task DeleteCourseAsync(string courseId, CancellationToken ct = default);

    /// <summary>Count of successfully indexed items for a course.</summary>
    Task<int> GetIndexedCountAsync(string courseId, CancellationToken ct = default);
}

/// <summary>
/// A note item ready to be written into the local index (spec §48). Produced by the sync/OCR
/// pipeline after an image or text block has been OCR'd and its features normalised.
/// </summary>
public record IndexableNoteItem
{
    public required string Id { get; init; }
    public required string CourseId { get; init; }
    public string? WeekId { get; init; }
    public string? WeekLabel { get; init; }
    public required string PageId { get; init; }
    public required string PageName { get; init; }
    public string HeadingPath { get; init; } = "";
    public string? HeadingText { get; init; }

    /// <summary>'image' or 'text'.</summary>
    public string SourceType { get; init; } = "image";

    public required string NotionPageUrl { get; init; }
    public string? NotionBlockId { get; init; }
    public string? LocalCacheId { get; init; }

    /// <summary>Hash of the source content (image bytes / text) for incremental sync (spec §47).</summary>
    public string? ContentHash { get; init; }

    public string? OcrRawText { get; init; }

    /// <summary>Normalised, searchable text (spec §53). This is what FTS indexes.</summary>
    public required string OcrNormalised { get; init; }

    public float OcrConfidence { get; init; } = 1.0f;

    /// <summary>'indexed' | 'pending' | 'failed' | 'low_confidence' (spec §60).</summary>
    public string OcrState { get; init; } = "indexed";
}

public record SearchQuery
{
    public required string RawText { get; init; }
    public required ExtractedFeatures Features { get; init; }
    public string? CourseId { get; init; }
    public string? WeekId { get; init; }
    public int MaxResults { get; init; } = 10;
}

public record ExtractedFeatures
{
    public required IReadOnlyList<string> Words { get; init; }
    public required IReadOnlyList<string> Variables { get; init; }
    public required IReadOnlyList<string> Numbers { get; init; }
    public required IReadOnlyList<string> Units { get; init; }
    public required IReadOnlyList<string> Expressions { get; init; }
    public required IReadOnlyList<string> Symbols { get; init; }
}

public record SearchResult
{
    public required string NoteItemId { get; init; }
    public required string CourseId { get; init; }
    public required string CourseName { get; init; }
    public required string? WeekLabel { get; init; }
    public required string PageName { get; init; }
    public required string HeadingPath { get; init; }
    public required string NotionPageUrl { get; init; }
    public required string? NotionBlockId { get; init; }

    /// <summary>Deterministic score 0–100. Never call this "AI confidence".</summary>
    public required double MatchScore { get; init; }

    /// <summary>Matched terms, variables, expressions etc. for display (spec §56).</summary>
    public required IReadOnlyList<MatchExplanation> Explanations { get; init; }
}

public record MatchExplanation
{
    public required MatchType Type { get; init; }
    public required string Value { get; init; }
}

public enum MatchType { Word, Variable, Symbol, Expression, Heading, PhraseBonuse }

// ─── NOTE SOURCE ────────────────────────────────────────────────────────────

/// <summary>
/// Abstracts note synchronisation (Notion or future sources) (spec §82).
/// </summary>
public interface INoteSource
{
    string SourceName { get; }
    bool IsConnected { get; }

    Task<bool> TestConnectionAsync(CancellationToken ct = default);
    Task SyncCourseAsync(string courseId, IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>Securely stores the integration token (spec §46). Never logged.</summary>
    Task StoreTokenAsync(string token, CancellationToken ct = default);

    /// <summary>True if a token has been stored (used to show connection status in the UI).</summary>
    Task<bool> HasStoredTokenAsync(CancellationToken ct = default);

    /// <summary>
    /// Lists the pages shared with the integration so the user can add one as a course without
    /// hunting for its page id (spec §43). Returns an empty list when blocked by policy or when no
    /// token is stored. Never uploads anything — it only reads the page list.
    /// </summary>
    Task<IReadOnlyList<DiscoveredPage>> DiscoverPagesAsync(CancellationToken ct = default);
}

/// <summary>A page discovered via the note source, offered to the user as a candidate course.</summary>
public record DiscoveredPage
{
    public required string Id { get; init; }
    public required string Title { get; init; }
}

public record SyncProgress
{
    public required string Phase { get; init; }
    public int CompletedItems { get; init; }
    public int TotalItems { get; init; }
    public double PercentComplete => TotalItems > 0 ? (double)CompletedItems / TotalItems * 100 : 0;
}

// ─── CAPTURE ────────────────────────────────────────────────────────────────

/// <summary>
/// Region capture service (spec §31–34, §82).
/// </summary>
public interface ICaptureService
{
    /// <summary>
    /// Begins a one-gesture capture (spec §139).
    /// Activates the overlay; resolves when the user releases or cancels.
    /// </summary>
    Task<CaptureResult?> CaptureRegionAsync(CancellationToken ct = default);
}

public record CaptureResult
{
    public required byte[] ImageBytes { get; init; }
    public required ScreenRect PhysicalRect { get; init; }
    public required string MonitorId { get; init; }
    public bool WasCancelled { get; init; }
}

// ─── CREDENTIAL STORE ───────────────────────────────────────────────────────

/// <summary>
/// Secure credential storage using DPAPI/Windows Credential Manager (spec §46).
/// </summary>
public interface ICredentialStore
{
    Task StoreAsync(string key, string secret, CancellationToken ct = default);
    Task<string?> RetrieveAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}

// ─── THEME ──────────────────────────────────────────────────────────────────

/// <summary>
/// Manages theme token resolution and application (spec §62, §82).
/// </summary>
public interface IThemeService
{
    event EventHandler ThemeChanged;

    object? GetResource(string tokenKey);
    void ApplyTheme(string themeId);
    IReadOnlyList<string> AvailableThemeIds { get; }
    string CurrentThemeId { get; }

    /// <summary>Applies a custom accent colour with contrast protection (spec §64, §158).</summary>
    void ApplyAccentColour(Color accent);
}
