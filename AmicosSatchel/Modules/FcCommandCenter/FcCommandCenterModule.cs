using System;
using System.Linq;
using AmicosSatchel.Modules.FcCommandCenter.Data;
using AmicosSatchel.Modules.FcCommandCenter.Game;
using AmicosSatchel.Modules.FcCommandCenter.Windows;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace AmicosSatchel.Modules.FcCommandCenter;

/// <summary>Saved settings for the FC Command Center.</summary>
[Serializable]
public sealed class FcCommandCenterSettings {
    /// <summary>Shows the tools for adding and changing things. Members who only import boards can turn it off.</summary>
    public bool OfficerTools { get; set; } = true;

    /// <summary>Minutes before an event to post a reminder in chat; 0 for none.</summary>
    public int ReminderMinutes { get; set; } = 15;

    /// <summary>On login, mention new announcements and tasks assigned to you.</summary>
    public bool LoginSummary { get; set; } = true;

    /// <summary>Say in chat when a submersible or airship is back.</summary>
    public bool VoyageAlerts { get; set; } = true;

    /// <summary>Minutes before a company action ends to warn in chat; 0 for none.</summary>
    public int ActionAlertMinutes { get; set; } = 60;

    /// <summary>Chat sound (1 to 16, like &lt;se.1&gt;) played with reminders and alerts; 0 for none.</summary>
    public int AlertSound { get; set; }

    /// <summary>Last FC seen, so its board opens before the game has loaded your FC.</summary>
    public ulong LastFcId { get; set; }
}

/// <summary>
/// A dashboard for Free Company officers and members: events, company action rotation, chest notes,
/// workshop projects and voyages, announcements and officer tasks. Kept on this PC and shared by
/// copying share codes; it only reads what the game shows.
/// </summary>
public sealed class FcCommandCenterModule : Module {
    public const string Command = "/fcc";

    private static readonly string[] SoundNames = ["None", .. Enumerable.Range(1, 16).Select(i => $"Sound {i}")];

    private Configuration? configuration;
    private WindowSystem? windows;
    private BoardStore? store;
    private Watcher? watcher;
    private CommandCenterWindow? window;

    public override string Id => "FcCommandCenter";
    public override string Name => "FC Command Center";

    public override string Description =>
        "A dashboard for your Free Company: event calendar, company action rotation, chest notes, " +
        "workshop projects and voyages, announcements and officer tasks. Share it with members using share codes.";

    public override string Category => "Free Company";
    public override string ImageFile => "fc-command-center.png";
    public override bool HasSettings => true;

    public override void Enable(ModuleContext context) {
        configuration = context.Configuration;
        windows = context.Windows;
        var settings = configuration.FcCommandCenter;

        store = new BoardStore(Services.PluginInterface.ConfigDirectory.FullName, settings.LastFcId);
        store.Switched += OnSwitched;

        var catalog = new Catalog();
        watcher = new Watcher(store, catalog, settings);
        window = new CommandCenterWindow(store, watcher, catalog, configuration);
        windows.AddWindow(window);

        Services.Commands.AddHandler(Command, new CommandInfo(OnCommand) {
            HelpMessage = "Open the FC Command Center. Add a tab name to jump to it, such as \"/fcc tasks\".",
        });
    }

    public override void Disable() {
        Services.Commands.RemoveHandler(Command);
        if (window is not null) windows?.RemoveWindow(window);
        watcher?.Dispose();
        if (store is not null) {
            store.Switched -= OnSwitched;
            store.Dispose();
        }

        window = null;
        watcher = null;
        store = null;
        windows = null;
        configuration = null;
    }

    public override void DrawSettings() {
        if (configuration is null) return;
        var settings = configuration.FcCommandCenter;
        var scale = ImGuiHelpers.GlobalScale;

        if (ImGui.Button("Open FC Command Center")) window?.Open(null);
        ImGui.SameLine();
        ImGui.TextDisabled($"or type {Command}");

        var officer = settings.OfficerTools;
        if (ImGui.Checkbox("Officer tools", ref officer)) {
            settings.OfficerTools = officer;
            configuration.Save();
        }
        ImGui.TextDisabled("Turn off to only read the board, such as for members who import share codes.");

        var summary = settings.LoginSummary;
        if (ImGui.Checkbox("Tell me about new announcements and my tasks when I log in", ref summary)) {
            settings.LoginSummary = summary;
            configuration.Save();
        }

        var minutes = settings.ReminderMinutes;
        ImGui.SetNextItemWidth(120 * scale);
        if (ImGui.InputInt("Minutes before an event to remind me in chat (0 = off)", ref minutes, 5)) {
            settings.ReminderMinutes = Math.Clamp(minutes, 0, 24 * 60);
            configuration.Save();
        }

        var voyages = settings.VoyageAlerts;
        if (ImGui.Checkbox("Tell me when a submersible or airship is back", ref voyages)) {
            settings.VoyageAlerts = voyages;
            configuration.Save();
        }

        var actionMinutes = settings.ActionAlertMinutes;
        ImGui.SetNextItemWidth(120 * scale);
        if (ImGui.InputInt("Minutes before a company action ends to warn me (0 = off)", ref actionMinutes, 15)) {
            settings.ActionAlertMinutes = Math.Clamp(actionMinutes, 0, 24 * 60);
            configuration.Save();
        }

        var sound = settings.AlertSound;
        ImGui.SetNextItemWidth(120 * scale);
        if (ImGui.Combo("Alert sound", ref sound, SoundNames, SoundNames.Length)) {
            settings.AlertSound = sound;
            configuration.Save();
            watcher?.PlayAlertSound();
        }
    }

    private void OnSwitched(FcBoard board) {
        if (configuration is null) return;
        configuration.FcCommandCenter.LastFcId = board.FcId;
        configuration.Save();
    }

    private void OnCommand(string command, string args) {
        if (window is null) return;
        var tab = args.Trim();
        if (tab.Length == 0 && window.IsOpen) window.IsOpen = false;
        else window.Open(tab.Length == 0 ? null : tab);
    }
}
