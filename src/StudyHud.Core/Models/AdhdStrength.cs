namespace StudyHud.Core.Models;

/// <summary>
/// How insistent the ADHD support layer is — how soon it nudges, how firm its wording, and how short
/// the recommended focus/break cadence. Higher strength = more scaffolding, never more shaming.
/// </summary>
public enum AdhdStrength
{
    /// <summary>Quiet background support: nudges late, softest wording, longer sessions.</summary>
    Gentle,

    /// <summary>Balanced, research-recommended support for ADHD.</summary>
    Standard,

    /// <summary>Maximum scaffolding: nudges early, firmest (still kind) wording, shorter sessions.</summary>
    Strong
}

/// <summary>
/// Maps an <see cref="AdhdStrength"/> to concrete behaviour, so the settings page, the one-tap ADHD
/// preset, and the on-screen companion all agree on what each level means. Pure — no state.
/// </summary>
public static class AdhdProfile
{
    /// <summary>The level the one-tap "Set up for ADHD" button applies.</summary>
    public const AdhdStrength Recommended = AdhdStrength.Standard;

    /// <summary>How many times you can switch away during a block before the companion gently nudges.</summary>
    public static int DistractionThreshold(AdhdStrength s) => s switch
    {
        AdhdStrength.Gentle => 5,
        AdhdStrength.Strong => 1,
        _ => 3
    };

    /// <summary>Recommended minutes of continuous focus before a break nudge (hyperfocus safety).</summary>
    public static int RecommendedSessionCap(AdhdStrength s) => s switch
    {
        AdhdStrength.Gentle => 90,
        AdhdStrength.Strong => 45,
        _ => 60
    };

    /// <summary>Recommended focus-block length (min).</summary>
    public static int RecommendedFocusMinutes(AdhdStrength s) => s switch
    {
        AdhdStrength.Gentle => 25,
        AdhdStrength.Strong => 15,
        _ => 20
    };

    /// <summary>Whether break prompts should escalate (a firmer second cue) at this strength.</summary>
    public static bool Escalate(AdhdStrength s) => s == AdhdStrength.Strong;
}
