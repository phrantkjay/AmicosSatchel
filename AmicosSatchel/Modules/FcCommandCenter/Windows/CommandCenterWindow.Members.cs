using System;
using System.Linq;
using System.Numerics;
using AmicosSatchel.Modules.FcCommandCenter.Data;
using AmicosSatchel.Modules.FcCommandCenter.Game;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AmicosSatchel.Modules.FcCommandCenter.Windows;

// Members: the FC roster as last read, with when each member was last online, open tasks and officer notes.
public sealed partial class CommandCenterWindow {
    private static readonly string[] MemberFilters = ["Everyone", "Not seen online in 14 days", "Not seen online in 30 days", "With notes"];
    private static readonly int[] InactiveDays = [0, 14, 30, 0];

    private string memberSearch = "";
    private int memberListFilter;

    private void DrawMembers() {
        var roster = Board.Roster;
        Hint("Open the Free Company window's Members tab in game to update this list. Last online is noted whenever someone using the " +
             "command center has that window open, so it's only as fresh as the last time anyone looked.");

        if (roster is null || roster.Members.Count == 0) {
            ImGui.TextDisabled("No members read yet.");
            return;
        }

        ImGui.TextDisabled($"{roster.Members.Count} members, as of {Schedule.Local(roster.CapturedUtc)}");
        ImGui.SetNextItemWidth(220 * Scale);
        ImGui.InputTextWithHint("##MemberSearch", "Search members", ref memberSearch, 64);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220 * Scale);
        ImGui.Combo("##MemberFilter", ref memberListFilter, MemberFilters, MemberFilters.Length);

        var now = DateTime.UtcNow;
        var openTasks = Board.Tasks.Where(t => !t.Deleted && t.State != TaskState.Done).ToList();
        var search = memberSearch;
        var inactiveFor = InactiveDays[memberListFilter];

        var shown = roster.Members
            .Where(m => search.Length == 0 || m.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(m => inactiveFor == 0 || now - (m.LastOnlineUtc ?? m.FirstSeenUtc) > TimeSpan.FromDays(inactiveFor))
            .Where(m => memberListFilter != 3 || NoteFor(m) is { Note.Length: > 0 })
            .ToList();

        var columns = Officer ? 7 : 5;
        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY;
        using var table = ImRaii.Table("##Members", columns, flags, new Vector2(-1, -1));
        if (!table) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 1.8f);
        ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, 45 * Scale);
        ImGui.TableSetupColumn("Last online", ImGuiTableColumnFlags.WidthFixed, 110 * Scale);
        ImGui.TableSetupColumn("Tasks", ImGuiTableColumnFlags.WidthFixed, 45 * Scale);
        if (Officer) {
            ImGui.TableSetupColumn("Officer note", ImGuiTableColumnFlags.WidthStretch, 2.4f);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 70 * Scale);
        }
        ImGui.TableHeadersRow();

        foreach (var member in shown) {
            using var id = ImRaii.PushId($"{member.Name}@{member.World}");
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(member.Name);
            if (ImGui.IsItemHovered() && member.FirstSeenUtc != default) {
                ImGui.SetTooltip($"First seen in the member list {Schedule.Local(member.FirstSeenUtc)}");
            }

            ImGui.TableNextColumn();
            ImGui.TextDisabled(member.World);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(member.Job);

            ImGui.TableNextColumn();
            if (member.LastOnlineUtc is { } online) {
                var ago = now - online;
                var text = ago < TimeSpan.FromMinutes(30) ? "now" : $"{Schedule.Span(ago)} ago";
                if (ago > TimeSpan.FromDays(30)) ErrorText(text);
                else ImGui.TextUnformatted(text);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(Schedule.Local(online));
            } else {
                ImGui.TextDisabled("not seen");
            }

            ImGui.TableNextColumn();
            var tasks = openTasks.Count(t => Watcher.Same(t.Assignee, member.Name));
            if (tasks > 0) ImGui.TextUnformatted(tasks.ToString());
            else ImGui.TextDisabled("–");

            if (!Officer) continue;

            ImGui.TableNextColumn();
            var note = NoteFor(member);
            var text2 = note?.Note ?? "";
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##Note", "Such as how they joined or when to promote", ref text2, 300)) SetMemberNote(member, text2);

            ImGui.TableNextColumn();
            if (DeleteButton("Remove")) {
                roster.Members.Remove(member);
                store.MarkChanged();
            }
        }
    }

    private MemberNote? NoteFor(Member member) {
        var id = MemberNote.IdFor(member.Name, member.World);
        return Board.MemberNotes.FirstOrDefault(n => n.Id == id);
    }

    private void SetMemberNote(Member member, string text) {
        var note = NoteFor(member);
        if (note is null) {
            if (text.Length == 0) return;
            note = new MemberNote { Id = MemberNote.IdFor(member.Name, member.World), Member = member.Name, World = member.World };
            Board.MemberNotes.Add(note);
        }

        note.Note = text;
        note.Deleted = text.Length == 0;
        Changed(note);
    }
}
