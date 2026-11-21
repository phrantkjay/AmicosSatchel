using System;
using System.Linq;
using AmicosSatchel.Modules.FcCommandCenter.Data;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AmicosSatchel.Modules.FcCommandCenter.Windows;

// Voyage loot: what each finished voyage brought back, and totals over time.
public sealed partial class CommandCenterWindow {
    private static readonly int[] LootRanges = [7, 30, 90];
    private static readonly string[] LootRangeNames = ["Last 7 days", "Last 30 days", "Last 90 days"];

    private int lootRange = 1;

    private void DrawLoot() {
        var loot = Board.Loot.Where(l => !l.Deleted).ToList();
        if (loot.Count == 0) return;

        if (!ImGui.CollapsingHeader($"Voyage loot ({loot.Count} voyages logged)###Loot")) return;
        Hint("Each vessel only remembers its last voyage, so a voyage is logged when someone visits the workshop before sending it out again.");

        ImGui.SetNextItemWidth(150 * Scale);
        ImGui.Combo("##LootRange", ref lootRange, LootRangeNames, LootRangeNames.Length);

        var since = DateTime.UtcNow.AddDays(-LootRanges[lootRange]);
        var inRange = loot.Where(l => l.ReturnUtc >= since).ToList();
        if (inRange.Count == 0) {
            ImGui.TextDisabled("No voyages logged in that time.");
            return;
        }

        var totals = inRange.SelectMany(l => l.Items)
            .GroupBy(i => i.ItemId)
            .Select(g => (ItemId: g.Key, Count: g.Sum(i => i.Count)))
            .OrderByDescending(t => t.Count)
            .ToList();

        ImGui.TextColored(theme.Heading, $"Totals from {inRange.Count} voyages");
        using (var table = ImRaii.Table("##LootTotals", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp)) {
            if (table) {
                ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 3);
                ImGui.TableSetupColumn("Amount", ImGuiTableColumnFlags.WidthFixed, 80 * Scale);
                foreach (var (itemId, count) in totals) {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    GameIcon(catalog.Item(itemId)?.Icon ?? 0);
                    ImGui.SameLine();
                    ImGui.TextUnformatted(catalog.ItemName(itemId));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted($"{count:N0}");
                }
            }
        }

        ImGui.TextColored(theme.Heading, "Voyages");
        foreach (var l in inRange.OrderByDescending(l => l.ReturnUtc)) {
            using var id = ImRaii.PushId(l.Id.ToString());
            var route = l.Route.Length > 0 ? $": {l.Route}" : "";
            if (!ImGui.TreeNode($"{Schedule.Local(l.ReturnUtc)}  {l.Vessel}{route}###Voyage")) continue;
            foreach (var item in l.Items) {
                GameIcon(catalog.Item(item.ItemId)?.Icon ?? 0);
                ImGui.SameLine();
                ImGui.TextUnformatted($"{catalog.ItemName(item.ItemId)}{(item.HighQuality ? " (HQ)" : "")} × {item.Count:N0}");
            }
            ImGui.TreePop();
        }
    }
}
