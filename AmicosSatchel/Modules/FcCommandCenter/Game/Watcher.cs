using System;
using System.Collections.Generic;
using System.Linq;
using AmicosSatchel.Modules.FcCommandCenter.Data;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace AmicosSatchel.Modules.FcCommandCenter.Game;

/// <summary>
/// Checks the game about once a second: which FC you're in, and whether the chest, workshop or
/// member list is open so it can copy what they show. Also sends reminders and alerts in chat.
/// </summary>
public sealed class Watcher : IDisposable {
    private const string ChestAddon = "FreeCompanyChest";
    private const string Tag = "[FC Command Center]";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    /// <summary>Voyages that came back longer ago than this aren't announced.</summary>
    private static readonly TimeSpan VoyageAlertWindow = TimeSpan.FromDays(2);

    private readonly BoardStore store;
    private readonly Catalog catalog;
    private readonly FcCommandCenterSettings settings;
    private DateTime nextCheck;
    private bool greeted;

    public Watcher(BoardStore store, Catalog catalog, FcCommandCenterSettings settings) {
        this.store = store;
        this.catalog = catalog;
        this.settings = settings;
        Services.Framework.Update += OnUpdate;
        Services.ClientState.Logout += OnLogout;
    }

    /// <summary>Your FC as last read; null if you aren't in one or it hasn't loaded yet.</summary>
    public FcInfo? Fc { get; private set; }

    /// <summary>Company actions running now.</summary>
    public IReadOnlyList<ActiveAction> ActiveActions { get; private set; } = [];

    public bool ChestOpen { get; private set; }

    /// <summary>The name of the character you're playing, for "updated by" and "assigned to me".</summary>
    public static string PlayerName => Services.ObjectTable.LocalPlayer?.Name.TextValue ?? "";

    public void Dispose() {
        Services.ClientState.Logout -= OnLogout;
        Services.Framework.Update -= OnUpdate;
    }

    /// <summary>
    /// The rotation step after the last one running now, skipping anything already running.
    /// With nothing running, the first step.
    /// </summary>
    public static RotationSlot? SuggestNext(FcBoard board, IReadOnlyList<ActiveAction> active) {
        var plan = board.Rotation.Where(r => !r.Deleted).OrderBy(r => r.Order).ToList();
        if (plan.Count == 0) return null;

        var running = active.Select(a => a.ActionId).OfType<uint>().ToHashSet();
        var last = plan.FindLastIndex(r => running.Contains(r.ActionId));

        for (var i = 1; i <= plan.Count; i++) {
            var slot = plan[(last + i + plan.Count) % plan.Count];
            if (!running.Contains(slot.ActionId)) return slot;
        }
        return null;
    }

    /// <summary>Plays the alert sound picked in the settings, if any.</summary>
    public void PlayAlertSound() {
        if (settings.AlertSound is >= 1 and <= 16) UIGlobals.PlayChatSoundEffect((uint)settings.AlertSound);
    }

    private void OnLogout(int type, int code) {
        Fc = null;
        ActiveActions = [];
        greeted = false;
        store.SaveNow();
    }

    private void OnUpdate(IFramework framework) {
        store.Tick();

        var now = DateTime.UtcNow;
        if (now < nextCheck) return;
        nextCheck = now + Interval;

        if (!Services.ClientState.IsLoggedIn || Services.ObjectTable.LocalPlayer is null) return;

        try {
            Check(now);
        } catch (Exception e) {
            // Keep going: one bad read shouldn't stop the rest of the module.
            Services.Log.Warning(e, $"{Tag} Couldn't read FC details this time.");
            nextCheck = now + TimeSpan.FromSeconds(10);
        }
    }

