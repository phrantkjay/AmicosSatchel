using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AmicosSatchel.Modules.FcCommandCenter.Data;
using AmicosSatchel.Modules.FcCommandCenter.Game;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AmicosSatchel.Modules.FcCommandCenter.Windows;

// Workshop: submersible and airship voyages, and projects being built with their materials.
public sealed partial class CommandCenterWindow {
    private string recipeFilter = "";
    private Guid? openProject;

    private void DrawWorkshop() {
        DrawVoyages();
        DrawLoot();
        DrawProjects();
    }

    private void DrawVoyages() {
        Heading("Voyages");
        var voyages = Board.Voyages;
        if (voyages is null || voyages.Vessels.Count == 0) {
            Hint("Go to the FC workshop and open the voyage control panel in game. Submersibles and airships are read from there.");
            return;
        }

        var by = voyages.CapturedBy.Length > 0 ? $", read by {voyages.CapturedBy}" : "";
        ImGui.TextDisabled($"As of {Schedule.Local(voyages.CapturedUtc)}{by}. Voyages sent since then don't show until someone visits the workshop.");

        using var table = ImRaii.Table("##Vessels", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp);
        if (!table) return;

        ImGui.TableSetupColumn("Vessel", ImGuiTableColumnFlags.WidthStretch, 2);
        ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 100 * Scale);
        ImGui.TableSetupColumn("Rank", ImGuiTableColumnFlags.WidthFixed, 50 * Scale);
        ImGui.TableSetupColumn("Back", ImGuiTableColumnFlags.WidthStretch, 2);
        ImGui.TableHeadersRow();

