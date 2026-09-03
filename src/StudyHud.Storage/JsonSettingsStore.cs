using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Models;
using StudyHud.Core.Services;

namespace StudyHud.Storage;

/// <summary>
/// JSON-file implementation of <see cref="ISettingsStore"/> (spec §71 local-first, §69 recovery).
///
/// Storage location: <c>%LOCALAPPDATA%\StudyHud\settings.json</c>.
/// Writes are atomic (temp file + move) so an interrupted save never truncates the real file.
/// A missing or corrupt file yields defaults rather than throwing.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _filePath;
    private readonly ILogger<JsonSettingsStore> _logger;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private StudyHudSettings _current = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonSettingsStore(string filePath, ILogger<JsonSettingsStore> logger)
    {
        _filePath = filePath;
        _logger = logger;
    }

    public StudyHudSettings Current => _current;

    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    public async Task<StudyHudSettings> LoadAsync(CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("No settings file at {Path}; using defaults.", _filePath);
                _current = new StudyHudSettings();
            }
            else
            {
                try
                {
                    await using var stream = File.OpenRead(_filePath);
                    var loaded = await JsonSerializer
                        .DeserializeAsync<StudyHudSettings>(stream, JsonOptions, ct)
                        .ConfigureAwait(false);
                    _current = loaded ?? new StudyHudSettings();
                    _logger.LogInformation("Settings loaded from {Path}.", _filePath);
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    // Corrupt/unreadable settings must not prevent startup (spec §69).
                    _logger.LogWarning(ex, "Settings file unreadable; falling back to defaults.");
                    _current = new StudyHudSettings();
                }
            }
        }
        finally
        {
            _ioLock.Release();
        }

        RaiseChanged();
        return _current;
    }

    public async Task SaveAsync(StudyHudSettings settings, CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            var tempPath = _filePath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }

            // Atomic replace so a crash never leaves a half-written settings file.
            File.Move(tempPath, _filePath, overwrite: true);

            _current = settings;
            _logger.LogDebug("Settings saved to {Path}.", _filePath);
        }
        finally
        {
            _ioLock.Release();
        }

        RaiseChanged();
    }

    public Task UpdateAsync(Func<StudyHudSettings, StudyHudSettings> transform, CancellationToken ct = default)
        => SaveAsync(transform(_current), ct);

    private void RaiseChanged()
        => SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { Settings = _current });
}
