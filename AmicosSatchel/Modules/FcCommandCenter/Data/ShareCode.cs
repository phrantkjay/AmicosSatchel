using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AmicosSatchel.Modules.FcCommandCenter.Data;

[Flags]
public enum ShareSections {
    None = 0,
    Events = 1 << 0,
    Announcements = 1 << 1,
    Tasks = 1 << 2,
    Rotation = 1 << 3,
    Chest = 1 << 4,
    Workshop = 1 << 5,
    Roster = 1 << 6,
    Signups = 1 << 7,

    /// <summary>What members usually need: no chest contents or roster.</summary>
    ForMembers = Events | Announcements | Tasks | Rotation | Workshop | Signups,
    Everything = Events | Announcements | Tasks | Rotation | Chest | Workshop | Roster | Signups,
}

/// <summary>The part of a board that travels in a share code.</summary>
public sealed class SharePayload {
    public int Version { get; set; } = FcBoard.CurrentVersion;
    public ulong FcId { get; set; }
    public string FcName { get; set; } = "";
    public string ExportedBy { get; set; } = "";
    public DateTime ExportedUtc { get; set; }
    public ShareSections Sections { get; set; }

    public List<FcEvent>? Events { get; set; }
    public List<Announcement>? Announcements { get; set; }
    public List<OfficerTask>? Tasks { get; set; }
    public List<RotationSlot>? Rotation { get; set; }
    public List<ChestNote>? ChestNotes { get; set; }
    public ChestSnapshot? Chest { get; set; }
    public List<WorkshopProject>? Projects { get; set; }
    public VoyageSnapshot? Voyages { get; set; }
    public RosterSnapshot? Roster { get; set; }
    public List<EventSignup>? Signups { get; set; }
    public List<MemberNote>? MemberNotes { get; set; }
    public List<ChestChange>? ChestChanges { get; set; }
    public List<VoyageLoot>? Loot { get; set; }
}

/// <summary>
/// Turns part of a board into text that can be pasted in chat, Discord or a DM, and back.
/// The text is "FCC1." followed by the board as compressed, URL-safe Base64 JSON.
/// </summary>
public static class ShareCode {
    private const string Prefix = "FCC1.";

    // Limits so a mistaken or hostile paste can't stall the game.
    private const int MaxCodeLength = 2_000_000;
    private const int MaxJsonBytes = 8_000_000;

    private static readonly JsonSerializerOptions CompactJson = new(BoardStore.Json) { WriteIndented = false };

    public static string Export(FcBoard board, ShareSections sections, string exportedBy) =>
        Encode(Build(board, sections, exportedBy));

    /// <summary>A reply code holding only <paramref name="member"/>'s event sign-ups, for members to send to officers.</summary>
    public static string ExportSignups(FcBoard board, string member) {
        var payload = Build(board, ShareSections.None, member);
        payload.Sections = ShareSections.Signups;
        payload.Signups = board.Signups.Where(s => string.Equals(s.Member, member, StringComparison.OrdinalIgnoreCase)).ToList();
        return Encode(payload);
    }

    private static SharePayload Build(FcBoard board, ShareSections sections, string exportedBy) =>
        new() {
            FcId = board.FcId,
            FcName = board.FcName,
            ExportedBy = exportedBy,
            ExportedUtc = DateTime.UtcNow,
            Sections = sections,
            Events = sections.HasFlag(ShareSections.Events) ? board.Events : null,
            Announcements = sections.HasFlag(ShareSections.Announcements) ? board.Announcements : null,
            Tasks = sections.HasFlag(ShareSections.Tasks) ? board.Tasks : null,
            Rotation = sections.HasFlag(ShareSections.Rotation) ? board.Rotation : null,
            ChestNotes = sections.HasFlag(ShareSections.Chest) ? board.ChestNotes : null,
            Chest = sections.HasFlag(ShareSections.Chest) ? board.Chest : null,
            Projects = sections.HasFlag(ShareSections.Workshop) ? board.Projects : null,
            Voyages = sections.HasFlag(ShareSections.Workshop) ? board.Voyages : null,
            Roster = sections.HasFlag(ShareSections.Roster) ? board.Roster : null,
            MemberNotes = sections.HasFlag(ShareSections.Roster) ? board.MemberNotes : null,
            ChestChanges = sections.HasFlag(ShareSections.Chest) ? board.ChestChanges : null,
            Loot = sections.HasFlag(ShareSections.Workshop) ? board.Loot : null,
            Signups = sections.HasFlag(ShareSections.Signups) ? board.Signups : null,
        };

