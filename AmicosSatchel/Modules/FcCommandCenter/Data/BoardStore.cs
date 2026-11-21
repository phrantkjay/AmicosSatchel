using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmicosSatchel.Modules.FcCommandCenter.Data;

/// <summary>
/// Loads and saves boards, one JSON file per Free Company in the plugin's config folder.
/// Changes are saved a moment after they happen, so typing doesn't write the file on every key.
/// </summary>
public sealed class BoardStore : IDisposable {
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

    public static readonly JsonSerializerOptions Json = new() {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string folder;
    private DateTime? saveDue;

    public BoardStore(string configFolder, ulong lastFcId) {
        folder = Path.Combine(configFolder, "FcCommandCenter");
        Directory.CreateDirectory(folder);
        Board = Load(lastFcId);
    }

    /// <summary>The board for the FC of the character you're playing (or the last one seen).</summary>
    public FcBoard Board { get; private set; }

    /// <summary>Raised after switching to another FC's board.</summary>
    public event Action<FcBoard>? Switched;

    /// <summary>
    /// Switches to the board for <paramref name="fcId"/>. Before the FC is known, notes go on a
    /// board with id 0; the first time a real FC is seen, that board becomes the FC's board.
    /// </summary>
    public void UseFc(ulong fcId, string fcName) {
        if (fcId == 0) return;

        if (Board.FcId == fcId) {
            if (fcName.Length > 0 && Board.FcName != fcName) {
                Board.FcName = fcName;
                MarkChanged();
            }
            return;
        }

        SaveNow();

        if (Board.FcId == 0 && !File.Exists(PathFor(fcId))) {
            // Adopt what was written before the FC was read.
            var unclaimed = PathFor(0);
            Board.FcId = fcId;
            Board.FcName = fcName;
            SaveNow();
            if (File.Exists(unclaimed)) File.Delete(unclaimed);
        } else {
            Board = Load(fcId);
            if (fcName.Length > 0) Board.FcName = fcName;
        }

        Switched?.Invoke(Board);
    }

    public void MarkChanged() => saveDue ??= DateTime.UtcNow + SaveDelay;

    /// <summary>Saves pending changes once their delay has passed. Call every frame or so.</summary>
    public void Tick() {
        if (saveDue is { } due && DateTime.UtcNow >= due) SaveNow();
    }

    public void SaveNow() {
        saveDue = null;
        var path = PathFor(Board.FcId);
        var temp = path + ".tmp";
        try {
            File.WriteAllText(temp, JsonSerializer.Serialize(Board, Json));
            File.Move(temp, path, overwrite: true);
        } catch (Exception e) {
            Services.Log.Error(e, $"[FC Command Center] Couldn't save {path}.");
        }
    }

    public void Dispose() {
        if (saveDue is not null) SaveNow();
    }

    private FcBoard Load(ulong fcId) {
        var path = PathFor(fcId);
        FcBoard? board = null;

        if (File.Exists(path)) {
            try {
                board = JsonSerializer.Deserialize<FcBoard>(File.ReadAllText(path), Json);
            } catch (Exception e) {
                // Keep the unreadable file so nothing is lost, and start fresh.
                Services.Log.Error(e, $"[FC Command Center] Couldn't read {path}; keeping it as .broken.");
                File.Copy(path, path + ".broken", overwrite: true);
            }
        }

        board ??= new FcBoard();
        board.FcId = fcId;
        board.Tidy();
        return board;
    }

    private string PathFor(ulong fcId) => Path.Combine(folder, fcId == 0 ? "no-fc.json" : $"fc-{fcId}.json");
}
