using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace StudyHud.Storage;

/// <summary>
/// SQLite database initialisation and versioned migrations (spec §49, §176).
/// Enables WAL mode, sets a busy timeout, applies foreign keys.
/// Each migration is atomic and idempotent.
/// </summary>
public sealed class DatabaseMigrator
{
    private readonly string _dbPath;
    private readonly ILogger<DatabaseMigrator> _logger;

    private const int CurrentSchemaVersion = 1;

    public DatabaseMigrator(string dbPath, ILogger<DatabaseMigrator> logger)
    {
        _dbPath = dbPath;
        _logger = logger;
    }

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting database migration. DB={Path}", _dbPath);

        using var conn = OpenWriteConnection();
        await conn.OpenAsync(ct);

        // Enable WAL and sensible settings (spec §49, §176)
        await ExecuteAsync(conn, "PRAGMA journal_mode=WAL;", ct);
        await ExecuteAsync(conn, "PRAGMA foreign_keys=ON;", ct);
        await ExecuteAsync(conn, "PRAGMA busy_timeout=5000;", ct);
        await ExecuteAsync(conn, "PRAGMA synchronous=NORMAL;", ct);

        int version = await GetSchemaVersionAsync(conn, ct);
        _logger.LogInformation("Current schema version: {Version}.", version);

        if (version < 1) await ApplyMigration1Async(conn, ct);

