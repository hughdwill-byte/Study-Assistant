using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Models;
using StudyHud.Core.Services;

namespace StudyHud.Storage;

/// <summary>
/// SQLite implementation of <see cref="ICourseRepository"/> (spec §43, §60). Reads/writes the
/// <c>courses</c> table and derives per-course index health from <c>note_items</c>. Parameterised SQL
/// throughout; short-lived connections (spec §49).
/// </summary>
public sealed class CourseRepository : ICourseRepository
{
    private readonly string _dbPath;
    private readonly ILogger<CourseRepository> _logger;

    public CourseRepository(string dbPath, ILogger<CourseRepository> logger)
    {
        _dbPath = dbPath;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Course>> GetAllAsync(CancellationToken ct = default)
    {
        using var conn = OpenRead();
        await conn.OpenAsync(ct).ConfigureAwait(false);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectColumns + " ORDER BY name COLLATE NOCASE";

        var courses = new List<Course>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            courses.Add(ReadCourse(reader));
        return courses;
    }

    public async Task<Course?> GetAsync(string courseId, CancellationToken ct = default)
    {
        using var conn = OpenRead();
        await conn.OpenAsync(ct).ConfigureAwait(false);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectColumns + " WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", courseId);

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadCourse(reader) : null;
    }

    public async Task UpsertAsync(Course course, CancellationToken ct = default)
    {
        using var conn = OpenWrite();
        await conn.OpenAsync(ct).ConfigureAwait(false);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO courses
                (id, name, description, notion_root_page_id, preferred_macro_profile_id,
                 preferred_workspace, preferred_layout_id, auto_activate, created_at, last_synced_at)
            VALUES
                (@id, @name, @desc, @root, @profile, @workspace, @layout, @auto, @created, @synced)
            ON CONFLICT(id) DO UPDATE SET
                name=excluded.name, description=excluded.description,
                notion_root_page_id=excluded.notion_root_page_id,
                preferred_macro_profile_id=excluded.preferred_macro_profile_id,
                preferred_workspace=excluded.preferred_workspace,
                preferred_layout_id=excluded.preferred_layout_id,
                auto_activate=excluded.auto_activate
            """;
        cmd.Parameters.AddWithValue("@id", course.CourseId);
        cmd.Parameters.AddWithValue("@name", course.Name);
        cmd.Parameters.AddWithValue("@desc", (object?)course.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@root", (object?)course.NotionRootPageId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@profile", (object?)course.PreferredMacroProfileId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@workspace", (object?)course.PreferredWorkspace?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@layout", (object?)course.PreferredLayoutId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@auto", course.AutoActivateOnSwitch ? 1 : 0);
        cmd.Parameters.AddWithValue("@created", course.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@synced", (object?)course.LastSyncedAt?.ToString("O") ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string courseId, CancellationToken ct = default)
    {
        using var conn = OpenWrite();
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // Remove dependent rows first (note_items has an FK to courses) in one transaction.
        await ExecAsync(conn, "BEGIN", ct).ConfigureAwait(false);
        try
        {
            await ExecAsync(conn,
                "DELETE FROM note_fts WHERE note_item_id IN (SELECT id FROM note_items WHERE course_id=@c)",
                ct, ("@c", courseId)).ConfigureAwait(false);
            await ExecAsync(conn, "DELETE FROM note_items WHERE course_id=@c", ct, ("@c", courseId)).ConfigureAwait(false);
            await ExecAsync(conn, "DELETE FROM sync_state WHERE course_id=@c", ct, ("@c", courseId)).ConfigureAwait(false);
            await ExecAsync(conn, "DELETE FROM courses WHERE id=@c", ct, ("@c", courseId)).ConfigureAwait(false);
            await ExecAsync(conn, "COMMIT", ct).ConfigureAwait(false);
        }
        catch
        {
            await ExecAsync(conn, "ROLLBACK", ct).ConfigureAwait(false);
            throw;
        }
    }

    public async Task SetLastSyncedAsync(string courseId, DateTimeOffset when, CancellationToken ct = default)
    {
        using var conn = OpenWrite();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await ExecAsync(conn, "UPDATE courses SET last_synced_at=@t WHERE id=@c", ct,
            ("@t", when.ToString("O")), ("@c", courseId)).ConfigureAwait(false);
    }

    public async Task<CourseIndexHealth> GetIndexHealthAsync(string courseId, CancellationToken ct = default)
    {
        using var conn = OpenRead();
        await conn.OpenAsync(ct).ConfigureAwait(false);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                COUNT(*)                                                        AS total,
                SUM(CASE WHEN source_type='image'      THEN 1 ELSE 0 END)       AS images,
                SUM(CASE WHEN ocr_state='indexed'       THEN 1 ELSE 0 END)      AS indexed,
                SUM(CASE WHEN ocr_state='low_confidence' THEN 1 ELSE 0 END)     AS low_conf,
                SUM(CASE WHEN ocr_state='failed'         THEN 1 ELSE 0 END)     AS failed,
                COUNT(DISTINCT page_id)                                         AS pages,
                COUNT(DISTINCT COALESCE(week_id, week_label))                   AS weeks
            FROM note_items WHERE course_id=@c
            """;
        cmd.Parameters.AddWithValue("@c", courseId);

        int total = 0, images = 0, indexed = 0, lowConf = 0, failed = 0, pages = 0, weeks = 0;
        using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                total   = GetInt(reader, 0);
                images  = GetInt(reader, 1);
                indexed = GetInt(reader, 2);
                lowConf = GetInt(reader, 3);
                failed  = GetInt(reader, 4);
                pages   = GetInt(reader, 5);
                weeks   = GetInt(reader, 6);
            }
        }

        var course = await GetAsync(courseId, ct).ConfigureAwait(false);

        var status = total == 0 ? IndexStatus.NotSynced
            : (failed > 0 || lowConf > 0) ? IndexStatus.NeedsReview
            : indexed < total ? IndexStatus.PartiallyIndexed
            : IndexStatus.Ready;

        return new CourseIndexHealth
        {
            CourseId = courseId,
            TotalWeeks = weeks,
            TotalPages = pages,
            TotalImages = images,
            IndexedImages = indexed,
            LowConfidenceItems = lowConf,
            UnavailableSourceItems = failed,
            LastSuccessfulSync = course?.LastSyncedAt,
            Status = status
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private const string SelectColumns = """
        SELECT id, name, description, notion_root_page_id, preferred_macro_profile_id,
               preferred_workspace, preferred_layout_id, auto_activate, created_at, last_synced_at
        FROM courses
        """;

    private static Course ReadCourse(DbDataReader r) => new()
    {
        CourseId = r.GetString(0),
        Name = r.GetString(1),
        Description = r.IsDBNull(2) ? null : r.GetString(2),
        NotionRootPageId = r.IsDBNull(3) ? null : r.GetString(3),
        PreferredMacroProfileId = r.IsDBNull(4) ? null : r.GetString(4),
        PreferredWorkspace = r.IsDBNull(5) ? null : ParseWorkspace(r.GetString(5)),
        PreferredLayoutId = r.IsDBNull(6) ? null : r.GetString(6),
        AutoActivateOnSwitch = r.GetInt64(7) != 0,
        CreatedAt = ParseDate(r, 8) ?? DateTimeOffset.UtcNow,
        LastSyncedAt = ParseDate(r, 9)
    };

    private static WorkspaceId? ParseWorkspace(string s)
        => Enum.TryParse<WorkspaceId>(s, out var w) ? w : null;

    private static DateTimeOffset? ParseDate(DbDataReader r, int i)
        => r.IsDBNull(i) ? null
           : DateTimeOffset.TryParse(r.GetString(i), out var d) ? d : null;

    private static int GetInt(DbDataReader r, int i) => r.IsDBNull(i) ? 0 : (int)r.GetInt64(i);

    private SqliteConnection OpenRead() => new($"Data Source={_dbPath};Mode=ReadOnly");
    private SqliteConnection OpenWrite() => new($"Data Source={_dbPath};Mode=ReadWrite;Cache=Shared");

    private static async Task ExecAsync(
        SqliteConnection conn, string sql, CancellationToken ct, params (string Name, object Value)[] ps)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
