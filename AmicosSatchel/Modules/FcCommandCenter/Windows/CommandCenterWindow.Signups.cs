using System;
using System.Collections.Generic;
using System.Linq;
using AmicosSatchel.Modules.FcCommandCenter.Data;
using AmicosSatchel.Modules.FcCommandCenter.Game;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AmicosSatchel.Modules.FcCommandCenter.Windows;

// Event sign-ups: everyone can reply to an event, officers see the headcount by role.
public sealed partial class CommandCenterWindow {
    private List<EventSignup> RepliesFor(FcEvent e, DateTime startUtc) =>
        Board.Signups.Where(s => !s.Deleted && s.EventId == e.Id && s.OccurrenceUtc == startUtc).ToList();

    /// <summary>"4 going (1 tank, 1 healer, 2 DPS) · 1 maybe · 1 can't".</summary>
    private static string ReplySummary(List<EventSignup> replies) {
        var going = replies.Where(r => r.Response == SignupResponse.Going).ToList();
        var parts = new List<string>();

        if (going.Count > 0) {
            var tanks = going.Count(r => r.Role == JobRole.Tank);
            var healers = going.Count(r => r.Role == JobRole.Healer);
            var dps = going.Count(r => r.Role is JobRole.Melee or JobRole.Ranged);
            var roles = new List<string>();
            if (tanks > 0) roles.Add(tanks == 1 ? "1 tank" : $"{tanks} tanks");
            if (healers > 0) roles.Add(healers == 1 ? "1 healer" : $"{healers} healers");
            if (dps > 0) roles.Add($"{dps} DPS");
            parts.Add(roles.Count > 0 ? $"{going.Count} going ({string.Join(", ", roles)})" : $"{going.Count} going");
        }

        var maybe = replies.Count(r => r.Response == SignupResponse.Maybe);
        var no = replies.Count(r => r.Response == SignupResponse.NotGoing);
        if (maybe > 0) parts.Add($"{maybe} maybe");
        if (no > 0) parts.Add($"{no} can't");
        return string.Join(" · ", parts);
    }

    private void DrawSignups(FcEvent e, DateTime startUtc) {
        var replies = RepliesFor(e, startUtc);
        var summary = ReplySummary(replies);

        if (summary.Length > 0) {
            ImGui.TextDisabled(summary);
            if (ImGui.IsItemHovered()) {
                using var tooltip = ImRaii.Tooltip();
                foreach (var group in replies.OrderBy(r => r.Response).ThenBy(r => r.Member).GroupBy(r => r.Response)) {
                    ImGui.TextColored(theme.Heading, ResponseLabel(group.Key));
                    foreach (var r in group) {
                        var job = r.Job.Length > 0 ? $" ({r.Job})" : "";
                        var comment = r.Comment.Length > 0 ? $": {r.Comment}" : "";
                        ImGui.TextUnformatted($"  {r.Member}{job}{comment}");
                    }
                }
            }
        }

        var me = Watcher.PlayerName;
        if (me.Length == 0 || startUtc.AddMinutes(e.DurationMinutes) < DateTime.UtcNow) return;

        var mine = replies.FirstOrDefault(r => Watcher.Same(r.Member, me));
        foreach (var response in Enum.GetValues<SignupResponse>()) {
            var chosen = mine?.Response == response;
            using (ImRaii.PushColor(ImGuiCol.Button, theme.Accent with { W = 0.55f }, chosen)) {
                if (ImGui.SmallButton($"{ResponseLabel(response)}##Reply{response}")) SetReply(e, startUtc, response, me);
            }
            ImGui.SameLine();
        }

        if (mine is not null && !Officer) {
            if (ImGui.SmallButton("Copy my reply code")) CopyMyReplies(me);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Officers only see your reply once you send them this code.");
        } else {
            ImGui.NewLine();
        }
    }

    private void SetReply(FcEvent e, DateTime startUtc, SignupResponse response, string me) {
        var id = EventSignup.IdFor(e.Id, startUtc, me);
        var reply = Board.Signups.FirstOrDefault(s => s.Id == id);
        if (reply is null) {
            reply = new EventSignup { Id = id, EventId = e.Id, OccurrenceUtc = startUtc, Member = me };
            Board.Signups.Add(reply);
        }

        var (job, role) = GameReader.CurrentJob();
        reply.Response = response;
        reply.Job = job;
        reply.Role = role;
        reply.Deleted = false;
        Changed(reply);
    }

    private void CopyMyReplies(string me) {
        ImGui.SetClipboardText(ShareCode.ExportSignups(Board, me));
        Services.ChatGui.Print("[FC Command Center] Your event replies are copied. Send the code to an officer.");
    }

    private static string ResponseLabel(SignupResponse response) => response switch {
        SignupResponse.Going => "Going",
        SignupResponse.Maybe => "Maybe",
        _ => "Can't go",
    };
}
