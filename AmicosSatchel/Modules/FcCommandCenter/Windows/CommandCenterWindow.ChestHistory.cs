using System;
using System.Linq;
using System.Numerics;
using AmicosSatchel.Modules.FcCommandCenter.Data;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;

namespace AmicosSatchel.Modules.FcCommandCenter.Windows;

// Chest history: how the FC's gil has moved, and what went in and out of the chest.
public sealed partial class CommandCenterWindow {
    private const int ChangesShown = 60;

    private string changeSearch = "";

    private void DrawGilTrend() {
        var history = Board.Chest.GilHistory;
        if (history.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var (label, days) in new[] { ("7 days", 7), ("30 days", 30) }) {
            if (History.GilChangeSince(Board.Chest, now.AddDays(-days)) is not { } change) continue;
            ImGui.SameLine();
            var text = $"{(change >= 0 ? "+" : "−")}{Math.Abs(change):N0} in {label}";
            if (change >= 0) ImGui.TextColored(ImGuiColors.HealerGreen, text);
            else ErrorText(text);
        }

        if (history.Count < 2) return;
        var values = history.TakeLast(60).Select(g => (float)g.Gil).ToArray();
        using (ImRaii.PushColor(ImGuiCol.PlotLines, theme.Accent)) {
            ImGui.PlotLines("##Gil", values, 0, "", values.Min() * 0.98f, values.Max() * 1.02f + 1, new Vector2(Math.Min(ImGui.GetContentRegionAvail().X, 420 * Scale), 40 * Scale), sizeof(float));
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Gil over the last {values.Length} readings, since {Schedule.Local(history[^values.Length].Utc)}");
    }

    private void DrawChestChanges() {
        var changes = Board.ChestChanges.Where(c => !c.Deleted && c.Delta != 0).ToList();
        if (changes.Count == 0) return;

        if (!ImGui.CollapsingHeader($"Recent changes ({changes.Count})###ChestChanges")) return;
        Hint("What went in and out between readings of the chest. \"Seen by\" is who had the chest open, not necessarily who moved the items.");

        ImGui.SetNextItemWidth(220 * Scale);
        ImGui.InputTextWithHint("##ChangeSearch", "Search items", ref changeSearch, 64);

        var search = changeSearch;
        var shown = changes
            .Where(c => search.Length == 0 || catalog.ItemName(c.ItemId).Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.SeenUtc)
            .Take(ChangesShown)
            .ToList();

        using var table = ImRaii.Table("##Changes", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp);
        if (!table) return;

        ImGui.TableSetupColumn("When", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 2.4f);
        ImGui.TableSetupColumn("Change", ImGuiTableColumnFlags.WidthFixed, 80 * Scale);
        ImGui.TableSetupColumn("Seen by", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableHeadersRow();

        foreach (var c in shown) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextDisabled(Schedule.Local(c.SeenUtc));
            ImGui.TableNextColumn();
            GameIcon(catalog.Item(c.ItemId)?.Icon ?? 0);
            ImGui.SameLine();
            ImGui.TextUnformatted(catalog.ItemName(c.ItemId));
            ImGui.TableNextColumn();
            if (c.Delta > 0) ImGui.TextColored(ImGuiColors.HealerGreen, $"+{c.Delta:N0}");
            else ErrorText($"−{-c.Delta:N0}");
            ImGui.TableNextColumn();
            ImGui.TextDisabled(c.ReadBy);
        }
    }
}
