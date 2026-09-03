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

// ─── Pre-indexing pipeline (spec §47, §50, §59) ──────────────────────────────

public sealed class NoteIndexerTests : IAsyncLifetime, IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;
    private LocalSearchIndex _index = null!;
    private FakeOcr _ocr = null!;
    private NoteIndexer _indexer = null!;

    private const string CourseId = "eng-mech";

    /// <summary>OCR stub: "recognises" an image by decoding its bytes as UTF-8; counts calls.</summary>
    private sealed class FakeOcr : IOcrService
    {
        public int Calls;
        public float Confidence = 0.95f;
        public bool IsAvailable { get; set; } = true;
        public string EngineName => "fake";

        public Task<OcrResult> RecogniseAsync(byte[] imageBytes, CancellationToken ct = default)
        {
            Calls++;
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

    public NoteIndexerTests()
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
        _indexer = new NoteIndexer(_ocr, _index, Mock.Of<ILogger<NoteIndexer>>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static RawNoteSource ImageSource(string id, string text) => new()
    {
        Id = id,
        PageId = "p1",
        PageName = "Bending",
        HeadingPath = "Bending > Flexure",
        NotionPageUrl = "https://notion.so/p1",
        ImageBytes = Encoding.UTF8.GetBytes(text)
    };

    private static SearchQuery Query(string text) => new()
    {
        RawText = text,
        Features = FeatureExtractor.Extract(text),
        CourseId = CourseId,
        MaxResults = 5
    };

    [Fact]
    public async Task Index_Images_OcrsAndMakesThemSearchable()
    {
        var summary = await _indexer.IndexCourseSourcesAsync(CourseId, "Engineering Mechanics", new[]
        {
            ImageSource("a", "bending stress flexure formula"),
            ImageSource("b", "torsion shear polar moment")
        });

        summary.TotalSources.Should().Be(2);
        summary.Indexed.Should().Be(2);
        _ocr.Calls.Should().Be(2);

        var results = await _index.SearchAsync(Query("bending stress"));
        results.Should().Contain(r => r.NoteItemId == "a");
    }

    [Fact]
    public async Task Index_TextSource_DoesNotCallOcr()
    {
        var summary = await _indexer.IndexCourseSourcesAsync(CourseId, "Engineering Mechanics", new[]
        {
            new RawNoteSource
            {
                Id = "t1", PageId = "p1", PageName = "Notes",
                NotionPageUrl = "https://notion.so/p1",
                Text = "entropy heat engine efficiency"
            }
        });

        summary.Indexed.Should().Be(1);
        _ocr.Calls.Should().Be(0, "text blocks need no OCR");
        (await _index.SearchAsync(Query("entropy efficiency"))).Should().Contain(r => r.NoteItemId == "t1");
    }

    [Fact]
    public async Task Index_UnchangedContent_IsSkippedWithoutReOcr()
    {
        var sources = new[] { ImageSource("a", "bending stress flexure formula") };

        await _indexer.IndexCourseSourcesAsync(CourseId, "Engineering Mechanics", sources);
        _ocr.Calls.Should().Be(1);

        var second = await _indexer.IndexCourseSourcesAsync(CourseId, "Engineering Mechanics", sources);

        second.Skipped.Should().Be(1);
        second.Indexed.Should().Be(0);
        _ocr.Calls.Should().Be(1, "unchanged content must not be re-OCR'd (spec §47)");
    }

    [Fact]
    public async Task Index_LowConfidence_IsFlaggedButSearchable()
    {
        _ocr.Confidence = 0.4f;

        var summary = await _indexer.IndexCourseSourcesAsync(CourseId, "Engineering Mechanics", new[]
        {
            ImageSource("a", "bending stress flexure")
        });

        summary.LowConfidence.Should().Be(1);
        summary.Indexed.Should().Be(0);
        (await _index.GetIndexedCountAsync(CourseId)).Should().Be(0,
            "low-confidence items are not counted as fully indexed");
        (await _index.SearchAsync(Query("bending stress"))).Should().Contain(r => r.NoteItemId == "a",
            "but they remain searchable");
    }

    [Fact]
    public async Task Index_EmptyImageWithNoText_IsCountedFailed()
    {
        var summary = await _indexer.IndexCourseSourcesAsync(CourseId, "Engineering Mechanics", new[]
        {
            new RawNoteSource
            {
                Id = "bad", PageId = "p1", PageName = "Bending",
                NotionPageUrl = "https://notion.so/p1",
                ImageBytes = Array.Empty<byte>()
            }
        });

        summary.Failed.Should().Be(1);
        summary.Indexed.Should().Be(0);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
