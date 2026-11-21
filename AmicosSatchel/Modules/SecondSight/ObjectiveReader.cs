using System.Linq;
using System.Collections.Generic;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using XIVMap.Shared;
using GameMap = FFXIVClientStructs.FFXIV.Client.Game.UI.Map;

namespace AmicosSatchel.Modules.SecondSight;

/// <summary>
/// Reads the quest and leve markers the game keeps for its own map. Read-only; game thread.
/// </summary>
internal static unsafe class ObjectiveReader {
    /// <param name="criticalEngagements">Sent separately; their event markers are skipped like FATEs'.</param>
    public static List<ObjectiveInfo> Read(List<CriticalEngagementInfo> criticalEngagements) {
        var result = new List<ObjectiveInfo>();
        var map = GameMap.Instance();
        if (map == null)
            return result;

        foreach (ref readonly var marker in map->QuestMarkers)
            Add(result, ObjectiveKind.Quest, in marker);
        foreach (ref readonly var marker in map->LevequestMarkers)
            Add(result, ObjectiveKind.Leve, in marker);
        Add(result, ObjectiveKind.Leve, in map->ActiveLevequestMarker);
        foreach (var marker in map->UnacceptedQuestMarkers)
            Add(result, ObjectiveKind.NewQuest, in marker);

        // Event markers are the least known part of the game's map data; if one can't be read,
        // keep everything else updating rather than losing the whole map.
        try {
            AddEventMarkers(result, criticalEngagements);
        } catch (System.Exception ex) {
            if (!eventMarkerErrorLogged)
                Services.Log.Warning(ex, "Could not read the game map's event markers; skipping them");
            eventMarkerErrorLogged = true;
        }
        return result;
    }

    private static bool eventMarkerErrorLogged;

    // An event marker this close (world units) to a FATE is that FATE, which the app already has.
    private const float FateMatchDistance = 2;

    /// <summary>
    /// The game map's live event markers for the whole zone: PvP objectives (the icon shows the
    /// team holding it), special events and the like. FATEs are skipped.
    /// </summary>
    private static void AddEventMarkers(List<ObjectiveInfo> result, List<CriticalEngagementInfo> criticalEngagements) {
        var agent = AgentMap.Instance();
        if (agent == null)
            return;

        var fates = new List<System.Numerics.Vector3>();
        foreach (var fate in Services.FateTable)
            fates.Add(fate.Position);
        foreach (var ce in criticalEngagements)
            fates.Add(new System.Numerics.Vector3(ce.Position.X, ce.Position.Y, ce.Position.Z));

        foreach (ref readonly var data in agent->EventMarkers.AsSpan()) {
            if (data.IconId == 0 || data.TerritoryTypeId == 0 || IsFate(data.Position, fates))
                continue;
            var name = Text(data.TooltipString);
            result.Add(new ObjectiveInfo(
                ObjectiveKind.Event,
                name,
                data.IconId,
                data.TerritoryTypeId,
                data.MapId,
                new Vec3(data.Position.X, data.Position.Y, data.Position.Z),
                data.Radius,
                data.RecommendedLevel));
        }
    }

    private static bool IsFate(System.Numerics.Vector3 position, List<System.Numerics.Vector3> fates) {
        foreach (var fate in fates) {
            if (System.Numerics.Vector3.Distance(position, fate) < FateMatchDistance)
                return true;
        }
        return false;
    }

    /// <summary>A number that changes when the game map's markers, or the objective-like objects nearby, change.</summary>
    public static int MapMarkerSignature() {
        var agent = AgentMap.Instance();
        if (agent == null)
            return 0;

        var hash = new System.HashCode();
        foreach (ref readonly var data in agent->EventMarkers.AsSpan())
            hash.Add((data.IconId, data.Position.X, data.Position.Z, data.Radius));
        // Every minimap entry with an icon: in Frontline the count field has read as 0 while icons showed.
        foreach (ref readonly var marker in agent->MiniMapMarkers)
            if (marker.MapMarker.IconId != 0)
                hash.Add((marker.MapMarker.IconId, marker.MapMarker.SecondaryIconId, marker.MapMarker.X, marker.MapMarker.Y));
        for (var i = 0; i < agent->MapMarkerCount && i < agent->MapMarkers.Length; i++)
            hash.Add((agent->MapMarkers[i].MapMarker.IconId, agent->MapMarkers[i].MapMarker.X, agent->MapMarkers[i].MapMarker.Y));
        foreach (var obj in ObjectiveObjects()) {
            var raw = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address;
            hash.Add((obj.BaseId, (int)obj.Position.X, (int)obj.Position.Z, obj.IsTargetable, raw->NamePlateIconId));
        }
        return hash.ToHashCode();
    }

