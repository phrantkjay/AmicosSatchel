using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AmicosSatchel.Modules.FcCommandCenter.Windows;

// Small controls shared by the tabs.
public sealed partial class CommandCenterWindow {
    private static readonly string[] Hours = Enumerable.Range(0, 24).Select(h => h.ToString("00")).ToArray();
    private static readonly string[] Minutes = Enumerable.Range(0, 12).Select(m => (m * 5).ToString("00")).ToArray();

    private DateTime pickerMonth = DateTime.Today;
    private string memberFilter = "";

    private void Heading(string text) {
        ImGui.Spacing();
        ImGui.TextColored(theme.Heading, text);
        var start = ImGui.GetCursorScreenPos() - new Vector2(0, ImGui.GetStyle().ItemSpacing.Y * 0.6f);
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.GetWindowDrawList().AddLine(start, start + new Vector2(width, 0), ImGui.GetColorU32(theme.CardBorder));
    }

    private void Hint(string text) {
        using var wrap = ImRaii.TextWrapPos(0);
        ImGui.TextDisabled(text);
    }

    private void ErrorText(string text) => ImGui.TextColored(theme.Error, text);

    private static void GoodText(string text) => ImGui.TextColored(ImGuiColors.HealerGreen, text);

    /// <summary>Draws a game icon at the current position, sized to a line of text unless given.</summary>
    private static void GameIcon(uint icon, float? size = null) {
        var side = size ?? ImGui.GetTextLineHeight();
        if (icon == 0) {
            ImGui.Dummy(new Vector2(side));
            return;
        }

        var wrap = Services.Textures.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrEmpty();
        ImGui.Image(wrap.Handle, new Vector2(side));
    }

    /// <summary>A delete button that only works while Ctrl is held, so nothing goes by accident.</summary>
    private static bool DeleteButton(string id = "Delete") {
        var ctrl = ImGui.GetIO().KeyCtrl;
        bool clicked;
        using (ImRaii.Disabled(!ctrl)) clicked = ImGui.SmallButton($"{id}");
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Hold Ctrl and click to delete.");
        return clicked && ctrl;
    }