        await ExecuteAsync(conn, $"PRAGMA user_version={CurrentSchemaVersion};", ct);
        _logger.LogInformation("Database at version {Version}.", CurrentSchemaVersion);
    }

    private async Task ApplyMigration1Async(SqliteConnection conn, CancellationToken ct)
    {
        _logger.LogInformation("Applying migration 1: initial schema.");

        // All DDL in one transaction
        using var tx = conn.BeginTransaction();
        try
        {
            // Courses
            await ExecuteAsync(conn, ct, """
                CREATE TABLE IF NOT EXISTS courses (
                    id                  TEXT PRIMARY KEY NOT NULL,
                    name                TEXT NOT NULL,
                    description         TEXT,
                    notion_root_page_id TEXT,
                    preferred_macro_profile_id TEXT,
                    preferred_workspace TEXT,
                    preferred_layout_id TEXT,
                    auto_activate       INTEGER NOT NULL DEFAULT 1,
                    created_at          TEXT NOT NULL,
                    last_synced_at      TEXT
                )
                """);

            // Note items (each OCR-able block in a note page)
            await ExecuteAsync(conn, ct, """
                CREATE TABLE IF NOT EXISTS note_items (
                    id               TEXT PRIMARY KEY NOT NULL,
                    course_id        TEXT NOT NULL REFERENCES courses(id),
                    week_id          TEXT,
                    week_label       TEXT,
                    page_id          TEXT NOT NULL,
                    page_name        TEXT NOT NULL,
                    heading_path     TEXT NOT NULL DEFAULT '',
                    heading_text     TEXT,
                    source_type      TEXT NOT NULL,    -- 'image' | 'text'
                    notion_page_url  TEXT NOT NULL,
                    notion_block_id  TEXT,
                    local_cache_id   TEXT,
                    image_hash       TEXT,
                    ocr_raw_text     TEXT,
                    ocr_normalised   TEXT,
                    ocr_confidence   REAL,
                    ocr_state        TEXT NOT NULL DEFAULT 'pending',
                    last_indexed_at  TEXT
                )
                """);

            // FTS5 virtual table for full-text search (spec §49)
            await ExecuteAsync(conn, ct, """
                CREATE VIRTUAL TABLE IF NOT EXISTS note_fts USING fts5(
                    ocr_normalised,
                    heading_path,
                    page_name,
                    content='note_items',
                    content_rowid='rowid',
                    tokenize='unicode61 remove_diacritics 1'
                )
                """);

            // Sync state for incremental Notion sync (spec §47)
            await ExecuteAsync(conn, ct, """
                CREATE TABLE IF NOT EXISTS sync_state (
                    id              TEXT PRIMARY KEY NOT NULL,
                    course_id       TEXT NOT NULL REFERENCES courses(id),
                    notion_page_id  TEXT NOT NULL,
                    last_edited_at  TEXT,
                    content_hash    TEXT,
                    sync_status     TEXT NOT NULL DEFAULT 'pending',
                    last_synced_at  TEXT
                )
                """);

            // Macro profiles and definitions
            await ExecuteAsync(conn, ct, """
                CREATE TABLE IF NOT EXISTS macro_profiles (
                    id          TEXT PRIMARY KEY NOT NULL,
                    name        TEXT NOT NULL,
                    enabled     INTEGER NOT NULL DEFAULT 1,
                    workspace   TEXT,
                    description TEXT,
                    macro_ids   TEXT NOT NULL DEFAULT '[]'
                )
                """);

            await ExecuteAsync(conn, ct, """
                CREATE TABLE IF NOT EXISTS macros (
                    id           TEXT PRIMARY KEY NOT NULL,
                    name         TEXT NOT NULL,
                    enabled      INTEGER NOT NULL DEFAULT 1,
                    trigger_json TEXT NOT NULL,
                    conditions_json TEXT NOT NULL DEFAULT '{}',
                    actions_json TEXT NOT NULL DEFAULT '[]',
                    cooldown_ms  INTEGER NOT NULL DEFAULT 0,
                    on_failure   TEXT NOT NULL DEFAULT 'StopMacro',
                    description  TEXT
                )
                """);

            // Panel layouts (spec §19)
            await ExecuteAsync(conn, ct, """
                CREATE TABLE IF NOT EXISTS panel_layouts (
                    layout_id    TEXT NOT NULL,
                    panel_id     TEXT NOT NULL,
                    workspace    TEXT NOT NULL,
                    monitor_id   TEXT NOT NULL,
                    norm_left    REAL NOT NULL,
                    norm_top     REAL NOT NULL,
                    norm_right   REAL NOT NULL,
                    norm_bottom  REAL NOT NULL,
                    logical_w    REAL NOT NULL,
                    logical_h    REAL NOT NULL,
                    visibility   TEXT NOT NULL DEFAULT 'Expanded',
                    responsive   TEXT NOT NULL DEFAULT 'Normal',
                    dock_state   TEXT NOT NULL DEFAULT 'Floating',
                    collapse_dir TEXT NOT NULL DEFAULT 'None',
                    dock_group_id TEXT,
                    reveal_tab_offset REAL NOT NULL DEFAULT 0.5,
                    z_order      INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (layout_id, panel_id)
                )
                """);

            // Dock groups
            await ExecuteAsync(conn, ct, """
                CREATE TABLE IF NOT EXISTS dock_groups (
                    group_id         TEXT PRIMARY KEY NOT NULL,
                    layout_id        TEXT NOT NULL,
                    panel_ids_json   TEXT NOT NULL DEFAULT '[]',
                    collapse_together INTEGER NOT NULL DEFAULT 0
                )
                """);

            // Indexes
            await ExecuteAsync(conn, ct,
                "CREATE INDEX IF NOT EXISTS idx_note_items_course ON note_items(course_id)");
            await ExecuteAsync(conn, ct,
                "CREATE INDEX IF NOT EXISTS idx_note_items_state ON note_items(ocr_state)");
            await ExecuteAsync(conn, ct,
                "CREATE INDEX IF NOT EXISTS idx_sync_state_course ON sync_state(course_id)");

            tx.Commit();
            _logger.LogInformation("Migration 1 applied successfully.");
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<int> GetSchemaVersionAsync(SqliteConnection conn, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is long v ? (int)v : 0;
    }

    private static async Task ExecuteAsync(SqliteConnection conn, string sql, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task ExecuteAsync(SqliteConnection conn, CancellationToken ct, string sql)
        => await ExecuteAsync(conn, sql, ct);

    private SqliteConnection OpenWriteConnection() =>
        new($"Data Source={_dbPath};Mode=ReadWriteCreate;Cache=Shared");
}
