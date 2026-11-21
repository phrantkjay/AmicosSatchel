using FFXIVClientStructs.FFXIV.Client.Game;
using XIVMap.Shared;

namespace AmicosSatchel.Modules.SecondSight;

/// <summary>Reads territory, map, instance and housing location. Read-only calls; game thread only.</summary>
internal static class LocationReader {
    public static ZoneMessage Read()
        => new(Services.ClientState.TerritoryType, Services.ClientState.MapId, Services.ClientState.Instance, ReadHousing());

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
