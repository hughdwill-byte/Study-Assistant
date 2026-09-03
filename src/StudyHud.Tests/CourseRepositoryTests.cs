using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StudyHud.Core.Models;
using StudyHud.Core.Services;
using StudyHud.Search;
using StudyHud.Storage;
using Xunit;

namespace StudyHud.Tests;

// ─── Course repository + index health (spec §43, §60) ────────────────────────

public sealed class CourseRepositoryTests : IAsyncLifetime, IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;
    private CourseRepository _repo = null!;
    private LocalSearchIndex _index = null!;

    public CourseRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "StudyHudTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "studyhud.db");
    }

    public async Task InitializeAsync()
    {
        await new DatabaseMigrator(_dbPath, Mock.Of<ILogger<DatabaseMigrator>>()).MigrateAsync();
        _repo = new CourseRepository(_dbPath, Mock.Of<ILogger<CourseRepository>>());
        _index = new LocalSearchIndex(_dbPath, Mock.Of<ILogger<LocalSearchIndex>>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Course Sample(string id = "eng") => new()
    {
        CourseId = id,
        Name = "Engineering Mechanics",
        Description = "Statics and dynamics",
        NotionRootPageId = "notion-page-123",
        PreferredWorkspace = WorkspaceId.QuestionFinder,
        AutoActivateOnSwitch = true
    };

    private IndexableNoteItem Item(string id, string state) => new()
    {
        Id = id, CourseId = "eng", PageId = "p1", PageName = "Bending",
        WeekLabel = "Week 6", SourceType = "image",
        NotionPageUrl = "https://notion.so/p1",
        OcrNormalised = state == "failed" ? "" : "bending stress",
        OcrState = state
    };

    [Fact]
    public async Task Upsert_ThenGet_RoundTripsAllFields()
    {
        await _repo.UpsertAsync(Sample());
        var course = await _repo.GetAsync("eng");

        course.Should().NotBeNull();
        course!.Name.Should().Be("Engineering Mechanics");
        course.Description.Should().Be("Statics and dynamics");
        course.NotionRootPageId.Should().Be("notion-page-123");
        course.PreferredWorkspace.Should().Be(WorkspaceId.QuestionFinder);
        course.AutoActivateOnSwitch.Should().BeTrue();
    }

    [Fact]
    public async Task Upsert_Existing_Updates()
    {
        await _repo.UpsertAsync(Sample());
        await _repo.UpsertAsync(Sample() with { Name = "Renamed", NotionRootPageId = "new-root" });

        var course = await _repo.GetAsync("eng");
        course!.Name.Should().Be("Renamed");
        course.NotionRootPageId.Should().Be("new-root");
        (await _repo.GetAllAsync()).Should().HaveCount(1, "upsert must not duplicate");
    }

    [Fact]
    public async Task SetLastSynced_IsPersisted()
    {
        await _repo.UpsertAsync(Sample());
        var when = DateTimeOffset.UtcNow;
        await _repo.SetLastSyncedAsync("eng", when);

        var course = await _repo.GetAsync("eng");
        course!.LastSyncedAt.Should().BeCloseTo(when, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Delete_RemovesCourseAndItsNotes()
    {
        await _repo.UpsertAsync(Sample());
        await _index.IndexItemsAsync(new[] { Item("a", "indexed") });

        await _repo.DeleteAsync("eng");

        (await _repo.GetAsync("eng")).Should().BeNull();
        (await _index.GetIndexedCountAsync("eng")).Should().Be(0, "dependent note items must be removed");
    }

    [Fact]
    public async Task GetIndexHealth_ComputesCountsAndStatus()
    {
        await _repo.UpsertAsync(Sample());
        await _index.IndexItemsAsync(new[]
        {
            Item("a", "indexed"),
            Item("b", "indexed"),
            Item("c", "low_confidence"),
            Item("d", "failed")
        });

        var health = await _repo.GetIndexHealthAsync("eng");

        health.TotalImages.Should().Be(4);
        health.IndexedImages.Should().Be(2);
        health.LowConfidenceItems.Should().Be(1);
        health.UnavailableSourceItems.Should().Be(1);
        health.TotalPages.Should().Be(1);
        health.TotalWeeks.Should().Be(1);
        health.Status.Should().Be(IndexStatus.NeedsReview, "there are low-confidence and failed items");
    }

    [Fact]
    public async Task GetIndexHealth_NoItems_IsNotSynced()
    {
        await _repo.UpsertAsync(Sample());
        (await _repo.GetIndexHealthAsync("eng")).Status.Should().Be(IndexStatus.NotSynced);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
