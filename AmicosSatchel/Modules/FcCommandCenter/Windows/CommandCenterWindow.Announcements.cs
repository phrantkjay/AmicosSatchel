using System;
using System.Linq;
using System.Numerics;
using AmicosSatchel.Modules.FcCommandCenter.Data;
using AmicosSatchel.Modules.FcCommandCenter.Game;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AmicosSatchel.Modules.FcCommandCenter.Windows;

// Announcements: news for members, pinned items first.
public sealed partial class CommandCenterWindow {
    private Guid? editingAnnouncement;
    private bool showExpired;

    private void DrawAnnouncements() {
        if (Officer) {
            if (ImGui.Button("Post announcement")) {
                var a = new Announcement { Title = "New announcement", Author = Watcher.PlayerName, PostedUtc = DateTime.UtcNow };
                Board.Announcements.Add(a);
                Changed(a);
                editingAnnouncement = a.Id;
            }
            ImGui.SameLine();
        }
        ImGui.Checkbox("Show expired", ref showExpired);

        var list = Board.Announcements
            .Where(a => !a.Deleted && (showExpired || !Watcher.Expired(a)))
            .OrderByDescending(a => a.Pinned).ThenByDescending(a => a.PostedUtc)
            .ToList();

        if (list.Count == 0) {
            Hint(Officer
                ? "No announcements yet. Post one, then share it from the Share tab."
                : "No announcements yet. Ask an officer for a share code and paste it in the Share tab.");
            return;
        }

        foreach (var a in list) {
            if (editingAnnouncement == a.Id && Officer) DrawAnnouncementEditor(a);
            else DrawAnnouncement(a);
        }
    }

    private void DrawAnnouncement(Announcement a) {
        using var id = ImRaii.PushId(a.Id.ToString());
        ImGui.Separator();

        if (a.PostedUtc > Board.AnnouncementsReadUtc) {
            ImGui.TextColored(theme.AccentText, "New");
            ImGui.SameLine();
        }
        if (a.Pinned) {
            ImGui.TextColored(theme.Heading, "Pinned");
            ImGui.SameLine();
        }
        ImGui.TextColored(theme.Heading, a.Title);

        var by = a.Author.Length > 0 ? $"{a.Author}, " : "";
        var expiry = a.ExpiresUtc is { } until ? (Watcher.Expired(a) ? " · expired" : $" · until {Schedule.Local(until)}") : "";
        ImGui.TextDisabled($"{by}{Schedule.Local(a.PostedUtc)}{expiry}");

        if (a.Body.Length > 0) {
            using var wrap = ImRaii.TextWrapPos(0);
            ImGui.TextUnformatted(a.Body);
        }

        if (ImGui.SmallButton("Copy for Discord")) {
            ImGui.SetClipboardText($"**{a.Title}**\n{a.Body}".TrimEnd());
            Services.ChatGui.Print("[FC Command Center] Announcement copied. Paste it into Discord.");
        }

        if (!Officer) return;
        ImGui.SameLine();
        if (ImGui.SmallButton("Edit")) editingAnnouncement = a.Id;
        ImGui.SameLine();
        if (ImGui.SmallButton(a.Pinned ? "Unpin" : "Pin")) {
            a.Pinned = !a.Pinned;
            Changed(a);
        }
        ImGui.SameLine();
        if (DeleteButton()) {
            a.Deleted = true;
            Changed(a);
        }
    }

    private void DrawAnnouncementEditor(Announcement a) {
        using var id = ImRaii.PushId(a.Id.ToString());
        ImGui.Separator();
        var width = ImGui.GetContentRegionAvail().X;
        var changed = false;

        var title = a.Title;
        ImGui.SetNextItemWidth(width);
        if (ImGui.InputTextWithHint("##Title", "Title", ref title, 120)) {
            a.Title = title;
            changed = true;
        }

        var body = a.Body;
        if (ImGui.InputTextMultiline("##Body", ref body, 4000, new Vector2(width, 110 * Scale))) {
            a.Body = body;
            changed = true;
        }

        var pinned = a.Pinned;
        if (ImGui.Checkbox("Pinned", ref pinned)) {
            a.Pinned = pinned;
            changed = true;
        }

        ImGui.SameLine();
        var expires = a.ExpiresUtc is not null;
        if (ImGui.Checkbox("Expires", ref expires)) {
            a.ExpiresUtc = expires ? DateTime.UtcNow.AddDays(7) : null;
            changed = true;
        }

        if (a.ExpiresUtc is { } until) {
            ImGui.SameLine();
            if (DateTimeField("Expires", ref until)) {
                a.ExpiresUtc = until;
                changed = true;
            }
        }

        if (changed) Changed(a);

        if (ImGui.SmallButton("Done")) editingAnnouncement = null;
        ImGui.SameLine();
        if (ImGui.SmallButton("Repost as new")) {
            // Shows it as new again for everyone who imports it.
            a.PostedUtc = DateTime.UtcNow;
            Changed(a);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Moves it to the top and marks it as new again for members.");
        ImGui.SameLine();
        if (DeleteButton()) {
            a.Deleted = true;
            Changed(a);
            editingAnnouncement = null;
        }
    }
}
