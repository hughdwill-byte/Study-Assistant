using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Services;

namespace StudyHud.Notion;

/// <summary>
/// Connects to the Notion API and incrementally syncs note content (spec §45, §47, §148).
/// Never sends captured question text or note content to generative AI.
/// Downloads images promptly before URLs expire.
/// Assessment Mode blocks all sync operations via IAssessmentPolicyService.
/// </summary>
public sealed class NotionConnector : INoteSource
{
    private readonly ICredentialStore _credentials;
    private readonly IAssessmentPolicyService _policy;
    private readonly INoteIndexer _indexer;
    private readonly ICourseRepository _courses;
    private readonly ILogger<NotionConnector> _logger;
    private readonly HttpClient _http;

    private const string CredentialKey = "StudyHud.NotionToken";
    private const string NotionApiBase = "https://api.notion.com/v1";
    private const string NotionVersion = "2022-06-28";

    public string SourceName => "Notion";
    public bool IsConnected { get; private set; }

    public NotionConnector(
        ICredentialStore credentials,
        IAssessmentPolicyService policy,
        INoteIndexer indexer,
        ICourseRepository courses,
        ILogger<NotionConnector> logger)
    {
        _credentials = credentials;
        _policy = policy;
        _indexer = indexer;
        _courses = courses;
        _logger = logger;

        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("Notion-Version", NotionVersion);
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    // ── Connection ──────────────────────────────────────────────────────────

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        if (!_policy.IsOperationAllowed(PolicyOperation.NotionSync))
        {
            _logger.LogInformation("Notion connection blocked: {Reason}",
                _policy.GetBlockReason(PolicyOperation.NotionSync));
            return false;
        }

        var token = await _credentials.RetrieveAsync(CredentialKey, ct);
        if (string.IsNullOrEmpty(token)) return false;

        ConfigureAuth(token);

        try
        {
            var resp = await _http.GetAsync($"{NotionApiBase}/users/me", ct);
            IsConnected = resp.IsSuccessStatusCode;
            return IsConnected;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notion connection test failed.");
            IsConnected = false;
            return false;
        }
    }

    public async Task StoreTokenAsync(string token, CancellationToken ct = default)
    {
        // Never log the token (spec §46)
        await _credentials.StoreAsync(CredentialKey, token, ct);
        _logger.LogInformation("Notion token stored securely.");
    }

    public async Task<bool> HasStoredTokenAsync(CancellationToken ct = default)
        => !string.IsNullOrEmpty(await _credentials.RetrieveAsync(CredentialKey, ct));

    // ── Sync ────────────────────────────────────────────────────────────────

    public async Task SyncCourseAsync(
        string courseId,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!_policy.IsOperationAllowed(PolicyOperation.NotionSync))
        {
            _logger.LogInformation("Notion sync blocked by policy: {Reason}",
                _policy.GetBlockReason(PolicyOperation.NotionSync));
            return;
        }

        var token = await _credentials.RetrieveAsync(CredentialKey, ct);
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("No Notion token configured.");
            return;
        }

        ConfigureAuth(token);
        _logger.LogInformation("Starting Notion sync for course {CourseId}.", courseId);

        try
        {
            await SyncCourseInternalAsync(courseId, progress, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Notion authentication failed (401). Check your integration token.");
            IsConnected = false;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Notion rate limit (429). Sync will resume at next scheduled attempt.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Notion sync failed for course {CourseId}.", courseId);
        }
    }

    private async Task SyncCourseInternalAsync(
        string courseId, IProgress<SyncProgress>? progress, CancellationToken ct)
    {
        // Resolve the course's configured Notion root page (spec §45). The root page id is set when
        // the user configures the course; without it there is nothing to sync.
        var course = await _courses.GetAsync(courseId, ct).ConfigureAwait(false);
        if (course is null || string.IsNullOrWhiteSpace(course.NotionRootPageId))
        {
            _logger.LogWarning(
                "Course {CourseId} has no Notion root page configured; nothing to sync.", courseId);
            return;
        }

        await SyncNotionPageAsync(courseId, course.Name, course.NotionRootPageId!, progress, ct)
            .ConfigureAwait(false);
        await _courses.SetLastSyncedAsync(courseId, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Syncs a single Notion page into the local index (spec §45, §50): fetches its block children,
    /// parses headings/images/text, downloads image bytes promptly (URLs expire), and hands the
    /// result to the pre-indexing pipeline. Only images are downloaded — never uploaded anywhere.
    /// </summary>
    public async Task SyncNotionPageAsync(
        string courseId, string courseName, string notionPageId,
        IProgress<SyncProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new SyncProgress { Phase = "Reading Notion page", CompletedItems = 0, TotalItems = 0 });

        var blocks = await FetchBlockChildrenAsync(notionPageId, ct).ConfigureAwait(false);
        var parsed = NotionBlockParser.ParsePage(blocks);

        var pageUrl = $"https://www.notion.so/{notionPageId.Replace("-", "")}";
        var sources = new List<RawNoteSource>();

        foreach (var block in parsed)
        {
            ct.ThrowIfCancellationRequested();
            byte[]? imageBytes = null;
            if (block.IsImage)
            {
                imageBytes = await TryDownloadAsync(block.ImageUrl!, ct).ConfigureAwait(false);
                if (imageBytes is null) continue; // expired/unavailable image — skip (spec §69)
            }

            sources.Add(new RawNoteSource
            {
                Id = $"{notionPageId}:{block.BlockId}",
                PageId = notionPageId,
                PageName = courseName,
                HeadingPath = block.HeadingPath,
                HeadingText = block.HeadingText,
                NotionPageUrl = pageUrl,
                NotionBlockId = block.BlockId,
                ImageBytes = imageBytes,
                Text = block.IsImage ? null : block.Text
            });
        }

        await _indexer.IndexCourseSourcesAsync(courseId, courseName, sources, progress, ct)
            .ConfigureAwait(false);

        _logger.LogInformation("Synced Notion page {Page}: {Count} note sources.", notionPageId, sources.Count);
    }

    /// <summary>Fetches all block children of a page, following pagination (spec §45 pagination).</summary>
    private async Task<List<JsonElement>> FetchBlockChildrenAsync(string pageId, CancellationToken ct)
    {
        var results = new List<JsonElement>();
        string? cursor = null;

        do
        {
            var path = $"blocks/{pageId}/children?page_size=100"
                     + (cursor is null ? "" : $"&start_cursor={Uri.EscapeDataString(cursor)}");
            var page = await GetAsync(path, ct).ConfigureAwait(false);
            if (page is null) break;

            if (page.Value.TryGetProperty("results", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var el in arr.EnumerateArray())
                    results.Add(el.Clone()); // detach from the document we are about to dispose

            cursor = page.Value.TryGetProperty("has_more", out var hasMore) && hasMore.ValueKind == JsonValueKind.True
                && page.Value.TryGetProperty("next_cursor", out var nc) && nc.ValueKind == JsonValueKind.String
                ? nc.GetString()
                : null;
        }
        while (cursor is not null);

        return results;
    }

    private async Task<byte[]?> TryDownloadAsync(string url, CancellationToken ct)
    {
        try
        {
            return await _http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download Notion image (URL may have expired).");
            return null;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void ConfigureAuth(string token)
    {
        // Remove existing auth header before setting new one
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<JsonElement?> GetAsync(string path, CancellationToken ct)
    {
        var resp = await _http.GetAsync($"{NotionApiBase}/{path}", ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}
