using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StudyHud.Core.Services;
using StudyHud.Search;
using StudyHud.Storage;
using Xunit;

namespace StudyHud.Tests;

// ─── Wordbank harvest + lookup over a real local index (spec §54, §71) ────────
//
// The Wordbank turns the user's OWN indexed notes (titles, headings, text and OCR'd image text)
// into a searchable glossary. It is deterministic, local, and never generates wording of its own.

public sealed class WordbankTests : IAsyncLifetime, IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;
    private LocalSearchIndex _index = null!;
    private Wordbank _wordbank = null!;

    private const string CourseId = "eng-mech";

    public WordbankTests()
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
        _wordbank = new Wordbank(_index, Mock.Of<ILogger<Wordbank>>());

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

    [Fact]
    public async Task Build_FlagsHeadingAndTitleTermsAsKeyTerms()
    {
        var wb = await _wordbank.BuildAsync(CourseId);

        var flexure = wb.Single(e => e.Term == "flexure");
        flexure.IsKeyTerm.Should().BeTrue("'flexure' appears in the heading 'Flexure Formula'");

        var phrase = wb.Single(e => e.Term == "flexure formula");
        phrase.IsKeyTerm.Should().BeTrue();
        phrase.IsPhrase.Should().BeTrue();
    }

    [Fact]
    public async Task Build_OrdersKeyTermsBeforeBodyOnlyTerms()
    {
        var wb = await _wordbank.BuildAsync(CourseId);

        int firstKey = wb.ToList().FindIndex(e => e.IsKeyTerm);
        int firstNonKey = wb.ToList().FindIndex(e => !e.IsKeyTerm);

        firstKey.Should().Be(0);
        if (firstNonKey >= 0)
            firstNonKey.Should().BeGreaterThan(firstKey, "key terms sort ahead of body-only terms");
    }

    [Fact]
    public async Task Build_LinksTermsToTheNoteTheyCameFrom()
    {
        var wb = await _wordbank.BuildAsync(CourseId);

        var term = wb.Single(e => e.Term == "thermodynamics");
        term.Locations.Should().ContainSingle();
        term.Locations[0].NoteItemId.Should().Be("thermo");
        term.Locations[0].PageName.Should().Be("Thermodynamics");
        term.Locations[0].NotionPageUrl.Should().Be("https://notion.so/p2");
    }

    [Fact]
    public async Task Build_ExcludesRareBodyOnlyTerms_ButLookupStillFindsThem()
    {
        var wb = await _wordbank.BuildAsync(CourseId);
        // "bending stress" occurs once, only in body text → not curated into Build...
        wb.Should().NotContain(e => e.Term == "bending stress");

        // ...but is still findable when the user searches the Wordbank.
        var hits = await _wordbank.LookupAsync(CourseId, "bending stress");
        hits.Should().Contain(e => e.Term == "bending stress");
    }

    [Fact]
    public async Task Lookup_RanksExactThenPrefixThenSubstring()
    {
        var hits = await _wordbank.LookupAsync(CourseId, "bending");

        hits.Should().NotBeEmpty();
        hits[0].Term.Should().Be("bending", "an exact term ranks above phrases that merely contain it");
        hits.Should().Contain(e => e.Term == "bending moment");
    }

    [Fact]
    public async Task Lookup_EmptyQuery_ReturnsEmpty()
    {
        (await _wordbank.LookupAsync(CourseId, "")).Should().BeEmpty();
        (await _wordbank.LookupAsync(CourseId, "   ")).Should().BeEmpty();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
