using System;
using System.Linq;
using System.Numerics;
using AmicosSatchel.Modules.FcCommandCenter.Data;
using AmicosSatchel.Modules.FcCommandCenter.Game;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AmicosSatchel.Modules.FcCommandCenter.Windows;

// Calendar: a month of FC events, the selected day's events and an editor.
public sealed partial class CommandCenterWindow {
    private static readonly string[] RepeatNames = ["Once", "Every week", "Every two weeks"];

    private DateTime calendarMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime calendarDay = DateTime.Today;
    private Guid? editingEvent;

    private void DrawCalendar() {
        using var table = ImRaii.Table("##Calendar", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.PadOuterX);
        if (!table) return;

        ImGui.TableSetupColumn("Month", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Day", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        if (MonthGrid("Month", ref calendarMonth, calendarDay, EventsOnDay, out var picked)) {
            calendarDay = picked;
            editingEvent = null;
        }
        ImGui.TextDisabled("Times are in your time zone.");

        ImGui.TableNextColumn();
        DrawDay();
    }

    private int EventsOnDay(DateTime localDay) {
        var fromUtc = localDay.ToUniversalTime();
        var toUtc = localDay.AddDays(1).ToUniversalTime();
        return Board.Events.Where(e => !e.Deleted).Sum(e => Schedule.Starts(e, fromUtc, toUtc).Count(s => s >= fromUtc));
    }

    private void DrawDay() {
        var fromUtc = calendarDay.ToUniversalTime();
        var toUtc = calendarDay.AddDays(1).ToUniversalTime();
        var day = Board.Events
            .Where(e => !e.Deleted)
            .SelectMany(e => Schedule.Starts(e, fromUtc, toUtc).Where(s => s >= fromUtc).Select(s => (e, s)))
            .OrderBy(x => x.s)
            .ToList();

        Heading(calendarDay == DateTime.Today ? $"Today, {calendarDay:dddd d MMMM}" : calendarDay.ToString("dddd d MMMM yyyy"));

        if (Officer) {
            if (ImGui.Button("Add event")) {
                var start = calendarDay.Date.AddHours(20);
                var e = new FcEvent { Title = "New event", StartUtc = start.ToUniversalTime(), Host = Watcher.PlayerName };
                Board.Events.Add(e);
                Changed(e);
                editingEvent = e.Id;
            }
            ImGui.SameLine();
        }

        if (day.Count == 0) {
            if (Officer) ImGui.SameLine();
            ImGui.TextDisabled("Nothing planned.");
        }

        foreach (var (e, start) in day) DrawEventRow(e, start);

        if (editingEvent is { } id && Board.Events.FirstOrDefault(e => e.Id == id && !e.Deleted) is { } editing) {
            Heading("Edit event");
            DrawEventEditor(editing);
        }

        Heading("Coming up");
        var upcoming = Schedule.Upcoming(Board, DateTime.UtcNow, TimeSpan.FromDays(30), 8);
        if (upcoming.Count == 0) ImGui.TextDisabled("Nothing in the next 30 days.");
        foreach (var (e, start) in upcoming) {
            if (ImGui.Selectable($"{Schedule.Local(start)}   {e.Title}##Up{e.Id}{start.Ticks}")) {
                var local = start.ToLocalTime().Date;
                calendarDay = local;
                calendarMonth = new DateTime(local.Year, local.Month, 1);
                editingEvent = null;
            }
        }
    }

    private void DrawEventRow(FcEvent e, DateTime start) {
        using var id = ImRaii.PushId($"{e.Id}{start.Ticks}");
        var end = start.AddMinutes(e.DurationMinutes);

        ImGui.TextColored(theme.AccentText, $"{Schedule.Local(start, withDate: false)}–{Schedule.Local(end, withDate: false)}");
        ImGui.SameLine();
        ImGui.TextUnformatted(e.Title);
        if (e.Repeat != EventRepeat.None) {
            ImGui.SameLine();
            ImGui.TextDisabled($"({RepeatNames[(int)e.Repeat].ToLowerInvariant()})");
        }

        if (Officer) {
            ImGui.SameLine();
            if (ImGui.SmallButton(editingEvent == e.Id ? "Close" : "Edit")) editingEvent = editingEvent == e.Id ? null : e.Id;
        }

        using var indent = ImRaii.PushIndent();
        var details = string.Join(" · ", new[] {
            e.Location.Length > 0 ? $"at {e.Location}" : "",
            e.Host.Length > 0 ? $"host {e.Host}" : "",
        }.Where(s => s.Length > 0));
        if (details.Length > 0) ImGui.TextDisabled(details);
        if (e.Description.Length > 0) Hint(e.Description);
        DrawSignups(e, start);
    }

    private void DrawEventEditor(FcEvent e) {
        using var id = ImRaii.PushId(e.Id.ToString());
        var width = Math.Min(ImGui.GetContentRegionAvail().X, 420 * Scale);
        var changed = false;

        var title = e.Title;
        ImGui.SetNextItemWidth(width);
        if (ImGui.InputTextWithHint("##Title", "Title", ref title, 100)) {
            e.Title = title;
            changed = true;
        }

        var start = e.StartUtc;
        if (DateTimeField("Start", ref start)) {
            e.StartUtc = start;
            changed = true;
        }

        var duration = e.DurationMinutes;
        ImGui.SetNextItemWidth(110 * Scale);
        if (ImGui.InputInt("Minutes long", ref duration, 15)) {
            e.DurationMinutes = Math.Clamp(duration, 0, 24 * 60);
            changed = true;
        }

        ImGui.SameLine();
        var repeat = (int)e.Repeat;
        ImGui.SetNextItemWidth(150 * Scale);
        if (ImGui.Combo("##Repeat", ref repeat, RepeatNames, RepeatNames.Length)) {
            e.Repeat = (EventRepeat)repeat;
            changed = true;
        }

        var location = e.Location;
        ImGui.SetNextItemWidth(width);
        if (ImGui.InputTextWithHint("##Location", "Where (for example: FC house, Mist ward 5 plot 12)", ref location, 120)) {
            e.Location = location;
            changed = true;
        }

        var host = e.Host;
        if (MemberField("Host", "Host", ref host, width)) {
            e.Host = host;
            changed = true;
        }

        var description = e.Description;
        if (ImGui.InputTextMultiline("##Description", ref description, 2000, new Vector2(width, 70 * Scale))) {
            e.Description = description;
            changed = true;
        }

        if (changed) Changed(e);

        if (ImGui.SmallButton("Done")) editingEvent = null;
        ImGui.SameLine();
        if (DeleteButton()) {
            e.Deleted = true;
            Changed(e);
            editingEvent = null;
        }
        ImGui.SameLine();
        ImGui.TextDisabled(e.UpdatedBy.Length > 0 ? $"Last changed by {e.UpdatedBy}" : "");
    }
}
