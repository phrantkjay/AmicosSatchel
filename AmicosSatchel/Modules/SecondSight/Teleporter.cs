using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using XIVMap.Shared;

namespace AmicosSatchel.Modules.SecondSight;

/// <summary>
/// Teleporting from the map, only when the player has turned it on in the module's settings
/// (off by default). The map asks after the player confirms there; this checks it's safe and
/// allowed, then uses Lifestream when it's installed (which also handles aethernet shards) or
/// the game's own teleport for main aetherytes. Never sends keys or clicks to the game.
/// </summary>
internal sealed unsafe class Teleporter : IDisposable {
    private const string Lifestream = "Lifestream";
    private static readonly TimeSpan InfoInterval = TimeSpan.FromSeconds(5);

    private readonly Configuration configuration;
    private readonly PipeServer pipe;
    private readonly ConcurrentQueue<Message> requests = new();
    private DateTime nextInfo;
    private TravelInfoMessage? lastInfo;

    public Teleporter(Configuration configuration, PipeServer pipe) {
        this.configuration = configuration;
        this.pipe = pipe;
        pipe.MessageReceived += OnMessage;
        Services.Framework.Update += OnUpdate;
    }

    public void Dispose() {
        Services.Framework.Update -= OnUpdate;
        pipe.MessageReceived -= OnMessage;
    }

    /// <summary>Pipe thread: just queue it; everything game-related happens on the game thread.</summary>
    private void OnMessage(Message message) {
        if (message is TeleportRequest or SetFlagRequest or ClearFlagRequest) requests.Enqueue(message);
    }

    private void OnUpdate(IFramework framework) {
        while (requests.TryDequeue(out var message)) {
            var (started, text) = message switch {
                TeleportRequest teleport => Handle(teleport),
                SetFlagRequest flag => SetFlag(flag),
                ClearFlagRequest => ClearFlag(),
                _ => (false, ""),
            };
            if (text.Length > 0) pipe.Send(new TeleportResultMessage(started, text));
        }

        if (!pipe.IsConnected || DateTime.UtcNow < nextInfo) return;
        nextInfo = DateTime.UtcNow + InfoInterval;
        var allowed = configuration.SecondSightAllowTeleport;
        var info = new TravelInfoMessage(allowed, IsLifestreamLoaded(), allowed ? ReadDestinations() : [], allowed && SortsByExpansion(),
            configuration.SecondSightAllowFlag);
        if (lastInfo is null || info.TeleportAllowed != lastInfo.TeleportAllowed || info.LifestreamAvailable != lastInfo.LifestreamAvailable
            || info.FlagAllowed != lastInfo.FlagAllowed
            || info.ByExpansion != lastInfo.ByExpansion || !info.Destinations.SequenceEqual(lastInfo.Destinations)) {
            lastInfo = info;
            pipe.PublishTravel(info);
        }
    }

    /// <summary>Sends fresh travel info on the next frame (e.g. right after the setting changed).</summary>
    public void RefreshSoon() {
        nextInfo = DateTime.MinValue;
        lastInfo = null;
    }