    private void Check(DateTime now) {
        Fc = GameReader.ReadFc();
        if (Fc is { } fc) store.UseFc(fc.Id, fc.Name);

        var board = store.Board;
        ActiveActions = GameReader.ReadActiveActions(catalog);

        var chest = Services.GameGui.GetAddonByName(ChestAddon);
        ChestOpen = !chest.IsNull && chest.IsVisible;
        if (ChestOpen) ReadChest(board, now);

        if (GameReader.ReadVessels() is { } vessels && !SameVessels(board.Voyages, vessels)) {
            board.Voyages = new VoyageSnapshot { CapturedUtc = now, CapturedBy = PlayerName, Vessels = vessels };
            store.MarkChanged();
        }

        foreach (var loot in GameReader.ReadVoyageLoot()) {
            if (History.RecordLoot(board, loot)) store.MarkChanged();
        }

        if (Fc is { } current && GameReader.ReadRoster(current.Id) is { } members) UpdateRoster(board, members, current, now);

        // Wait for the FC to load, so the summary comes from the right board.
        if (!greeted && Fc is not null) Greet(board, now);
        if (settings.ReminderMinutes > 0) RemindEvents(board, now);
        if (settings.VoyageAlerts) AlertVoyages(board, now);
        if (settings.ActionAlertMinutes > 0) AlertActions(board, now);
    }

    private void ReadChest(FcBoard board, DateTime now) {
        var reading = GameReader.CaptureChest(board.Chest, PlayerName);
        if (!reading.Captured) return;

        History.RecordChestChanges(board, reading.Changes, PlayerName, now);
        if (board.Chest.GilCapturedUtc is not null) History.RecordGil(board.Chest, board.Chest.Gil, now);
        store.MarkChanged();
    }

    private void UpdateRoster(FcBoard board, List<(Member Member, bool Online)> seen, FcInfo fc, DateTime now) {
        var roster = board.Roster ??= new RosterSnapshot();
        var known = roster.Members.ToDictionary(m => (m.Name, m.World));
        var changed = false;

        // Online states are only fresh while the Free Company window is open.
        var onlineFresh = GameReader.FreeCompanyWindowOpen();

        foreach (var (member, online) in seen) {
            var isOnline = onlineFresh && online;
            if (known.TryGetValue((member.Name, member.World), out var old)) {
                if (old.Job != member.Job && member.Job.Length > 0) {
                    old.Job = member.Job;
                    changed = true;
                }
                if (old.FirstSeenUtc == default) {
                    old.FirstSeenUtc = old.LastSeenUtc == default ? now : old.LastSeenUtc;
                    changed = true;
                }
                // Only note times a few times an hour, so an open list doesn't save every second.
                if (now - old.LastSeenUtc > TimeSpan.FromMinutes(15)) {
                    old.LastSeenUtc = now;
                    changed = true;
                }
                if (isOnline && (old.LastOnlineUtc is null || now - old.LastOnlineUtc > TimeSpan.FromMinutes(15))) {
                    old.LastOnlineUtc = now;
                    changed = true;
                }
            } else {
                if (isOnline) member.LastOnlineUtc = now;
                roster.Members.Add(member);
                changed = true;
            }
        }

        // When the whole list is loaded, anyone missing from it has left the FC.
        if (seen.Count >= fc.TotalMembers && fc.TotalMembers > 0) {
            var current = seen.Select(m => (m.Member.Name, m.Member.World)).ToHashSet();
            changed |= roster.Members.RemoveAll(m => !current.Contains((m.Name, m.World))) > 0;
        }

        if (!changed) return;
        roster.Members.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        roster.CapturedUtc = now;
        store.MarkChanged();
    }

    /// <summary>Once per login, mentions new announcements, your open tasks and anything overdue.</summary>
    private void Greet(FcBoard board, DateTime now) {
        greeted = true;
        if (!settings.LoginSummary) return;

        var unread = board.Announcements.Count(a => !a.Deleted && a.PostedUtc > board.AnnouncementsReadUtc && !Expired(a));
        var me = PlayerName;
        var open = board.Tasks.Where(t => !t.Deleted && t.State != TaskState.Done).ToList();
        var mine = open.Count(t => Same(t.Assignee, me));
        var overdue = settings.OfficerTools ? open.Count(t => t.DueUtc < now) : open.Count(t => t.DueUtc < now && Same(t.Assignee, me));

        var parts = new List<string>();
        if (unread > 0) parts.Add(unread == 1 ? "1 new announcement" : $"{unread} new announcements");
        if (mine > 0) parts.Add(mine == 1 ? "1 task assigned to you" : $"{mine} tasks assigned to you");
        if (overdue > 0) parts.Add(overdue == 1 ? "1 overdue task" : $"{overdue} overdue tasks");
        if (parts.Count == 0) return;

        var list = parts.Count == 1 ? parts[0] : string.Join(", ", parts[..^1]) + " and " + parts[^1];
        Services.ChatGui.Print($"{Tag} {list}. Type {FcCommandCenterModule.Command} to see them.");
    }

