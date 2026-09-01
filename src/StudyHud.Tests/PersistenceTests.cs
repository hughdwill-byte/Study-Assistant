using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StudyHud.Core.Models;
using StudyHud.Core.Services;
using StudyHud.Storage;
using Xunit;

namespace StudyHud.Tests;

// ─── Settings Store Tests (spec §19, §71) ────────────────────────────────────

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _file;

    public JsonSettingsStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "StudyHudTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "settings.json");
    }

    private JsonSettingsStore NewStore()
        => new(_file, Mock.Of<ILogger<JsonSettingsStore>>());

    [Fact]
    public async Task Load_WhenNoFile_ReturnsDefaults()
    {
        var store = NewStore();
        var settings = await store.LoadAsync();

        settings.ThemeId.Should().Be("Default");
        settings.CurrentWorkspace.Should().Be(WorkspaceId.NoteTaking);
        settings.HoldToInteract.Type.Should().Be(HoldTriggerType.KeyboardKey);
        settings.AssessmentModeActive.Should().BeFalse();
    }

    [Fact]
    public async Task Save_ThenLoad_RoundTripsAllFields()
    {
        var original = new StudyHudSettings
        {
            ThemeId = "Dark",
            CurrentWorkspace = WorkspaceId.QuestionFinder,
            CurrentCourseId = "eng-mechanics",
            AssessmentModeActive = true,
            HoldToInteract = new HoldTriggerSettings { Type = HoldTriggerType.MouseButton, MouseButton = 5 },
            WorkspaceMacroProfiles = new Dictionary<WorkspaceId, string>
            {
                [WorkspaceId.QuestionFinder] = "question-profile"
            }
        };

        await NewStore().SaveAsync(original);
        var loaded = await NewStore().LoadAsync();

        loaded.ThemeId.Should().Be("Dark");
        loaded.CurrentWorkspace.Should().Be(WorkspaceId.QuestionFinder);
        loaded.CurrentCourseId.Should().Be("eng-mechanics");
        loaded.AssessmentModeActive.Should().BeTrue();
        loaded.HoldToInteract.Type.Should().Be(HoldTriggerType.MouseButton);
        loaded.WorkspaceMacroProfiles.Should().ContainKey(WorkspaceId.QuestionFinder)
            .WhoseValue.Should().Be("question-profile");
    }

    [Fact]
    public async Task Load_WhenFileCorrupt_ReturnsDefaultsWithoutThrowing()
    {
        await File.WriteAllTextAsync(_file, "{ this is not valid json ");
        var store = NewStore();

        var settings = await store.LoadAsync();

        settings.ThemeId.Should().Be("Default", "a corrupt settings file must not break startup");
    }

    [Fact]
    public async Task Update_AppliesTransformAndPersists()
    {
        var store = NewStore();
        await store.LoadAsync();

        await store.UpdateAsync(s => s with { ThemeId = "Light" });

        store.Current.ThemeId.Should().Be("Light");
        (await NewStore().LoadAsync()).ThemeId.Should().Be("Light");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}

// ─── Layout Service Tests (spec §19, §160) ───────────────────────────────────

public sealed class LayoutServiceTests : IDisposable
{
    private readonly string _dir;

