using System;
using System.Linq;
using System.Numerics;
using AmicosSatchel.Modules.FcCommandCenter.Data;
using AmicosSatchel.Modules.FcCommandCenter.Game;
using AmicosSatchel.Windows;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace AmicosSatchel.Modules.FcCommandCenter.Windows;

/// <summary>
/// The FC Command Center dashboard: one tab per part, in the satchel's theme.
/// Each tab lives in its own file (CommandCenterWindow.*.cs).
/// </summary>
public sealed partial class CommandCenterWindow : Window {
    private enum Tab {
        Overview,
        Calendar,
        Announcements,
        Tasks,
        Actions,
        Chest,
        Workshop,
        Members,
        Share,
    }

    private static readonly (Tab Tab, string Label, string[] Aliases)[] Tabs = [
        (Tab.Overview, "Overview", ["overview", "home"]),
        (Tab.Calendar, "Calendar", ["calendar", "events", "event"]),
        (Tab.Announcements, "Announcements", ["announcements", "news", "board"]),
        (Tab.Tasks, "Tasks", ["tasks", "task", "todo"]),
        (Tab.Actions, "Company Actions", ["actions", "action", "rotation", "buffs"]),
        (Tab.Chest, "Chest", ["chest", "inventory"]),
        (Tab.Workshop, "Workshop", ["workshop", "subs", "voyages", "projects"]),
        (Tab.Members, "Members", ["members", "roster"]),
        (Tab.Share, "Share", ["share", "import", "export"]),
    ];

    private readonly BoardStore store;
    private readonly Watcher watcher;
    private readonly Catalog catalog;
    private readonly Configuration configuration;

    private SatchelTheme theme = SatchelTheme.CrystalBoard;
    private IDisposable? pushedStyle;
    private Tab? jumpTo;
    private Tab currentTab = Tab.Overview;

    public CommandCenterWindow(BoardStore store, Watcher watcher, Catalog catalog, Configuration configuration)
        : base("FC Command Center##FcCommandCenter") {
        this.store = store;
        this.watcher = watcher;
        this.catalog = catalog;
        this.configuration = configuration;

        Size = new Vector2(780, 580);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2(560, 380),
            MaximumSize = new Vector2(1800, 1400),
        };
    }

    private FcBoard Board => store.Board;
    private bool Officer => configuration.FcCommandCenter.OfficerTools;
    private static float Scale => ImGuiHelpers.GlobalScale;

    /// <summary>Opens the window, on the tab whose name or alias is <paramref name="tab"/> if given.</summary>
    public void Open(string? tab) {
        IsOpen = true;
        if (tab is null) return;

        var match = Tabs.FirstOrDefault(t => t.Aliases.Any(a => a.StartsWith(tab, StringComparison.OrdinalIgnoreCase)));
        if (match.Label is null) Services.ChatGui.PrintError($"[FC Command Center] No tab called \"{tab}\".");
        else jumpTo = match.Tab;
    }

    public override void PreDraw() {
        theme = SatchelTheme.For(configuration.Theme);
        pushedStyle = SatchelStyle.Push(theme);
    }

    public override void PostDraw() {
        pushedStyle?.Dispose();
        pushedStyle = null;
    }

    public override void OnClose() => LeaveTab(currentTab);

    public override void Draw() {
        DrawHeader();

        using var tabBar = ImRaii.TabBar("##FccTabs", ImGuiTabBarFlags.FittingPolicyScroll);
        if (!tabBar) return;

        foreach (var (tab, label, _) in Tabs) {
            var flags = jumpTo == tab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            var title = tab == Tab.Announcements && UnreadAnnouncements() is > 0 and var unread ? $"{label} ({unread})###{label}" : $"{label}###{label}";

            using var item = ImRaii.TabItem(title, flags);
            if (!item) continue;

            if (currentTab != tab) {
                LeaveTab(currentTab);
                currentTab = tab;
            }

            using var child = ImRaii.Child($"##{label}Body", new Vector2(-1, -1), false);
            if (!child) continue;

            switch (tab) {
                case Tab.Overview: DrawOverview(); break;
                case Tab.Calendar: DrawCalendar(); break;
                case Tab.Announcements: DrawAnnouncements(); break;
                case Tab.Tasks: DrawTasks(); break;
                case Tab.Actions: DrawActions(); break;
                case Tab.Chest: DrawChest(); break;
                case Tab.Workshop: DrawWorkshop(); break;
                case Tab.Members: DrawMembers(); break;
                case Tab.Share: DrawShare(); break;
            }
        }

        jumpTo = null;
        fileDialog.Draw();
    }

    private void DrawHeader() {
        var name = Board.FcName.Length > 0 ? Board.FcName : "Your Free Company";
        ImGui.TextColored(theme.Heading, name);

        ImGui.SameLine();
        if (watcher.Fc is { } fc) ImGui.TextDisabled($"{fc.OnlineMembers} of {fc.TotalMembers} online");
        else ImGui.TextDisabled(Services.ClientState.IsLoggedIn ? "Not in a Free Company, or it hasn't loaded yet" : "Not logged in");

        var chip = Officer ? "Officer tools on" : "Read only";
        var chipWidth = ImGui.CalcTextSize(chip).X + 18 * Scale;
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - chipWidth);
        SatchelStyle.Chip(chip, theme.ChipBg, theme.ChipText);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Change this under FC Command Center → Settings in /satchel.");
    }

    /// <summary>Runs when the player switches away from a tab.</summary>
    private void LeaveTab(Tab tab) {
        if (tab != Tab.Announcements) return;
        Board.AnnouncementsReadUtc = DateTime.UtcNow;
        store.MarkChanged();
    }

    /// <summary>Records that the player changed <paramref name="item"/>.</summary>
    private void Changed(BoardItem item) {
        item.Touch(Watcher.PlayerName);
        store.MarkChanged();
    }

    private int UnreadAnnouncements() =>
        Board.Announcements.Count(a => !a.Deleted && !Watcher.Expired(a) && a.PostedUtc > Board.AnnouncementsReadUtc);
}
