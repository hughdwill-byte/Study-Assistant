using FluentAssertions;
using StudyHud.Core.Models;
using Xunit;

namespace StudyHud.Tests;

// ─── ADHD support-strength mapping ───────────────────────────────────────────

public sealed class AdhdProfileTests
{
    [Fact]
    public void StrongNudgesEarlierThanGentle()
    {
        AdhdProfile.DistractionThreshold(AdhdStrength.Strong)
            .Should().BeLessThan(AdhdProfile.DistractionThreshold(AdhdStrength.Gentle));
    }

    [Fact]
    public void StrongHasShorterSessionsThanGentle()
    {
        AdhdProfile.RecommendedSessionCap(AdhdStrength.Strong)
            .Should().BeLessThan(AdhdProfile.RecommendedSessionCap(AdhdStrength.Gentle));
        AdhdProfile.RecommendedFocusMinutes(AdhdStrength.Strong)
            .Should().BeLessThan(AdhdProfile.RecommendedFocusMinutes(AdhdStrength.Gentle));
    }

    [Fact]
    public void OnlyStrongEscalates()
    {
        AdhdProfile.Escalate(AdhdStrength.Strong).Should().BeTrue();
        AdhdProfile.Escalate(AdhdStrength.Standard).Should().BeFalse();
        AdhdProfile.Escalate(AdhdStrength.Gentle).Should().BeFalse();
    }

    [Fact]
    public void RecommendedIsAValidLevel()
    {
        AdhdProfile.RecommendedFocusMinutes(AdhdProfile.Recommended).Should().BeGreaterThan(0);
    }
}
