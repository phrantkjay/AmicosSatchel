using System;
using System.Diagnostics;
using System.IO;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Lumina.Excel.Sheets;
using XIVMap.Shared;

namespace AmicosSatchel.Modules.SecondSight;

/// <summary>
/// Feeds the Amico's Second Sight desktop map: where you are, what is around you, FATEs and
/// Critical Engagements, over a local named pipe. Read-only: it never sends input to the game.
/// </summary>
public sealed class SecondSightModule : Module {
    private const string Command = "/secondsight";
    private const string OldCommand = "/xivmap"; // kept working from before the rename
    private const string AppFileName = AppRelease.FileName;

    private const float PathBoxWidth = 360; // fixed, so it doesn't stretch with the window

    private Configuration? configuration;
    private PipeServer? pipe;
    private StateSampler? sampler;
    private AppInstaller? installer;
    private VistaGuide? vistaGuide;
    private Teleporter? teleporter;
    private TreasureNotifier? treasureNotifier;
    private string pathInput = "";

    // Saved on/off state is keyed by this, so it keeps the id from when the module was "XIV Map".
    public override string Id => "XivMap";
    public override string Name => "Amico's Second Sight";

    public override string Description =>
        "A live map on your second screen. Sends your position, nearby objects, FATEs and Critical Engagements " +
        "to the Amico's Second Sight desktop app.";

    public override string Category => "Map";
    public override string ImageFile => "second-sight.png";
    public override bool HasSettings => true;

    public override void Enable(ModuleContext context) {
        configuration = context.Configuration;
        CarryOverOldAppPath(configuration);
        pathInput = configuration.SecondSightAppPath ?? "";

        installer = new AppInstaller(Services.PluginInterface.ConfigDirectory.FullName);
        Services.Framework.Update += OnUpdate;
        pipe = new PipeServer(Services.Log, Services.PluginInterface.Manifest.AssemblyVersion.ToString());
        sampler = new StateSampler(pipe);
        vistaGuide = new VistaGuide(configuration);
        teleporter = new Teleporter(configuration, pipe);
        treasureNotifier = new TreasureNotifier(configuration);

        Services.Commands.AddHandler(Command, new CommandInfo(OnCommand) {
            HelpMessage = "Print your zone, position and whether the Second Sight map is connected. " +
                          $"\"{Command} vistas\" shows your sightseeing log counts. \"{Command} markers\" writes the game map's markers to the Dalamud log.",
        });
        Services.Commands.AddHandler(OldCommand, new CommandInfo(OnCommand) { ShowInHelp = false });
    }

    public override void Disable() {
        Services.Framework.Update -= OnUpdate;
        Services.Commands.RemoveHandler(OldCommand);
        Services.Commands.RemoveHandler(Command);
        treasureNotifier?.Dispose();
        teleporter?.Dispose();
        vistaGuide?.Dispose();
        sampler?.Dispose();
        pipe?.Dispose();
        installer?.Dispose();
        treasureNotifier = null;
        teleporter = null;
        vistaGuide = null;
        sampler = null;
        pipe = null;
        installer = null;
        configuration = null;
    }

    private DateTime nextInstallerTick;

    /// <summary>Every few seconds: keep the downloaded map up to date (see AppInstaller.Tick).</summary>
    private void OnUpdate(Dalamud.Plugin.Services.IFramework framework) {
        if (installer is null || configuration is null || DateTime.UtcNow < nextInstallerTick) return;
        nextInstallerTick = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        // Only the downloaded copy is managed; your own copy is left alone.
        if (configuration.SecondSightAppPath is null) installer.Tick(configuration.SecondSightAutoUpdate);
    }

