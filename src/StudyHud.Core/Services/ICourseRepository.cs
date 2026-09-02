using StudyHud.Core.Models;

namespace StudyHud.Core.Services;

/// <summary>
/// Persists <see cref="Course"/> records and reports per-course index health (spec §43, §48, §60).
/// This is the single writer for the <c>courses</c> table; the search index only ensures a course
/// row exists as a foreign-key target.
/// </summary>
public interface ICourseRepository
{
    Task<IReadOnlyList<Course>> GetAllAsync(CancellationToken ct = default);

    Task<Course?> GetAsync(string courseId, CancellationToken ct = default);

    /// <summary>Inserts or updates a course (keyed on <see cref="Course.CourseId"/>).</summary>
    Task UpsertAsync(Course course, CancellationToken ct = default);

    /// <summary>Deletes a course and all of its note items / full-text rows / sync state.</summary>
    Task DeleteAsync(string courseId, CancellationToken ct = default);

    /// <summary>Records the time of the last successful sync for a course (spec §60 "Last sync").</summary>
    Task SetLastSyncedAsync(string courseId, DateTimeOffset when, CancellationToken ct = default);

    /// <summary>
    /// Computes the library health summary for a course from the indexed note items (spec §60):
    /// page/week/image counts, how many are indexed, low-confidence, or failed, and a derived status.
    /// </summary>
    Task<CourseIndexHealth> GetIndexHealthAsync(string courseId, CancellationToken ct = default);
}
