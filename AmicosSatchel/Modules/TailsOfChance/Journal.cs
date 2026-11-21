using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace AmicosSatchel.Modules.TailsOfChance;

/// <summary>
/// Snapshot of the player's current Wondrous Tails journal.
/// </summary>
/// <param name="PlacedSeals">Bit mask of squares holding a seal (bit = row * 4 + column).</param>
/// <param name="SealCount">Number of seals placed.</param>
/// <param name="SecondChancePoints">Second Chance points available for shuffling.</param>
public readonly record struct Journal(ushort PlacedSeals, int SealCount, int SecondChancePoints) {
    /// <summary>
    /// Reads the journal from the game.
    /// </summary>
    /// <returns>The journal, or null when the player has no current journal.</returns>
    public static unsafe Journal? Read() {
        var playerState = PlayerState.Instance();
        if (playerState == null || !playerState->HasWeeklyBingoJournal) return null;

        ushort placed = 0;
        for (var square = 0; square < LineOdds.SquareCount; square++) {
            if (playerState->IsWeeklyBingoStickerPlaced(square)) {
                placed |= (ushort)(1 << square);
            }
        }

        return new Journal(placed, playerState->WeeklyBingoNumPlacedStickers, (int)playerState->WeeklyBingoNumSecondChancePoints);
    }
}
