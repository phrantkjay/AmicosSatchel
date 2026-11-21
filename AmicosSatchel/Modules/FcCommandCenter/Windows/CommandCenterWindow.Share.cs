using System;
using System.IO;
using System.Linq;
using System.Numerics;
using AmicosSatchel.Modules.FcCommandCenter.Data;
using AmicosSatchel.Modules.FcCommandCenter.Game;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;

namespace AmicosSatchel.Modules.FcCommandCenter.Windows;

// Share: copy the board as a code for members, paste codes from officers, and make a Discord post.
public sealed partial class CommandCenterWindow {
    private static readonly (ShareSections Section, string Label)[] SectionLabels = [
        (ShareSections.Events, "Events"),
        (ShareSections.Announcements, "Announcements"),
        (ShareSections.Tasks, "Tasks"),
        (ShareSections.Signups, "Event sign-ups"),
        (ShareSections.Rotation, "Company action rotation"),
        (ShareSections.Workshop, "Workshop projects, voyages and loot"),
        (ShareSections.Chest, "Chest contents, notes and change log"),
        (ShareSections.Roster, "Member list and member notes"),
    ];

    private const int PasteBoxLimit = 32_000;

    private readonly FileDialogManager fileDialog = new();
    private string calendarStatus = "";

    private ShareSections exportSections = ShareSections.ForMembers;
    private string exportStatus = "";
    private string importText = "";
    private SharePayload? importPreview;
    private string? importError;
    private string importStatus = "";

    private void DrawShare() {
        Hint("The command center is kept on your PC. To share it, copy a share code and paste it in your FC's Discord or a DM. " +
             "Others paste it below to bring their board up to date. Newer changes win, item by item, so pasting the same code twice is harmless.");

        if (Officer) DrawExport();
        DrawReplies();
        DrawImport();
        DrawCalendarFile();
    }

    private void DrawReplies() {
        var me = Watcher.PlayerName;
        var mine = Board.Signups.Count(s => !s.Deleted && Watcher.Same(s.Member, me));
        if (Officer && mine == 0) return;

        Heading("Your event replies");
        if (mine == 0) {
            ImGui.TextDisabled("Reply to events in the Calendar tab, then send your replies to an officer from here.");
            return;
        }

        if (ImGui.Button("Copy my reply code")) CopyMyReplies(me);
        ImGui.SameLine();
        ImGui.TextDisabled(mine == 1 ? "1 reply. Send the code to an officer." : $"{mine} replies. Send the code to an officer.");
    }

    private void DrawCalendarFile() {
        Heading("Calendar file");
        Hint("Save the events as an .ics file to import into Google Calendar, Outlook or Apple Calendar. Repeating events repeat there too.");

        using (ImRaii.Disabled(!Board.Events.Any(e => !e.Deleted))) {
            if (ImGui.Button("Save calendar file")) {
                var name = Board.FcName.Length > 0 ? new string(Board.FcName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()) : "FC";
                fileDialog.SaveFileDialog("Save calendar file", ".ics", $"{name} events.ics", ".ics", (ok, path) => {
                    if (!ok) return;
                    try {
                        File.WriteAllText(path, CalendarFile.Build(Board));
                        calendarStatus = $"Saved {path}";
                    } catch (Exception e) {
                        calendarStatus = $"Couldn't save the file: {e.Message}";
                    }
                });
            }
        }

        if (calendarStatus.Length > 0) Hint(calendarStatus);
    }

    private void DrawExport() {
        Heading("Share your board");
        ImGui.TextDisabled("Include:");

        using (var table = ImRaii.Table("##Sections", 2, ImGuiTableFlags.SizingStretchSame)) {
            if (table) {
                foreach (var (section, label) in SectionLabels) {
                    ImGui.TableNextColumn();
                    var on = exportSections.HasFlag(section);
                    if (ImGui.Checkbox(label, ref on)) exportSections = on ? exportSections | section : exportSections & ~section;
                }
            }
        }

        if (exportSections.HasFlag(ShareSections.Chest)) ImGui.TextDisabled("Chest contents are often officers-only. Leave them out for member codes.");

        using (ImRaii.Disabled(exportSections == ShareSections.None)) {
            if (ImGui.Button("Copy share code")) {
                var code = ShareCode.Export(Board, exportSections, Watcher.PlayerName);
                ImGui.SetClipboardText(code);
                exportStatus = $"Copied a {code.Length:N0}-character share code. Paste it where your members will see it.";
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Copy Discord post")) {
            var upcoming = Schedule.Upcoming(Board, DateTime.UtcNow, TimeSpan.FromDays(7));
            ImGui.SetClipboardText(ShareCode.DiscordPost(Board, upcoming));
            exportStatus = "Copied this week's events and pinned announcements. Event times show in each reader's own time zone on Discord.";
        }

        if (exportStatus.Length > 0) Hint(exportStatus);
    }

    private void DrawImport() {
        Heading("Paste a share code");

        if (ImGui.Button("Paste from clipboard")) {
            importText = ImGui.GetClipboardText() ?? "";
            Preview();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("or paste into the box");

        // The text box keeps a buffer of its full size every frame, so big codes skip it.
        if (importText.Length > PasteBoxLimit) {
            ImGui.TextDisabled($"Pasted a {importText.Length:N0}-character code.");
        } else if (ImGui.InputTextMultiline("##Import", ref importText, PasteBoxLimit, new Vector2(-1, 60 * Scale))) {
            Preview();
        }

        if (importError is { } error) ErrorText(error);

        if (importPreview is { } payload) {
            var from = payload.ExportedBy.Length > 0 ? payload.ExportedBy : "someone";
            var fc = payload.FcName.Length > 0 ? payload.FcName : "an FC";
            Hint($"From {from} in {fc}, made {Schedule.Local(payload.ExportedUtc)}. It holds {ShareCode.Describe(payload)}.");

            var otherFc = payload.FcId != 0 && Board.FcId != 0 && payload.FcId != Board.FcId;
            if (otherFc) ErrorText("This code is from a different Free Company than the one you're in.");

            if (ImGui.Button(otherFc ? "Import anyway" : "Import")) {
                var result = ShareCode.Import(Board, payload);
                store.MarkChanged();
                importStatus = result.Added + result.Updated == 0
                    ? "Already up to date: nothing in that code was newer than your board."
                    : $"Imported: {result.Added} new, {result.Updated} updated.";
                importText = "";
                importPreview = null;
            }
            ImGui.SameLine();
            if (ImGui.Button("Clear")) {
                importText = "";
                importPreview = null;
                importError = null;
            }
        }

        if (importStatus.Length > 0) Hint(importStatus);
    }

    private void Preview() {
        importStatus = "";
        importPreview = null;
        importError = null;
        if (string.IsNullOrWhiteSpace(importText)) return;
        importPreview = ShareCode.Parse(importText, out importError);
    }
}
