using System.IO;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StudyHud.Core.Services;
using StudyHud.Search;
using StudyHud.Storage;
using Xunit;

namespace StudyHud.Tests;

// ─── Question Finder runtime (spec §38, §54, §57, §59) ───────────────────────

public sealed class QuestionFinderTests : IAsyncLifetime, IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;
    private LocalSearchIndex _index = null!;
    private FakeOcr _ocr = null!;
    private QuestionFinder _finder = null!;

    private sealed class FakeOcr : IOcrService
    {
        public float Confidence = 0.95f;
        public bool IsAvailable { get; set; } = true;
        public string EngineName => "fake";

        public Task<OcrResult> RecogniseAsync(byte[] imageBytes, CancellationToken ct = default)
        {
            var text = Encoding.UTF8.GetString(imageBytes);
            return Task.FromResult(new OcrResult
            {
                RawText = text,
                NormalisedText = text,
                Confidence = Confidence,
                Words = Array.Empty<OcrWord>(),
                EngineName = "fake"
            });
        }
    }

    public QuestionFinderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "StudyHudTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "studyhud.db");
    }

    public async Task InitializeAsync()
    {
        await new DatabaseMigrator(_dbPath, Mock.Of<ILogger<DatabaseMigrator>>()).MigrateAsync();
        _index = new LocalSearchIndex(_dbPath, Mock.Of<ILogger<LocalSearchIndex>>());
        _ocr = new FakeOcr();
        _finder = new QuestionFinder(_ocr, _index, Mock.Of<ILogger<QuestionFinder>>());

        await _index.UpsertCourseAsync("eng", "Engineering Mechanics");
        await _index.IndexItemsAsync(new[]
        {
            new IndexableNoteItem
            {
                Id = "flexure", CourseId = "eng", WeekLabel = "Week 6",
                PageId = "p1", PageName = "Bending", HeadingPath = "Bending > Flexure Formula",
                HeadingText = "Flexure Formula", NotionPageUrl = "https://notion.so/p1",
                OcrNormalised = "flexure formula bending stress maximum bending moment sigma My/I",
                OcrConfidence = 0.95f
            }
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FindFromImage_ReturnsOcrTextAndRankedResults()
    {
        var image = Encoding.UTF8.GetBytes("determine the maximum bending stress");
        var result = await _finder.FindFromImageAsync(image, courseId: "eng");

        result.OcrText.Should().Contain("bending");
        result.IsLowConfidence.Should().BeFalse();
        result.Results.Should().NotBeEmpty();
        result.Results[0].NoteItemId.Should().Be("flexure");
        result.Features.Words.Should().Contain("bending");
    }

    [Fact]
    public async Task FindFromImage_LowConfidence_IsFlagged()
    {
        _ocr.Confidence = 0.4f;
        var result = await _finder.FindFromImageAsync(Encoding.UTF8.GetBytes("bending stress"));

        result.IsLowConfidence.Should().BeTrue("so the UI can offer a correction (spec §59)");
        result.Results.Should().NotBeEmpty("search still runs on low-confidence text");
    }

    [Fact]
    public async Task FindFromText_SkipsOcrAndSearches()
    {
        var result = await _finder.FindFromTextAsync("flexure formula", courseId: "eng");

        result.OcrConfidence.Should().Be(1.0f);
        result.Results.Should().Contain(r => r.NoteItemId == "flexure");
    }

    [Fact]
    public async Task FindFromImage_EmptyImage_ReturnsNoResults()
    {
        var result = await _finder.FindFromImageAsync(Array.Empty<byte>());

        result.OcrText.Should().BeEmpty();
        result.Results.Should().BeEmpty();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
