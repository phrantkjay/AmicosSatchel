using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using XIVMap.Shared;

namespace AmicosSatchel.Modules.SecondSight;

/// <summary>
/// Runs on the game thread. Does nothing unless the app is connected. Then it reads the full
/// state (objects, FATEs) every <see cref="Interval"/>, and in between only the player's
/// position every <see cref="PlayerInterval"/>, which is a couple of field reads.
/// </summary>
internal sealed class StateSampler : IDisposable {
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(125);      // ~8 full updates per second
    private static readonly TimeSpan PlayerInterval = TimeSpan.FromMilliseconds(33); // ~30 position updates per second

    private readonly PipeServer pipe;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private TimeSpan nextSample;
    private TimeSpan nextPlayerSample;
    // Attunements and aether currents change rarely; a few hundred bit checks every 2 s.
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(2);
    private readonly ProgressReader progress = new();
    private TimeSpan nextProgress;
    private TreasureSpotInfo? treasure;
    // Friends' zones change rarely; the list is a few dozen entries at most.
    private static readonly TimeSpan FriendsInterval = TimeSpan.FromSeconds(3);
    private TimeSpan nextFriends;
    private List<FriendInfo>? lastFriends;
    private static readonly TimeSpan MarkerLogInterval = TimeSpan.FromSeconds(10);
    private TimeSpan nextMarkerLog;
    private int lastMarkerSignature;

    public StateSampler(PipeServer pipe) {
        this.pipe = pipe;

        // Load XIVMap.Shared and JIT the sampling code now, during plugin load, instead of
        // on the first framework tick (which showed up as a ~170 ms hitch).
        RuntimeHelpers.PrepareMethod(typeof(StateSampler).GetMethod(nameof(OnUpdate), BindingFlags.NonPublic | BindingFlags.Instance)!.MethodHandle);
        RuntimeHelpers.PrepareMethod(typeof(StateSampler).GetMethod(nameof(Sample), BindingFlags.NonPublic | BindingFlags.Static)!.MethodHandle);
        RuntimeHelpers.PrepareMethod(typeof(StateSampler).GetMethod(nameof(SamplePlayer), BindingFlags.NonPublic | BindingFlags.Static)!.MethodHandle);

        Services.Framework.Update += OnUpdate;
    }

    public void Dispose() {
        Services.Framework.Update -= OnUpdate;
    }

    private void OnUpdate(IFramework framework) {
        if (!pipe.IsConnected)
            return;

        var now = clock.Elapsed;
        if (now >= nextProgress) {
            nextProgress = now + ProgressInterval;
            if (progress.ReadIfChanged() is { } changed)
                pipe.PublishProgress(changed);
            // A deciphered treasure map changes rarely too.
            treasure = MapExtrasReader.ReadTreasure();
        }

        if (now >= nextFriends) {
            nextFriends = now + FriendsInterval;
            var friends = FriendReader.Read();
            // Records compare lists by reference, so compare the friends themselves.
            if (lastFriends == null || !lastFriends.SequenceEqual(friends)) {
                lastFriends = friends;
                pipe.PublishFriends(new FriendsMessage(friends));
            }
        }

        // In PvP matches, log the game map's markers whenever they change, to learn how each
        // mode shows its objectives. Read-only; the Wolves' Den has nothing to learn from.
        if (now >= nextMarkerLog && Services.ClientState.IsPvPExcludingDen) {
            nextMarkerLog = now + MarkerLogInterval;
            var signature = ObjectiveReader.MapMarkerSignature();
            if (signature != lastMarkerSignature) {
                lastMarkerSignature = signature;
                ObjectiveReader.LogMapMarkers();
            }
        }

        if (now >= nextSample) {
            nextSample = now + Interval;
            nextPlayerSample = now + PlayerInterval;
            pipe.Publish(LocationReader.Read(), Sample(treasure));
        } else if (now >= nextPlayerSample) {
            nextPlayerSample = now + PlayerInterval;
            pipe.PublishPlayer(new PlayerMessage(SamplePlayer()));
        }
    }

    private static PlayerInfo? SamplePlayer()
        => Services.ObjectTable.LocalPlayer is { } player ? new PlayerInfo(ToVec3(player.Position), player.Rotation) : null;