    /// <summary>
    /// Writes what the game's map holds right now, plus the objective-like objects around (event
    /// objects and non-player NPCs), to the Dalamud log, to learn how content such as a Frontline
    /// map shows its objectives and who holds them. Read-only.
    /// </summary>
    public static int LogMapMarkers() {
        var agent = AgentMap.Instance();
        if (agent == null)
            return 0;

        var count = 0;
        Services.Log.Information($"[markers] territory {agent->CurrentTerritoryId} map {agent->CurrentMapId} pvp {Services.ClientState.IsPvP} (minimap count {agent->MiniMapMarkerCount}, map count {agent->MapMarkerCount})");
        foreach (ref readonly var data in agent->EventMarkers.AsSpan()) {
            if (data.TerritoryTypeId != 0 && data.TerritoryTypeId != Services.ClientState.TerritoryType) continue; // left over from another zone
            count++;
            var tooltip = Text(data.TooltipString);
            Services.Log.Information($"[markers] event icon {data.IconId} type {data.MarkerType} state {data.EventState} flags {data.Flags} data {data.DataId} level {data.LevelId} objective {data.ObjectiveId} terr {data.TerritoryTypeId} map {data.MapId} pos {data.Position} r {data.Radius} end {data.EndTimestamp} \"{tooltip}\"");
        }
        var index = 0;
        foreach (ref readonly var marker in agent->MiniMapMarkers) {
            if (marker.MapMarker.IconId != 0)
                Services.Log.Information($"[markers] minimap[{index}] icon {marker.MapMarker.IconId}/{marker.MapMarker.SecondaryIconId} type {marker.DataType} key {marker.DataKey} flags {marker.MapMarker.IconFlags} scale {marker.MapMarker.Scale} at {marker.MapMarker.X},{marker.MapMarker.Y} \"{marker.MapMarker.Subtext}\"");
            index++;
        }
        for (var i = 0; i < agent->MapMarkerCount && i < agent->MapMarkers.Length; i++) {
            ref readonly var marker = ref agent->MapMarkers[i];
            Services.Log.Information($"[markers] map icon {marker.MapMarker.IconId}/{marker.MapMarker.SecondaryIconId} type {marker.DataType} key {marker.DataKey} at {marker.MapMarker.X},{marker.MapMarker.Y} \"{marker.MapMarker.Subtext}\"");
        }
        foreach (var obj in ObjectiveObjects()) {
            var raw = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address;
            Services.Log.Information($"[markers] object {obj.ObjectKind}/{obj.SubKind} base {obj.BaseId} \"{obj.Name.TextValue}\" at {obj.Position} targetable {obj.IsTargetable} nameplate {raw->NamePlateIconId} event {raw->EventId.Id} render {raw->RenderFlags}");
        }
        return count;
    }

    /// <summary>Event objects and non-player NPCs with a name: where map objectives usually live. At most 80.</summary>
    private static IEnumerable<Dalamud.Game.ClientState.Objects.Types.IGameObject> ObjectiveObjects()
        => Services.ObjectTable
            .Where(o => o.ObjectKind is Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj
                            or Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc
                        && o.Name.TextValue.Length > 0)
            .Take(80);

    private static void Add(List<ObjectiveInfo> result, ObjectiveKind kind, in MarkerInfo marker) {
        if (!marker.ShouldRender && kind != ObjectiveKind.NewQuest)
            return;

        var label = Text(in marker.Label);
        foreach (ref readonly var data in marker.MarkerData.AsSpan()) {
            if (data.IconId == 0 || data.TerritoryTypeId == 0)
                continue;

            var name = label;
            if (Text(data.TooltipString) is { Length: > 0 } tooltip)
                name = tooltip;

            result.Add(new ObjectiveInfo(
                kind,
                name,
                data.IconId,
                data.TerritoryTypeId,
                data.MapId,
                new Vec3(data.Position.X, data.Position.Y, data.Position.Z),
                data.Radius,
                data.RecommendedLevel != 0 ? data.RecommendedLevel : marker.RecommendedLevel));
        }
    }

    /// <summary>Plain text of a game string, without colour or link codes.</summary>
    // Anything longer is not a marker name; treat it as junk rather than read it.
    private const int MaxTextLength = 512;

    /// <summary>
    /// Plain text of a game string, or "" if it is missing. Some markers (seen in PvP) have a
    /// string whose length is set but whose text pointer is null, so check before reading.
    /// </summary>
    private static string Text(Utf8String* text) => text == null ? "" : Text(in *text);

    private static string Text(in Utf8String text)
        => !text.StringPtr.HasValue || text.StringLength <= 0 || text.StringLength > MaxTextLength
            ? ""
            : Text(text.AsSpan());

    private static string Text(System.ReadOnlySpan<byte> bytes)
        => bytes.Length == 0 ? "" : SeString.Parse(bytes).TextValue;
}
