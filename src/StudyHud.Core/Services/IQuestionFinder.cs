namespace StudyHud.Core.Services;

/// <summary>
/// The Question Finder runtime (spec §38, §54, §57): captures a question, OCRs it locally, extracts
/// deterministic features, and searches the local index for the user's own relevant notes. It
/// NEVER attempts to answer or solve the question and never calls any generative/remote service
/// (spec §39, §66) — every step is local and deterministic.
/// </summary>
public interface IQuestionFinder
{
    /// <summary>OCRs the captured image, then searches. Returns the detected text plus ranked results.</summary>
    Task<QuestionResult> FindFromImageAsync(
        byte[] questionImage, string? courseId = null, string? weekId = null,
        int maxResults = 3, CancellationToken ct = default);

    /// <summary>
    /// Searches from already-available text — used to re-run after the user corrects low-confidence
    /// OCR (spec §59), skipping OCR entirely.
    /// </summary>
    Task<QuestionResult> FindFromTextAsync(
        string questionText, string? courseId = null, string? weekId = null,
        int maxResults = 3, CancellationToken ct = default);
}

/// <summary>
/// The outcome of a Question Finder run. Carries the detected/searched text and confidence so the UI
/// can show it and offer correction (spec §59), the extracted features for transparency (spec §56),
/// and the ranked matches.
/// </summary>
public record QuestionResult
{
    public required string OcrText { get; init; }
    public float OcrConfidence { get; init; } = 1.0f;
    public bool IsLowConfidence { get; init; }
    public required ExtractedFeatures Features { get; init; }
    public required IReadOnlyList<SearchResult> Results { get; init; }
}