    public override void DrawSettings() {
        var connected = pipe?.IsConnected == true;
        var scale = ImGuiHelpers.GlobalScale;

        if (connected) ImGui.Text("Connected to the Second Sight map.");
        else ImGui.TextDisabled("The Second Sight map isn't open.");

        // Your own copy, when set, wins over the downloaded one.
        var ownCopy = configuration?.SecondSightAppPath is { } own && File.Exists(own) ? own : null;
        if (ownCopy is not null) DrawOpenButton(ownCopy, connected);
        else if (installer is not null) {
            DrawInstaller(installer, connected, scale);
            if (configuration is not null) {
                var auto = configuration.SecondSightAutoUpdate;
                if (ImGui.Checkbox("Update the map automatically", ref auto)) {
                    configuration.SecondSightAutoUpdate = auto;
                    configuration.Save();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("New versions download in the background and go in the next time the map is closed.");
            }
        }

        ImGui.Spacing();
        ImGui.TextDisabled($"Use your own copy instead (optional, path to {AppFileName}):");
        ImGui.SetNextItemWidth(PathBoxWidth * scale);
        ImGui.InputTextWithHint("##SecondSightPath", "Leave empty to use the downloaded map", ref pathInput, 512);
        if (ImGui.IsItemDeactivatedAfterEdit() && configuration is not null) {
            var trimmed = pathInput.Trim().Trim('"');
            configuration.SecondSightAppPath = trimmed.Length == 0 ? null : trimmed;
            configuration.Save();
        }

        ImGui.Spacing();
        ImGui.TextDisabled("In-game help:");
        if (configuration is not null) {
            var chat = configuration.SecondSightVistaChat;
            if (ImGui.Checkbox("Tell me in chat when I reach a vista, with the emote to use", ref chat)) {
                configuration.SecondSightVistaChat = chat;
                configuration.Save();
            }
            var markers = configuration.SecondSightVistaMarkers;
            if (ImGui.Checkbox("Show a glowing ring at A Realm Reborn vistas I haven't logged", ref markers)) {
                configuration.SecondSightVistaMarkers = markers;
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Later expansions mark their vistas in the world already; A Realm Reborn's don't. Drawn on screen only.");
            var treasure = configuration.SecondSightTreasureChat;
            if (ImGui.Checkbox("Put a deciphered treasure map's location in chat (click it, then share with <flag>)", ref treasure)) {
                configuration.SecondSightTreasureChat = treasure;
                configuration.Save();
            }
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Travel:");
        if (configuration is not null) {
            var teleport = configuration.SecondSightAllowTeleport;
            if (ImGui.Checkbox("Allow teleporting by clicking an aetheryte on the map", ref teleport)) {
                configuration.SecondSightAllowTeleport = teleport;
                configuration.Save();
                teleporter?.RefreshSoon();
            }
            ImGui.Indent();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + PathBoxWidth * scale);
            ImGui.TextDisabled("The map asks you to confirm first. The plugin then checks you're not in combat, a duty or a cutscene, " +
                               "and uses Lifestream if it's installed (needed for aethernet shards) or the game's own teleport. " +
                               "With this off, the module only reads the game.");
            ImGui.PopTextWrapPos();
            ImGui.Unindent();

            var flag = configuration.SecondSightAllowFlag;
            if (ImGui.Checkbox("Allow setting the game's map flag from the map (right-click → Set map flag here)", ref flag)) {
                configuration.SecondSightAllowFlag = flag;
                configuration.Save();
                teleporter?.RefreshSoon();
            }
            if (configuration.SecondSightAllowFlag) {
                ImGui.Indent();
                var toChat = configuration.SecondSightFlagToChat;
                if (ImGui.Checkbox("Then put <flag> in my chat box, ready to send", ref toChat)) {
                    configuration.SecondSightFlagToChat = toChat;
                    configuration.Save();
                }
                ImGui.Unindent();
            }
        }

        ImGui.TextDisabled($"Type {Command} in chat to see your position and whether the map is connected.");
    }

    private static void DrawInstaller(AppInstaller installer, bool connected, float scale) {
        var sizeText = installer.TotalBytes > 0 ? $" ({installer.TotalBytes / 1_000_000.0:0} MB)" : "";

        switch (installer.State) {
            case AppInstaller.InstallState.NotPublished:
                using (ImRaii.Disabled()) ImGui.Button("Download Second Sight");
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip("The map app hasn't been published for download yet.");
                break;

            case AppInstaller.InstallState.NotInstalled:
                if (ImGui.Button($"Download Second Sight{sizeText}")) installer.StartDownload();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Downloads the map app from GitHub and checks it before use. Nothing else needs installing.");
                break;

            case AppInstaller.InstallState.Downloading:
                ImGui.ProgressBar(installer.Progress, new System.Numerics.Vector2(PathBoxWidth * 0.6f * scale, 0),
                    $"Downloading… {installer.Progress * 100:0}%");
                ImGui.SameLine();
                if (ImGui.Button("Cancel")) installer.CancelDownload();
                break;

            case AppInstaller.InstallState.UpdateRequired:
                // The installed map can't talk to this satchel any more.
                if (installer.UpdateStaged) {
                    ImGui.TextWrapped("A required map update is downloaded. Close the map and it goes in straight away.");
                } else {
                    if (ImGui.Button($"Update Second Sight{sizeText}")) installer.StartDownload();
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("This version of the satchel needs a newer map. Your settings and pins are kept.");
                }
                break;

            case AppInstaller.InstallState.UpdateAvailable:
                // Still works with this satchel: keep using it, update whenever.
                DrawOpenButton(installer, connected);
                ImGui.SameLine();
                if (installer.UpdateStaged) {
                    ImGui.TextDisabled("Update downloaded: it goes in next time the map is closed.");
                } else if (ImGui.SmallButton($"Update available{sizeText}")) {
                    installer.StartDownload();
                }
                break;

            case AppInstaller.InstallState.Installed:
                DrawOpenButton(installer, connected);
                break;
        }

        if (installer.Error is { } error) ImGui.TextWrapped(error);
    }

    /// <summary>Open for the downloaded map: puts in a waiting update first, since the map is closed.</summary>
    private static void DrawOpenButton(AppInstaller installer, bool connected) {
        using (ImRaii.Disabled(connected)) {
            if (ImGui.Button("Open Second Sight")) {
                installer.TryApplyStaged();
                OpenApp(installer.ExePath);
            }
        }
        if (connected && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("The map is already open.");
    }

    private static void DrawOpenButton(string path, bool connected) {
        using (ImRaii.Disabled(connected)) {
            if (ImGui.Button("Open Second Sight")) OpenApp(path);
        }
        if (connected && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("The map is already open.");
    }

    /// <summary>The app used to be XIVMap.exe; point at the renamed exe in the same folder.</summary>
    private static void CarryOverOldAppPath(Configuration configuration) {
        if (configuration.XivMapAppPath is not { } old) return;

        if (configuration.SecondSightAppPath is null && Path.GetDirectoryName(old) is { } folder) {
            var renamed = Path.Combine(folder, AppFileName);
            if (File.Exists(renamed)) configuration.SecondSightAppPath = renamed;
        }
        configuration.XivMapAppPath = null;
        configuration.Save();
    }

    private static void OpenApp(string path) {
        try {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) });
        } catch (Exception e) {
            Services.Log.Warning(e, "Couldn't start the Second Sight map.");
            Services.ChatGui.PrintError($"[Second Sight] Couldn't start the map: {e.Message}");
        }
    }

    private void OnCommand(string command, string args) {
        if (args.Trim().Equals("vistas", StringComparison.OrdinalIgnoreCase)) {
            Services.ChatGui.Print("[Second Sight] Sightseeing log, as the plugin sees it (compare with the game's Sightseeing Log):");
            foreach (var line in VistaLog.Summary())
                Services.ChatGui.Print($"[Second Sight]   {line}");
            return;
        }

        if (args.Trim().Equals("markers", StringComparison.OrdinalIgnoreCase)) {
            var events = ObjectiveReader.LogMapMarkers();
            Services.ChatGui.Print($"[Second Sight] {events} event markers; details written to the Dalamud log.");
            return;
        }

        Services.ChatGui.Print($"[Second Sight] Map {(pipe?.IsConnected == true ? "connected" : "not connected")}");

        var player = Services.ObjectTable.LocalPlayer;
        if (player is null) {
            Services.ChatGui.PrintError("[Second Sight] Not logged in.");
            return;
        }

        var territoryId = Services.ClientState.TerritoryType;
        var mapId = Services.ClientState.MapId;
        var territory = Services.DataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId);
        var map = Services.DataManager.GetExcelSheet<Map>().GetRowOrDefault(mapId);
        var zoneName = territory?.PlaceName.ValueNullable?.Name.ExtractText() ?? "?";

        var pos = player.Position;
        var facingDegrees = player.Rotation * 180f / MathF.PI;

        var location = LocationReader.Read();
        Services.ChatGui.Print($"[Second Sight] {zoneName} (territory {territoryId}, map {mapId} \"{map?.Id.ExtractText()}\", instance {location.Instance})");
        if (location.Housing is { } h) {
            Services.ChatGui.Print($"[Second Sight] Housing: ward {h.Ward}, plot {h.Plot?.ToString() ?? "-"}, room {h.Room?.ToString() ?? "-"}, " +
                                   $"subdivision {h.Subdivision}, indoors {h.Indoors}");
        }
        Services.ChatGui.Print($"[Second Sight] World X {pos.X:F2}  Y {pos.Y:F2}  Z {pos.Z:F2}  facing {player.Rotation:F2} rad ({facingDegrees:F0} deg)");

        if (map is { } m) {
            var mapX = MapCoords.WorldToMap(pos.X, m.SizeFactor, m.OffsetX);
            var mapY = MapCoords.WorldToMap(pos.Z, m.SizeFactor, m.OffsetY);
            Services.ChatGui.Print($"[Second Sight] Map ( {mapX:F1} , {mapY:F1} )  scale {m.SizeFactor}, offset {m.OffsetX}/{m.OffsetY}");
        }
    }
}
