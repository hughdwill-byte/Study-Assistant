using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StudyHud.Core.Services;
using StudyHud.Search;
using StudyHud.Storage;
using Xunit;

namespace StudyHud.Tests;

// ─── Search index write + read integration (spec §49, §50, §54, §55) ─────────
//
// Exercises the full local pipeline against a real SQLite database: migrate → index
// note items → deterministic FTS/BM25 search with variable/word boosts and explanations.

public sealed class SearchIndexTests : IAsyncLifetime, IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;
    private LocalSearchIndex _index = null!;

    private const string CourseId = "eng-mech";

    public SearchIndexTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "StudyHudTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "studyhud.db");
    }

    public async Task InitializeAsync()
    {
        var migrator = new DatabaseMigrator(_dbPath, Mock.Of<ILogger<DatabaseMigrator>>());
        await migrator.MigrateAsync();

        _index = new LocalSearchIndex(_dbPath, Mock.Of<ILogger<LocalSearchIndex>>());
        await _index.UpsertCourseAsync(CourseId, "Engineering Mechanics");
        await _index.IndexItemsAsync(new[]
        {
            new IndexableNoteItem
            {
                Id = "flexure",
                CourseId = CourseId,
                WeekLabel = "Week 6",
                PageId = "p1",
                PageName = "Bending",
                HeadingPath = "Bending > Flexure Formula",
                HeadingText = "Flexure Formula",
                NotionPageUrl = "https://notion.so/p1",
                ContentHash = "hash-flexure",
                OcrNormalised = "Flexure formula bending stress sigma = My/I maximum bending moment",
                OcrConfidence = 0.95f
            },
            new IndexableNoteItem
            {
                Id = "thermo",
                CourseId = CourseId,
                WeekLabel = "Week 3",
                PageId = "p2",
                PageName = "Thermodynamics",
                HeadingPath = "Thermodynamics > First Law",
                HeadingText = "First Law",
                NotionPageUrl = "https://notion.so/p2",
                ContentHash = "hash-thermo",
                OcrNormalised = "Thermodynamics first law entropy heat engine efficiency",
                OcrConfidence = 0.9f
            }
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static SearchQuery Query(string text, int max = 3) => new()
    {
        RawText = text,
        Features = FeatureExtractor.Extract(text),
        CourseId = CourseId,
        MaxResults = max
    };

    [Fact]
    public async Task IsReady_AfterIndexing_IsTrue()
    {
        (await _index.IsReadyAsync(CourseId)).Should().BeTrue();
        (await _index.GetIndexedCountAsync(CourseId)).Should().Be(2);
    }

    [Fact]
    public async Task Search_BendingStressQuestion_RanksFlexureFirst()
    {
        var results = await _index.SearchAsync(Query("Determine the maximum bending stress M and I"));

        results.Should().NotBeEmpty();
        results[0].NoteItemId.Should().Be("flexure");
        results[0].MatchScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Search_UnrelatedQuery_DoesNotReturnFlexure()
    {
        var results = await _index.SearchAsync(Query("entropy heat engine efficiency"));

        results.Should().OnlyContain(r => r.NoteItemId == "thermo");
    }

    [Fact]
    public async Task Search_ProducesExplanations()
    {
        var results = await _index.SearchAsync(Query("bending stress M I"));

        var flexure = results.Single(r => r.NoteItemId == "flexure");
        flexure.Explanations.Should().Contain(e => e.Type == MatchType.Word && e.Value == "bending");
        flexure.Explanations.Should().Contain(e => e.Type == MatchType.Variable && e.Value == "M");
    }

    [Fact]
    public async Task GetContentHashes_ReturnsStoredHashes()
    {
        var hashes = await _index.GetContentHashesAsync(CourseId);

        hashes.Should().ContainKey("flexure").WhoseValue.Should().Be("hash-flexure");
        hashes.Should().ContainKey("thermo").WhoseValue.Should().Be("hash-thermo");
    }

    [Fact]
    public async Task IndexItems_ReplacesExistingItem()
    {
        // Re-index the same id with new content; the old FTS row must not linger.
        await _index.IndexItemsAsync(new[]
        {
            new IndexableNoteItem
            {
                Id = "flexure",
                CourseId = CourseId,
                PageId = "p1",
                PageName = "Bending",
                HeadingPath = "Bending > Flexure Formula",
                NotionPageUrl = "https://notion.so/p1",
                ContentHash = "hash-flexure-v2",
                OcrNormalised = "torsion shear stress polar moment of inertia",
                OcrConfidence = 0.95f
            }
        });

        (await _index.GetIndexedCountAsync(CourseId)).Should().Be(2, "re-indexing replaces, not duplicates");
        // "flexure"/"formula" were unique to the old content and must be gone from the index.
        var stale = await _index.SearchAsync(Query("flexure formula"));
        stale.Should().NotContain(r => r.NoteItemId == "flexure",
            "the replaced item's old full-text row must be removed");
        var fresh = await _index.SearchAsync(Query("torsion shear"));
        fresh.Should().Contain(r => r.NoteItemId == "flexure");
    }

    [Fact]
    public async Task DeleteCourse_RemovesAllItemsAndFtsRows()
    {
        await _index.DeleteCourseAsync(CourseId);

        (await _index.GetIndexedCountAsync(CourseId)).Should().Be(0);
        (await _index.SearchAsync(Query("bending stress"))).Should().BeEmpty();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
