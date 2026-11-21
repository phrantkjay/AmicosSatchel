using System;
using System.Collections.Generic;
using System.Linq;

namespace AmicosSatchel.Modules.FcCommandCenter.Data;

/// <summary>
/// Keeps the chest change log, gil history and voyage loot log.
/// </summary>
public static class History {
    /// <summary>Changes noticed by the same person this close together count as one visit to the chest.</summary>
    private static readonly TimeSpan SameVisit = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Works out what was added and taken between two readings of the same pages, by item.
    /// Only pages read both times count, and items moved between those pages cancel out.
    /// </summary>
    public static List<(uint ItemId, int Delta)> Diff(IReadOnlyDictionary<int, List<ChestItem>> before, IReadOnlyDictionary<int, List<ChestItem>> after) {
        var pages = before.Keys.Intersect(after.Keys).ToList();
        var totals = new Dictionary<uint, int>();

        foreach (var page in pages) {
            foreach (var item in before[page]) totals[item.ItemId] = totals.GetValueOrDefault(item.ItemId) - item.Quantity;
            foreach (var item in after[page]) totals[item.ItemId] = totals.GetValueOrDefault(item.ItemId) + item.Quantity;
        }

        return totals.Where(t => t.Value != 0).Select(t => (t.Key, t.Value)).OrderBy(t => t.Key).ToList();
    }

    /// <summary>
    /// Adds changes to the log. Within one visit, changes to the same item are added together,
    /// so taking 10 and putting 4 back shows as −6.
    /// </summary>
    /// <returns>Whether the log changed.</returns>
    public static bool RecordChestChanges(FcBoard board, IEnumerable<(uint ItemId, int Delta)> changes, string readBy, DateTime nowUtc) {
        var changed = false;

        foreach (var (itemId, delta) in changes) {
            var visit = board.ChestChanges.LastOrDefault(c => c.ItemId == itemId && c.ReadBy == readBy && nowUtc - c.SeenUtc < SameVisit);
            if (visit is null) {
                board.ChestChanges.Add(new ChestChange { ItemId = itemId, Delta = delta, SeenUtc = nowUtc, ReadBy = readBy, UpdatedBy = readBy });
            } else {
                visit.Delta += delta;
                visit.SeenUtc = nowUtc;
                visit.Deleted = visit.Delta == 0;
                visit.Touch(readBy);
            }
            changed = true;
        }

        return changed;
    }

    /// <summary>Adds a gil reading if it differs from the last one.</summary>
    public static bool RecordGil(ChestSnapshot chest, long gil, DateTime nowUtc) {
        if (chest.GilHistory.Count > 0 && chest.GilHistory[^1].Gil == gil) return false;
        chest.GilHistory.Add(new GilReading { Utc = nowUtc, Gil = gil });
        return true;
    }

    /// <summary>Gil change since <paramref name="sinceUtc"/>, or null without a reading from before then.</summary>
    public static long? GilChangeSince(ChestSnapshot chest, DateTime sinceUtc) {
        var before = chest.GilHistory.LastOrDefault(g => g.Utc <= sinceUtc);
        if (before is null || chest.GilHistory.Count == 0) return null;
        return chest.GilHistory[^1].Gil - before.Gil;
    }

    /// <summary>Adds gil readings from someone else's copy, keeping one list in time order.</summary>
    public static int MergeGil(ChestSnapshot target, IEnumerable<GilReading> incoming) {
        var known = target.GilHistory.Select(g => g.Utc).ToHashSet();
        var added = 0;
        foreach (var reading in incoming) {
            if (!known.Add(reading.Utc)) continue;
            target.GilHistory.Add(reading);
            added++;
        }
        if (added > 0) target.GilHistory.Sort((a, b) => a.Utc.CompareTo(b.Utc));
        return added;
    }

    /// <summary>Adds a finished voyage's loot unless it's already logged.</summary>
    public static bool RecordLoot(FcBoard board, VoyageLoot loot) {
        if (loot.Items.Count == 0 || board.Loot.Any(l => l.Id == loot.Id)) return false;
        board.Loot.Add(loot);
        return true;
    }
}