    private (bool Started, string Text) Handle(TeleportRequest request) {
        if (!configuration.SecondSightAllowTeleport)
            return (false, "Teleporting from the map is turned off. Turn it on in /satchel → Amico's Second Sight.");
        if (Services.ObjectTable.LocalPlayer is not { } player)
            return (false, "Can't teleport: not logged in.");
        if (WhyNot(player.IsCasting) is { } reason)
            return (false, $"Can't teleport right now: {reason}.");

        var lifestream = IsLifestreamLoaded();
        if (lifestream && Call<bool>("IsBusy"))
            return (false, "Lifestream is busy with another trip. Try again in a moment.");

        if (request.IsShard) {
            if (!lifestream)
                return (false, $"Install Lifestream to travel to {request.Name} by aethernet shard.");
            // Standing at a crystal of that aethernet, Lifestream can use it straight away. Only ask
            // then: asked from anywhere else it answers "Destination could not be found" in chat.
            if (AtCrystalOf(request.AetheryteId) && Call<bool>("AethernetTeleportById", request.AetheryteId)) {
                Services.Log.Information($"[Second Sight] Aethernet to {request.Name} from here.");
                return (true, $"Lifestream is taking you to {request.Name}.");
            }

            // Anywhere else, Lifestream's own "/li <name>" finds the way: it teleports to that city's
            // aetheryte and takes the aethernet from there (its IPC aethernet call only works at a crystal).
            CallAction("ExecuteCommand", request.Name);
            Services.Log.Information($"[Second Sight] Asked Lifestream: /li {request.Name}");
            return (true, $"Lifestream is taking you to {request.Name} (teleport to the city, then the aethernet).");
        }

        // Anything in the game's own teleport list can be teleported to (aetherytes, estate halls, apartments).
        if (!ReadDestinations().Any(d => d.AetheryteId == request.AetheryteId && d.SubIndex == request.SubIndex))
            return (false, $"{request.Name} isn't in your teleport list. Have you attuned to it?");

        return StartTeleport(request.AetheryteId, lifestream, request.SubIndex)
            ? (true, $"Teleporting to {request.Name}.")
            : (false, $"The game didn't start the teleport to {request.Name}.");
    }

    /// <summary>Puts the game's own map flag where the player chose on the Second Sight map.</summary>
    private (bool Started, string Text) SetFlag(SetFlagRequest request) {
        if (!configuration.SecondSightAllowFlag)
            return (false, "Setting the map flag from the map is turned off. Turn it on in /satchel → Amico's Second Sight.");
        if (Services.ObjectTable.LocalPlayer is null)
            return (false, "Can't set the flag: not logged in.");
        var agent = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentMap.Instance();
        if (agent == null)
            return (false, "Can't set the flag right now.");
        // The game keeps an existing flag rather than moving it, so clear it first.
        agent->FlagMarkerCount = 0;
        agent->SetFlagMapMarker(request.TerritoryId, request.MapId, request.X, request.Z, FlagIcon);

        // Put <flag> in the chat box, as if typed, so sharing is just Enter. Never sends anything.
        if (configuration.SecondSightFlagToChat) {
            var chat = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentChatLog.Instance();
            if (chat != null && chat->InsertTextCommandParam(FlagTextParam, false))
                return (true, $"Map flag set at {request.Where}. <flag> is in your chat box: pick the channel and press Enter to share it.");
        }
        return (true, $"Map flag set at {request.Where}. Type <flag> in chat to share it.");
    }

    /// <summary>Removes the game's map flag.</summary>
    private (bool Started, string Text) ClearFlag() {
        if (!configuration.SecondSightAllowFlag)
            return (false, "Changing the map flag from the map is turned off. Turn it on in /satchel → Amico's Second Sight.");
        var agent = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentMap.Instance();
        if (agent == null || agent->FlagMarkerCount == 0)
            return (false, "There's no map flag to remove.");
        agent->FlagMarkerCount = 0;
        return (true, "Map flag removed.");
    }

    private const uint FlagIcon = 60561;      // the game's red flag
    private const uint FlagTextParam = 1048;  // TextCommandParam row for <flag>

    /// <summary>Teleports to a main aetheryte through Lifestream when it's there, otherwise the game's own teleport.</summary>
    private static bool StartTeleport(uint aetheryteId, bool lifestream, byte subIndex = 0) {
        if (lifestream && Call<bool>("Teleport", aetheryteId, subIndex)) return true;
        var telepo = Telepo.Instance();
        return telepo != null && telepo->Teleport(aetheryteId, subIndex);
    }

    /// <summary>
    /// Whether Lifestream sees you at an aetheryte or shard of the same aethernet as this shard,
    /// i.e. close enough to take the aethernet there.
    /// </summary>
    private static bool AtCrystalOf(uint shardId) {
        var active = Call<uint>("GetActiveAetheryte");
        if (active == 0) return false;
        var sheet = Services.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>();
        return sheet.GetRowOrDefault(active) is { AethernetGroup: not 0 } here
               && sheet.GetRowOrDefault(shardId) is { } shard
               && here.AethernetGroup == shard.AethernetGroup;
    }

