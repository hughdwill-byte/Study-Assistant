using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Models;
using StudyHud.Core.Services;

namespace StudyHud.Storage;

/// <summary>
/// JSON-file implementation of <see cref="ILayoutService"/> (spec §19, §21, §160).
///
/// Each named layout is stored as <c>%LOCALAPPDATA%\StudyHud\layouts\&lt;id&gt;.json</c>
/// holding a list of <see cref="PanelLayout"/>. Positions are monitor-relative normalised
/// coordinates (0..1) so a layout survives resolution/DPI/monitor changes without pixel drift.
///
/// Writes are atomic. A missing layout returns an empty list rather than throwing.
/// </summary>
public sealed class LayoutService : ILayoutService
{
    private readonly string _layoutsDir;
    private readonly ILogger<LayoutService> _logger;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public LayoutService(string layoutsDirectory, ILogger<LayoutService> logger)
    {
        _layoutsDir = layoutsDirectory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PanelLayout>> LoadLayoutAsync(string layoutId, CancellationToken ct = default)
    {
        var path = PathFor(layoutId);
        if (!File.Exists(path))
            return Array.Empty<PanelLayout>();

        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var stream = File.OpenRead(path);
            var layouts = await JsonSerializer
                .DeserializeAsync<List<PanelLayout>>(stream, JsonOptions, ct)
                .ConfigureAwait(false);
            return layouts ?? new List<PanelLayout>();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A corrupt layout file must not break the HUD — recover with an empty layout (spec §69).
            _logger.LogWarning(ex, "Layout '{Id}' unreadable; ignoring it.", layoutId);
            return Array.Empty<PanelLayout>();
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task SaveLayoutAsync(string layoutId, IEnumerable<PanelLayout> panels, CancellationToken ct = default)
    {
        var path = PathFor(layoutId);
        var list = panels.ToList();

        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_layoutsDir);
            var tempPath = path + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, list, JsonOptions, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            File.Move(tempPath, path, overwrite: true);
            _logger.LogDebug("Layout '{Id}' saved ({Count} panels).", layoutId, list.Count);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public Task<IReadOnlyList<string>> GetSavedLayoutNamesAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_layoutsDir))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        var names = Directory.EnumerateFiles(_layoutsDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(names);
    }

    public Task DeleteLayoutAsync(string layoutId, CancellationToken ct = default)
    {
        var path = PathFor(layoutId);
        if (File.Exists(path))
        {
            try { File.Delete(path); }
            catch (IOException ex) { _logger.LogWarning(ex, "Could not delete layout '{Id}'.", layoutId); }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reassigns panels whose stored monitor is gone to a currently-present monitor and clamps
    /// their normalised rectangle back inside the visible area, so a panel can never be stranded
    /// off-screen after a monitor is disconnected (spec §160, §296).
    /// </summary>
    public IReadOnlyList<PanelLayout> RecoverPanelsForCurrentMonitors(
        IEnumerable<PanelLayout> panels,
        IEnumerable<MonitorInfo> availableMonitors)
    {
        var monitors = availableMonitors.ToList();
        if (monitors.Count == 0)
            return panels.ToList();

        var validIds = monitors.Select(m => m.MonitorId).ToHashSet();
        var fallback = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];

        var recovered = new List<PanelLayout>();
        foreach (var panel in panels)
        {
            var target = validIds.Contains(panel.MonitorId) ? panel.MonitorId : fallback.MonitorId;
            var rect = ClampIntoView(panel.NormalizedPosition);

            recovered.Add(panel with
            {
                MonitorId = target,
                NormalizedPosition = rect
            });

            if (target != panel.MonitorId)
                _logger.LogInformation(
                    "Panel '{Panel}' recovered from missing monitor '{Old}' to '{New}'.",
                    panel.PanelId, panel.MonitorId, target);
        }
        return recovered;
    }

    /// <summary>Clamps a normalised rectangle so its top-left stays within [0,1) and it never
    /// extends past the far edge, preserving width/height where possible.</summary>
    private static NormalizedRect ClampIntoView(NormalizedRect r)
    {
        double w = Math.Clamp(r.Width, 0.02, 1.0);
        double h = Math.Clamp(r.Height, 0.02, 1.0);
        double left = Math.Clamp(r.Left, 0.0, 1.0 - w);
        double top = Math.Clamp(r.Top, 0.0, 1.0 - h);
        return new NormalizedRect(left, top, left + w, top + h);
    }

    private string PathFor(string layoutId)
        => Path.Combine(_layoutsDir, Sanitize(layoutId) + ".json");

    private static string Sanitize(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = id.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var cleaned = new string(chars);
        return string.IsNullOrWhiteSpace(cleaned) ? "default" : cleaned;
    }
}
