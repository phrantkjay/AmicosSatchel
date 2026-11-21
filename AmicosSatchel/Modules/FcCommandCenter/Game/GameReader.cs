using System;
using System.Collections.Generic;
using System.Linq;
using AmicosSatchel.Modules.FcCommandCenter.Data;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;

namespace AmicosSatchel.Modules.FcCommandCenter.Game;

/// <summary>Free Company the current character belongs to.</summary>
public readonly record struct FcInfo(ulong Id, string Name, int OnlineMembers, int TotalMembers);

/// <summary>
/// A company action currently running, read from the player's status effects.
/// <paramref name="Remaining"/> is null when the game's timers couldn't be matched to it.
/// </summary>
public readonly record struct ActiveAction(uint StatusId, string Name, uint Icon, TimeSpan? Remaining, uint? ActionId);

/// <summary>
/// Reads Free Company details the game has already loaded. Read-only: it never asks the server for
/// anything or sends input, so each part only fills in once you open the matching game window.
/// </summary>
public static unsafe class GameReader {
    private const int ChestPages = 5;

    /// <summary>Your FC, once the game has loaded it after logging in; null if you aren't in one.</summary>
    public static FcInfo? ReadFc() {
        var proxy = InfoProxyFreeCompany.Instance();
        if (proxy == null || proxy->Id == 0) return null;
        return new FcInfo(proxy->Id, proxy->NameString, proxy->OnlineMembers, proxy->TotalMembers);
    }

    /// <summary>
    /// The member list, filled in by the game while the Free Company window's Members tab is open.
    /// Null if it isn't loaded or belongs to a different FC.
    /// </summary>
    public static List<(Member Member, bool Online)>? ReadRoster(ulong fcId) {
        var proxy = InfoProxyFreeCompanyMember.Instance();
        if (proxy == null || proxy->FreeCompanyId != fcId) return null;

        var count = proxy->GetEntryCount();
        if (count == 0) return null;

        var jobs = Services.DataManager.GetExcelSheet<ClassJob>();
        var worlds = Services.DataManager.GetExcelSheet<World>();
        var now = DateTime.UtcNow;
        var members = new List<(Member, bool)>();

        for (var i = 0u; i < count; i++) {
            var entry = proxy->GetEntry(i);
            if (entry == null) continue;

            var name = entry->NameString;
            if (string.IsNullOrWhiteSpace(name)) continue;

            var member = new Member {
                Name = name,
                World = worlds.GetRowOrDefault(entry->HomeWorld)?.Name.ExtractText() ?? "",
                Job = entry->Job == 0 ? "" : jobs.GetRowOrDefault(entry->Job)?.Abbreviation.ExtractText() ?? "",
                LastSeenUtc = now,
                FirstSeenUtc = now,
            };
            members.Add((member, IsOnline(entry->State)));
        }

        return members.Count == 0 ? null : members;
    }

    /// <summary>Whether the Free Company window is open, so its member list is fresh.</summary>
    public static bool FreeCompanyWindowOpen() {
        var agent = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentFreeCompany.Instance();
        return agent != null && agent->IsAgentActive();
    }

    private static bool IsOnline(InfoProxyCommonList.CharacterData.OnlineStatus state) {
        const InfoProxyCommonList.CharacterData.OnlineStatus away =
            InfoProxyCommonList.CharacterData.OnlineStatus.Disconnected |
            InfoProxyCommonList.CharacterData.OnlineStatus.NotFound |
            InfoProxyCommonList.CharacterData.OnlineStatus.OfflineExd;
        return state != InfoProxyCommonList.CharacterData.OnlineStatus.Offline && (state & away) == 0;
    }

    /// <summary>What a chest reading found: whether anything was read, and what changed since the last reading.</summary>
    public sealed record ChestReading(bool Captured, List<(uint ItemId, int Delta)> Changes);

