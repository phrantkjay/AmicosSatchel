using System;
using System.Collections.Generic;
using System.Linq;
using AmicosSatchel.Modules.FcCommandCenter.Data;
using AmicosSatchel.Modules.FcCommandCenter.Game;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AmicosSatchel.Modules.FcCommandCenter.Windows;

// Company actions: what's running now, and the order officers plan to run them in.
public sealed partial class CommandCenterWindow {
    private static readonly TimeSpan EndingSoon = TimeSpan.FromHours(6);

    private string actionFilter = "";

    private List<RotationSlot> RotationInOrder() =>
        Board.Rotation.Where(r => !r.Deleted).OrderBy(r => r.Order).ToList();

    private RotationSlot? SuggestedNext() => Watcher.SuggestNext(Board, watcher.ActiveActions);

    private void DrawActions() {
        Heading("Running now");
        var active = watcher.ActiveActions;
        if (!Services.ClientState.IsLoggedIn) ImGui.TextDisabled("Log in to see your company actions.");
        else if (active.Count == 0) ImGui.TextDisabled("No company actions are running.");

        foreach (var a in active) {
            GameIcon(a.Icon, ImGui.GetFrameHeight());
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(a.Name);
            ImGui.SameLine();
            if (a.Remaining is not { } remaining) {
                ImGui.TextDisabled("time left unknown");
                continue;
            }
            var ends = $"{Schedule.Span(remaining)} left, ends {Schedule.Local(DateTime.UtcNow + remaining)}";
            if (remaining < EndingSoon) ErrorText(ends);
            else ImGui.TextDisabled(ends);
        }
        if (active.Count > 0) ImGui.TextDisabled("Read from your own status effects. Two company actions can run at once.");

        Heading("Planned rotation");
        var plan = RotationInOrder();
        var next = SuggestedNext();

        if (plan.Count == 0) {
            Hint(Officer
                ? "Add the company actions you plan to run, in order. The next one to activate is highlighted."
                : "Officers haven't shared a rotation yet.");
        } else {
            DrawRotationTable(plan, next);
        }

        if (!Officer) return;
        ImGui.Spacing();
        if (SearchCombo("AddAction", "Add a company action…", ref actionFilter, catalog.Actions, a => a.Name, _ => null,
                out var chosen, 300 * Scale) && chosen is not null) {
            var slot = new RotationSlot { ActionId = chosen.Id, Order = plan.Count == 0 ? 0 : plan.Max(r => r.Order) + 1 };
            Board.Rotation.Add(slot);
            Changed(slot);
        }
    }

    private void DrawRotationTable(List<RotationSlot> plan, RotationSlot? next) {
        var running = watcher.ActiveActions.Select(a => a.ActionId).OfType<uint>().ToHashSet();
        var columns = Officer ? 5 : 4;
        using var table = ImRaii.Table("##Rotation", columns, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp);
        if (!table) return;

        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 26 * Scale);
        ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthStretch, 2.2f);
        ImGui.TableSetupColumn("In stock", ImGuiTableColumnFlags.WidthFixed, 90 * Scale);
        ImGui.TableSetupColumn("Note", ImGuiTableColumnFlags.WidthStretch, 2);
        if (Officer) ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 110 * Scale);
        ImGui.TableHeadersRow();

        for (var i = 0; i < plan.Count; i++) {
            var slot = plan[i];
            using var id = ImRaii.PushId(slot.Id.ToString());
            var info = catalog.Action(slot.ActionId);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled($"{i + 1}");

            ImGui.TableNextColumn();
            GameIcon(info?.Icon ?? 0, ImGui.GetFrameHeight());
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(info?.Name ?? $"Action {slot.ActionId}");
            if (info is not null && ImGui.IsItemHovered()) {
                using var tooltip = ImRaii.Tooltip();
                using var wrap = ImRaii.TextWrapPos(340 * Scale);
                ImGui.TextUnformatted(info.Description);
                ImGui.TextDisabled($"{info.Cost:N0} company credits");
            }
            if (running.Contains(slot.ActionId)) {
                ImGui.SameLine();
                GoodText("running");
            } else if (next == slot) {
                ImGui.SameLine();
                ImGui.TextColored(theme.AccentText, "next");
            }

            ImGui.TableNextColumn();
            if (Officer) {
                var stock = slot.Stock;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputInt("##Stock", ref stock, 1)) {
                    slot.Stock = Math.Clamp(stock, 0, 99);
                    Changed(slot);
                }
            } else {
                ImGui.TextUnformatted(slot.Stock.ToString());
            }

            ImGui.TableNextColumn();
            if (Officer) {
                var note = slot.Note;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputTextWithHint("##Note", "For example: use on raid nights", ref note, 200)) {
                    slot.Note = note;
                    Changed(slot);
                }
            } else {
                ImGui.TextUnformatted(slot.Note);
            }

            if (!Officer) continue;
            ImGui.TableNextColumn();
            using (ImRaii.Disabled(i == 0)) {
                if (ImGui.ArrowButton("##Up", ImGuiDir.Up)) Move(plan, i, i - 1);
            }
            ImGui.SameLine();
            using (ImRaii.Disabled(i == plan.Count - 1)) {
                if (ImGui.ArrowButton("##Down", ImGuiDir.Down)) Move(plan, i, i + 1);
            }
            ImGui.SameLine();
            if (DeleteButton("Remove")) {
                slot.Deleted = true;
                Changed(slot);
            }
        }
    }

    /// <summary>Moves a step and renumbers the plan, touching only the steps whose position changed.</summary>
    private void Move(List<RotationSlot> plan, int from, int to) {
        var order = plan.ToList();
        var slot = order[from];
        order.RemoveAt(from);
        order.Insert(to, slot);

        for (var i = 0; i < order.Count; i++) {
            if (order[i].Order == i) continue;
            order[i].Order = i;
            Changed(order[i]);
        }
    }
}
