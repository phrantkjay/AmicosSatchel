using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace AmicosSatchel.Modules.SecondSight;

/// <summary>
/// Helps with the sightseeing log in game: a chat line when you reach a vista you haven't
/// logged (with the emote to use, or when to come back), and a glowing ring at A Realm Reborn
/// vistas, which unlike later ones have no marker of their own. Only reads game state; the
/// chat line is printed locally and the ring is drawn on screen.
/// </summary>
internal sealed unsafe class VistaGuide : IDisposable {
    private const float ArriveDistance = 20;  // yalms: close enough to "be at" the vista
    private const float LeaveDistance = 40;   // yalms: far enough to announce it again next time
    private const float MarkerDistance = 120; // yalms: rings further than this aren't drawn
    private const float RingRadius = 2.5f;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(500);

    private sealed record VistaInfo(
        uint Index, uint RowId, string Name, int Number, string ExpansionName, bool IsARealmReborn, uint TerritoryId, Vector3 Position,
        string Command, string EmoteName, (int Start, int Length)? Window, byte WeatherId, string? WeatherName);

    private readonly Configuration configuration;
    private readonly ILookup<uint, VistaInfo> byTerritory;
    private readonly Dictionary<byte, string> weatherNames;
    private readonly HashSet<uint> announced = [];

    // A Realm Reborn opens with #001-#020; the rest stay hidden in game until those are all logged.
    private const int ArrStarterVistas = 20;
    private readonly List<VistaInfo> arrStarters;
    private DateTime nextCheck;
    private uint lastTerritory;

    public VistaGuide(Configuration configuration) {
        this.configuration = configuration;
        weatherNames = Services.DataManager.GetExcelSheet<Weather>()
            .Where(w => w.RowId is > 0 and < 256)
            .ToDictionary(w => (byte)w.RowId, w => w.Name.ExtractText());
        var all = LoadVistas(weatherNames);
        byTerritory = all.ToLookup(v => v.TerritoryId);
        arrStarters = all.Where(v => v.IsARealmReborn && v.Number <= ArrStarterVistas).ToList();

        Services.Framework.Update += OnUpdate;
        Services.PluginInterface.UiBuilder.Draw += DrawMarkers;
    }

    public void Dispose() {
        Services.PluginInterface.UiBuilder.Draw -= DrawMarkers;
        Services.Framework.Update -= OnUpdate;
    }

    /// <summary>Every vista, numbered within its expansion the way the sightseeing log lists them.</summary>
    private static List<VistaInfo> LoadVistas(Dictionary<byte, string> weatherNames) {
        var result = new List<VistaInfo>();
        var sheet = Services.DataManager.GetExcelSheet<Adventure>();
        var first = sheet.FirstOrDefault().RowId;
        var numbers = new Dictionary<uint, int>();

        foreach (var adventure in sheet) {
            if (adventure.Level.ValueNullable is not { } level || level.Territory.RowId == 0) continue;
            var territory = level.Territory.Value;
            var expansion = territory.ExVersion.RowId;
            numbers[expansion] = numbers.GetValueOrDefault(expansion) + 1;

            (int, int)? window = null;
            if (adventure.MinTime != adventure.MaxTime) {
                // HHMM, with an inclusive end minute (08:00-11:59).
                var start = adventure.MinTime / 100 * 60 + adventure.MinTime % 100;
                var end = adventure.MaxTime / 100 * 60 + adventure.MaxTime % 100 + 1;
                window = (start, (end - start + 1440) % 1440);
            }

            var emote = adventure.Emote.ValueNullable;
            var weather = adventure.Unknown0; // the required weather; 0 = any
            result.Add(new VistaInfo(
                adventure.RowId - first,
                adventure.RowId,
                adventure.Name.ExtractText(),
                numbers[expansion],
                territory.ExVersion.ValueNullable?.Name.ExtractText() ?? "",
                expansion == 0,
                level.Territory.RowId,
                new Vector3(level.X, level.Y, level.Z),
                emote?.TextCommand.ValueNullable?.Command.ExtractText() ?? "",
                emote?.Name.ExtractText() ?? "",
                window,
                weather,
                weather != 0 ? weatherNames.GetValueOrDefault(weather) : null));
        }
        return result;
    }

    // ---- Chat line on arrival ----

    private void OnUpdate(IFramework framework) {
        var now = DateTime.UtcNow;
        if (now < nextCheck) return;
        nextCheck = now + CheckInterval;

        var territory = Services.ClientState.TerritoryType;
        if (territory != lastTerritory) {
            announced.Clear();
            lastTerritory = territory;
        }
        if (!configuration.SecondSightVistaChat || Services.ObjectTable.LocalPlayer is not { } player) return;

        foreach (var vista in byTerritory[territory]) {
            var distance = Vector3.Distance(player.Position, vista.Position);
            if (distance > LeaveDistance) announced.Remove(vista.Index);
            if (distance > ArriveDistance || IsLogged(vista) || !IsUnlocked(vista) || !announced.Add(vista.Index)) continue;
            Announce(vista);
        }
    }

    private void Announce(VistaInfo vista) {
        var command = vista.Command.Length > 0 ? vista.Command : vista.EmoteName;
        var builder = new SeStringBuilder()
            .AddUiForeground("[Second Sight] ", 37)
            .AddText("Sightseeing vista here: ")
            .AddUiForeground($"#{vista.Number:D3} {vista.Name}", 500)
            .AddText(". ");

        var missing = MissingConditions(vista);
        if (missing.Count == 0) {
            builder.AddText("Use ").AddUiForeground(command, 45).AddText(" to log it.");
        } else {
            builder.AddText($"It needs {string.Join(" and ", missing)}. Come back then and use ")
                .AddUiForeground(command, 45).AddText(".");
        }
        Services.ChatGui.Print(builder.Build());
    }

