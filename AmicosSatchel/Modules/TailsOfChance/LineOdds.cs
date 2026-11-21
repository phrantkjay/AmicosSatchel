using System.Collections.Generic;
using System.Numerics;

namespace AmicosSatchel.Modules.TailsOfChance;

/// <summary>
/// Chances of finishing a Wondrous Tails journal with at least one, two, or three lines.
/// </summary>
/// <param name="AtLeastOne">Chance of one or more completed lines.</param>
/// <param name="AtLeastTwo">Chance of two or more completed lines.</param>
/// <param name="AtLeastThree">Chance of three or more completed lines.</param>
public readonly record struct Odds(double AtLeastOne, double AtLeastTwo, double AtLeastThree) {
    public double this[int lines] => lines switch {
        1 => AtLeastOne,
        2 => AtLeastTwo,
        _ => AtLeastThree,
    };
}

/// <summary>
/// Exact line probabilities for the 4x4 Wondrous Tails board.
/// </summary>
/// <remarks>
/// A finished journal holds 9 seals, and each new seal lands on a random empty square.
/// That makes every 9-square board that contains the seals already placed equally likely,
/// so the odds are found by listing all 9-square boards once and counting the ones that
/// match the current seals.
/// </remarks>
public static class LineOdds {
    public const int BoardSize = 4;
    public const int SquareCount = BoardSize * BoardSize;
    public const int SealsToFinish = 9;

    // Every row, column, and both diagonals, as bit masks over the 16 squares (bit = row * 4 + column).
    private static readonly ushort[] Lines = BuildLines();

    // Every possible finished board, paired with how many lines it completes.
    private static readonly (ushort Board, byte LineCount)[] FinishedBoards = BuildFinishedBoards();

    /// <summary>
    /// Odds after a shuffle. Rearranging the placed seals at random and then filling the rest at random
    /// is the same as drawing a completely random finished board, so this is the same for any seal count.
    /// </summary>
    public static Odds ShuffleAverage { get; } = Calculate(0)!.Value;

    /// <summary>
    /// Calculates the odds for a board.
    /// </summary>
    /// <param name="placedSeals">Bit mask of squares that already hold a seal.</param>
    /// <returns>The odds, or null when the board holds more seals than a finished journal.</returns>
    public static Odds? Calculate(ushort placedSeals) {
        if (BitOperations.PopCount(placedSeals) > SealsToFinish) return null;

        var reachable = 0;
        var oneOrMore = 0;
        var twoOrMore = 0;
        var threeOrMore = 0;

        foreach (var (board, lineCount) in FinishedBoards) {
            if ((board & placedSeals) != placedSeals) continue;

            reachable++;
            if (lineCount >= 1) oneOrMore++;
            if (lineCount >= 2) twoOrMore++;
            if (lineCount >= 3) threeOrMore++;
        }

        return new Odds(
            (double)oneOrMore / reachable,
            (double)twoOrMore / reachable,
            (double)threeOrMore / reachable);
    }

    private static ushort[] BuildLines() {
        var lines = new List<ushort>();

        for (var i = 0; i < BoardSize; i++) {
            ushort row = 0, column = 0;
            for (var j = 0; j < BoardSize; j++) {
                row |= Square(i, j);
                column |= Square(j, i);
            }
            lines.Add(row);
            lines.Add(column);
        }

        ushort diagonal = 0, antiDiagonal = 0;
        for (var i = 0; i < BoardSize; i++) {
            diagonal |= Square(i, i);
            antiDiagonal |= Square(i, BoardSize - 1 - i);
        }
        lines.Add(diagonal);
        lines.Add(antiDiagonal);

        return [.. lines];
    }

    private static (ushort, byte)[] BuildFinishedBoards() {
        var boards = new List<(ushort, byte)>();

        for (var board = 0; board <= ushort.MaxValue; board++) {
            if (BitOperations.PopCount((uint)board) != SealsToFinish) continue;

            byte lineCount = 0;
            foreach (var line in Lines) {
                if ((board & line) == line) lineCount++;
            }
            boards.Add(((ushort)board, lineCount));
        }

        return [.. boards];
    }

    private static ushort Square(int row, int column) => (ushort)(1 << (row * BoardSize + column));
}
