using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using XIVMap.Shared;
using OnlineStatus = FFXIVClientStructs.FFXIV.Client.UI.Info.InfoProxyCommonList.CharacterData.OnlineStatus;

namespace AmicosSatchel.Modules.SecondSight;

/// <summary>
/// Reads the online friends from the game's Friend List: name, world and the zone they're in.
/// Read-only; it never asks the server for a refresh. Game thread only.
/// </summary>
internal static unsafe class FriendReader {
    public static List<FriendInfo> Read() {
        var result = new List<FriendInfo>();
        var list = InfoProxyFriendList.Instance();
        if (list == null)
            return result;

        var myWorld = Services.ObjectTable.LocalPlayer?.CurrentWorld.RowId ?? 0;
        var worlds = Services.DataManager.GetExcelSheet<World>();
        var count = list->GetEntryCount();
        for (var i = 0u; i < count; i++) {
            var entry = list->GetEntry(i);
            if (entry == null || (entry->State & OnlineStatus.Online) == 0)
                continue;

            var name = entry->NameString;
            if (string.IsNullOrEmpty(name))
                continue;

            var world = worlds.GetRowOrDefault(entry->CurrentWorld)?.Name.ExtractText() ?? "";
            result.Add(new FriendInfo(name, world, entry->Location, myWorld != 0 && entry->CurrentWorld != myWorld, Status(entry->State)));
        }

        return result.OrderBy(f => f.Name, System.StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Status(OnlineStatus state) {
        if ((state & OnlineStatus.InDuty) != 0) return "In a duty";
        if ((state & OnlineStatus.ViewingCutscene) != 0) return "Watching a cutscene";
        if ((state & OnlineStatus.AwayFromKeyboard) != 0) return "Away";
        if ((state & OnlineStatus.Busy) != 0) return "Busy";
        if ((state & OnlineStatus.PvP) != 0) return "In PvP";
        return "";
    }
}
