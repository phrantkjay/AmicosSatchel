using System;
using System.Collections.Generic;
using System.Linq;

namespace AmicosSatchel.Modules.FcCommandCenter.Data;

/// <summary>
/// Works out when events happen, repeats included.
/// </summary>
public static class Schedule {
    /// <summary>Every start of <paramref name="e"/> from <paramref name="fromUtc"/> up to (not including) <paramref name="toUtc"/>.</summary>
    public static IEnumerable<DateTime> Starts(FcEvent e, DateTime fromUtc, DateTime toUtc) {
        var first = DateTime.SpecifyKind(e.StartUtc, DateTimeKind.Utc);
        var duration = TimeSpan.FromMinutes(Math.Max(0, e.DurationMinutes));

        if (e.Repeat == EventRepeat.None) {
            if (first + duration > fromUtc && first < toUtc) yield return first;
            yield break;
        }

        var step = TimeSpan.FromDays(e.Repeat == EventRepeat.Weekly ? 7 : 14);

        // Jump straight to the first repeat that hasn't finished by fromUtc.
        var start = first;
        if (start + duration <= fromUtc) {
            var skips = (long)Math.Ceiling((fromUtc - duration - first).Ticks / (double)step.Ticks);
            start = first + TimeSpan.FromTicks(step.Ticks * Math.Max(0, skips));
            if (start + duration <= fromUtc) start += step;
        }

        for (; start < toUtc; start += step) yield return start;
    }

    /// <summary>Events that haven't finished yet, soonest first.</summary>
    public static List<(FcEvent Event, DateTime StartUtc)> Upcoming(FcBoard board, DateTime nowUtc, TimeSpan within, int max = int.MaxValue) =>
        board.Events
            .Where(e => !e.Deleted)
            .SelectMany(e => Starts(e, nowUtc, nowUtc + within).Select(start => (e, start)))
            .OrderBy(x => x.start)
            .Take(max)
            .ToList();

    /// <summary>"in 3h 12m", "in 2d 4h", or "now".</summary>
    public static string Until(DateTime whenUtc, DateTime nowUtc) {
        var left = whenUtc - nowUtc;
        return left <= TimeSpan.Zero ? "now" : "in " + Span(left);
    }

    /// <summary>A short length of time: "45m", "3h 12m", "2d 4h".</summary>
    public static string Span(TimeSpan span) {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
        return $"{Math.Max(1, (int)Math.Ceiling(span.TotalMinutes))}m";
    }

    /// <summary>A time shown in the player's own time zone, such as "Thu 20 Nov, 20:00".</summary>
    public static string Local(DateTime utc, bool withDate = true) {
        var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        return withDate ? local.ToString("ddd d MMM, HH:mm") : local.ToString("HH:mm");
    }
}