    private static string Encode(SharePayload payload) {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, CompactJson);
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize)) deflate.Write(json);

        return Prefix + Convert.ToBase64String(output.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Reads a share code. Returns null and sets <paramref name="error"/> if the text isn't one.</summary>
    public static SharePayload? Parse(string text, out string? error) {
        error = null;
        text = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

        var start = text.IndexOf(Prefix, StringComparison.Ordinal);
        if (start < 0) {
            error = "That isn't an FC Command Center share code (they start with FCC1.).";
            return null;
        }
        if (text.Length > MaxCodeLength) {
            error = "That share code is too long.";
            return null;
        }

        try {
            var base64 = text[(start + Prefix.Length)..].Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            var compressed = Convert.FromBase64String(base64);

            using var deflate = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var json = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = deflate.Read(buffer)) > 0) {
                json.Write(buffer, 0, read);
                if (json.Length > MaxJsonBytes) {
                    error = "That share code holds too much data.";
                    return null;
                }
            }

            var payload = JsonSerializer.Deserialize<SharePayload>(json.ToArray(), BoardStore.Json);
            if (payload is null) error = "That share code is empty.";
            else if (payload.Version > FcBoard.CurrentVersion) error = "That code was made by a newer version of Amico's Satchel. Update the plugin, then try again.";
            return error is null ? payload : null;
        } catch (Exception e) when (e is FormatException or InvalidDataException or JsonException or IOException) {
            error = "That share code is damaged or incomplete. Copy it again, all of it.";
            return null;
        }
    }

    /// <summary>Merges a share code's contents into the board. Newer changes win, item by item.</summary>
    public static MergeResult Import(FcBoard board, SharePayload payload) {
        var result = new MergeResult(0, 0);

        if (payload.Events is { } events) result += BoardMerge.Merge(board.Events, events);
        if (payload.Announcements is { } announcements) result += BoardMerge.Merge(board.Announcements, announcements);
        if (payload.Tasks is { } tasks) result += BoardMerge.Merge(board.Tasks, tasks);
        if (payload.Rotation is { } rotation) result += BoardMerge.Merge(board.Rotation, rotation);
        if (payload.ChestNotes is { } notes) result += BoardMerge.Merge(board.ChestNotes, notes);
        if (payload.Projects is { } projects) result += BoardMerge.Merge(board.Projects, projects);
        if (payload.Signups is { } signups) result += BoardMerge.Merge(board.Signups, signups);
        if (payload.MemberNotes is { } memberNotes) result += BoardMerge.Merge(board.MemberNotes, memberNotes);
        if (payload.ChestChanges is { } chestChanges) result += BoardMerge.Merge(board.ChestChanges, chestChanges);
        if (payload.Loot is { } loot) result += BoardMerge.Merge(board.Loot, loot);

        if (payload.Chest is { } chest) result += MergeChest(board.Chest, chest);

        if (payload.Voyages is { } voyages && (board.Voyages is null || voyages.CapturedUtc > board.Voyages.CapturedUtc)) {
            board.Voyages = voyages;
            result += new MergeResult(0, 1);
        }

        if (payload.Roster is { } roster && (board.Roster is null || roster.CapturedUtc > board.Roster.CapturedUtc)) {
            board.Roster = roster;
            result += new MergeResult(0, 1);
        }

        if (board.FcName.Length == 0) board.FcName = payload.FcName;
        return result;
    }

    private static MergeResult MergeChest(ChestSnapshot target, ChestSnapshot incoming) {
        var updated = 0;

        foreach (var (number, page) in incoming.Pages) {
            if (!target.Pages.TryGetValue(number, out var mine) || page.CapturedUtc > mine.CapturedUtc) {
                target.Pages[number] = page;
                updated++;
            }
        }

        if (incoming.GilCapturedUtc > (target.GilCapturedUtc ?? DateTime.MinValue)) {
            target.Gil = incoming.Gil;
            target.GilCapturedUtc = incoming.GilCapturedUtc;
            updated++;
        }

        if (incoming.CrystalsCapturedUtc > (target.CrystalsCapturedUtc ?? DateTime.MinValue)) {
            target.Crystals = incoming.Crystals;
            target.CrystalsCapturedUtc = incoming.CrystalsCapturedUtc;
            updated++;
        }

        if (updated > 0) target.CapturedBy = incoming.CapturedBy;
        var gil = History.MergeGil(target, incoming.GilHistory ?? []);
        return new MergeResult(gil, updated);
    }

    /// <summary>Counts what a payload holds, for the import preview.</summary>
    public static string Describe(SharePayload payload) {
        var parts = new List<string>();
        void Add(string label, int? count) {
            if (count is > 0) parts.Add($"{count} {label}");
        }

        Add("events", payload.Events?.Count(i => !i.Deleted));
        Add("announcements", payload.Announcements?.Count(i => !i.Deleted));
        Add("tasks", payload.Tasks?.Count(i => !i.Deleted));
        Add("rotation steps", payload.Rotation?.Count(i => !i.Deleted));
        Add("chest notes", payload.ChestNotes?.Count(i => !i.Deleted));
        Add("chest pages", payload.Chest?.Pages.Count);
        Add("workshop projects", payload.Projects?.Count(i => !i.Deleted));
        Add("vessels", payload.Voyages?.Vessels.Count);
        Add("members", payload.Roster?.Members.Count);
        Add("member notes", payload.MemberNotes?.Count(i => !i.Deleted));
        Add("event sign-ups", payload.Signups?.Count(i => !i.Deleted));
        Add("chest changes", payload.ChestChanges?.Count(i => !i.Deleted));
        Add("voyage loot logs", payload.Loot?.Count(i => !i.Deleted));

        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }

    /// <summary>Text for a Discord post: upcoming events with Discord timestamps, pinned news and open tasks.</summary>
    public static string DiscordPost(FcBoard board, IEnumerable<(FcEvent Event, DateTime StartUtc)> upcoming) {
        var text = new StringBuilder();
        var name = board.FcName.Length > 0 ? board.FcName : "Free Company";
        text.AppendLine($"## {name}: this week");

        var events = upcoming.ToList();
        if (events.Count > 0) {
            text.AppendLine().AppendLine("**Events**");
            foreach (var (e, start) in events) {
                var unix = new DateTimeOffset(start).ToUnixTimeSeconds();
                text.Append($"- <t:{unix}:F> (<t:{unix}:R>): **{e.Title}**");
                if (e.Location.Length > 0) text.Append($" at {e.Location}");
                if (e.Host.Length > 0) text.Append($", hosted by {e.Host}");
                text.AppendLine();
            }
        }

        var pinned = board.Announcements.Where(a => !a.Deleted && a.Pinned).OrderByDescending(a => a.PostedUtc).ToList();
        if (pinned.Count > 0) {
            text.AppendLine().AppendLine("**Announcements**");
            foreach (var a in pinned) {
                text.AppendLine($"- **{a.Title}**");
                if (a.Body.Length > 0) text.AppendLine($"  {a.Body.ReplaceLineEndings("\n  ")}");
            }
        }

        return text.ToString().TrimEnd();
    }
}
