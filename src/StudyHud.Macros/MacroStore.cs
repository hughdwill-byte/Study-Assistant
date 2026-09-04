using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace StudyHud.Macros;

/// <summary>
/// Persists user macros as a flat JSON list of <see cref="MacroSpec"/> under
/// %LOCALAPPDATA%\StudyHud\macros.json (spec §30, §71). Corrupt/missing file → the built-in defaults.
/// </summary>
public sealed class MacroStore
{
    private readonly string _path;
    private readonly ILogger<MacroStore> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public MacroStore(ILogger<MacroStore> logger)
    {
        _logger = logger;
        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StudyHud", "macros.json");
    }

    public IReadOnlyList<MacroSpec> Load()
    {
        try
        {
            if (!File.Exists(_path))
                return MacroSpec.Defaults();

            var json = File.ReadAllText(_path);
            var specs = JsonSerializer.Deserialize<List<MacroSpec>>(json, JsonOptions);
            return specs is { Count: > 0 } ? specs : MacroSpec.Defaults();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read macros.json; using defaults.");
            return MacroSpec.Defaults();
        }
    }

    public void Save(IReadOnlyList<MacroSpec> specs)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(specs, JsonOptions));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save macros.json.");
        }
    }
}
