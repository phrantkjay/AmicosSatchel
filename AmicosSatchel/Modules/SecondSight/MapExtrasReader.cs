using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using XIVMap.Shared;

namespace AmicosSatchel.Modules.SecondSight;

/// <summary>
/// The game's map flag and the deciphered treasure map's spot. Read-only; game thread.
/// </summary>
internal static unsafe class MapExtrasReader {
    public static MapFlag? ReadFlag() {
        var agent = AgentMap.Instance();
        if (agent == null || agent->FlagMarkerCount == 0)
            return null;

        ref readonly var flag = ref agent->FlagMapMarkers[0];
        return flag.TerritoryId == 0 ? null : new MapFlag(flag.TerritoryId, flag.MapId, flag.XFloat, flag.YFloat);
    }

    /// <summary>Null unless a treasure map is deciphered. Both calls just return stored values.</summary>
    public static TreasureSpotInfo? ReadTreasure() {
        var items = EventItemManager.Instance();
        if (items == null)
            return null;

        var rank = items->GetTreasureHuntRank();
        return rank == 0 ? null : new TreasureSpotInfo(rank, items->GetTreasureSpotSubKey());
    }
}
