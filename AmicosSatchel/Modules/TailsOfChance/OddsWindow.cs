using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AmicosSatchel.Modules.TailsOfChance;

/// <summary>
/// Shows line odds inside the Wondrous Tails journal, in the description box under the seal prompt.
/// Falls back to a small panel beside the journal if that box can't be found.
/// </summary>
public sealed class OddsWindow : Window {
    private const string JournalAddon = "WeeklyBingo";

    // Text node holding the journal's description ("Complete a task to receive a seal.").
    // The text only fills the top of the box, so the odds go in the space below it.
    private const uint DescriptionNodeId = 34;
    private const float GapBelowDescription = 10f;
    private const float InsetFromBoxEdge = 8f;

    // Dark outline around text drawn on the journal, like the game's own text.
    private const int OutlineWidth = 1;
    private static readonly Vector4 OutlineColor = new(0f, 0f, 0f, 0.9f);

    private const float GapFromJournal = 8f;

    // Range of placed seals for which the game offers a shuffle.
    // The maximum (7) is confirmed in game; the minimum (3) still needs checking.
    private const int MinSealsToShuffle = 3;
    private const int MaxSealsToShuffle = 7;

    private const ImGuiWindowFlags CommonFlags =
        ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav |
        ImGuiWindowFlags.NoSavedSettings;

    private const ImGuiWindowFlags EmbeddedFlags =
        CommonFlags | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground |
        ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoScrollbar;

    private readonly IGameGui gameGui;

    private Journal? journal;
    private bool embedded;
    private ushort? lastBoard;
    private Odds? currentOdds;

    public OddsWindow(IGameGui gameGui)
        : base("Tails of Chance##OddsPanel", CommonFlags) {
        this.gameGui = gameGui;
        IsOpen = true;
        RespectCloseHotkey = false;
        ShowCloseButton = false;
        PositionCondition = ImGuiCond.Always;
    }

    public override unsafe bool DrawConditions() {
        var addon = gameGui.GetAddonByName(JournalAddon);
        if (addon.IsNull || !addon.IsVisible) return false;

        journal = Journal.Read();
        if (journal is null) return false;

        var unitBase = (AtkUnitBase*)addon.Address;
        var description = unitBase->GetTextNodeById(DescriptionNodeId);
        embedded = description != null && description->IsVisible();

        if (embedded) {
            var scale = unitBase->Scale;

            // The description can wrap onto several lines, so measure what the game actually drew.
            ushort drawnWidth, drawnHeight;
            description->GetTextDrawSize(&drawnWidth, &drawnHeight);
            var textBottom = (drawnHeight + GapBelowDescription) * scale;

            Position = ImGuiHelpers.MainViewport.Pos +
                       new Vector2(description->ScreenX + InsetFromBoxEdge * scale, description->ScreenY + textBottom);
            Flags = EmbeddedFlags;
        } else {
            Position = ImGuiHelpers.MainViewport.Pos + new Vector2(addon.X + addon.ScaledWidth + GapFromJournal, addon.Y);
            Flags = CommonFlags;
        }

        return true;
    }

    public override void PreDraw() {
        if (!embedded) return;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, ImGui.GetStyle().CellPadding with { Y = 0 });
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, Vector4.Zero);
    }

    public override void PostDraw() {
        if (!embedded) return;
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);
    }

    public override void Draw() {
        var state = journal!.Value;

        if (lastBoard != state.PlacedSeals) {
            lastBoard = state.PlacedSeals;
            currentOdds = LineOdds.Calculate(state.PlacedSeals);
        }

        // The journal itself shows the seal count and Second Chance points; only repeat them in the side panel.
        if (!embedded) {
            ImGui.Text($"Seals placed: {state.SealCount} / {LineOdds.SealsToFinish}");
            ImGui.TextDisabled($"Second Chance points: {state.SecondChancePoints}");
            ImGui.Spacing();
        }

        if (currentOdds is not { } odds) {
            ImGui.TextColored(ImGuiColors.DalamudRed, "Unable to read this board.");
            return;
        }

        var canShuffle = state.SealCount is >= MinSealsToShuffle and <= MaxSealsToShuffle &&
                         state.SecondChancePoints > 0;
        DrawOddsTable(odds, canShuffle);

        if (canShuffle && !embedded) {
            ImGui.Spacing();
            ImGui.TextDisabled("Green: better than a shuffle. Red: worse.");
        }
    }

    /// <summary>
    /// Draws the odds with one column per line count, matching the journal's Reward List.
    /// </summary>
    /// <param name="canShuffle">
    /// Whether a shuffle is available right now. The shuffle row always shows as a reference
    /// (it is also the odds of a completely random board), but the current odds are only colored
    /// against it when the player can actually act on the comparison.
    /// </param>
    private void DrawOddsTable(Odds odds, bool canShuffle) {
        var flags = ImGuiTableFlags.SizingFixedFit | (embedded ? ImGuiTableFlags.None : ImGuiTableFlags.RowBg);
        if (!ImGui.BeginTable("##LineOdds", 4, flags)) return;

        var labelColor = ImGuiColors.DalamudWhite;
        var shuffleColor = canShuffle ? ImGuiColors.DalamudWhite : ImGuiColors.DalamudGrey;

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        for (var lines = 1; lines <= 3; lines++) {
            ImGui.TableNextColumn();
            DrawText(lines == 1 ? "1+ line" : $"{lines}+ lines", labelColor);
        }

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        DrawText("Now", labelColor);
        for (var lines = 1; lines <= 3; lines++) {
            ImGui.TableNextColumn();
            DrawText(FormatPercent(odds[lines]), ColorFor(odds[lines], LineOdds.ShuffleAverage[lines], canShuffle));
        }

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        DrawText("Shuffle", shuffleColor);
        for (var lines = 1; lines <= 3; lines++) {
            ImGui.TableNextColumn();
            DrawText(FormatPercent(LineOdds.ShuffleAverage[lines]), shuffleColor);
        }

        ImGui.EndTable();
    }

    /// <summary>
    /// Draws text, with a dark outline when shown on the journal so it stays readable over the artwork.
    /// </summary>
    private void DrawText(string text, Vector4 color) {
        if (embedded) {
            var position = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var outline = ImGui.GetColorU32(OutlineColor);
            for (var dx = -OutlineWidth; dx <= OutlineWidth; dx++) {
                for (var dy = -OutlineWidth; dy <= OutlineWidth; dy++) {
                    if (dx != 0 || dy != 0) drawList.AddText(position + new Vector2(dx, dy), outline, text);
                }
            }
        }

        ImGui.TextColored(color, text);
    }

    private static Vector4 ColorFor(double now, double shuffle, bool compareToShuffle) {
        if (now >= 1) return ImGuiColors.DalamudYellow;
        if (now <= 0) return ImGuiColors.DalamudGrey3;
        if (!compareToShuffle) return ImGuiColors.DalamudWhite;
        return now >= shuffle ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed;
    }

    private static string FormatPercent(double value) => $"{value * 100:0.0}%";
}
