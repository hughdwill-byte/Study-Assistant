using FluentAssertions;
using Moq;
using StudyHud.Core.Models;
using StudyHud.Core.Services;
using StudyHud.Search;
using Xunit;
using Microsoft.Extensions.Logging;

namespace StudyHud.Tests;

// ─── Feature Extraction Tests (spec §51, §86) ─────────────────────────────────

public class FeatureExtractorTests
{
    [Fact]
    public void Extract_EngineeringQuestion_FindsVariables()
    {
        const string text = "Determine the maximum bending stress when M = 3.2 kNm";
        var features = FeatureExtractor.Extract(text);

        features.Variables.Should().Contain("M");
        features.Numbers.Should().Contain("3.2");
        features.Units.Should().Contain("kNm");
        features.Words.Should().Contain("bending");
        features.Words.Should().Contain("stress");
        features.Words.Should().Contain("maximum");
        features.Words.Should().NotContain("the");
        features.Words.Should().NotContain("when");
    }

    [Fact]
    public void Extract_StressFormula_FindsGreekSymbols()
    {
        const string text = "σ = My/I";
        var features = FeatureExtractor.Extract(text);

        features.Variables.Should().Contain("σ");
        features.Variables.Should().Contain("M");
        features.Variables.Should().Contain("I");
    }

    [Fact]
    public void Extract_EmptyText_ReturnsEmptyFeatures()
    {
        var features = FeatureExtractor.Extract(string.Empty);

        features.Words.Should().BeEmpty();
        features.Variables.Should().BeEmpty();
        features.Numbers.Should().BeEmpty();
        features.Units.Should().BeEmpty();
    }

    [Fact]
    public void Extract_MultipleUnits_FindsAll()
    {
        const string text = "The beam has M = 5 kNm and F = 200 kN with E = 200 GPa";
        var features = FeatureExtractor.Extract(text);

        features.Units.Should().Contain("kNm");
        features.Units.Should().Contain("kN");
        features.Units.Should().Contain("GPa");
        features.Variables.Should().Contain("M");
        features.Variables.Should().Contain("F");
        features.Variables.Should().Contain("E");
    }

    [Fact]
    public void Extract_Numbers_DetectsDecimals()
    {
        const string text = "Load = 3.14 kN at 1.5e3 mm";
        var features = FeatureExtractor.Extract(text);

        features.Numbers.Should().Contain("3.14");
        features.Numbers.Should().Contain("1.5e3");
    }
}

// ─── ApplicationState Tests (spec §125, §126, §127) ──────────────────────────

public class ApplicationStateServiceTests
{
    private readonly ApplicationStateService _sut;

    public ApplicationStateServiceTests()
    {
        var logger = Mock.Of<ILogger<ApplicationStateService>>();
        _sut = new ApplicationStateService(logger);
    }

    [Fact]
    public void DefaultState_IsGhostAndNoteTaking()
    {
        _sut.Current.HudInteractionState.Should().Be(HudInteractionState.Ghost);
        _sut.Current.CurrentWorkspace.Should().Be(WorkspaceId.NoteTaking);
        _sut.Current.HudVisible.Should().BeTrue();
        _sut.Current.AssessmentModeActive.Should().BeFalse();
    }

    [Fact]
    public void SetHudInteractionState_GhostToActive_TransitionsCorrectly()
    {
        _sut.SetHudInteractionState(HudInteractionState.Active);
        _sut.Current.HudInteractionState.Should().Be(HudInteractionState.Active);
    }

    [Fact]
    public void SetHudInteractionState_WhileInEditMode_GhostKeyReleaseIsIgnored()
    {
        // Enter Edit mode first
        _sut.SetHudInteractionState(HudInteractionState.Edit);
        // Releasing Hold-to-Interact while in Edit must NOT return to Ghost (spec §126)
        _sut.SetHudInteractionState(HudInteractionState.Ghost);
        _sut.Current.HudInteractionState.Should().Be(HudInteractionState.Edit,
            "Edit mode must not be exited by releasing Hold-to-Interact");
    }

    [Fact]
    public void SetHudVisible_False_HidesHud()
    {
        _sut.SetHudVisible(false);
        _sut.Current.HudVisible.Should().BeFalse();
    }

    [Fact]
    public void Update_RaisesStateChangedEvent()
    {
        ApplicationStateChangedEventArgs? args = null;
        _sut.StateChanged += (_, e) => args = e;

        _sut.SetHudVisible(false);

        args.Should().NotBeNull();
        args!.Previous.HudVisible.Should().BeTrue();
        args.Current.HudVisible.Should().BeFalse();
    }

    [Fact]
    public async Task SwitchWorkspace_ClearsCaptureMode()
    {
        _sut.SetCaptureModeActive(true);
        await _sut.SwitchWorkspaceAsync(WorkspaceId.QuestionFinder);

        _sut.Current.IsCaptureModeActive.Should().BeFalse(
            "workspace switch should cancel active capture mode");
        _sut.Current.CurrentWorkspace.Should().Be(WorkspaceId.QuestionFinder);
    }

