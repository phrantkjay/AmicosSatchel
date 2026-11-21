using System;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using XIVMap.Shared;

namespace AmicosSatchel.Modules.SecondSight;

/// <summary>
/// When you decipher a treasure map, prints where its chest is buried as a chat line only you see,
/// with the game's own clickable map link. Clicking the link puts the game's flag there, so you
/// can share it with &lt;flag&gt;. Only reads the game; the flag is set by your click.
/// </summary>
internal sealed class TreasureNotifier : IDisposable {
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);

    private readonly Configuration configuration;
    private DateTime nextCheck;
    private TreasureSpotInfo? last;
    private bool seenFirst; // the map that was already deciphered when we started isn't news

    public TreasureNotifier(Configuration configuration) {
        this.configuration = configuration;
        Services.Framework.Update += OnUpdate;
    }

    public void Dispose() => Services.Framework.Update -= OnUpdate;

    private void OnUpdate(IFramework framework) {
        var now = DateTime.UtcNow;
        if (now < nextCheck) return;
        nextCheck = now + CheckInterval;
        if (Services.ObjectTable.LocalPlayer is null) return;

        var treasure = MapExtrasReader.ReadTreasure();
        var changed = treasure != last;
        last = treasure;
        if (!seenFirst) {
            seenFirst = true;
            return;
        }
        if (changed && treasure is { } spot && configuration.SecondSightTreasureChat)
            Announce(spot);
    }

    private static void Announce(TreasureSpotInfo treasure) {
        if (Services.DataManager.GetSubrowExcelSheet<TreasureSpot>().GetSubrowOrDefault(treasure.Rank, treasure.Spot) is not { } row
            || row.Location.ValueNullable is not { } level || level.Territory.RowId == 0)
            return;

        var mapId = level.Map.RowId != 0 ? level.Map.RowId : level.Territory.Value.Map.RowId;
        if (Services.DataManager.GetExcelSheet<Map>().GetRowOrDefault(mapId) is not { } map) return;

        var x = MapCoords.WorldToMap(level.X, map.SizeFactor, map.OffsetX);
        var y = MapCoords.WorldToMap(level.Z, map.SizeFactor, map.OffsetY);
        var mapName = Services.DataManager.GetExcelSheet<TreasureHuntRank>().GetRowOrDefault(treasure.Rank)?.ItemName.ValueNullable?.Name.ExtractText();
        if (string.IsNullOrEmpty(mapName)) mapName = "Treasure map";

        var message = new SeStringBuilder()
            .AddUiForeground("[Second Sight] ", 37)
            .AddText($"{mapName} deciphered: dig at ")
            .Append(SeString.CreateMapLink(level.Territory.RowId, mapId, x, y))
            .AddText(". Click the link to set your flag, then type <flag> in chat to share it.")
            .Build();
        Services.ChatGui.Print(message);
    }
}
