using StudyHud.Core.Models;

namespace StudyHud.Core.Services;

/// <summary>
/// Reads a single Notion page's content for display on the Cheat Sheet HUD panel (a read-only view of
/// the user's own notes — no generative AI, nothing uploaded). Distinct from <see cref="INoteSource"/>,
/// which syncs whole courses into the search index; this fetches one page on demand and preserves its
/// formatting and images for faithful display.
/// </summary>
public interface INotionPageReader
{
    /// <summary>Lists the pages shared with the integration, for the Cheat Sheet page picker.</summary>
    Task<IReadOnlyList<DiscoveredPage>> ListPagesAsync(CancellationToken ct = default);

    /// <summary>
    /// Loads one Notion page and returns it as a formatting-preserving <see cref="CheatSheetDocument"/>
    /// (headings, lists, quotes, callouts, code and images). Returns null when there is no stored token
    /// or the operation is blocked by policy (e.g. Assessment Mode).
    /// </summary>
    Task<CheatSheetDocument?> LoadPageAsync(string pageId, CancellationToken ct = default);
}
