using System;
using System.Collections.Generic;
using System.Linq;

namespace AmicosSatchel.Modules.FcCommandCenter.Data;

/// <summary>
/// Everything the command center knows about one Free Company. Saved as one file per FC.
/// </summary>
public sealed class FcBoard {
    public const int CurrentVersion = 1;

    /// <summary>How long deleted items are kept so the deletion can still reach other copies.</summary>
    private static readonly TimeSpan KeepDeletedFor = TimeSpan.FromDays(90);

    /// <summary>How long chest changes and voyage loot are kept.</summary>
    public static readonly TimeSpan KeepLogsFor = TimeSpan.FromDays(90);

    public int Version { get; set; } = CurrentVersion;

    /// <summary>The game's id for the FC; 0 when it isn't known (not in an FC, or not read yet).</summary>
    public ulong FcId { get; set; }

    public string FcName { get; set; } = "";

    public List<FcEvent> Events { get; set; } = [];
    public List<Announcement> Announcements { get; set; } = [];
    public List<OfficerTask> Tasks { get; set; } = [];
    public List<RotationSlot> Rotation { get; set; } = [];
    public List<ChestNote> ChestNotes { get; set; } = [];
    public List<WorkshopProject> Projects { get; set; } = [];
    public List<EventSignup> Signups { get; set; } = [];
    public List<MemberNote> MemberNotes { get; set; } = [];
    public List<ChestChange> ChestChanges { get; set; } = [];
    public List<VoyageLoot> Loot { get; set; } = [];

    public ChestSnapshot Chest { get; set; } = new();
    public VoyageSnapshot? Voyages { get; set; }
    public RosterSnapshot? Roster { get; set; }

    // Only for this PC; never exported.

    /// <summary>Announcements posted after this are shown as new.</summary>
    public DateTime AnnouncementsReadUtc { get; set; }

    /// <summary>Alerts already sent (event reminders, voyages back, actions ending), as "what @ ticks".</summary>
    public HashSet<string> RemindedStarts { get; set; } = [];

    /// <summary>Drops long-deleted items and old reminder marks. Called after loading.</summary>
    public void Tidy() {
        var cutoff = DateTime.UtcNow - KeepDeletedFor;
        Events.RemoveAll(i => i.Deleted && i.UpdatedUtc < cutoff);
        Announcements.RemoveAll(i => i.Deleted && i.UpdatedUtc < cutoff);
        Tasks.RemoveAll(i => i.Deleted && i.UpdatedUtc < cutoff);
        Rotation.RemoveAll(i => i.Deleted && i.UpdatedUtc < cutoff);
        ChestNotes.RemoveAll(i => i.Deleted && i.UpdatedUtc < cutoff);
        Projects.RemoveAll(i => i.Deleted && i.UpdatedUtc < cutoff);
        MemberNotes.RemoveAll(i => i.Deleted && i.UpdatedUtc < cutoff);

        // Logs only keep a while, so files and share codes stay small.
        Signups.RemoveAll(s => s.OccurrenceUtc < DateTime.UtcNow - TimeSpan.FromDays(30));
        ChestChanges.RemoveAll(c => c.SeenUtc < DateTime.UtcNow - KeepLogsFor);
        Loot.RemoveAll(l => l.ReturnUtc < DateTime.UtcNow - KeepLogsFor);
        Chest.GilHistory.RemoveAll(g => g.Utc < DateTime.UtcNow - TimeSpan.FromDays(365));

        var oldTicks = (DateTime.UtcNow - TimeSpan.FromDays(2)).Ticks;
        RemindedStarts.RemoveWhere(key => !long.TryParse(key.AsSpan(key.LastIndexOf('@') + 1), out var ticks) || ticks < oldTicks);
    }

    /// <summary>The note for an item in the chest, if an officer wrote one.</summary>
    public ChestNote? NoteFor(uint itemId) => ChestNotes.FirstOrDefault(n => !n.Deleted && n.ItemId == itemId);
}

/// <summary>How many items an import added and changed.</summary>
public sealed record MergeResult(int Added, int Updated) {
    public static MergeResult operator +(MergeResult a, MergeResult b) => new(a.Added + b.Added, a.Updated + b.Updated);
}

public static class BoardMerge {
    /// <summary>
    /// Copies items from <paramref name="incoming"/> into <paramref name="target"/>.
    /// Items are matched by id; when both sides have one, the newer change wins.
    /// </summary>
    public static MergeResult Merge<T>(List<T> target, IEnumerable<T> incoming) where T : BoardItem {
        var byId = target.ToDictionary(i => i.Id);
        int added = 0, updated = 0;

        foreach (var item in incoming) {
            if (!byId.TryGetValue(item.Id, out var existing)) {
                target.Add(item);
                byId[item.Id] = item;
                added++;
            } else if (item.UpdatedUtc > existing.UpdatedUtc) {
                target[target.IndexOf(existing)] = item;
                byId[item.Id] = item;
                updated++;
            }
        }

        return new MergeResult(added, updated);
    }
}
