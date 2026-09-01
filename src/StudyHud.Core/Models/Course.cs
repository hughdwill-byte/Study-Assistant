namespace StudyHud.Core.Models;

/// <summary>
/// A study course that groups notes, macros, and layouts (spec §43, §77).
/// </summary>
public record Course
{
    public required string CourseId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? NotionRootPageId { get; init; }
    public string? PreferredMacroProfileId { get; init; }
    public WorkspaceId? PreferredWorkspace { get; init; }
    public string? PreferredLayoutId { get; init; }
    public bool AutoActivateOnSwitch { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSyncedAt { get; init; }
}

/// <summary>
/// Index health summary for a single course (spec §60, §98, §150).
/// </summary>
public record CourseIndexHealth
{
    public required string CourseId { get; init; }
    public int TotalWeeks { get; init; }
    public int TotalPages { get; init; }
    public int TotalImages { get; init; }
    public int IndexedImages { get; init; }
    public int LowConfidenceItems { get; init; }
    public int UnavailableSourceItems { get; init; }
    public DateTimeOffset? LastSuccessfulSync { get; init; }
    public IndexStatus Status { get; init; }
}

public enum IndexStatus
{
    Ready,
    Syncing,
    OcrProcessing,
    NeedsReview,
    PartiallyIndexed,
    Offline,
    NotSynced
}
