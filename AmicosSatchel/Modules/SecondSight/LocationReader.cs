using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using MapSheet = Lumina.Excel.Sheets.Map;
using XIVMap.Shared;

namespace AmicosSatchel.Modules.SecondSight;

/// <summary>Reads territory, map, instance and housing location. Read-only calls; game thread only.</summary>
internal static class LocationReader {
    private static (uint Agent, uint Game, uint Override) lastSeen;

    public static ZoneMessage Read() {
        var territoryId = Services.ClientState.TerritoryType;
        return new(territoryId, CurrentMapId(territoryId), Services.ClientState.Instance, ReadHousing());
    }

    /// <summary>
    /// The map the player is on. Some zones have more than one map in the same territory (the
    /// Island Sanctuary's surface and its cave), and the game keeps the active one in more than one
    /// place, so this prefers the area override, then the game's own current map, and falls back
    /// to Dalamud's value. A candidate only counts if it is a map of this territory.
    /// </summary>
    private static unsafe uint CurrentMapId(uint territoryId) {
        var agentMap = Services.ClientState.MapId;
        var gameMain = GameMain.Instance();
        var gameMap = gameMain != null ? gameMain->CurrentMapId : 0;
        var info = TerritoryInfo.Instance();
        var overrideMap = info != null ? info->MapIdOverride : 0;

        if ((agentMap, gameMap, overrideMap) != lastSeen) {
            lastSeen = (agentMap, gameMap, overrideMap);
            Services.Log.Information($"[map] territory {territoryId}: dalamud {agentMap}, game {gameMap}, override {overrideMap}");
        }

        if (IsMapOf(overrideMap, territoryId))
            return overrideMap;
        if (gameMap != agentMap && IsMapOf(gameMap, territoryId))
            return gameMap;
        return agentMap;
    }

    private static bool IsMapOf(uint mapId, uint territoryId)
        => mapId != 0
           && Services.DataManager.GetExcelSheet<MapSheet>().GetRowOrDefault(mapId) is { } map
           && map.TerritoryType.RowId == territoryId;

    private static unsafe HousingInfo? ReadHousing() {
        var housing = HousingManager.Instance();
        if (housing == null)
            return null;

        // The game uses 0-based ward/plot numbers and -1 (or other negatives) for "none".
        var ward = housing->GetCurrentWard();
        if (ward < 0)
            return null;

        var plot = housing->GetCurrentPlot();
        var room = housing->GetCurrentRoom();

        return new HousingInfo(
            Ward: ward + 1,
            Plot: plot >= 0 ? plot + 1 : null,
            Room: room > 0 ? room : null,
            Subdivision: housing->GetCurrentDivision() == 2,
            Indoors: housing->IsInside());
    }
}