    /// <summary>The time or weather the vista still needs, e.g. "ET 08:00-12:00 (now 14:30)"; empty if it can be logged now.</summary>
    private List<string> MissingConditions(VistaInfo vista) {
        var missing = new List<string>();
        if (vista.Window is { } window) {
            var minute = EorzeaMinute();
            if ((minute - window.Start + 1440) % 1440 >= window.Length)
                missing.Add($"ET {Clock(window.Start)}-{Clock(window.Start + window.Length)} (it's {Clock(minute)} now)");
        }
        if (vista.WeatherId != 0) {
            var current = CurrentWeather();
            if (current != vista.WeatherId) {
                var now = weatherNames.GetValueOrDefault(current);
                missing.Add(now is { Length: > 0 } ? $"{vista.WeatherName} weather (it's {now} now)" : $"{vista.WeatherName} weather");
            }
        }
        return missing;
    }

    // ---- Rings at A Realm Reborn vistas ----

    private void DrawMarkers() {
        if (!configuration.SecondSightVistaMarkers || Services.GameGui.GameUiHidden || Services.ObjectTable.LocalPlayer is not { } player)
            return;

        var drawList = ImGui.GetBackgroundDrawList();
        foreach (var vista in byTerritory[Services.ClientState.TerritoryType]) {
            if (!vista.IsARealmReborn) continue;
            var distance = Vector3.Distance(player.Position, vista.Position);
            if (distance > MarkerDistance || IsLogged(vista) || !IsUnlocked(vista)) continue;

            // Full strength up close, fading out toward the edge of the range.
            var fade = Math.Clamp((MarkerDistance - distance) / (MarkerDistance * 0.5f), 0, 1);
            var ready = MissingConditions(vista).Count == 0;
            var color = ready ? new Vector4(0.45f, 0.85f, 1f, 1f) : new Vector4(0.65f, 0.7f, 0.8f, 1f);
            DrawRing(drawList, vista.Position, color, fade * (ready ? 1f : 0.55f));
            DrawLabel(drawList, vista, color, fade, ready, distance, vista.Position.Y - player.Position.Y);
        }
    }

    private static void DrawRing(ImDrawListPtr drawList, Vector3 center, Vector4 color, float alpha) {
        const int segments = 40;
        Span<Vector2> points = stackalloc Vector2[segments];
        var visible = 0;
        for (var i = 0; i < segments; i++) {
            var angle = i * MathF.Tau / segments;
            var world = center + new Vector3(MathF.Cos(angle) * RingRadius, 0.05f, MathF.Sin(angle) * RingRadius);
            if (Services.GameGui.WorldToScreen(world, out var screen)) visible++;
            points[i] = screen;
        }
        if (visible < segments / 2) return; // mostly behind the camera

        // A soft outer glow, then a bright core line.
        for (var i = 0; i < segments; i++) {
            var a = points[i];
            var b = points[(i + 1) % segments];
            drawList.AddLine(a, b, ImGui.GetColorU32(color with { W = 0.18f * alpha }), 9f);
            drawList.AddLine(a, b, ImGui.GetColorU32(color with { W = 0.9f * alpha }), 2.5f);
        }
    }

    private static void DrawLabel(ImDrawListPtr drawList, VistaInfo vista, Vector4 color, float alpha, bool ready, float distance, float height) {
        if (!Services.GameGui.WorldToScreen(vista.Position + new Vector3(0, 2.2f, 0), out var screen)) return;

        var text = $"Vista #{vista.Number:D3}  {(vista.Command.Length > 0 ? vista.Command : vista.EmoteName)}";
        // The ring shows through walls, so say how far and whether to go up or down to reach it.
        text += $"  ·  {distance:0} yalms";
        if (MathF.Abs(height) >= 3) text += height > 0 ? $"  ·  {height:0} yalms up" : $"  ·  {-height:0} yalms down";
        if (!ready) text += "  (not now)";
        var size = ImGui.CalcTextSize(text);
        var topLeft = screen - new Vector2(size.X / 2 + 8, size.Y / 2 + 4);
        var bottomRight = screen + new Vector2(size.X / 2 + 8, size.Y / 2 + 4);
        drawList.AddRectFilled(topLeft, bottomRight, ImGui.GetColorU32(new Vector4(0.06f, 0.09f, 0.2f, 0.75f * alpha)), 6f);
        drawList.AddRect(topLeft, bottomRight, ImGui.GetColorU32(color with { W = 0.8f * alpha }), 6f);
        drawList.AddText(topLeft + new Vector2(8, 4), ImGui.GetColorU32(new Vector4(1, 1, 1, alpha)), text);
    }

    // ---- Game state (read-only) ----

    private static bool IsLogged(VistaInfo vista) => VistaLog.IsLogged(vista.RowId);

    /// <summary>False for A Realm Reborn #021-#080 until #001-#020 are all logged.</summary>
    private bool IsUnlocked(VistaInfo vista)
        => !vista.IsARealmReborn || vista.Number <= ArrStarterVistas || arrStarters.All(IsLogged);

    private static byte CurrentWeather() {
        var weather = WeatherManager.Instance();
        return weather == null ? (byte)0 : weather->GetCurrentWeather();
    }

    /// <summary>Minute of the Eorzean day (0-1439), from the server's clock.</summary>
    private static int EorzeaMinute() {
        var seconds = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.GetServerTime();
        return (int)(seconds * 60 / 175 % 1440);
    }

    private static string Clock(int minute) => $"{minute / 60 % 24:D2}:{minute % 60:D2}";
}