    /// <summary>Text field for a character name, with a list of FC members to pick from.</summary>
    private bool MemberField(string id, string hint, ref string name, float width) {
        var changed = false;
        var buttonWidth = ImGui.GetFrameHeight();

        ImGui.SetNextItemWidth(Math.Max(60, width - buttonWidth - ImGui.GetStyle().ItemInnerSpacing.X));
        changed |= ImGui.InputTextWithHint($"##{id}", hint, ref name, 64);

        ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
        if (ImGui.ArrowButton($"##{id}Pick", ImGuiDir.Down)) {
            memberFilter = "";
            ImGui.OpenPopup($"##{id}Members");
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Pick an FC member");

        using var popup = ImRaii.Popup($"##{id}Members");
        if (!popup) return changed;

        var members = Board.Roster?.Members ?? [];
        if (members.Count == 0) {
            ImGui.TextDisabled("No members known yet.");
            ImGui.TextDisabled("Open the Free Company window's Members tab in game.");
            return changed;
        }

        ImGui.SetNextItemWidth(220 * Scale);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        ImGui.InputTextWithHint("##Filter", "Search members", ref memberFilter, 64);

        using var list = ImRaii.Child("##List", new Vector2(220 * Scale, Math.Min(members.Count, 10) * ImGui.GetTextLineHeightWithSpacing() + 8 * Scale));
        foreach (var member in members.Where(m => memberFilter.Length == 0 || m.Name.Contains(memberFilter, StringComparison.OrdinalIgnoreCase))) {
            var label = member.Job.Length > 0 ? $"{member.Name}  ({member.Job})" : member.Name;
            if (!ImGui.Selectable(label, member.Name == name)) continue;
            name = member.Name;
            changed = true;
            ImGui.CloseCurrentPopup();
        }

        return changed;
    }

    /// <summary>Date and time picker, shown and edited in the player's time zone, stored in UTC.</summary>
    private bool DateTimeField(string id, ref DateTime utc) {
        var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        var changed = false;

        if (ImGui.Button($"{local:ddd d MMM yyyy}##{id}Date")) {
            pickerMonth = new DateTime(local.Year, local.Month, 1);
            ImGui.OpenPopup($"##{id}Calendar");
        }

        using (var popup = ImRaii.Popup($"##{id}Calendar")) {
            if (popup.Success && MonthGrid($"{id}Grid", ref pickerMonth, local.Date, _ => 0, out var picked, compact: true)) {
                local = picked + local.TimeOfDay;
                changed = true;
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();
        var hour = local.Hour;
        ImGui.SetNextItemWidth(58 * Scale);
        if (ImGui.Combo($"##{id}Hour", ref hour, Hours, Hours.Length)) {
            local = local.Date + new TimeSpan(hour, local.Minute, 0);
            changed = true;
        }

        ImGui.SameLine(0, 2 * Scale);
        ImGui.TextUnformatted(":");
        ImGui.SameLine(0, 2 * Scale);
        var minute = local.Minute / 5;
        ImGui.SetNextItemWidth(58 * Scale);
        if (ImGui.Combo($"##{id}Minute", ref minute, Minutes, Minutes.Length)) {
            local = local.Date + new TimeSpan(local.Hour, minute * 5, 0);
            changed = true;
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"your time · {DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime():HH:mm} server (UTC)");

        if (changed) utc = DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
        return changed;
    }

    /// <summary>
    /// A month of days to click. <paramref name="marks"/> gives how many things happen on a day,
    /// shown as dots. Returns true when a day was clicked.
    /// </summary>
    private bool MonthGrid(string id, ref DateTime month, DateTime? selected, Func<DateTime, int> marks, out DateTime picked, bool compact = false) {
        picked = default;
        var clicked = false;
        var firstDay = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;

        var cellWidth = (compact ? 34 : 46) * Scale;

        if (ImGui.ArrowButton($"##{id}Prev", ImGuiDir.Left)) month = month.AddMonths(-1);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Previous month");
        ImGui.SameLine();
        if (ImGui.ArrowButton($"##{id}Next", ImGuiDir.Right)) month = month.AddMonths(1);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Next month");
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(theme.Heading, month.ToString("MMMM yyyy"));
        if (!compact && (month.Year != DateTime.Today.Year || month.Month != DateTime.Today.Month)) {
            ImGui.SameLine();
            if (ImGui.SmallButton($"Today##{id}")) month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        }

        if (!ImGui.BeginTable($"##{id}", 7, ImGuiTableFlags.SizingFixedSame | ImGuiTableFlags.NoHostExtendX)) return false;

        for (var i = 0; i < 7; i++) {
            ImGui.TableSetupColumn(((DayOfWeek)(((int)firstDay + i) % 7)).ToString()[..2], ImGuiTableColumnFlags.WidthFixed, cellWidth);
        }
        ImGui.TableHeadersRow();

        var start = month.AddDays(-(((int)month.DayOfWeek - (int)firstDay + 7) % 7));
        var today = DateTime.Today;
        var rowHeight = (compact ? 22 : 34) * Scale;

        for (var week = 0; week < 6; week++) {
            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
            for (var d = 0; d < 7; d++) {
                ImGui.TableNextColumn();
                var day = start.AddDays(week * 7 + d);
                var inMonth = day.Month == month.Month;
                var isSelected = selected?.Date == day;
                var count = inMonth ? marks(day) : 0;

                var color = !inMonth ? theme.Muted with { W = 0.45f } : day == today ? theme.AccentText : theme.Text;
                using (ImRaii.PushColor(ImGuiCol.Text, color)) {
                    if (ImGui.Selectable($"{day.Day}##{id}{day:yyyyMMdd}", isSelected, ImGuiSelectableFlags.None, new Vector2(cellWidth, rowHeight - 2 * Scale))) {
                        picked = day;
                        clicked = true;
                        if (!inMonth) month = new DateTime(day.Year, day.Month, 1);
                    }
                }

                if (count > 0) {
                    var rect = ImGui.GetItemRectMax();
                    var radius = 2.5f * Scale;
                    var drawList = ImGui.GetWindowDrawList();
                    for (var dot = 0; dot < Math.Min(count, 3); dot++) {
                        var center = new Vector2(rect.X - 6 * Scale - dot * (radius * 2.6f), rect.Y - 6 * Scale);
                        drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(theme.Accent));
                    }
                }
            }
        }

        ImGui.EndTable();
        return clicked;
    }

    /// <summary>Combo box over a list, with a search field at the top.</summary>
    private static bool SearchCombo<T>(string id, string preview, ref string filter, IEnumerable<T> items, Func<T, string> label,
        Func<T, string?> group, out T? chosen, float width) where T : class {
        chosen = null;
        ImGui.SetNextItemWidth(width);
        using var combo = ImRaii.Combo($"##{id}", preview, ImGuiComboFlags.HeightLarge);
        if (!combo) return false;

        if (ImGui.IsWindowAppearing()) {
            filter = "";
            ImGui.SetKeyboardFocusHere();
        }
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##Filter", "Search", ref filter, 64);

        string? lastGroup = null;
        var search = filter;
        foreach (var item in items.Where(i => search.Length == 0 || label(i).Contains(search, StringComparison.OrdinalIgnoreCase))) {
            var heading = group(item);
            if (heading is not null && heading != lastGroup && heading.Length > 0) {
                ImGui.TextDisabled(heading);
                lastGroup = heading;
            }

            if (!ImGui.Selectable($"{label(item)}##{id}{item.GetHashCode()}")) continue;
            chosen = item;
            return true;
        }

        return false;
    }
}
