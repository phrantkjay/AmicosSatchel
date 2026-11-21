using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace AmicosSatchel.Modules.SecondSight;

/// <summary>
/// Whether sightseeing vistas are logged. Asks Dalamud's own unlock tracking, which follows
/// game updates. Read-only; game thread.
/// </summary>
internal static unsafe class VistaLog {
    private static ExcelSheet<Adventure> Sheet => Services.DataManager.GetExcelSheet<Adventure>();

    public static bool IsLogged(uint adventureRowId)
        => Sheet.GetRowOrDefault(adventureRowId) is { } row && Services.UnlockState.IsAdventureComplete(row);

    /// <summary>
    /// "A Realm Reborn 54/80 (game memory 54)", one line per expansion, comparing Dalamud's answer
    /// with reading the game's completion bits directly, to compare with the in-game Sightseeing Log.
    /// </summary>
    public static List<string> Summary() {
        var rows = Sheet.ToList();
        var first = rows.Count > 0 ? rows[0].RowId : 0;
        var result = new List<string>();
        foreach (var expansion in rows.Where(r => r.Level.ValueNullable is { Territory.RowId: not 0 })
                     .GroupBy(r => r.Level.Value.Territory.Value.ExVersion.RowId)
                     .OrderBy(g => g.Key)) {
            var name = expansion.First().Level.Value.Territory.Value.ExVersion.ValueNullable?.Name.ExtractText();
            var dalamud = expansion.Count(r => Services.UnlockState.IsAdventureComplete(r));
            var direct = expansion.Count(r => ReadBit(r.RowId - first));
            result.Add($"{name} {dalamud}/{expansion.Count()} (game memory {direct})");
        }
        return result;
    }

    /// <summary>The game's own completion bit for the vista at this position in the sheet.</summary>
    private static bool ReadBit(uint index) {
        var player = PlayerState.Instance();
        return player != null && player->IsAdventureComplete(index);
    }
}
