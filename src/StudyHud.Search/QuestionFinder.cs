using Microsoft.Extensions.Logging;
using StudyHud.Core.Services;

namespace StudyHud.Search;

/// <summary>
/// Implements <see cref="IQuestionFinder"/> (spec §38, §54): local OCR → deterministic feature
/// extraction → local FTS/BM25 search with explanations. It only ever locates the user's own notes;
/// it never answers the question and never touches a remote or generative service (spec §39, §71).
/// Local OCR and local search are permitted even in Assessment Mode (spec §41), so no policy gate is
/// needed here — the class simply has no code path that could make a prohibited call.
/// </summary>
public sealed class QuestionFinder : IQuestionFinder
{
    private const float LowConfidenceThreshold = 0.6f;

    private readonly IOcrService _ocr;
    private readonly ISearchIndex _index;
    private readonly ILogger<QuestionFinder> _logger;

    public QuestionFinder(IOcrService ocr, ISearchIndex index, ILogger<QuestionFinder> logger)
    {
        _ocr = ocr;
        _index = index;
        _logger = logger;
    }

    public async Task<QuestionResult> FindFromImageAsync(
        byte[] questionImage, string? courseId = null, string? weekId = null,
        int maxResults = 3, CancellationToken ct = default)
    {
        string text = string.Empty;
        float confidence = 0f;

        if (questionImage is { Length: > 0 } && _ocr.IsAvailable)
        {
            var ocr = await _ocr.RecogniseAsync(questionImage, ct).ConfigureAwait(false);
            text = ocr.NormalisedText;
            confidence = ocr.Confidence;
        }
        else
        {
            _logger.LogWarning("Question capture had no image or OCR is unavailable.");
        }

        return await SearchAsync(text, confidence, courseId, weekId, maxResults, ct).ConfigureAwait(false);
    }

    public Task<QuestionResult> FindFromTextAsync(
        string questionText, string? courseId = null, string? weekId = null,
        int maxResults = 3, CancellationToken ct = default)
        => SearchAsync(questionText ?? string.Empty, 1.0f, courseId, weekId, maxResults, ct);

    private async Task<QuestionResult> SearchAsync(
        string text, float confidence, string? courseId, string? weekId, int maxResults, CancellationToken ct)
    {
        var features = FeatureExtractor.Extract(text);

        IReadOnlyList<SearchResult> results = Array.Empty<SearchResult>();
        if (!string.IsNullOrWhiteSpace(text))
        {
            var query = new SearchQuery
            {
                RawText = text,
                Features = features,
                CourseId = courseId,
                WeekId = weekId,
                MaxResults = maxResults
            };
            results = await _index.SearchAsync(query, ct).ConfigureAwait(false);
        }

        _logger.LogDebug("Question Finder: {Count} results (confidence {Conf:P0}).", results.Count, confidence);

        return new QuestionResult
        {
            OcrText = text,
            OcrConfidence = confidence,
            IsLowConfidence = confidence < LowConfidenceThreshold,
            Features = features,
            Results = results
        };
    }
}
