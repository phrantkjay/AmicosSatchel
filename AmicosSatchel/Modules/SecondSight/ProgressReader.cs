using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using XIVMap.Shared;

namespace AmicosSatchel.Modules.SecondSight;

/// <summary>
/// Checks which aetherytes are attuned, which aether currents are collected and which
/// sightseeing vistas are logged. Read-only;
/// game thread. Returns null when nothing changed since the last call.
/// </summary>
internal sealed unsafe class ProgressReader {
    private readonly uint[] aetheryteIds;
    private readonly uint[] aetherCurrentIds;
    private readonly uint[] vistaIds;
    private readonly uint firstVistaId;
    private ProgressMessage? last;

    public ProgressReader() {
        aetheryteIds = Services.DataManager.GetExcelSheet<Aetheryte>().Where(a => a.Territory.RowId != 0).Select(a => a.RowId).ToArray();
        aetherCurrentIds = Services.DataManager.GetExcelSheet<AetherCurrent>().Select(a => a.RowId).Where(id => id != 0).ToArray();
        // The game tracks vistas by position in the Adventure sheet, whose row ids are contiguous.
        vistaIds = Services.DataManager.GetExcelSheet<Adventure>().Select(a => a.RowId).ToArray();
        firstVistaId = vistaIds.Length > 0 ? vistaIds[0] : 0;
    }

    /// <summary>The last result, so a newly connected app gets it too.</summary>
    public ProgressMessage? Last => last;

    public ProgressMessage? ReadIfChanged() {
        var ui = UIState.Instance();
        var player = PlayerState.Instance();
        if (ui == null || player == null || Services.ObjectTable.LocalPlayer == null)
            return null;

        var attuned = new List<uint>();
        foreach (var id in aetheryteIds) {
            if (ui->IsAetheryteUnlocked(id))
                attuned.Add(id);
        }

        var currents = new List<uint>();
        foreach (var id in aetherCurrentIds) {
            if (player->IsAetherCurrentUnlocked(id))
                currents.Add(id);
        }

        var vistas = new List<uint>();
        foreach (var id in vistaIds) {
            if (VistaLog.IsLogged(id))
                vistas.Add(id);
        }

        if (last != null && last.AttunedAetherytes.SequenceEqual(attuned) && last.AetherCurrents.SequenceEqual(currents)
            && last.Vistas.SequenceEqual(vistas))
            return null;

        last = new ProgressMessage(attuned, currents, vistas);
        return last;
    }
}
