using System;
using System.Linq;
using AmicosSatchel.Modules.FcCommandCenter.Data;
using AmicosSatchel.Modules.FcCommandCenter.Game;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AmicosSatchel.Modules.FcCommandCenter.Windows;

// Overview: the most useful line or two from every other tab.
public sealed partial class CommandCenterWindow {
    private void DrawOverview() {
        var now = DateTime.UtcNow;

        using var table = ImRaii.Table("##Overview", 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.PadOuterX);
        if (!table) return;

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        OverviewEvents(now);
        OverviewAnnouncements();
        OverviewTasks();

        ImGui.TableNextColumn();
        OverviewActions();
        OverviewVoyages(now);
        OverviewChest();
    }

    private void OverviewEvents(DateTime now) {
        Heading("Next events");
        var upcoming = Schedule.Upcoming(Board, now, TimeSpan.FromDays(14), 4);
        if (upcoming.Count == 0) {
            ImGui.TextDisabled("Nothing in the next two weeks.");
            return;
        }

        foreach (var (e, start) in upcoming) {
            ImGui.TextUnformatted(e.Title.Length > 0 ? e.Title : "(untitled)");
            ImGui.SameLine();
            var running = start <= now;
            if (running) GoodText("happening now");
            else ImGui.TextDisabled($"{Schedule.Local(start)} · {Schedule.Until(start, now)}");

            var going = RepliesFor(e, start).Count(r => r.Response == SignupResponse.Going);
            if (going > 0) {
                ImGui.SameLine();
                ImGui.TextColored(theme.AccentText, $"{going} going");
            }
        }
    }

    private void OverviewAnnouncements() {
        Heading("Announcements");
        var shown = Board.Announcements
            .Where(a => !a.Deleted && !Watcher.Expired(a) && (a.Pinned || a.PostedUtc > Board.AnnouncementsReadUtc))
            .OrderByDescending(a => a.Pinned).ThenByDescending(a => a.PostedUtc)
            .Take(4).ToList();

        if (shown.Count == 0) {
            ImGui.TextDisabled("No pinned or new announcements.");
            return;
        }

        foreach (var a in shown) {
            if (a.PostedUtc > Board.AnnouncementsReadUtc) {
                ImGui.TextColored(theme.AccentText, "New");
                ImGui.SameLine();
            } else if (a.Pinned) {
                ImGui.TextDisabled("Pinned");
                ImGui.SameLine();
            }
            ImGui.TextUnformatted(a.Title);
        }
    }

    private void OverviewTasks() {
        Heading("Tasks");
        var me = Watcher.PlayerName;
        var open = Board.Tasks.Where(t => !t.Deleted && t.State != TaskState.Done).ToList();
        var mine = open.Where(t => Watcher.Same(t.Assignee, me)).OrderBy(t => t.DueUtc ?? DateTime.MaxValue).ToList();
        var overdue = open.Count(t => t.DueUtc < DateTime.UtcNow);

        ImGui.TextDisabled($"{open.Count} open{(overdue > 0 ? $", {overdue} overdue" : "")}");
        if (mine.Count == 0) {
            ImGui.TextDisabled("Nothing assigned to you.");
            return;
        }

        foreach (var t in mine.Take(4)) {
            ImGui.TextUnformatted(t.Title);
            if (t.DueUtc is { } due) {
                ImGui.SameLine();
                if (due < DateTime.UtcNow) ErrorText("overdue");
                else ImGui.TextDisabled($"due {Schedule.Local(due)}");
            }
        }
    }

    private void OverviewActions() {
        Heading("Company actions");
        var active = watcher.ActiveActions;
        if (active.Count == 0) ImGui.TextDisabled("None running.");

        foreach (var a in active) {
            GameIcon(a.Icon);
            ImGui.SameLine();
            ImGui.TextUnformatted(a.Name);
            ImGui.SameLine();
            if (a.Remaining is not { } remaining) ImGui.TextDisabled("time left unknown");
            else if (remaining < TimeSpan.FromHours(6)) ErrorText($"{Schedule.Span(remaining)} left");
            else ImGui.TextDisabled($"{Schedule.Span(remaining)} left");
        }

        if (SuggestedNext() is { } next && catalog.Action(next.ActionId) is { } info) {
            ImGui.TextDisabled("Up next:");
            ImGui.SameLine();
            GameIcon(info.Icon);
            ImGui.SameLine();
            ImGui.TextUnformatted(info.Name);
        }
    }

    private void OverviewVoyages(DateTime now) {
        Heading("Voyages");
        var vessels = Board.Voyages?.Vessels ?? [];
        if (vessels.Count == 0) {
            ImGui.TextDisabled("Visit the workshop to read your submersibles and airships.");
            return;
        }

        var ready = vessels.Count(v => v.ReturnUtc <= now);
        var docked = vessels.Count(v => v.ReturnUtc is null);
        if (ready > 0) GoodText(ready == 1 ? "1 vessel is back" : $"{ready} vessels are back");
        if (docked > 0) ImGui.TextDisabled(docked == 1 ? "1 vessel is docked with no voyage" : $"{docked} vessels are docked with no voyage");

        if (vessels.Where(v => v.ReturnUtc > now).OrderBy(v => v.ReturnUtc).FirstOrDefault() is { ReturnUtc: { } back } soonest) {
            ImGui.TextDisabled($"Next back: {soonest.Name} {Schedule.Until(back, now)}");
        }
    }

    private void OverviewChest() {
        Heading("Chest");
        var chest = Board.Chest;
        if (chest.GilCapturedUtc is null && chest.Pages.Count == 0) {
            ImGui.TextDisabled("Open the FC chest in game to read it.");
            return;
        }

        if (chest.GilCapturedUtc is not null) ImGui.TextUnformatted($"{chest.Gil:N0} gil");

        var low = LowStock().ToList();
        if (low.Count == 0) {
            ImGui.TextDisabled("Nothing below its minimum.");
            return;
        }

        foreach (var (note, have) in low.Take(5)) {
            ErrorText($"{catalog.ItemName(note.ItemId)}: {have} of {note.MinQuantity}");
        }
    }
}
