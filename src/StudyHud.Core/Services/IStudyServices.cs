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
