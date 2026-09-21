namespace StudyHud.Core.Services;

/// <summary>
/// Builds and searches a <em>Wordbank</em>: a deterministic, per-course glossary of the user's OWN key
/// terms and phrases, harvested from their indexed notes — page titles, heading paths, note text and
/// the OCR'd text on their images/attachments (spec §54, §71).
///
/// It exists to make note lookup <em>precise</em>: it surfaces the exact wording a concept is written
/// with in the user's material, and points back to the note it came from. Like the rest of Study HUD it
/// is local and non-generative — it never invents a term, never writes an answer, and never calls a
/// remote or generative service. It only ever reflects the words already in the user's notes, so it is
/// a study/revision aid, not an answer engine.
/// </summary>
public interface IWordbank
{
    /// <summary>
    /// Harvests the full Wordbank for a course from the local index, ordered so the most useful terms
    /// (key terms that appear in a title/heading, then the most frequent) come first.
    /// </summary>
    Task<IReadOnlyList<WordbankEntry>> BuildAsync(string courseId, CancellationToken ct = default);

    /// <summary>
    /// Looks a term or phrase up in the course Wordbank, returning the matching entries (prefix and
    /// substring matches) so the user can jump straight to the note that defines the wording they need.
    /// </summary>
    Task<IReadOnlyList<WordbankEntry>> LookupAsync(
        string courseId, string query, int maxResults = 25, CancellationToken ct = default);
}

/// <summary>A single glossary entry: one term/phrase, how often it appears, and where.</summary>
public record WordbankEntry
{
    /// <summary>The canonical wording of the term/phrase as it appears in the notes.</summary>
    public required string Term { get; init; }

    /// <summary>Total occurrences across the course corpus (drives ordering and weight).</summary>
    public required int Frequency { get; init; }

    /// <summary>True when the term appears in a page title or heading — i.e. a concept the user named.</summary>
    public required bool IsKeyTerm { get; init; }

    /// <summary>True when the term is a multi-word phrase (e.g. "bending stress") rather than a single word.</summary>
    public bool IsPhrase => Term.Contains(' ');

    /// <summary>The notes this term appears in (deduplicated, most-relevant first).</summary>
    public required IReadOnlyList<WordbankLocation> Locations { get; init; }
}

/// <summary>Where a Wordbank term was found, so the user can open that exact note.</summary>
public record WordbankLocation
{
    public required string NoteItemId { get; init; }
    public required string PageName { get; init; }
    public string? WeekLabel { get; init; }
    public string HeadingPath { get; init; } = "";
    public required string NotionPageUrl { get; init; }
    public string? NotionBlockId { get; init; }
}
