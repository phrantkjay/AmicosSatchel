using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace AmicosSatchel.Windows;

/// <summary>
/// Applies a <see cref="SatchelTheme"/> to ImGui, so every satchel window shares one look.
/// </summary>
public static class SatchelStyle {
    /// <summary>
    /// Pushes the theme's colours and shapes. Dispose the result (in the window's PostDraw) to undo it.
    /// </summary>
    public static IDisposable Push(SatchelTheme t) {
        const float hover = 0.08f;

        var colors = ImRaii.PushColor(ImGuiCol.WindowBg, t.WindowBg)
            .Push(ImGuiCol.PopupBg, t.Card)
            .Push(ImGuiCol.ChildBg, Vector4.Zero)
            .Push(ImGuiCol.Border, t.Border)
            .Push(ImGuiCol.TitleBg, t.TitleBg)
            .Push(ImGuiCol.TitleBgActive, t.TitleBgActive)
            .Push(ImGuiCol.TitleBgCollapsed, t.TitleBg)
            .Push(ImGuiCol.Text, t.Text)
            .Push(ImGuiCol.TextDisabled, t.Muted)
            .Push(ImGuiCol.FrameBg, t.Input)
            .Push(ImGuiCol.FrameBgHovered, SatchelTheme.Mix(t.Input, t.Text, hover))
            .Push(ImGuiCol.FrameBgActive, SatchelTheme.Mix(t.Input, t.Text, hover * 2))
            .Push(ImGuiCol.Button, t.Card)
            .Push(ImGuiCol.ButtonHovered, SatchelTheme.Mix(t.Card, t.Text, hover))
            .Push(ImGuiCol.ButtonActive, SatchelTheme.Mix(t.Card, t.Text, hover * 2))
            .Push(ImGuiCol.Header, SatchelTheme.Mix(t.Card, t.Accent, 0.25f))
            .Push(ImGuiCol.HeaderHovered, SatchelTheme.Mix(t.Card, t.Accent, 0.35f))
            .Push(ImGuiCol.HeaderActive, SatchelTheme.Mix(t.Card, t.Accent, 0.45f))
            .Push(ImGuiCol.CheckMark, t.Accent)
            .Push(ImGuiCol.Separator, t.CardBorder)
            .Push(ImGuiCol.Tab, t.Card)
            .Push(ImGuiCol.TabHovered, SatchelTheme.Mix(t.Card, t.Accent, 0.35f))
            .Push(ImGuiCol.TabActive, SatchelTheme.Mix(t.Card, t.Accent, 0.25f))
            .Push(ImGuiCol.TableHeaderBg, t.Card)
            .Push(ImGuiCol.TableBorderLight, t.CardBorder)
            .Push(ImGuiCol.TableBorderStrong, t.CardBorder)
            .Push(ImGuiCol.TableRowBgAlt, SatchelTheme.Mix(t.WindowBg, t.Text, 0.03f))
            .Push(ImGuiCol.PlotHistogram, t.Accent)
            .Push(ImGuiCol.ScrollbarBg, Vector4.Zero)
            .Push(ImGuiCol.ScrollbarGrab, t.CardBorder)
            .Push(ImGuiCol.ScrollbarGrabHovered, SatchelTheme.Mix(t.CardBorder, t.Text, 0.15f))
            .Push(ImGuiCol.ScrollbarGrabActive, SatchelTheme.Mix(t.CardBorder, t.Text, 0.3f))
            .Push(ImGuiCol.ResizeGrip, t.Border with { W = 0.45f })
            .Push(ImGuiCol.ResizeGripHovered, t.Border)
            .Push(ImGuiCol.ResizeGripActive, t.Border);

        var scale = ImGuiHelpers.GlobalScale;
        var styles = ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, t.WindowRounding * scale)
            .Push(ImGuiStyleVar.WindowBorderSize, 1f)
            .Push(ImGuiStyleVar.WindowPadding, new Vector2(14, 14) * scale)
            .Push(ImGuiStyleVar.FrameRounding, t.ControlRounding * scale)
            .Push(ImGuiStyleVar.FrameBorderSize, 1f)
            .Push(ImGuiStyleVar.FramePadding, new Vector2(10, 6) * scale)
            .Push(ImGuiStyleVar.PopupRounding, t.ControlRounding * scale)
            .Push(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8) * scale)
            .Push(ImGuiStyleVar.ScrollbarRounding, t.ControlRounding * scale);

        return new Scope(styles, colors);
    }

    /// <summary>Draws a small rounded label, such as a category or a count.</summary>
    public static void Chip(string text, Vector4 background, Vector4 foreground) {
        var scale = ImGuiHelpers.GlobalScale;
        var padding = new Vector2(9, 1) * scale;
        var textSize = ImGui.CalcTextSize(text);
        var position = ImGui.GetCursorScreenPos();
        var size = textSize + padding * 2;

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(position, position + size, ImGui.GetColorU32(background), size.Y / 2);
        drawList.AddText(position + padding, ImGui.GetColorU32(foreground), text);
        ImGui.Dummy(size);
    }

    private sealed class Scope(IDisposable first, IDisposable second) : IDisposable {
        public void Dispose() {
            first.Dispose();
            second.Dispose();
        }
    }
}
