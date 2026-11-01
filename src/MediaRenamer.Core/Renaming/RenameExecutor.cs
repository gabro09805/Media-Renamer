using System.Text.Json;

namespace MediaRenamer.Core.Renaming;

public sealed record JournalEntry(string From, string To, DateTime Time);

public sealed record RenameOutcome(int Renamed, IReadOnlyList<(string Source, string Error)> Failures, string? JournalPath);

/// <summary>
/// Applies a plan: never overwrites, goes through temporary names when targets and sources overlap,
/// and records every move in a journal so it can be undone.
/// </summary>
public sealed class RenameExecutor
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public RenameOutcome Execute(IReadOnlyList<PlanItem> plan, string? journalPath = null)
    {
        var ops = plan.Where(p => p.CanRename && p.Target is not null)
            .Select(p => (From: Path.GetFullPath(p.Source), To: p.Target!)).ToList();
        var journal = new List<JournalEntry>();
        var failures = new List<(string, string)>();
        var sources = ops.Select(o => o.From).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Phase 1: items whose target is another item's source (or a case-only change) move to a temp name first.
        var staged = new List<(string Temp, string From, string To)>();
        var direct = new List<(string From, string To)>();
        foreach (var (from, to) in ops)
        {
            if (!File.Exists(from)) { failures.Add((from, "The source file no longer exists.")); continue; }
            var needsTemp = sources.Contains(to) || string.Equals(from, to, StringComparison.OrdinalIgnoreCase);
            if (!needsTemp) { direct.Add((from, to)); continue; }
            var temp = Path.Combine(Path.GetDirectoryName(from)!, ".mr-" + Guid.NewGuid().ToString("N") + ".tmp");
            try { File.Move(from, temp, false); staged.Add((temp, from, to)); }
            catch (Exception ex) { failures.Add((from, ex.Message)); }
        }

        foreach (var (from, to) in direct)
            Move(from, from, to, journal, failures);
        foreach (var (temp, from, to) in staged)
            Move(temp, from, to, journal, failures);

        if (journalPath is not null && journal.Count > 0) WriteJournal(journalPath, journal);
        return new RenameOutcome(journal.Count, failures, journal.Count > 0 ? journalPath : null);
    }

    private static void Move(string current, string original, string to, List<JournalEntry> journal, List<(string, string)> failures)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Move(current, to, false);
            journal.Add(new JournalEntry(original, to, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            failures.Add((original, ex.Message));
            // Put a staged file back where it came from.
            if (current != original && File.Exists(current) && !File.Exists(original))
                try { File.Move(current, original, false); } catch { /* left under its temp name; reported above */ }
        }
    }

    private static void WriteJournal(string path, List<JournalEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(entries, Json));
    }

    /// <summary>Restores the original names recorded in a journal, in reverse order, skipping anything that would overwrite.</summary>
    public RenameOutcome Undo(string journalPath)
    {
        var entries = JsonSerializer.Deserialize<List<JournalEntry>>(File.ReadAllText(journalPath)) ?? new();
        var failures = new List<(string, string)>();
        var restored = 0;
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var e = entries[i];
            try
            {
                if (!File.Exists(e.To)) { failures.Add((e.To, "File no longer exists.")); continue; }
                if (File.Exists(e.From)) { failures.Add((e.To, "Original name is occupied.")); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(e.From)!);
                File.Move(e.To, e.From, false);
                restored++;
            }
            catch (Exception ex) { failures.Add((e.To, ex.Message)); }
        }
        return new RenameOutcome(restored, failures, journalPath);
    }
}