    /// <summary>Why the game wouldn't let you teleport now, or null if it should.</summary>
    private static string? WhyNot(bool casting) {
        var condition = Services.Condition;
        if (condition[ConditionFlag.InCombat]) return "you're in combat";
        if (condition[ConditionFlag.BoundByDuty] || condition[ConditionFlag.BoundByDuty56] || condition[ConditionFlag.BoundByDuty95])
            return "you're in a duty";
        if (condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51]) return "you're changing areas";
        if (condition[ConditionFlag.OccupiedInCutSceneEvent] || condition[ConditionFlag.WatchingCutscene] || condition[ConditionFlag.WatchingCutscene78])
            return "a cutscene is playing";
        if (condition[ConditionFlag.Occupied] || condition[ConditionFlag.OccupiedInEvent] || condition[ConditionFlag.OccupiedInQuestEvent])
            return "you're busy with something in game";
        if (condition[ConditionFlag.Unconscious]) return "you're knocked out";
        if (casting || condition[ConditionFlag.Casting]) return "you're casting";
        return null;
    }

    /// <summary>The game's teleport list with today's fees: the same list its Teleport window shows.</summary>
    private static List<TeleportDestination> ReadDestinations() {
        var result = new List<TeleportDestination>();
        var telepo = Telepo.Instance();
        if (telepo == null || Services.ObjectTable.LocalPlayer is null) return result;
        var list = telepo->UpdateAetheryteList();
        if (list == null) return result;
        foreach (var info in *list) {
            if (info.AetheryteId == 0) continue;
            result.Add(new TeleportDestination(info.AetheryteId, info.SubIndex, info.GilCost, info.TerritoryId, info.IsFavourite,
                info.IsApartment, info.IsSharedHouse, info.Ward, info.Plot));
        }
        return result;
    }

    /// <summary>The Teleport window's "Display by" choice; true for by expansion.</summary>
    private bool SortsByExpansion() {
        if (!Services.GameConfig.UiConfig.TryGet("TelepoCategoryType", out uint type)) return false;
        if (type != lastCategoryType) {
            lastCategoryType = type;
            Services.Log.Information($"[Second Sight] Teleport window \"Display by\" setting is {type}.");
        }
        return type == 1;
    }

    private uint? lastCategoryType;

    private static bool IsLifestreamLoaded()
        => Services.PluginInterface.InstalledPlugins.Any(p => p.InternalName == Lifestream && p.IsLoaded);

    // Lifestream's IPC: "Lifestream.<Method>". Any failure (not loaded, API changed) counts as "didn't work".
    private static T Call<T>(string method) {
        try {
            return Services.PluginInterface.GetIpcSubscriber<T>($"{Lifestream}.{method}").InvokeFunc();
        } catch (Exception e) {
            Services.Log.Debug(e, $"Lifestream.{method} failed");
            return default!;
        }
    }

    private static T Call<T>(string method, uint arg) {
        try {
            return Services.PluginInterface.GetIpcSubscriber<uint, T>($"{Lifestream}.{method}").InvokeFunc(arg);
        } catch (Exception e) {
            Services.Log.Debug(e, $"Lifestream.{method} failed");
            return default!;
        }
    }

    private static T Call<T>(string method, uint arg, byte arg2) {
        try {
            return Services.PluginInterface.GetIpcSubscriber<uint, byte, T>($"{Lifestream}.{method}").InvokeFunc(arg, arg2);
        } catch (Exception e) {
            Services.Log.Debug(e, $"Lifestream.{method} failed");
            return default!;
        }
    }

    private static void CallAction(string method, string arg) {
        try {
            Services.PluginInterface.GetIpcSubscriber<string, object>($"{Lifestream}.{method}").InvokeAction(arg);
        } catch (Exception e) {
            Services.Log.Debug(e, $"Lifestream.{method} failed");
        }
    }
}
