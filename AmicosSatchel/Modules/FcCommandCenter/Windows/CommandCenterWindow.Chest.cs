using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AmicosSatchel.Modules.FcCommandCenter.Data;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AmicosSatchel.Modules.FcCommandCenter.Windows;

// Chest: what the FC chest held when last opened, with officers' notes and minimum amounts.
public sealed partial class CommandCenterWindow {
    private string chestSearch = "";
    private bool chestOnlyNoted;

    /// <summary>One item in the chest, added up over every page and over HQ and NQ.</summary>
    private sealed record ChestLine(uint ItemId, int Quantity, int HighQuality, IReadOnlyList<int> Pages);

    private List<ChestLine> ChestTotals() =>
        Board.Chest.Pages
            .SelectMany(p => p.Value.Items.Select(item => (Page: p.Key, Item: item)))
            .GroupBy(x => x.Item.ItemId)
            .Select(g => new ChestLine(g.Key, g.Sum(x => x.Item.Quantity), g.Where(x => x.Item.HighQuality).Sum(x => x.Item.Quantity),
                g.Select(x => x.Page).Distinct().Order().ToList()))
            .ToList();

    /// <summary>How many of an item the chest held when last read, crystals included.</summary>
    private int InChest(uint itemId) =>
        Board.Chest.Pages.Values.Sum(p => p.Items.Where(i => i.ItemId == itemId).Sum(i => i.Quantity)) +
        Board.Chest.Crystals.Where(i => i.ItemId == itemId).Sum(i => i.Quantity);

    /// <summary>Items with a minimum amount that the chest is below.</summary>
    private IEnumerable<(ChestNote Note, int Have)> LowStock() =>
        Board.ChestNotes
            .Where(n => !n.Deleted && n.ItemId != 0 && n.MinQuantity > 0)
            .Select(n => (n, InChest(n.ItemId)))
            .Where(x => x.Item2 < x.n.MinQuantity)
            .OrderBy(x => x.Item2 / (double)x.n.MinQuantity);

    private void DrawChest() {
        var chest = Board.Chest;

        if (watcher.ChestOpen) GoodText("Reading the chest now. Click through each page in game to update it.");
        else Hint("Open the FC chest in game to update this. Each page updates when you view it.");

        if (chest.GilCapturedUtc is { } gilTime) {
            ImGui.TextUnformatted($"{chest.Gil:N0} gil");
            ImGui.SameLine();
            ImGui.TextDisabled($"as of {Schedule.Local(gilTime)}{(chest.CapturedBy.Length > 0 ? $", read by {chest.CapturedBy}" : "")}");
            DrawGilTrend();
        }

        if (chest.Pages.Count > 0) {
            var pages = string.Join("  ", Enumerable.Range(1, 5).Select(p =>
                chest.Pages.TryGetValue(p, out var page) ? $"Page {p}: {Schedule.Local(page.CapturedUtc)}" : $"Page {p}: not read"));
            ImGui.TextDisabled(pages);
        }

        DrawGeneralChestNote();
        DrawLowStock();
        DrawCrystals();
        DrawChestChanges();

        Heading("Items");
        if (chest.Pages.Count == 0 && !Board.ChestNotes.Any(n => !n.Deleted && n.ItemId != 0)) {
            ImGui.TextDisabled("No items read yet.");
            return;
        }

        ImGui.SetNextItemWidth(220 * Scale);
        ImGui.InputTextWithHint("##ChestSearch", "Search items", ref chestSearch, 64);
        ImGui.SameLine();
        ImGui.Checkbox("Only items with notes", ref chestOnlyNoted);

        DrawChestTable();
    }

    private void DrawGeneralChestNote() {
        var general = Board.NoteFor(0);
        if (!Officer) {
            if (general is { Note.Length: > 0 }) {
                Heading("Chest rules");
                Hint(general.Note);
            }
            return;
        }

        Heading("Chest rules");
        var text = general?.Note ?? "";
        if (ImGui.InputTextMultiline("##ChestRules", ref text, 2000, new Vector2(-1, 54 * Scale))) {
            SetChestNote(0, text, general?.MinQuantity ?? 0);
        }
        if (text.Length == 0) ImGui.TextDisabled("Notes for everyone, such as which page is for what or what may be taken.");
    }