    private static StateMessage Sample(TreasureSpotInfo? treasure) {
        var player = Services.ObjectTable.LocalPlayer;
        if (player == null)
            return new StateMessage(null, [], [], [], [], null, null, []);

        var objects = new List<ObjectInfo>();
        foreach (var obj in Services.ObjectTable) {
            if (obj.Address == player.Address)
                continue;

            var category = Classify(obj);
            if (category == null)
                continue;

            objects.Add(new ObjectInfo(
                obj.GameObjectId,
                category.Value,
                (byte)obj.ObjectKind,
                obj.SubKind,
                obj.BaseId,
                obj.Name.TextValue,
                ToVec3(obj.Position),
                obj.Rotation,
                ReadNamePlateIcon(obj),
                HealthPercent(obj),
                Spent: !obj.IsTargetable && IsIslandNode(obj)));
        }

        var fates = new List<FateInfo>();
        foreach (var fate in Services.FateTable) {
            fates.Add(new FateInfo(
                fate.FateId,
                fate.Name.TextValue,
                ToVec3(fate.Position),
                fate.Radius,
                fate.Progress,
                (byte)fate.State,
                // Before a FATE starts its start time is 0, so the game's remaining time is garbage.
                fate.State == FateState.Running ? Math.Max(0, fate.TimeRemaining) : 0,
                fate.Level,
                fate.MapIconId));
        }

        var criticalEngagements = CriticalEngagementReader.Read();

        var party = new List<PartyMemberInfo>();
        foreach (var member in Services.PartyList) {
            if (member.EntityId == player.EntityId)
                continue;
            party.Add(new PartyMemberInfo(member.EntityId, member.Name.TextValue, member.Territory.RowId, ToVec3(member.Position)));
        }

        return new StateMessage(new PlayerInfo(ToVec3(player.Position), player.Rotation), objects, fates, party, ObjectiveReader.Read(criticalEngagements),
            MapExtrasReader.ReadFlag(), treasure, criticalEngagements);
    }

    /// <summary>Names of Island Sanctuary gathering nodes (trees, rocks, crops…), any capitalisation.</summary>
    private static readonly HashSet<string> IslandNodeNames = Services.DataManager.GetExcelSheet<Lumina.Excel.Sheets.MJIGatheringObject>()
        .Select(o => o.Name.ValueNullable?.Singular.ExtractText() ?? "").Where(n => n.Length > 0)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsIslandNode(IGameObject obj)
        => obj.ObjectKind == ObjectKind.ReactionEventObject && IslandNodeNames.Contains(obj.Name.TextValue);

    /// <summary>Returns null for objects the map never shows (pets, mounts, minions, invisible helpers).</summary>
    private static ObjectCategory? Classify(IGameObject obj) {
        // Island Sanctuary gathering nodes: "reaction" objects that all share one id, told apart
        // only by name. Untargetable ones have been gathered and not respawned yet; they're sent
        // too, marked Spent, so the map can show them faded.
        if (IsIslandNode(obj))
            return ObjectCategory.EventObject;

        switch (obj.ObjectKind) {
            case ObjectKind.Pc when obj is ICharacter pc:
                if ((pc.StatusFlags & (StatusFlags.PartyMember | StatusFlags.AllianceMember)) != 0)
                    return ObjectCategory.PartyMember;
                // The game flags nearby players who are on your friend list.
                return (pc.StatusFlags & StatusFlags.Friend) != 0 ? ObjectCategory.Friend : ObjectCategory.Player;

            case ObjectKind.BattleNpc when obj is IBattleNpc npc:
                if (npc.IsDead)
                    return null;
                return npc.BattleNpcKind switch {
                    BattleNpcSubKind.Combatant when (npc.StatusFlags & StatusFlags.Hostile) != 0 => ObjectCategory.Monster,
                    BattleNpcSubKind.Combatant when npc.Name.TextValue.Length > 0 => ObjectCategory.Npc,
                    BattleNpcSubKind.NpcPartyMember => ObjectCategory.PartyMember,
                    _ => null,
                };

            case ObjectKind.EventNpc:
                // Untargetable ones are hidden helpers (on the Island Sanctuary, dozens with internal names).
                return obj.Name.TextValue.Length > 0 && obj.IsTargetable ? ObjectCategory.Npc : null;

            case ObjectKind.Treasure:
                return ObjectCategory.Treasure;

            case ObjectKind.Aetheryte:
                return ObjectCategory.Aetheryte;

            case ObjectKind.GatheringPoint:
                return obj.IsTargetable ? ObjectCategory.GatheringPoint : null;

            case ObjectKind.EventObj:
                return obj.IsTargetable ? ObjectCategory.EventObject : null;

            default:
                return null;
        }
    }

    /// <summary>The icon drawn over the object's head (quest markers and similar). Read-only.</summary>
    private static unsafe uint ReadNamePlateIcon(IGameObject obj)
        => ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address)->NamePlateIconId;

    /// <summary>Rounded up, so a monster on its last hit point still reads 1%, not 0%.</summary>
    private static byte? HealthPercent(IGameObject obj)
        => obj is ICharacter { MaxHp: > 0 } character
            ? (byte)Math.Min(100, Math.Ceiling(character.CurrentHp * 100.0 / character.MaxHp))
            : null;

    private static Vec3 ToVec3(Vector3 v) => new(v.X, v.Y, v.Z);
}