    /// <summary>
    /// Copies every chest page the game has loaded into <paramref name="chest"/>. Call only while the
    /// chest window is open: pages load as you view them, and stay loaded afterwards with old contents.
    /// </summary>
    public static ChestReading CaptureChest(ChestSnapshot chest, string capturedBy) {
        var inventory = InventoryManager.Instance();
        if (inventory == null) return new ChestReading(false, []);

        var now = DateTime.UtcNow;
        var captured = false;
        var before = new Dictionary<int, List<ChestItem>>();
        var after = new Dictionary<int, List<ChestItem>>();

        for (var page = 1; page <= ChestPages; page++) {
            var items = ReadContainer(inventory, (InventoryType)((int)InventoryType.FreeCompanyPage1 + page - 1));
            if (items is null) continue;
            if (chest.Pages.TryGetValue(page, out var old)) before[page] = old.Items;
            after[page] = items;
            chest.Pages[page] = new ChestPage { CapturedUtc = now, Items = items };
            captured = true;
        }

        var gil = inventory->GetInventoryContainer(InventoryType.FreeCompanyGil);
        if (gil != null && gil->IsLoaded && gil->Size > 0) {
            chest.Gil = gil->GetInventorySlot(0)->Quantity;
            chest.GilCapturedUtc = now;
            captured = true;
        }

        if (ReadContainer(inventory, InventoryType.FreeCompanyCrystals) is { } crystals) {
            // Crystals count as page 0 for working out changes.
            if (chest.CrystalsCapturedUtc is not null) before[0] = chest.Crystals;
            after[0] = crystals;
            chest.Crystals = crystals;
            chest.CrystalsCapturedUtc = now;
            captured = true;
        }

        if (captured) chest.CapturedBy = capturedBy;
        return new ChestReading(captured, History.Diff(before, after));
    }

    /// <summary>
    /// Loot from finished voyages, while you're in the workshop. Each vessel keeps only its last
    /// voyage's loot, so this needs a visit between voyages to catch every one.
    /// </summary>
    public static List<VoyageLoot> ReadVoyageLoot() {
        var housing = HousingManager.Instance();
        if (housing == null || housing->WorkshopTerritory == null) return [];

        var workshop = housing->WorkshopTerritory;
        var now = DateTime.UtcNow;
        var points = Services.DataManager.GetExcelSheet<SubmarineExploration>();
        var found = new List<VoyageLoot>();

        var subs = workshop->Submersible.Data;
        for (var i = 0; i < subs.Length; i++) {
            ref var sub = ref subs[i];
            var name = sub.NameString;
            if (sub.RankId == 0 || string.IsNullOrWhiteSpace(name) || sub.RegisterTime == 0) continue;

            // Still out: the loot held now is from the voyage before.
            if (FromUnix(sub.ReturnTime) is { } back && back > now) continue;

            var items = new List<LootItem>();
            var route = new List<string>();
            foreach (var gathered in sub.GatheredData) {
                if (gathered.Point == 0) continue;
                route.Add(points.GetRowOrDefault(gathered.Point)?.Destination.ExtractText() ?? $"Point {gathered.Point}");
                AddLoot(items, gathered.ItemIdPrimary, gathered.ItemCountPrimary, gathered.ItemHQPrimary);
                AddLoot(items, gathered.ItemIdAdditional, gathered.ItemCountAdditional, gathered.ItemHQAdditional);
            }

            found.Add(new VoyageLoot {
                Id = VoyageLoot.IdFor(VesselKind.Submersible, name, FromUnix(sub.RegisterTime)!.Value),
                Kind = VesselKind.Submersible,
                Vessel = name,
                ReturnUtc = FromUnix(sub.ReturnTime) ?? now,
                Route = string.Join(" → ", route),
                Items = items,
            });
        }

        var airships = workshop->Airship.Data;
        var airshipCount = Math.Min((int)workshop->Airship.AirshipCount, airships.Length);
        for (var i = 0; i < airshipCount; i++) {
            ref var ship = ref airships[i];
            var name = ship.NameString;
            if (ship.RankId == 0 || string.IsNullOrWhiteSpace(name) || ship.RegisterTime == 0) continue;
            if (FromUnix(ship.ReturnTime) is { } back && back > now) continue;

            var items = new List<LootItem>();
            foreach (var gathered in ship.GatheredData) {
                if (gathered.AirshipItemValidPrimary) AddLoot(items, gathered.ItemIdPrimary, gathered.ItemCountPrimary, false);
                if (gathered.AirshipItemValidAdditional) AddLoot(items, gathered.ItemIdAdditional, gathered.ItemCountAdditional, false);
            }

            found.Add(new VoyageLoot {
                Id = VoyageLoot.IdFor(VesselKind.Airship, name, FromUnix(ship.RegisterTime)!.Value),
                Kind = VesselKind.Airship,
                Vessel = name,
                ReturnUtc = FromUnix(ship.ReturnTime) ?? now,
                Items = items,
            });
        }

        return found;
    }

    private static void AddLoot(List<LootItem> items, uint itemId, int count, bool hq) {
        if (itemId == 0 || count <= 0) return;
        var existing = items.Find(i => i.ItemId == itemId && i.HighQuality == hq);
        if (existing is null) items.Add(new LootItem { ItemId = itemId, Count = count, HighQuality = hq });
        else existing.Count += count;
    }