    private void DrawLowStock() {
        var low = LowStock().ToList();
        if (low.Count == 0) return;

        Heading("Running low");
        foreach (var (note, have) in low) {
            GameIcon(catalog.Item(note.ItemId)?.Icon ?? 0);
            ImGui.SameLine();
            ErrorText($"{catalog.ItemName(note.ItemId)}: {have} of {note.MinQuantity}");
            if (note.Note.Length > 0) {
                ImGui.SameLine();
                ImGui.TextDisabled(note.Note);
            }
        }
    }

    private void DrawCrystals() {
        var crystals = Board.Chest.Crystals;
        if (crystals.Count == 0) return;

        Heading("Crystals");
        var columns = Math.Clamp((int)(ImGui.GetContentRegionAvail().X / (110 * Scale)), 1, 6);
        using var table = ImRaii.Table("##Crystals", columns, ImGuiTableFlags.SizingStretchSame);
        if (!table) return;

        foreach (var c in crystals.OrderBy(c => c.ItemId)) {
            ImGui.TableNextColumn();
            GameIcon(catalog.Item(c.ItemId)?.Icon ?? 0);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(catalog.ItemName(c.ItemId));
            ImGui.SameLine();
            ImGui.TextUnformatted($"{c.Quantity:N0}");
        }
    }

    private void DrawChestTable() {
        var lines = ChestTotals();

        // Items with a note but none in the chest still get a row, so their minimum shows.
        foreach (var note in Board.ChestNotes.Where(n => !n.Deleted && n.ItemId != 0)) {
            if (lines.All(l => l.ItemId != note.ItemId)) lines.Add(new ChestLine(note.ItemId, 0, 0, []));
        }

        var search = chestSearch;
        var shown = lines
            .Where(l => search.Length == 0 || catalog.ItemName(l.ItemId).Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(l => !chestOnlyNoted || Board.NoteFor(l.ItemId) is not null)
            .OrderBy(l => catalog.ItemName(l.ItemId), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var columns = Officer ? 5 : 4;
        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY;
        using var table = ImRaii.Table("##ChestItems", columns, flags, new Vector2(-1, Math.Max(200 * Scale, ImGui.GetContentRegionAvail().Y)));
        if (!table) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 2.4f);
        ImGui.TableSetupColumn("Amount", ImGuiTableColumnFlags.WidthFixed, 80 * Scale);
        ImGui.TableSetupColumn("Page", ImGuiTableColumnFlags.WidthFixed, 50 * Scale);
        ImGui.TableSetupColumn("Note", ImGuiTableColumnFlags.WidthStretch, 2.4f);
        if (Officer) ImGui.TableSetupColumn("Keep at least", ImGuiTableColumnFlags.WidthFixed, 100 * Scale);
        ImGui.TableHeadersRow();

        foreach (var line in shown) {
            using var id = ImRaii.PushId((int)line.ItemId);
            var note = Board.NoteFor(line.ItemId);
            var info = catalog.Item(line.ItemId);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            GameIcon(info?.Icon ?? 0, ImGui.GetFrameHeight());
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(info?.Name ?? $"Item {line.ItemId}");

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            var low = note is { MinQuantity: > 0 } && line.Quantity < note.MinQuantity;
            if (low) ErrorText($"{line.Quantity:N0}");
            else ImGui.TextUnformatted($"{line.Quantity:N0}");
            if (line.HighQuality > 0 && ImGui.IsItemHovered()) ImGui.SetTooltip($"{line.HighQuality:N0} high quality");

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled(string.Join(",", line.Pages));

            ImGui.TableNextColumn();
            if (Officer) {
                var text = note?.Note ?? "";
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputTextWithHint("##Note", "Add a note", ref text, 200)) SetChestNote(line.ItemId, text, note?.MinQuantity ?? 0);
            } else {
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(note?.Note ?? "");
            }

            if (!Officer) continue;
            ImGui.TableNextColumn();
            var min = note?.MinQuantity ?? 0;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputInt("##Min", ref min, 0)) SetChestNote(line.ItemId, note?.Note ?? "", Math.Clamp(min, 0, 999_999));
        }
    }

    /// <summary>Creates, changes or removes the note for an item (0 for the general chest note).</summary>
    private void SetChestNote(uint itemId, string text, int minQuantity) {
        var note = Board.NoteFor(itemId) ?? Board.ChestNotes.FirstOrDefault(n => n.ItemId == itemId);
        if (note is null) {
            if (text.Length == 0 && minQuantity == 0) return;
            note = new ChestNote { ItemId = itemId };
            Board.ChestNotes.Add(note);
        }

        note.Note = text;
        note.MinQuantity = minQuantity;
        note.Deleted = text.Length == 0 && minQuantity == 0;
        Changed(note);
    }
}