    private void RemindEvents(FcBoard board, DateTime now) {
        var lead = TimeSpan.FromMinutes(settings.ReminderMinutes);
        foreach (var e in board.Events.Where(e => !e.Deleted)) {
            foreach (var start in Schedule.Starts(e, now, now + lead)) {
                if (start < now || !board.RemindedStarts.Add($"{e.Id}@{start.Ticks}")) continue;

                var where = e.Location.Length > 0 ? $" at {e.Location}" : "";
                var going = board.Signups.Count(s => !s.Deleted && s.EventId == e.Id && s.OccurrenceUtc == start && s.Response == SignupResponse.Going);
                var count = going > 0 ? $" {going} signed up." : "";
                Services.ChatGui.Print($"{Tag} {e.Title}{where} starts {Schedule.Until(start, now)} ({Schedule.Local(start, withDate: false)}).{count}");
                PlayAlertSound();
                store.MarkChanged();
            }
        }
    }

    private void AlertVoyages(FcBoard board, DateTime now) {
        if (board.Voyages is not { } voyages) return;

        var back = new List<string>();
        foreach (var v in voyages.Vessels) {
            if (v.ReturnUtc is not { } returned || returned > now || now - returned > VoyageAlertWindow) continue;
            if (board.RemindedStarts.Add($"voyage:{v.Kind}:{v.Slot}:{v.Name}@{returned.Ticks}")) back.Add(v.Name);
        }

        if (back.Count == 0) return;
        var names = back.Count == 1 ? back[0] : string.Join(", ", back[..^1]) + " and " + back[^1];
        Services.ChatGui.Print($"{Tag} {names} {(back.Count == 1 ? "is" : "are")} back from {(back.Count == 1 ? "its voyage" : "their voyages")}.");
        PlayAlertSound();
        store.MarkChanged();
    }

    private void AlertActions(FcBoard board, DateTime now) {
        var warnAt = TimeSpan.FromMinutes(settings.ActionAlertMinutes);
        foreach (var action in ActiveActions) {
            if (action.Remaining is not { } left || left > warnAt || left <= TimeSpan.Zero) continue;

            // The end time shifts by a second or so between readings, so match any alert within the hour.
            var end = now + left;
            var prefix = $"action:{action.StatusId}@";
            var alerted = board.RemindedStarts.Any(key => key.StartsWith(prefix, StringComparison.Ordinal) &&
                                                         long.TryParse(key.AsSpan(prefix.Length), out var ticks) &&
                                                         Math.Abs((new DateTime(ticks, DateTimeKind.Utc) - end).TotalHours) < 1);
            if (alerted) continue;

            board.RemindedStarts.Add($"{prefix}{end.Ticks}");
            var next = SuggestNext(board, ActiveActions.Where(a => a.StatusId != action.StatusId).ToList()) is { } slot && catalog.Action(slot.ActionId) is { } info
                ? $" Next in your rotation: {info.Name}."
                : "";
            Services.ChatGui.Print($"{Tag} {action.Name} ends in {Schedule.Span(left)}.{next}");
            PlayAlertSound();
            store.MarkChanged();
        }
    }

    public static bool Expired(Announcement a) => a.ExpiresUtc is { } expires && expires < DateTime.UtcNow;

    public static bool Same(string a, string b) => a.Length > 0 && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameVessels(VoyageSnapshot? old, List<Vessel> fresh) =>
        old is not null && old.Vessels.Count == fresh.Count &&
        old.Vessels.Zip(fresh).All(p => p.First.Kind == p.Second.Kind && p.First.Slot == p.Second.Slot &&
                                         p.First.Name == p.Second.Name && p.First.Rank == p.Second.Rank &&
                                         p.First.ReturnUtc == p.Second.ReturnUtc);
}
