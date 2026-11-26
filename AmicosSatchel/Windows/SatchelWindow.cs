using System;
using System.IO;
using System.Linq;
using System.Numerics;
using AmicosSatchel.Modules;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;

namespace AmicosSatchel.Windows;

/// <summary>
/// Main window: lists every module with a switch, its description and its settings, in the chosen theme.
/// </summary>
public sealed class SatchelWindow : Window, IDisposable {
    private const string Title = "Amico's Satchel";
    private const string Subtitle = "Small features you can switch on and off";
    private const string KofiUrl = "https://ko-fi.com/phrantkjay";
    private const float IconSize = 56f;
    private const float CardPadding = 12f;
    private const float ModuleImageSize = 48f;
    private const float ImagePreviewSize = 128f;

    private readonly ModuleManager modules;
    private readonly Configuration configuration;
    private readonly IFontHandle titleFont;
    private readonly IFontHandle labelFont;
    private readonly string imagesFolder;
    private readonly string iconPath;

    private string search = string.Empty;
    private SatchelTheme theme = SatchelTheme.CrystalBoard;
    private IDisposable? pushedStyle;

    public SatchelWindow(ModuleManager modules, Configuration configuration)
        : base($"{Title}##Main") {
        this.modules = modules;
        this.configuration = configuration;

        // The game's own serif font, for the title.
        titleFont = Services.PluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Jupiter20));

        // The game's small UI font, for little labels.
        labelFont = Services.PluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis12));
        imagesFolder = Path.Combine(Services.PluginInterface.AssemblyLocation.DirectoryName!, "images");
        iconPath = Path.Combine(imagesFolder, "icon-128.png");

        // A heart in the title bar that opens the Ko-fi page, for anyone who wants to support the plugin.
        TitleBarButtons.Add(new TitleBarButton {
            Icon = FontAwesomeIcon.Heart,
            IconOffset = new Vector2(2, 1),
            Click = _ => Util.OpenLink(KofiUrl),
            ShowTooltip = () => ImGui.SetTooltip("Support Amico's Satchel on Ko-fi"),
        });

        Size = new Vector2(520, 460);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2(400, 300),
            MaximumSize = new Vector2(1000, 1400),
        };
    }

    public void Dispose() {
        titleFont.Dispose();
        labelFont.Dispose();
    }

    public override void PreDraw() {
        theme = SatchelTheme.For(configuration.Theme);
        pushedStyle = SatchelStyle.Push(theme);
    }

    public override void PostDraw() {
        pushedStyle?.Dispose();
        pushedStyle = null;
    }

    public override void Draw() {
        using var content = SatchelStyle.PushContent(theme);
        var scale = ImGuiHelpers.GlobalScale;

        DrawHeader(scale);
        DrawRule(scale);

        // Search box, with the border colour taken from the theme.
        using (ImRaii.PushColor(ImGuiCol.Border, theme.InputBorder)) {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##Search", "Search features", ref search, 64);
        }

        if (ImGui.BeginChild("##Modules", new Vector2(-1, -1), false)) {
            var shown = 0;
            foreach (var module in modules.Modules.Where(Matches)) {
                DrawModuleCard(module, scale);
                shown++;
            }

            if (shown == 0) ImGui.TextDisabled("No features match your search.");
            else ImGui.TextDisabled("More features coming soon.");
        }
        ImGui.EndChild();
    }

    private void DrawHeader(float scale) {
        var icon = Services.Textures.GetFromFile(iconPath).GetWrapOrEmpty();
        ImGui.Image(icon.Handle, new Vector2(IconSize) * scale);

        ImGui.SameLine();
        ImGui.BeginGroup();
        using (titleFont.Push()) ImGui.TextColored(theme.Heading, Title);
        ImGui.TextDisabled(Subtitle);
        ImGui.EndGroup();

        var on = modules.Modules.Count(modules.IsEnabled);
        var count = $"{on} of {modules.Modules.Count} on";
        var chipWidth = ImGui.CalcTextSize(count).X + 18 * scale;
        var pickerWidth = 150 * scale;

        // Right-hand column: the counter, with the theme picker under it, both aligned to the right edge.
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - pickerWidth);
        ImGui.BeginGroup();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + pickerWidth - chipWidth);
        DrawChip(count, scale);
        using (labelFont.Push()) ImGui.TextDisabled("Theme");
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 6 * scale);
        DrawThemePicker(pickerWidth, scale);
        ImGui.EndGroup();
    }

    private void DrawRule(float scale) {
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var end = start + new Vector2(width, 2 * scale);
        var drawList = ImGui.GetWindowDrawList();

        var solid = ImGui.GetColorU32(theme.Rule);
        if (theme.RuleFades) {
            var clear = ImGui.GetColorU32(theme.Rule with { W = 0 });
            drawList.AddRectFilledMultiColor(start, end, solid, clear, clear, solid);
        } else {
            drawList.AddRectFilled(start, end, solid, 2 * scale);
        }

        ImGui.Dummy(new Vector2(width, 2 * scale));
    }

    private void DrawModuleCard(Module module, float scale) {
        var drawList = ImGui.GetWindowDrawList();
        var padding = CardPadding * scale;
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var enabled = modules.IsEnabled(module);

        // Draw the content first, then the card background behind it once its height is known.
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        ImGui.PushID(module.Id);
        ImGui.SetCursorScreenPos(start + new Vector2(padding, padding));

        if (DrawSwitch("##Enabled", enabled, scale)) modules.SetEnabled(module, !enabled);
        ImGui.SameLine(0, 12 * scale);

        if (module.ImageFile is { } imageFile) {
            var image = Services.Textures.GetFromFile(Path.Combine(imagesFolder, "modules", imageFile)).GetWrapOrEmpty();
            var tint = enabled ? Vector4.One : new Vector4(1, 1, 1, 0.45f);
            ImGui.Image(image.Handle, new Vector2(ModuleImageSize) * scale, Vector2.Zero, Vector2.One, tint);

            // Hovering the image shows it full size.
            if (ImGui.IsItemHovered()) {
                ImGui.BeginTooltip();
                ImGui.Image(image.Handle, new Vector2(ImagePreviewSize) * scale);
                ImGui.EndTooltip();
            }

            ImGui.SameLine(0, 10 * scale);
        }

        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - (ImGui.GetCursorScreenPos().X - start.X) - padding);

        ImGui.TextColored(enabled ? theme.Text : theme.Muted, module.Name);
        if (module.Category is { } category) {
            ImGui.SameLine();
            DrawChip(category, scale);
        }

        ImGui.TextDisabled(module.Description);

        if (modules.ErrorFor(module) is { } error) {
            ImGui.TextColored(theme.Error, $"Couldn't start: {error} See /xllog for details.");
        }

        if (module.HasSettings && modules.IsRunning(module)) {
            using var accent = ImRaii.PushColor(ImGuiCol.Text, theme.AccentText);
            if (ImGui.TreeNode("Settings")) {
                accent.Pop();
                module.DrawSettings();
                ImGui.TreePop();
            }
        }

        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        ImGui.PopID();

        var end = new Vector2(start.X + width, ImGui.GetItemRectMax().Y + padding);

        drawList.ChannelsSetCurrent(0);
        var rounding = theme.ControlRounding * scale;
        drawList.AddRectFilled(start, end, ImGui.GetColorU32(theme.Card), rounding);
        drawList.AddRect(start, end, ImGui.GetColorU32(theme.CardBorder), rounding);
        drawList.ChannelsMerge();

        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(end - start);
    }

    /// <summary>
    /// Draws an on/off switch. Returns true when it was clicked.
    /// </summary>
    private bool DrawSwitch(string id, bool on, float scale) {
        var size = new Vector2(34, 18) * scale;
        var position = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();

        var radius = size.Y / 2;
        var track = on ? theme.Accent : theme.ToggleOff;
        if (hovered) track = SatchelTheme.Mix(track, theme.Text, 0.12f);

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(position, position + size, ImGui.GetColorU32(track), radius);
        drawList.AddRect(position, position + size, ImGui.GetColorU32(on ? theme.Accent : theme.InputBorder), radius);

        var knobX = on ? position.X + size.X - radius : position.X + radius;
        drawList.AddCircleFilled(new Vector2(knobX, position.Y + radius), radius - 3 * scale,
            ImGui.GetColorU32(on ? theme.KnobOn : theme.KnobOff));

        if (hovered) ImGui.SetTooltip(on ? "Turn off" : "Turn on");
        return clicked;
    }

    private void DrawChip(string text, float scale) => SatchelStyle.Chip(text, theme.ChipBg, theme.ChipText);

    /// <summary>
    /// Theme dropdown, shown in the header under the "on" counter.
    /// </summary>
    private void DrawThemePicker(float width, float scale) {
        using var border = ImRaii.PushColor(ImGuiCol.Border, theme.InputBorder);
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(8, 3) * scale);

        ImGui.SetNextItemWidth(width);
        if (!ImGui.BeginCombo("##Theme", theme.Name)) return;

        foreach (var option in SatchelTheme.All) {
            if (ImGui.Selectable(option.Name, option.Id == configuration.Theme)) {
                configuration.Theme = option.Id;
                configuration.Save();
            }
        }

        ImGui.EndCombo();
    }

    private bool Matches(Module module) =>
        search.Length == 0 ||
        module.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        module.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        (module.Category?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
}
