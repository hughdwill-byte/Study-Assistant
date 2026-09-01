using Microsoft.Extensions.Logging;
using StudyHud.Core.Services;

namespace StudyHud.Core.Services;

/// <summary>
/// Enforces Study HUD network/AI restrictions during Assessment Mode (spec §41, §182, §183).
/// Every Study HUD component that makes outbound requests must check IsOperationAllowed()
/// before proceeding. This service NEVER disables Windows networking or blocks unrelated apps.
/// </summary>
public sealed class AssessmentPolicyService : IAssessmentPolicyService
{
    private readonly ILogger<AssessmentPolicyService> _logger;
    private volatile bool _assessmentActive;

    /// <summary>
    /// Operations that are completely blocked in Assessment Mode.
    /// </summary>
    private static readonly HashSet<PolicyOperation> _blockedInAssessment = new()
    {
        PolicyOperation.NotionSync,
        PolicyOperation.CapturedQuestionUpload,
        PolicyOperation.LlmRequest,
        PolicyOperation.EmbeddingRequest,
        PolicyOperation.CloudOcrRequest,
        PolicyOperation.WebSearch,
        PolicyOperation.UpdateCheck
    };

    private static readonly Dictionary<PolicyOperation, string> _blockReasons = new()
    {
        [PolicyOperation.NotionSync] = "Notion sync is disabled in Assessment Mode (local index only).",
        [PolicyOperation.CapturedQuestionUpload] = "Captured questions cannot be uploaded during Assessment Mode.",
        [PolicyOperation.LlmRequest] = "LLM/generative AI requests are blocked in Assessment Mode.",
        [PolicyOperation.EmbeddingRequest] = "Embedding/vector model requests are blocked in Assessment Mode.",
        [PolicyOperation.CloudOcrRequest] = "Cloud OCR is disabled in Assessment Mode (local OCR only).",
        [PolicyOperation.WebSearch] = "Web search is blocked in Assessment Mode.",
        [PolicyOperation.UpdateCheck] = "Update checks are blocked while Assessment Mode network policy is active."
    };

    public AssessmentPolicyService(ILogger<AssessmentPolicyService> logger)
    {
        _logger = logger;
    }

    public bool IsAssessmentModeActive => _assessmentActive;

    public event EventHandler<PolicyChangedEventArgs>? PolicyChanged;

    public void SetAssessmentMode(bool enabled)
    {
        _assessmentActive = enabled;
        _logger.LogInformation(
            "Assessment policy changed: active={Active}. Study HUD will {Block} prohibited operations. " +
            "Windows networking and unrelated applications are NOT affected.",
            enabled, enabled ? "block" : "allow");

        PolicyChanged?.Invoke(this, new PolicyChangedEventArgs { AssessmentModeActive = enabled });
    }

    public bool IsOperationAllowed(PolicyOperation operation)
    {
        if (!_assessmentActive) return true;
        bool allowed = !_blockedInAssessment.Contains(operation);
        if (!allowed)
            _logger.LogDebug("Operation {Op} blocked by Assessment Mode policy.", operation);
        return allowed;
    }

    public string GetBlockReason(PolicyOperation operation)
    {
        if (!_assessmentActive) return string.Empty;
        return _blockReasons.TryGetValue(operation, out var reason)
            ? reason
            : $"{operation} is not permitted during Assessment Mode.";
    }
}
