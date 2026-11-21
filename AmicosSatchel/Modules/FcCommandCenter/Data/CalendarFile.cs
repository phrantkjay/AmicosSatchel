using System;
using System.Linq;
using System.Text;

namespace AmicosSatchel.Modules.FcCommandCenter.Data;

/// <summary>
/// Writes FC events as an iCalendar (.ics) file that Google Calendar, Outlook and Apple Calendar can import.
/// </summary>
public static class CalendarFile {
    public static string Build(FcBoard board) {
        var name = board.FcName.Length > 0 ? board.FcName : "Free Company";
        var stamp = Format(DateTime.UtcNow);
        var text = new StringBuilder();

        Line(text, "BEGIN:VCALENDAR");
        Line(text, "VERSION:2.0");
        Line(text, "PRODID:-//Amico's Satchel//FC Command Center//EN");
        Line(text, "CALSCALE:GREGORIAN");
        Line(text, $"X-WR-CALNAME:{Escape(name)} events");

        foreach (var e in board.Events.Where(e => !e.Deleted).OrderBy(e => e.StartUtc)) {
            var start = DateTime.SpecifyKind(e.StartUtc, DateTimeKind.Utc);
            Line(text, "BEGIN:VEVENT");
            Line(text, $"UID:{e.Id}@fc-command-center");
            Line(text, $"DTSTAMP:{stamp}");
            Line(text, $"LAST-MODIFIED:{Format(e.UpdatedUtc)}");
            Line(text, $"DTSTART:{Format(start)}");
            Line(text, $"DTEND:{Format(start.AddMinutes(Math.Max(1, e.DurationMinutes)))}");
            Line(text, $"SUMMARY:{Escape(e.Title.Length > 0 ? e.Title : "FC event")}");
            if (e.Location.Length > 0) Line(text, $"LOCATION:{Escape(e.Location)}");

            var description = e.Host.Length > 0 ? $"Host: {e.Host}\n{e.Description}" : e.Description;
            if (description.Trim().Length > 0) Line(text, $"DESCRIPTION:{Escape(description.Trim())}");

            if (e.Repeat != EventRepeat.None) Line(text, $"RRULE:FREQ=WEEKLY;INTERVAL={(e.Repeat == EventRepeat.Weekly ? 1 : 2)}");
            Line(text, "END:VEVENT");
        }

        Line(text, "END:VCALENDAR");
        return text.ToString();
    }

    private static string Format(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyyMMdd'T'HHmmss'Z'");

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").ReplaceLineEndings("\\n");

    /// <summary>Adds a line, folded at 75 bytes as the format requires.</summary>
    private static void Line(StringBuilder text, string line) {
        var bytes = 0;
        var start = 0;
        for (var i = 0; i < line.Length; i++) {
            var size = Encoding.UTF8.GetByteCount(line.AsSpan(i, char.IsHighSurrogate(line[i]) && i + 1 < line.Length ? 2 : 1));
            if (bytes + size > (start == 0 ? 75 : 74)) {
                text.Append(line, start, i - start).Append("\r\n ");
                start = i;
                bytes = 0;
            }
            bytes += size;
            if (char.IsHighSurrogate(line[i])) i++;
        }
        text.Append(line, start, line.Length - start).Append("\r\n");
    }
}
