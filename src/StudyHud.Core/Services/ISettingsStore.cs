using StudyHud.Core.Models;

namespace StudyHud.Core.Services;

/// <summary>
/// Loads and persists <see cref="StudyHudSettings"/> to local storage (spec §71 local-first).
/// Implementations must:
///  - never throw on a missing/corrupt file (return defaults instead — spec §69),
///  - write atomically so a crash mid-save cannot leave a truncated file,
///  - never store secrets (the Notion token lives in the credential store — spec §46).
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// The current in-memory settings. Available synchronously after <see cref="LoadAsync"/>;
    /// returns defaults before the first load.
    /// </summary>
    StudyHudSettings Current { get; }

    /// <summary>Loads settings from disk (or returns defaults if none exist). Sets <see cref="Current"/>.</summary>
    Task<StudyHudSettings> LoadAsync(CancellationToken ct = default);

    /// <summary>Persists the given settings atomically and updates <see cref="Current"/>.</summary>
    Task SaveAsync(StudyHudSettings settings, CancellationToken ct = default);

    /// <summary>Applies a transform to the current settings and saves the result.</summary>
    Task UpdateAsync(Func<StudyHudSettings, StudyHudSettings> transform, CancellationToken ct = default);

    /// <summary>Raised after settings are loaded or saved. Args carry the new settings.</summary>
    event EventHandler<SettingsChangedEventArgs> SettingsChanged;
}

public sealed class SettingsChangedEventArgs : EventArgs
{
    public required StudyHudSettings Settings { get; init; }
}

// ---------------------------------------------------------------------------

/// <summary>
/// Minimal abstraction that lets coordination code (e.g. the workspace coordinator in the
/// overlay layer) switch the active macro profile without taking a dependency on the whole
/// macro engine assembly (spec §29). Implemented by <c>MacroEngine</c>.
/// </summary>
public interface IMacroProfileSwitcher
{
    /// <summary>Activates the profile with the given id. Unknown ids are ignored.</summary>
    void SetActiveProfile(string profileId);
}