    /// <summary>The job you're on now, as an abbreviation and party role.</summary>
    public static (string Job, JobRole Role) CurrentJob() {
        var player = Services.ObjectTable.LocalPlayer;
        if (player?.ClassJob.ValueNullable is not { } job) return ("", JobRole.None);
        var role = (JobRole)job.Role;
        return (job.Abbreviation.ExtractText(), Enum.IsDefined(role) ? role : JobRole.None);
    }

    /// <summary>
    /// Submersibles and airships, while you're in the FC workshop and the game has their details.
    /// Null when nothing usable is loaded.
    /// </summary>
    public static List<Vessel>? ReadVessels() {
        var housing = HousingManager.Instance();
        if (housing == null || housing->WorkshopTerritory == null) return null;

        var workshop = housing->WorkshopTerritory;
        var vessels = new List<Vessel>();

        var subs = workshop->Submersible.Data;
        for (var i = 0; i < subs.Length; i++) {
            ref var sub = ref subs[i];
            var name = sub.NameString;
            if (sub.RankId == 0 || string.IsNullOrWhiteSpace(name)) continue;
            vessels.Add(new Vessel {
                Kind = VesselKind.Submersible,
                Slot = i,
                Name = name,
                Rank = sub.RankId,
                ReturnUtc = FromUnix(sub.ReturnTime),
            });
        }

        var airships = workshop->Airship.Data;
        var airshipCount = Math.Min((int)workshop->Airship.AirshipCount, airships.Length);
        for (var i = 0; i < airshipCount; i++) {
            ref var ship = ref airships[i];
            var name = ship.NameString;
            if (ship.RankId == 0 || string.IsNullOrWhiteSpace(name)) continue;
            vessels.Add(new Vessel {
                Kind = VesselKind.Airship,
                Slot = i,
                Name = name,
                Rank = ship.RankId,
                ReturnUtc = FromUnix(ship.ReturnTime),
            });
        }

        return vessels.Count == 0 ? null : vessels;
    }

    /// <summary>Company actions running now, from your own status effects.</summary>
    public static List<ActiveAction> ReadActiveActions(Catalog catalog) {
        var player = Services.ObjectTable.LocalPlayer;
        if (player is null) return [];

        // The statuses' own RemainingTime is a placeholder the game keeps topping up (always about 30 s).
        // The real time left is in the Free Company agent, one slot per running action, in the same
        // order as the statuses. It stays filled in while the Free Company window is closed.
        var timers = ReadActionTimers();
        var statuses = new List<(uint Id, Lumina.Excel.Sheets.Status Row)>();
        foreach (var status in player.StatusList) {
            if (status.StatusId == 0 || status.GameData.ValueNullable is not { IsFcBuff: true } row) continue;
            statuses.Add((status.StatusId, row));
        }

        var matched = timers.Count == statuses.Count;
        var active = new List<ActiveAction>();
        for (var i = 0; i < statuses.Count; i++) {
            var (id, row) = statuses[i];
            var name = row.Name.ExtractText();
            active.Add(new ActiveAction(id, name, row.Icon, matched ? timers[i] : null, catalog.ActionIdForStatusName(name)));
        }

        return active;
    }

    /// <summary>
    /// Time left on each running company action, from the Free Company agent: the seconds left when
    /// the game last updated it, minus the seconds since then. Empty slots are skipped.
    /// </summary>
    private static List<TimeSpan> ReadActionTimers() {
        var agent = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentFreeCompany.Instance();
        if (agent == null) return [];

        ref var timer = ref agent->ActionTimeRemaining;
        var slots = timer.TimeRemainingAtUpdate;
        var left = new List<TimeSpan>();
        for (var i = 0; i < slots.Length; i++) {
            if (slots[i] == 0) continue;
            left.Add(TimeSpan.FromSeconds(Math.Max(0, (long)slots[i] - timer.TimeSinceUpdate)));
        }

        return left;
    }

    private static List<ChestItem>? ReadContainer(InventoryManager* inventory, InventoryType type) {
        var container = inventory->GetInventoryContainer(type);
        if (container == null || !container->IsLoaded || container->Size == 0) return null;

        var items = new List<ChestItem>();
        for (var slot = 0; slot < container->Size; slot++) {
            var item = container->GetInventorySlot(slot);
            if (item == null || item->ItemId == 0 || item->Quantity <= 0) continue;
            items.Add(new ChestItem {
                ItemId = item->GetBaseItemId(),
                HighQuality = item->IsHighQuality(),
                Quantity = item->Quantity,
            });
        }

        return items;
    }

    private static DateTime? FromUnix(uint seconds) =>
        seconds == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
}