        var now = DateTime.UtcNow;
        foreach (var v in voyages.Vessels.OrderBy(v => v.Kind).ThenBy(v => v.Slot)) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(v.Name);
            ImGui.TableNextColumn();
            ImGui.TextDisabled(v.Kind.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(v.Rank.ToString());
            ImGui.TableNextColumn();
            if (v.ReturnUtc is not { } back) ImGui.TextDisabled("Docked");
            else if (back <= now) GoodText($"Back since {Schedule.Local(back)}");
            else ImGui.TextUnformatted($"{Schedule.Local(back)} ({Schedule.Until(back, now)})");
        }
    }

    private void DrawProjects() {
        Heading("Projects");
        var projects = Board.Projects.Where(p => !p.Deleted).OrderBy(p => p.Completed).ThenBy(p => p.Name).ToList();

        if (Officer) {
            if (SearchCombo("AddProject", "Track a workshop project…", ref recipeFilter, catalog.Recipes, r => r.Name, r => r.Category,
                    out var recipe, 300 * Scale) && recipe is not null) {
                var p = new WorkshopProject { Name = recipe.Name, SequenceId = recipe.Id, Assignee = Watcher.PlayerName };
                Board.Projects.Add(p);
                Changed(p);
                openProject = p.Id;
            }
            ImGui.SameLine();
            if (ImGui.Button("Other project")) {
                var p = new WorkshopProject { Name = "New project" };
                p.CustomMaterials.Add(new CustomMaterial { Name = "", Needed = 1 });
                Board.Projects.Add(p);
                Changed(p);
                openProject = p.Id;
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("A project with your own list of materials, such as a housing build or an FC fundraiser.");
        }

        if (projects.Count == 0) {
            ImGui.TextDisabled("No projects tracked.");
            return;
        }

        foreach (var p in projects) DrawProject(p);
    }

    private void DrawProject(WorkshopProject p) {
        using var id = ImRaii.PushId(p.Id.ToString());
        var recipe = p.SequenceId == 0 ? null : catalog.Recipe(p.SequenceId);
        var (done, total) = recipe is null
            ? (p.CustomMaterials.Sum(m => Math.Min(m.Have, m.Needed)), p.CustomMaterials.Sum(m => m.Needed))
            : (Math.Min(p.PhasesDone, recipe.Phases.Count), recipe.Phases.Count);
        var fraction = p.Completed ? 1f : total == 0 ? 0 : done / (float)total;

        ImGui.Separator();
        var isOpen = openProject == p.Id;
        if (recipe is not null) {
            GameIcon(recipe.ResultIcon, ImGui.GetFrameHeight());
            ImGui.SameLine();
        }
        ImGui.AlignTextToFramePadding();
        if (ImGui.Selectable($"{p.Name}##Open", isOpen, ImGuiSelectableFlags.None, new Vector2(220 * Scale, 0))) openProject = isOpen ? null : p.Id;

        ImGui.SameLine();
        var label = p.Completed ? "Finished" : recipe is null ? $"{fraction:P0}" : $"Phase {done} of {total} done";
        ImGui.ProgressBar(fraction, new Vector2(200 * Scale, ImGui.GetFrameHeight()), label);

        if (p.Assignee.Length > 0) {
            ImGui.SameLine();
            ImGui.TextDisabled($"lead: {p.Assignee}");
        }

        if (!isOpen) return;
        using var indent = ImRaii.PushIndent();
        if (recipe is not null) DrawRecipeProject(p, recipe);
        else DrawCustomProject(p);
        DrawProjectFooter(p);
    }

    private void DrawRecipeProject(WorkshopProject p, ProjectRecipe recipe) {
        var phasesDone = Math.Clamp(p.PhasesDone, 0, recipe.Phases.Count);

        if (!p.Completed && phasesDone < recipe.Phases.Count) {
            var phase = recipe.Phases[phasesDone];
            ImGui.TextColored(theme.Heading, $"Now: {phase.PartName}, phase {phase.PhaseNumber}");
            DrawMaterials(p, $"Phase{phasesDone}", phase.Materials.Select(m => (m.ItemId, m.Total, $"{m.Sets} × {m.PerSet}")));

            if (Officer) {
                if (ImGui.Button("Phase finished")) {
                    p.PhasesDone = phasesDone + 1;
                    if (p.PhasesDone >= recipe.Phases.Count) p.Completed = true;
                    Changed(p);
                }
                ImGui.SameLine();
            }
        }

        if (Officer) {
            using (ImRaii.Disabled(phasesDone == 0)) {
                if (ImGui.Button("Undo a phase")) {
                    p.PhasesDone = phasesDone - 1;
                    p.Completed = false;
                    Changed(p);
                }
            }
        }

        // Everything still to hand in, for gathering ahead.
        var remaining = recipe.Phases.Skip(p.Completed ? recipe.Phases.Count : phasesDone)
            .SelectMany(ph => ph.Materials)
            .GroupBy(m => m.ItemId)
            .Select(g => (g.Key, g.Sum(m => m.Total), ""))
            .ToList();

        if (remaining.Count > 0 && ImGui.TreeNode($"Everything still needed ({recipe.Phases.Count - phasesDone} phases)")) {
            DrawMaterials(p, "Remaining", remaining);
            ImGui.TreePop();
        }
    }

    /// <summary>Table of materials with how many the chest held when last read, and who's bringing them.</summary>
    private void DrawMaterials(WorkshopProject project, string id, IEnumerable<(uint ItemId, int Needed, string Detail)> materials) {
        using var table = ImRaii.Table($"##{id}", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp);
        if (!table) return;

        ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.WidthStretch, 2.4f);
        ImGui.TableSetupColumn("Needed", ImGuiTableColumnFlags.WidthFixed, 90 * Scale);
        ImGui.TableSetupColumn("In chest", ImGuiTableColumnFlags.WidthFixed, 70 * Scale);
        ImGui.TableSetupColumn("Short", ImGuiTableColumnFlags.WidthFixed, 60 * Scale);
        ImGui.TableSetupColumn("Bringing", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableHeadersRow();

        var chestRead = Board.Chest.Pages.Count > 0;
        foreach (var (itemId, needed, detail) in materials) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            GameIcon(catalog.Item(itemId)?.Icon ?? 0);
            ImGui.SameLine();
            ImGui.TextUnformatted(catalog.ItemName(itemId));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{needed:N0}");
            if (detail.Length > 0 && ImGui.IsItemHovered()) ImGui.SetTooltip($"{detail} (sets × items per set)");

            ImGui.TableNextColumn();
            var have = InChest(itemId);
            if (chestRead) ImGui.TextUnformatted($"{have:N0}");
            else ImGui.TextDisabled("?");

            ImGui.TableNextColumn();
            if (chestRead) {
                if (have >= needed) GoodText("—");
                else ErrorText($"{needed - have:N0}");
            }

            ImGui.TableNextColumn();
            using var row = ImRaii.PushId((int)itemId);
            PledgeField(project, itemId.ToString());
        }
    }

    /// <summary>Who's bringing a material; editable for officers.</summary>
    private void PledgeField(WorkshopProject project, string key) {
        var who = project.Pledges.GetValueOrDefault(key, "");
        if (!Officer) {
            ImGui.TextUnformatted(who);
            return;
        }

        ImGui.SetNextItemWidth(-1);
        if (!ImGui.InputTextWithHint("##Pledge", "Who's bringing it", ref who, 120)) return;
        if (who.Length == 0) project.Pledges.Remove(key);
        else project.Pledges[key] = who;
        Changed(project);
    }

    private void DrawCustomProject(WorkshopProject p) {
        if (!Officer) {
            foreach (var m in p.CustomMaterials) {
                var pledge = p.Pledges.GetValueOrDefault("custom:" + m.Name, "");
                var line = $"{m.Name}: {m.Have:N0} of {m.Needed:N0}{(pledge.Length > 0 ? $", {pledge} is bringing it" : "")}";
                if (m.Have >= m.Needed) GoodText(line);
                else ImGui.TextUnformatted(line);
            }
            return;
        }

        var name = p.Name;
        ImGui.SetNextItemWidth(300 * Scale);
        if (ImGui.InputTextWithHint("##Name", "Project name", ref name, 100)) {
            p.Name = name;
            Changed(p);
        }

        using (var table = ImRaii.Table("##Custom", 5, ImGuiTableFlags.SizingStretchProp)) {
            if (table) {
                ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.WidthStretch, 2.4f);
                ImGui.TableSetupColumn("Have", ImGuiTableColumnFlags.WidthFixed, 100 * Scale);
                ImGui.TableSetupColumn("Needed", ImGuiTableColumnFlags.WidthFixed, 100 * Scale);
                ImGui.TableSetupColumn("Bringing", ImGuiTableColumnFlags.WidthStretch, 1.6f);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 60 * Scale);
                ImGui.TableHeadersRow();

                for (var i = 0; i < p.CustomMaterials.Count; i++) {
                    var m = p.CustomMaterials[i];
                    using var row = ImRaii.PushId(i);
                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    var materialName = m.Name;
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputTextWithHint("##Material", "Material", ref materialName, 100)) {
                        m.Name = materialName;
                        Changed(p);
                    }

                    ImGui.TableNextColumn();
                    var have = m.Have;
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputInt("##Have", ref have, 0)) {
                        m.Have = Math.Max(0, have);
                        Changed(p);
                    }

                    ImGui.TableNextColumn();
                    var needed = m.Needed;
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputInt("##Needed", ref needed, 0)) {
                        m.Needed = Math.Max(0, needed);
                        Changed(p);
                    }

                    ImGui.TableNextColumn();
                    PledgeField(p, "custom:" + m.Name);

                    ImGui.TableNextColumn();
                    if (ImGui.SmallButton("Remove")) {
                        p.CustomMaterials.RemoveAt(i);
                        Changed(p);
                        break;
                    }
                }
            }
        }

        if (ImGui.SmallButton("Add material")) {
            p.CustomMaterials.Add(new CustomMaterial { Needed = 1 });
            Changed(p);
        }
    }

    private void DrawProjectFooter(WorkshopProject p) {
        if (!Officer) {
            if (p.Notes.Length > 0) Hint(p.Notes);
            return;
        }

        var lead = p.Assignee;
        if (MemberField("Lead", "Who's leading it", ref lead, 300 * Scale)) {
            p.Assignee = lead;
            Changed(p);
        }

        var notes = p.Notes;
        if (ImGui.InputTextMultiline("##Notes", ref notes, 2000, new Vector2(Math.Min(ImGui.GetContentRegionAvail().X, 460 * Scale), 54 * Scale))) {
            p.Notes = notes;
            Changed(p);
        }

        if (p.SequenceId == 0) {
            var completed = p.Completed;
            if (ImGui.Checkbox("Finished", ref completed)) {
                p.Completed = completed;
                Changed(p);
            }
            ImGui.SameLine();
        }

        if (DeleteButton("Stop tracking")) {
            p.Deleted = true;
            Changed(p);
            openProject = null;
        }
    }
}
