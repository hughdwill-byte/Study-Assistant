using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Models;
using StudyHud.Core.Services;

namespace StudyHud.Notion;

/// <summary>
/// Connects to the Notion API and incrementally syncs note content (spec §45, §47, §148).
/// Never sends captured question text or note content to generative AI.
/// Downloads images promptly before URLs expire.
/// Assessment Mode blocks all sync operations via IAssessmentPolicyService.
/// </summary>
public sealed class NotionConnector : INoteSource, INotionPageReader
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

    // ── Discovery ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Lists pages shared with the integration via the Notion search endpoint (spec §43), following
    /// pagination. Only page ids and titles are read — nothing is uploaded.
    /// </summary>
    public async Task<IReadOnlyList<DiscoveredPage>> DiscoverPagesAsync(CancellationToken ct = default)
    {
        if (!_policy.IsOperationAllowed(PolicyOperation.NotionSync))
        {
            _logger.LogInformation("Notion discovery blocked: {Reason}",
                _policy.GetBlockReason(PolicyOperation.NotionSync));
            return Array.Empty<DiscoveredPage>();
        }

        var token = await _credentials.RetrieveAsync(CredentialKey, ct);
        if (string.IsNullOrEmpty(token)) return Array.Empty<DiscoveredPage>();
        ConfigureAuth(token);

        var pages = new List<DiscoveredPage>();
        string? cursor = null;

        do
        {
            var body = new Dictionary<string, object?>
            {
                ["filter"] = new { value = "page", property = "object" },
                ["page_size"] = 100
            };
            if (cursor is not null) body["start_cursor"] = cursor;

            var page = await PostAsync("search", body, ct).ConfigureAwait(false);
            if (page is null) break;

            if (page.Value.TryGetProperty("results", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    if (el.TryGetProperty("object", out var ob) && ob.GetString() != "page") continue;
                    if (el.TryGetProperty("archived", out var ar) && ar.ValueKind == JsonValueKind.True) continue;
                    var id = el.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    if (string.IsNullOrEmpty(id)) continue;
                    pages.Add(new DiscoveredPage { Id = id!, Title = ExtractPageTitle(el) });
                }
            }

            cursor = page.Value.TryGetProperty("has_more", out var hm) && hm.ValueKind == JsonValueKind.True
                && page.Value.TryGetProperty("next_cursor", out var nc) && nc.ValueKind == JsonValueKind.String
                ? nc.GetString()
                : null;
        }
        while (cursor is not null);

        _logger.LogInformation("Notion discovery found {Count} shared page(s).", pages.Count);
        return pages;
    }

    private static string ExtractPageTitle(JsonElement page)
    {
        if (page.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in props.EnumerateObject())
            {
                if (prop.Value.TryGetProperty("type", out var tp) && tp.GetString() == "title"
                    && prop.Value.TryGetProperty("title", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var t in arr.EnumerateArray())
                        if (t.TryGetProperty("plain_text", out var txt))
                            sb.Append(txt.GetString());
                    var s = sb.ToString().Trim();
                    if (s.Length > 0) return s;
                }
            }
        }
        return "Untitled";
    }

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

    /// <summary>Maximum sub-page nesting depth to follow, as a safety bound against cycles/runaways.</summary>
    private const int MaxPageDepth = 8;

    /// <summary>
    /// Syncs a Notion page AND every sub-page beneath it into the local index (spec §45, §50).
    /// The user configures one root page per course; this walks the whole tree so nested weeks and
    /// pages are indexed automatically without adding each one by hand. Direct child pages of the
    /// root are treated as "weeks" for labelling. Only images are downloaded — never uploaded.
    /// </summary>
    public async Task SyncNotionPageAsync(
        string courseId, string courseName, string notionPageId,
        IProgress<SyncProgress>? progress, CancellationToken ct)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int pageCount = await SyncPageRecursiveAsync(
            courseId, courseName, notionPageId,
            pageName: courseName, weekLabel: null, depth: 0, visited, progress, ct)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Synced course {CourseId}: walked {Count} Notion page(s).", courseId, pageCount);
    }

    /// <summary>
    /// Indexes one page's content (including content nested in toggles/columns) and then recurses
    /// into its sub-pages. Returns the number of pages walked.
    /// </summary>
    private async Task<int> SyncPageRecursiveAsync(
        string courseId, string courseName, string notionPageId, string pageName,
        string? weekLabel, int depth, HashSet<string> visited,
        IProgress<SyncProgress>? progress, CancellationToken ct)
    {
        var key = notionPageId.Replace("-", "");
        if (depth > MaxPageDepth || !visited.Add(key))
            return 0;

        ct.ThrowIfCancellationRequested();
        progress?.Report(new SyncProgress { Phase = $"Reading “{pageName}”", CompletedItems = 0, TotalItems = 0 });

        // Gather this page's own content blocks (flattening layout containers) and its sub-pages.
        var content = new List<JsonElement>();
        var childPages = new List<(string Id, string Title)>();
        await GatherPageContentAsync(notionPageId, content, childPages, depth, ct).ConfigureAwait(false);

        var parsed = NotionBlockParser.ParsePage(content);
        var pageUrl = $"https://www.notion.so/{key}";
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
                PageName = pageName,
                HeadingPath = block.HeadingPath,
                HeadingText = block.HeadingText,
                WeekLabel = weekLabel,
                NotionPageUrl = pageUrl,
                NotionBlockId = block.BlockId,
                ImageBytes = imageBytes,
                Text = block.IsImage ? null : block.Text
            });
        }

        if (sources.Count > 0)
            await _indexer.IndexCourseSourcesAsync(courseId, courseName, sources, progress, ct)
                .ConfigureAwait(false);

        int walked = 1;
        foreach (var (childId, childTitle) in childPages)
        {
            ct.ThrowIfCancellationRequested();
            // Direct children of the course root are the "weeks"; deeper pages inherit that label.
            var childWeek = depth == 0 ? childTitle : weekLabel;
            try
            {
                walked += await SyncPageRecursiveAsync(
                    courseId, courseName, childId, childTitle, childWeek, depth + 1, visited, progress, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One bad sub-page must not abort the rest of the course (spec §69).
                _logger.LogWarning(ex, "Sub-page {Id} (“{Title}”) failed to sync; continuing.",
                    childId, childTitle);
            }
        }

        return walked;
    }

    /// <summary>
    /// Collects a page's descendant content blocks into <paramref name="content"/> (flattening
    /// containers like toggles and columns so nested images/text aren't missed), while recording any
    /// child pages it encounters into <paramref name="childPages"/> for separate sub-page syncing.
    /// </summary>
    private async Task GatherPageContentAsync(
        string blockId, List<JsonElement> content,
        List<(string Id, string Title)> childPages, int depth, CancellationToken ct)
    {
        var children = await FetchBlockChildrenAsync(blockId, ct).ConfigureAwait(false);

        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();
            var type = child.TryGetProperty("type", out var t) ? t.GetString() : null;

            if (type == "child_page")
            {
                var id = child.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var title = child.TryGetProperty("child_page", out var cp)
                    && cp.TryGetProperty("title", out var ti) ? ti.GetString() : null;
                if (!string.IsNullOrEmpty(id))
                    childPages.Add((id!, string.IsNullOrWhiteSpace(title) ? "Untitled" : title!));
                continue; // handled as its own page — don't flatten its content into this page
            }

            content.Add(child);

            // Recurse into layout/list containers so their nested content is indexed too.
            bool hasChildren = child.TryGetProperty("has_children", out var hc)
                               && hc.ValueKind == JsonValueKind.True;
            if (hasChildren && depth <= MaxPageDepth)
            {
                var id = child.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (!string.IsNullOrEmpty(id))
                    await GatherPageContentAsync(id!, content, childPages, depth + 1, ct)
                        .ConfigureAwait(false);
            }
        }
    }

    /// <summary>Fetches all block children of a page/block, following pagination (spec §45).</summary>
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

    // ── Cheat Sheet: read one page for display (INotionPageReader) ────────────

    public Task<IReadOnlyList<DiscoveredPage>> ListPagesAsync(CancellationToken ct = default)
        => DiscoverPagesAsync(ct);

    /// <summary>
    /// Loads a single page and preserves its formatting (headings, lists, quotes, callouts, code,
    /// inline styles) and images, so the Cheat Sheet panel can show just the notes. Read-only — nothing
    /// is uploaded. Gated by the same policy as sync, so it is blocked in Assessment Mode.
    /// </summary>
    public async Task<CheatSheetDocument?> LoadPageAsync(string pageId, CancellationToken ct = default)
    {
        if (!_policy.IsOperationAllowed(PolicyOperation.NotionSync))
        {
            _logger.LogInformation("Cheat Sheet load blocked: {Reason}",
                _policy.GetBlockReason(PolicyOperation.NotionSync));
            return null;
        }

        var token = await _credentials.RetrieveAsync(CredentialKey, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token)) return null;
        ConfigureAuth(token);

        string title = "Notion Page";
        var meta = await GetAsync($"pages/{pageId}", ct).ConfigureAwait(false);
        if (meta is not null) title = ExtractPageTitle(meta.Value);

        var blocks = new List<CheatBlock>();
        await RenderChildrenAsync(pageId, blocks, indent: 0, depth: 0, ct).ConfigureAwait(false);

        _logger.LogInformation("Cheat Sheet loaded page “{Title}” ({Count} block(s)).", title, blocks.Count);
        return new CheatSheetDocument { Title = title, Blocks = blocks };
    }

    /// <summary>Walks a page/block's children in order, converting each to a rendered <see cref="CheatBlock"/>.</summary>
    private async Task RenderChildrenAsync(
        string blockId, List<CheatBlock> outp, int indent, int depth, CancellationToken ct)
    {
        if (depth > MaxPageDepth) return;
        var children = await FetchBlockChildrenAsync(blockId, ct).ConfigureAwait(false);

        int number = 0;
        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();
            var type = child.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() : null;
            if (type is null) continue;

            // Numbered-list counter runs over consecutive siblings; any other block resets it.
            if (type == "numbered_list_item") number++; else number = 0;

            switch (type)
            {
                case "heading_1": AddText(outp, child, type, CheatBlockKind.Heading1, indent); break;
                case "heading_2": AddText(outp, child, type, CheatBlockKind.Heading2, indent); break;
                case "heading_3": AddText(outp, child, type, CheatBlockKind.Heading3, indent); break;
                case "paragraph": AddText(outp, child, type, CheatBlockKind.Paragraph, indent); break;
                case "bulleted_list_item": AddText(outp, child, type, CheatBlockKind.Bulleted, indent); break;
                case "numbered_list_item": AddText(outp, child, type, CheatBlockKind.Numbered, indent, number: number); break;
                case "toggle": AddText(outp, child, type, CheatBlockKind.Paragraph, indent); break;
                case "quote": AddText(outp, child, type, CheatBlockKind.Quote, indent); break;
                case "code": AddText(outp, child, type, CheatBlockKind.Code, indent); break;
                case "to_do":
                    AddText(outp, child, type, CheatBlockKind.ToDo, indent, isChecked: IsChecked(child));
                    break;
                case "callout":
                    AddText(outp, child, type, CheatBlockKind.Callout, indent, emoji: CalloutEmoji(child));
                    break;
                case "divider":
                    outp.Add(new CheatBlock { Kind = CheatBlockKind.Divider, IndentLevel = indent });
                    break;
                case "image":
                    var url = ImageUrlOfBlock(child);
                    if (!string.IsNullOrEmpty(url))
                    {
                        var bytes = await TryDownloadAsync(url!, ct).ConfigureAwait(false);
                        if (bytes is not null)
                            outp.Add(new CheatBlock
                            {
                                Kind = CheatBlockKind.Image, IndentLevel = indent,
                                ImageBytes = bytes, ImageCaption = CaptionOf(child, "image")
                            });
                    }
                    break;
                case "child_page":
                    var childTitle = child.TryGetProperty("child_page", out var cp)
                        && cp.TryGetProperty("title", out var ti) ? ti.GetString() : null;
                    outp.Add(new CheatBlock
                    {
                        Kind = CheatBlockKind.ChildPageLink, IndentLevel = indent,
                        Inlines = new[] { new CheatInline { Text = string.IsNullOrWhiteSpace(childTitle) ? "Untitled" : childTitle! } }
                    });
                    break;
                default:
                    // Unknown block that still carries rich text (e.g. a new list variant) — render as text.
                    AddText(outp, child, type, CheatBlockKind.Paragraph, indent);
                    break;
            }

            // Recurse into containers (toggles, nested lists, callouts, columns) — but never into a
            // child_page (it is a separate page, shown here only as a link).
            bool hasChildren = child.TryGetProperty("has_children", out var hc)
                               && hc.ValueKind == JsonValueKind.True;
            if (hasChildren && type != "child_page")
            {
                var id = child.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                // Column containers lay out side-by-side; stacking them vertically shouldn't add indent.
                int childIndent = type is "column_list" or "column" ? indent : indent + 1;
                if (!string.IsNullOrEmpty(id))
                    await RenderChildrenAsync(id!, outp, childIndent, depth + 1, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Adds a text-bearing block, skipping empties (except headings/callouts, which may anchor structure).</summary>
    private static void AddText(
        List<CheatBlock> outp, JsonElement block, string type, CheatBlockKind kind, int indent,
        int? number = null, bool isChecked = false, string? emoji = null)
    {
        var inlines = RichInlinesOf(block, type);
        if (inlines.Count == 0
            && kind is not (CheatBlockKind.Heading1 or CheatBlockKind.Heading2 or CheatBlockKind.Heading3 or CheatBlockKind.Callout))
            return;

        outp.Add(new CheatBlock
        {
            Kind = kind, IndentLevel = indent, Inlines = inlines,
            Number = number, Checked = isChecked, Emoji = emoji
        });
    }

    /// <summary>Parses a block's <c>rich_text</c> array into styled <see cref="CheatInline"/> runs.</summary>
    private static List<CheatInline> RichInlinesOf(JsonElement block, string type)
    {
        var inlines = new List<CheatInline>();
        if (!block.TryGetProperty(type, out var body) || body.ValueKind != JsonValueKind.Object)
            return inlines;
        if (!body.TryGetProperty("rich_text", out var rt) || rt.ValueKind != JsonValueKind.Array)
            return inlines;

        foreach (var span in rt.EnumerateArray())
        {
            var text = span.TryGetProperty("plain_text", out var pt) && pt.ValueKind == JsonValueKind.String
                ? pt.GetString() ?? "" : "";
            if (text.Length == 0) continue;

            bool bold = false, italic = false, underline = false, strike = false, code = false;
            string? color = null;
            if (span.TryGetProperty("annotations", out var an) && an.ValueKind == JsonValueKind.Object)
            {
                bold = IsTrue(an, "bold");
                italic = IsTrue(an, "italic");
                underline = IsTrue(an, "underline");
                strike = IsTrue(an, "strikethrough");
                code = IsTrue(an, "code");
                if (an.TryGetProperty("color", out var col) && col.ValueKind == JsonValueKind.String)
                    color = MapColor(col.GetString());
            }

            inlines.Add(new CheatInline
            {
                Text = text, Bold = bold, Italic = italic, Underline = underline,
                Strikethrough = strike, Code = code, ColorHex = color
            });
        }
        return inlines;
    }

    private static bool IsTrue(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static bool IsChecked(JsonElement block)
        => block.TryGetProperty("to_do", out var td) && IsTrue(td, "checked");

    private static string? CalloutEmoji(JsonElement block)
        => block.TryGetProperty("callout", out var co)
           && co.TryGetProperty("icon", out var icon)
           && icon.TryGetProperty("type", out var it) && it.GetString() == "emoji"
           && icon.TryGetProperty("emoji", out var em) ? em.GetString() : null;

    private static string? ImageUrlOfBlock(JsonElement block)
    {
        if (!block.TryGetProperty("image", out var img) || img.ValueKind != JsonValueKind.Object)
            return null;
        var kind = img.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (kind == "external" && img.TryGetProperty("external", out var ext)
            && ext.TryGetProperty("url", out var u1) && u1.ValueKind == JsonValueKind.String)
            return u1.GetString();
        if (kind == "file" && img.TryGetProperty("file", out var file)
            && file.TryGetProperty("url", out var u2) && u2.ValueKind == JsonValueKind.String)
            return u2.GetString();
        return null;
    }

    private static string? CaptionOf(JsonElement block, string type)
    {
        if (!block.TryGetProperty(type, out var body) || !body.TryGetProperty("caption", out var cap)
            || cap.ValueKind != JsonValueKind.Array)
            return null;
        var sb = new StringBuilder();
        foreach (var s in cap.EnumerateArray())
            if (s.TryGetProperty("plain_text", out var pt)) sb.Append(pt.GetString());
        var text = sb.ToString().Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>Maps Notion's named text colours to hex; background variants and "default" return null.</summary>
    private static string? MapColor(string? c) => c switch
    {
        "gray" => "#9AA4B2",
        "brown" => "#B0785A",
        "orange" => "#E8912D",
        "yellow" => "#E6C34A",
        "green" => "#4FBF84",
        "blue" => "#4FA8E8",
        "purple" => "#9B7BE0",
        "pink" => "#E86FA6",
        "red" => "#E8615A",
        _ => null // "default" and every "*_background" fall back to the theme's text colour
    };

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void ConfigureAuth(string token)
    {
        // Remove existing auth header before setting new one
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    private Task<JsonElement?> GetAsync(string path, CancellationToken ct)
        => SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{NotionApiBase}/{path}"), ct);

    private Task<JsonElement?> PostAsync(string path, object body, CancellationToken ct)
        => SendAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"{NotionApiBase}/{path}")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            return req;
        }, ct);

    // ── Request throttling + 429 handling ─────────────────────────────────────
    // Notion allows ~3 requests/second; exceeding it returns HTTP 429. Without pacing, a multi-page
    // course sync bursts past the limit and the whole sync aborts partway — which is why some courses
    // only pulled a few weeks. We space requests out and back off (honouring Retry-After) on 429.

    private readonly SemaphoreSlim _throttle = new(1, 1);
    private DateTimeOffset _nextAllowed = DateTimeOffset.MinValue;
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(350); // ~3 req/s

    private async Task<JsonElement?> SendAsync(Func<HttpRequestMessage> makeRequest, CancellationToken ct)
    {
        const int maxRetries = 6;
        for (int attempt = 0; ; attempt++)
        {
            await PaceAsync(ct).ConfigureAwait(false);

            using var req = makeRequest();
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.TooManyRequests && attempt < maxRetries)
            {
                var wait = RetryAfter(resp)
                           ?? TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
                _logger.LogWarning("Notion rate limit (429); waiting {Sec:0.#}s then retrying.",
                    wait.TotalSeconds);
                await Task.Delay(wait, ct).ConfigureAwait(false);
                continue;
            }

            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
    }

    private async Task PaceAsync(CancellationToken ct)
    {
        await _throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (now < _nextAllowed)
                await Task.Delay(_nextAllowed - now, ct).ConfigureAwait(false);
            _nextAllowed = DateTimeOffset.UtcNow + MinInterval;
        }
        finally
        {
            _throttle.Release();
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage resp)
    {
        if (resp.Headers.TryGetValues("Retry-After", out var values)
            && double.TryParse(values.FirstOrDefault(), out var seconds))
            return TimeSpan.FromSeconds(Math.Min(60, seconds) + 0.5);
        return null;
    }
}