    public LayoutServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "StudyHudTests", Guid.NewGuid().ToString("N"));
    }

    private LayoutService NewService()
        => new(_dir, Mock.Of<ILogger<LayoutService>>());

    private static PanelLayout Panel(string id, string monitorId) => new()
    {
        PanelId = id,
        Workspace = WorkspaceId.NoteTaking,
        MonitorId = monitorId,
        NormalizedPosition = new NormalizedRect(0.1, 0.1, 0.4, 0.5),
        LogicalWidth = 280,
        LogicalHeight = 600
    };

    private static MonitorInfo Monitor(string id, bool primary) => new()
    {
        MonitorId = id,
        DeviceName = "\\\\.\\" + id,
        Bounds = new ScreenRect(0, 0, 1920, 1080),
        WorkArea = new ScreenRect(0, 0, 1920, 1040),
        ScaleFactor = 1.0,
        Dpi = 96,
        IsPrimary = primary
    };

    [Fact]
    public async Task Save_ThenLoad_RoundTripsPanels()
    {
        var service = NewService();
        var panels = new[] { Panel("macro", "mon-1"), Panel("finder", "mon-1") };

        await service.SaveLayoutAsync("workspace-NoteTaking", panels);
        var loaded = await service.LoadLayoutAsync("workspace-NoteTaking");

        loaded.Should().HaveCount(2);
        loaded.Select(p => p.PanelId).Should().Contain(new[] { "macro", "finder" });
        loaded.First(p => p.PanelId == "macro").LogicalWidth.Should().Be(280);
    }

    [Fact]
    public async Task Load_UnknownLayout_ReturnsEmpty()
    {
        var loaded = await NewService().LoadLayoutAsync("does-not-exist");
        loaded.Should().BeEmpty();
    }

    [Fact]
    public void Recover_MissingMonitor_RemapsToPrimary()
    {
        var service = NewService();
        var panels = new[] { Panel("macro", "gone-monitor") };
        var monitors = new[] { Monitor("mon-A", primary: false), Monitor("mon-B", primary: true) };

        var recovered = service.RecoverPanelsForCurrentMonitors(panels, monitors);

        recovered.Should().ContainSingle()
            .Which.MonitorId.Should().Be("mon-B", "a panel on a missing monitor moves to the primary");
    }

    [Fact]
    public void Recover_PresentMonitor_IsUnchanged()
    {
        var service = NewService();
        var panels = new[] { Panel("macro", "mon-A") };
        var monitors = new[] { Monitor("mon-A", primary: true) };

        var recovered = service.RecoverPanelsForCurrentMonitors(panels, monitors);

        recovered.Single().MonitorId.Should().Be("mon-A");
    }

    [Fact]
    public void Recover_OffScreenPanel_IsClampedIntoView()
    {
        var service = NewService();
        var offScreen = Panel("macro", "mon-A") with
        {
            NormalizedPosition = new NormalizedRect(1.5, 1.5, 1.8, 1.9) // fully off-screen
        };
        var monitors = new[] { Monitor("mon-A", primary: true) };

        var rect = service.RecoverPanelsForCurrentMonitors(new[] { offScreen }, monitors).Single().NormalizedPosition;

        rect.Left.Should().BeInRange(0.0, 1.0);
        rect.Top.Should().BeInRange(0.0, 1.0);
        rect.Right.Should().BeLessThanOrEqualTo(1.0);
        rect.Bottom.Should().BeLessThanOrEqualTo(1.0);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}

// ─── Assessment Mode enforcement sync (spec §41, §182) ───────────────────────

public sealed class AssessmentPolicySyncTests
{
    [Fact]
    public async Task PolicyFollowsApplicationState_WhenAssessmentModeToggled()
    {
        var appState = new ApplicationStateService(Mock.Of<ILogger<ApplicationStateService>>());
        var policy = new AssessmentPolicyService(Mock.Of<ILogger<AssessmentPolicyService>>(), appState);

        policy.IsOperationAllowed(PolicyOperation.NotionSync).Should().BeTrue();

        await appState.SetAssessmentModeAsync(true);

        policy.IsAssessmentModeActive.Should().BeTrue(
            "the enforcement policy must mirror ApplicationState or the toggle blocks nothing");
        policy.IsOperationAllowed(PolicyOperation.NotionSync).Should().BeFalse();
        policy.IsOperationAllowed(PolicyOperation.LocalSearch).Should().BeTrue();
    }

    [Fact]
    public void PolicyWithoutAppState_StillWorksStandalone()
    {
        // The parameterless-appState constructor path must remain usable (existing call sites).
        var policy = new AssessmentPolicyService(Mock.Of<ILogger<AssessmentPolicyService>>());
        policy.SetAssessmentMode(true);
        policy.IsOperationAllowed(PolicyOperation.LlmRequest).Should().BeFalse();
    }
}
