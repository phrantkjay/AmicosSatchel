using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AmicosSatchel.Modules.FcCommandCenter.Data;
using AmicosSatchel.Modules.FcCommandCenter.Game;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AmicosSatchel.Modules.FcCommandCenter.Windows;

// Tasks: jobs officers hand out to each other (or to members).
public sealed partial class CommandCenterWindow {
    private static readonly string[] TaskFilters = ["Open", "Assigned to me", "Unassigned", "Done", "All"];
    private static readonly string[] PriorityNames = ["Low", "Normal", "High"];
    private static readonly string[] StateNames = ["To do", "In progress", "Done"];

    private int taskFilter;
    private Guid? editingTask;

    private void DrawTasks() {
        if (Officer) {
            if (ImGui.Button("Add task")) {
                var t = new OfficerTask { Title = "New task", CreatedBy = Watcher.PlayerName };
                Board.Tasks.Add(t);
                Changed(t);
                editingTask = t.Id;
                if (taskFilter is 3) taskFilter = 0;
            }
            ImGui.SameLine();
        }

        ImGui.SetNextItemWidth(160 * Scale);
        ImGui.Combo("##TaskFilter", ref taskFilter, TaskFilters, TaskFilters.Length);

        var me = Watcher.PlayerName;
        IEnumerable<OfficerTask> tasks = Board.Tasks.Where(t => !t.Deleted);
        tasks = taskFilter switch {
            0 => tasks.Where(t => t.State != TaskState.Done),
            1 => tasks.Where(t => t.State != TaskState.Done && Watcher.Same(t.Assignee, me)),
            2 => tasks.Where(t => t.State != TaskState.Done && t.Assignee.Trim().Length == 0),
            3 => tasks.Where(t => t.State == TaskState.Done),
            _ => tasks,
        };
        var list = tasks
            .OrderBy(t => t.State == TaskState.Done)
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.DueUtc ?? DateTime.MaxValue)
            .ToList();

        if (list.Count == 0) {
            ImGui.TextDisabled("No tasks here.");
        } else {
            DrawTaskTable(list);
        }

        if (editingTask is { } id && Board.Tasks.FirstOrDefault(t => t.Id == id && !t.Deleted) is { } editing && Officer) {
            Heading("Edit task");
            DrawTaskEditor(editing);
        }
    }

    private void DrawTaskTable(List<OfficerTask> list) {
        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY;
        var height = editingTask is null ? -1 : Math.Min(list.Count + 1.5f, 9) * ImGui.GetFrameHeightWithSpacing();
        using var table = ImRaii.Table("##Tasks", 5, flags, new Vector2(-1, height));
        if (!table) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Task", ImGuiTableColumnFlags.WidthStretch, 3);
        ImGui.TableSetupColumn("Assigned to", ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableSetupColumn("Due", ImGuiTableColumnFlags.WidthStretch, 1.3f);
        ImGui.TableSetupColumn("Priority", ImGuiTableColumnFlags.WidthFixed, 60 * Scale);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 110 * Scale);
        ImGui.TableHeadersRow();

        var now = DateTime.UtcNow;
        foreach (var t in list) {
            using var id = ImRaii.PushId(t.Id.ToString());
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            var done = t.State == TaskState.Done;
            using (ImRaii.PushColor(ImGuiCol.Text, theme.Muted, done)) {
                if (ImGui.Selectable(t.Title, editingTask == t.Id, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap) && Officer) {
                    editingTask = editingTask == t.Id ? null : t.Id;
                }
            }
            if (t.Details.Length > 0 && ImGui.IsItemHovered()) {
                using var tooltip = ImRaii.Tooltip();
                using var wrap = ImRaii.TextWrapPos(360 * Scale);
                ImGui.TextUnformatted(t.Details);
            }

            ImGui.TableNextColumn();
            if (t.Assignee.Length > 0) ImGui.TextUnformatted(t.Assignee);
            else ImGui.TextDisabled("Anyone");

            ImGui.TableNextColumn();
            if (t.DueUtc is { } due) {
                if (!done && due < now) ErrorText($"{Schedule.Local(due)} (late)");
                else ImGui.TextDisabled(Schedule.Local(due));
            }

            ImGui.TableNextColumn();
            if (t.Priority == TaskPriority.High) ErrorText("High");
            else ImGui.TextDisabled(PriorityNames[(int)t.Priority]);

            ImGui.TableNextColumn();
            if (Officer) {
                var state = (int)t.State;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.Combo("##State", ref state, StateNames, StateNames.Length)) {
                    t.State = (TaskState)state;
                    Changed(t);
                }
            } else {
                ImGui.TextUnformatted(StateNames[(int)t.State]);
            }
        }
    }

    private void DrawTaskEditor(OfficerTask t) {
        using var id = ImRaii.PushId($"Edit{t.Id}");
        var width = Math.Min(ImGui.GetContentRegionAvail().X, 460 * Scale);
        var changed = false;

        var title = t.Title;
        ImGui.SetNextItemWidth(width);
        if (ImGui.InputTextWithHint("##Title", "What needs doing", ref title, 140)) {
            t.Title = title;
            changed = true;
        }

        var assignee = t.Assignee;
        if (MemberField("Assignee", "Assigned to (empty for anyone)", ref assignee, width)) {
            t.Assignee = assignee;
            changed = true;
        }

        var priority = (int)t.Priority;
        ImGui.SetNextItemWidth(110 * Scale);
        if (ImGui.Combo("##Priority", ref priority, PriorityNames, PriorityNames.Length)) {
            t.Priority = (TaskPriority)priority;
            changed = true;
        }
        ImGui.SameLine();
        ImGui.TextDisabled("priority");

        var hasDue = t.DueUtc is not null;
        if (ImGui.Checkbox("Due by", ref hasDue)) {
            t.DueUtc = hasDue ? DateTime.Today.AddDays(7).AddHours(20).ToUniversalTime() : null;
            changed = true;
        }
        if (t.DueUtc is { } due) {
            ImGui.SameLine();
            if (DateTimeField("Due", ref due)) {
                t.DueUtc = due;
                changed = true;
            }
        }

        var details = t.Details;
        if (ImGui.InputTextMultiline("##Details", ref details, 3000, new Vector2(width, 80 * Scale))) {
            t.Details = details;
            changed = true;
        }

        if (changed) Changed(t);

        if (ImGui.SmallButton("Done editing")) editingTask = null;
        ImGui.SameLine();
        if (DeleteButton()) {
            t.Deleted = true;
            Changed(t);
            editingTask = null;
        }
        ImGui.SameLine();
        ImGui.TextDisabled(t.CreatedBy.Length > 0 ? $"Added by {t.CreatedBy}" : "");
    }
}
