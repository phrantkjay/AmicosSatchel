using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace AmicosSatchel.Modules.FcCommandCenter.Game;

public sealed record CompanyActionInfo(uint Id, string Name, string Description, uint Icon, uint Cost, int Rank);

public sealed record ItemInfo(uint Id, string Name, uint Icon);

/// <summary>One material handed in during a workshop phase.</summary>
/// <param name="PerSet">Items in one set.</param>
/// <param name="Sets">Sets the phase needs.</param>
public sealed record PhaseMaterial(uint ItemId, int PerSet, int Sets) {
    public int Total => PerSet * Sets;
}

public sealed record ProjectPhase(string PartName, int PartNumber, int PhaseNumber, IReadOnlyList<PhaseMaterial> Materials);

/// <summary>A workshop project from the CompanyCraftSequence sheet, with every phase in order.</summary>
public sealed record ProjectRecipe(uint Id, string Name, string Category, uint ResultIcon, IReadOnlyList<ProjectPhase> Phases);

/// <summary>
/// Lookups into the game's data sheets: company actions, items and workshop projects.
/// Built the first time each one is needed, then kept.
/// </summary>
public sealed class Catalog {
    private readonly Dictionary<uint, ItemInfo?> items = [];
    private List<CompanyActionInfo>? actions;
    private Dictionary<string, uint>? actionByName;
    private List<ProjectRecipe>? recipes;
    private Dictionary<uint, ProjectRecipe>? recipeById;

    public IReadOnlyList<CompanyActionInfo> Actions => actions ??= LoadActions();

    public CompanyActionInfo? Action(uint id) => Actions.FirstOrDefault(a => a.Id == id);

    /// <summary>
    /// The company action that grants a status. Statuses and actions share their names
    /// ("The Heat of Battle II"), which is the only link the sheets give.
    /// </summary>
    public uint? ActionIdForStatusName(string statusName) {
        actionByName ??= Actions.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        return actionByName.TryGetValue(statusName, out var id) ? id : null;
    }

    public ItemInfo? Item(uint id) {
        if (id == 0) return null;
        if (items.TryGetValue(id, out var cached)) return cached;

        var row = Services.DataManager.GetExcelSheet<Item>().GetRowOrDefault(id);
        var info = row is { } r && !r.Name.IsEmpty ? new ItemInfo(id, r.Name.ExtractText(), r.Icon) : null;
        items[id] = info;
        return info;
    }

    public string ItemName(uint id) => Item(id)?.Name ?? $"Item {id}";

    /// <summary>Every workshop project the game knows, sorted by category then name.</summary>
    public IReadOnlyList<ProjectRecipe> Recipes => recipes ??= LoadRecipes();

    public ProjectRecipe? Recipe(uint id) {
        recipeById ??= Recipes.ToDictionary(r => r.Id);
        return recipeById.GetValueOrDefault(id);
    }

    private static List<CompanyActionInfo> LoadActions() =>
        Services.DataManager.GetExcelSheet<CompanyAction>()
            .Where(a => !a.Name.IsEmpty && a.Icon != 0)
            .Select(a => new CompanyActionInfo(a.RowId, a.Name.ExtractText(), a.Description.ExtractText(), (uint)a.Icon, a.Cost,
                (int)a.FCRank.RowId))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private List<ProjectRecipe> LoadRecipes() {
        var list = new List<ProjectRecipe>();

        foreach (var sequence in Services.DataManager.GetExcelSheet<CompanyCraftSequence>()) {
            if (sequence.ResultItem.RowId == 0 || Item(sequence.ResultItem.RowId) is not { } result) continue;

            var phases = new List<ProjectPhase>();
            var partNumber = 0;
            foreach (var partRef in sequence.CompanyCraftPart) {
                if (partRef.RowId == 0 || partRef.ValueNullable is not { } part) continue;
                partNumber++;
                var partName = part.CompanyCraftType.ValueNullable?.Name.ExtractText() ?? $"Part {partNumber}";

                var phaseNumber = 0;
                foreach (var processRef in part.CompanyCraftProcess) {
                    if (processRef.RowId == 0 || processRef.ValueNullable is not { } process) continue;

                    var materials = new List<PhaseMaterial>();
                    for (var i = 0; i < process.SupplyItem.Count; i++) {
                        var itemId = process.SupplyItem[i].ValueNullable?.Item.RowId ?? 0;
                        int perSet = process.SetQuantity[i], sets = process.SetsRequired[i];
                        if (itemId == 0 || perSet == 0 || sets == 0) continue;
                        materials.Add(new PhaseMaterial(itemId, perSet, sets));
                    }

                    if (materials.Count == 0) continue;
                    phaseNumber++;
                    phases.Add(new ProjectPhase(partName, partNumber, phaseNumber, materials));
                }
            }

            if (phases.Count == 0) continue;
            var category = sequence.CompanyCraftDraftCategory.ValueNullable?.Name.ExtractText() ?? "";
            list.Add(new ProjectRecipe(sequence.RowId, result.Name, category, result.Icon, phases));
        }

        return list.OrderBy(r => r.Category).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