    [Fact]
    public async Task AssessmentMode_PersistsAcrossWorkspaceSwitch()
    {
        await _sut.SetAssessmentModeAsync(true);
        await _sut.SwitchWorkspaceAsync(WorkspaceId.QuestionFinder);

        _sut.Current.AssessmentModeActive.Should().BeTrue(
            "Assessment Mode must not be cleared by workspace switching (spec §153)");
    }
}

// ─── Assessment Policy Tests (spec §41, §89, §152, §182) ────────────────────

public class AssessmentPolicyServiceTests
{
    private readonly AssessmentPolicyService _sut;

    public AssessmentPolicyServiceTests()
    {
        var logger = Mock.Of<ILogger<AssessmentPolicyService>>();
        _sut = new AssessmentPolicyService(logger);
    }

    [Theory]
    [InlineData(PolicyOperation.NotionSync)]
    [InlineData(PolicyOperation.LlmRequest)]
    [InlineData(PolicyOperation.EmbeddingRequest)]
    [InlineData(PolicyOperation.CloudOcrRequest)]
    [InlineData(PolicyOperation.WebSearch)]
    [InlineData(PolicyOperation.CapturedQuestionUpload)]
    public void AssessmentModeEnabled_BlocksProhibitedOperations(PolicyOperation op)
    {
        _sut.SetAssessmentMode(true);
        _sut.IsOperationAllowed(op).Should().BeFalse(
            $"{op} must be blocked in Assessment Mode");
    }

    [Theory]
    [InlineData(PolicyOperation.LocalOcr)]
    [InlineData(PolicyOperation.LocalSearch)]
    [InlineData(PolicyOperation.LocalIndex)]
    public void AssessmentModeEnabled_AllowsLocalOperations(PolicyOperation op)
    {
        _sut.SetAssessmentMode(true);
        _sut.IsOperationAllowed(op).Should().BeTrue(
            $"{op} must be allowed in Assessment Mode (local-only)");
    }

    [Fact]
    public void AssessmentModeDisabled_AllOperationsAllowed()
    {
        _sut.SetAssessmentMode(false);
        _sut.IsOperationAllowed(PolicyOperation.NotionSync).Should().BeTrue();
        _sut.IsOperationAllowed(PolicyOperation.LlmRequest).Should().BeTrue();
    }

    [Fact]
    public void GetBlockReason_ReturnsExplanation()
    {
        _sut.SetAssessmentMode(true);
        var reason = _sut.GetBlockReason(PolicyOperation.LlmRequest);
        reason.Should().NotBeNullOrEmpty("blocked operations must provide an explanation");
    }

    [Fact]
    public void PolicyChanged_RaisedOnModeChange()
    {
        PolicyChangedEventArgs? args = null;
        _sut.PolicyChanged += (_, e) => args = e;

        _sut.SetAssessmentMode(true);

        args.Should().NotBeNull();
        args!.AssessmentModeActive.Should().BeTrue();
    }
}

// ─── Panel Layout / Recovery Tests (spec §19, §86, §160) ────────────────────

public class PanelLayoutTests
{
    [Fact]
    public void NormalizedRect_WidthAndHeight_AreCorrect()
    {
        var rect = new NormalizedRect(0.1, 0.2, 0.6, 0.8);
        rect.Width.Should().BeApproximately(0.5, 0.001);
        rect.Height.Should().BeApproximately(0.6, 0.001);
    }

    [Fact]
    public void PanelLayout_DefaultVisibility_IsExpanded()
    {
        var layout = new PanelLayout
        {
            PanelId = "test-panel",
            Workspace = WorkspaceId.NoteTaking,
            MonitorId = "monitor-1",
            NormalizedPosition = new NormalizedRect(0, 0, 0.3, 1.0),
            LogicalWidth = 280,
            LogicalHeight = 600
        };

        layout.VisibilityState.Should().Be(PanelVisibilityState.Expanded);
        layout.ResponsiveState.Should().Be(PanelResponsiveState.Normal);
        layout.DockState.Should().Be(PanelDockState.Floating);
    }
}

// ─── OCR Normaliser Tests (spec §53) ─────────────────────────────────────────

public class OcrNormaliserTests
{
    [Fact]
    public void Normalise_PreservesEngineeringUnits()
    {
        var result = StudyHud.Ocr.OcrNormaliser.Normalise("Force = 10 kNm applied at E = 200 GPa");
        result.Should().Contain("kNm");
        result.Should().Contain("GPa");
    }

    [Fact]
    public void Normalise_EmptyString_ReturnsEmpty()
    {
        StudyHud.Ocr.OcrNormaliser.Normalise(string.Empty).Should().BeEmpty();
    }
}
