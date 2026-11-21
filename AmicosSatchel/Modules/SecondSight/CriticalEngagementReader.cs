using System.Collections.Generic;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using XIVMap.Shared;

namespace AmicosSatchel.Modules.SecondSight;

/// <summary>
/// Critical Engagements in Occult Crescent and Bozja, from the content's own event list (the
/// one its CE window shows). Read-only; game thread. Empty anywhere else.
/// </summary>
internal static unsafe class CriticalEngagementReader {
    private const int MaxNameLength = 256;

    public static List<CriticalEngagementInfo> Read() {
        var result = new List<CriticalEngagementInfo>();
        try {
            var occult = PublicContentOccultCrescent.GetInstance();
            if (occult != null)
                Add(result, &occult->DynamicEventContainer);

            var bozja = PublicContentBozja.GetInstance();
            if (bozja != null)
                Add(result, &bozja->DynamicEventContainer);
        } catch (System.Exception ex) {
            // Keep the rest of the map updating if this content's data can't be read.
            if (!errorLogged)
                Services.Log.Warning(ex, "Could not read Critical Engagements; skipping them");
            errorLogged = true;
            result.Clear();
        }
        return result;
    }

    private static bool errorLogged;

    private static void Add(List<CriticalEngagementInfo> result, DynamicEventContainer* container) {
        foreach (ref readonly var ce in container->Events) {
            if (ce.State == DynamicEventState.Inactive || ce.DynamicEventId == 0)
                continue;

            var marker = ce.MapMarker;
            result.Add(new CriticalEngagementInfo(
                ce.DynamicEventId,
                Text(in ce.Name),
                new Vec3(marker.Position.X, marker.Position.Y, marker.Position.Z),
                marker.Radius,
                (CriticalEngagementState)ce.State,
                ce.Progress,
                SecondsLeft(in ce),
                ce.Participants,
                ce.MaxParticipants,
                marker.IconId != 0 ? marker.IconId : ce.IconObjective0));
        }
    }

    /// <summary>
    /// Time left in the current phase. The game only counts SecondsLeft down during the battle;
    /// for sign-up and warm-up it is worked out from when the event began plus the phase lengths.
    /// Falls back to SecondsLeft if that gives something out of range.
    /// </summary>
    private static uint SecondsLeft(in DynamicEvent ce) {
        if (ce.SecondsLeft > 0 || ce.StartTimestamp <= 0)
            return ce.SecondsLeft;

        var now = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.GetServerTime();
        LogTiming(in ce, now);

        // StartTimestamp is normally when sign-up opened; if it is still ahead, treat it as the
        // moment the current phase ends.
        long phaseEnd = ce.StartTimestamp > now ? ce.StartTimestamp : ce.State switch {
            DynamicEventState.Register => ce.StartTimestamp + RegistrationSeconds(in ce),
            DynamicEventState.Warmup => ce.StartTimestamp + RegistrationSeconds(in ce) + WarmupSeconds(in ce),
            _ => 0,
        };
        var left = phaseEnd - now;
        var limit = (long)RegistrationSeconds(in ce) + WarmupSeconds(in ce);
        return phaseEnd > 0 && left > 0 && left <= limit ? (uint)left : ce.SecondsLeft;
    }

    // ClientStructs keeps these two lengths private; read them at their offsets (0x6C, 0x70).
    private static uint RegistrationSeconds(in DynamicEvent ce) => ReadUInt(in ce, 0x6C);
    private static uint WarmupSeconds(in DynamicEvent ce) => ReadUInt(in ce, 0x70);

    private static uint ReadUInt(in DynamicEvent ce, int offset)
        => *(uint*)((byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref System.Runtime.CompilerServices.Unsafe.AsRef(in ce)) + offset);

    private static readonly HashSet<(ushort, DynamicEventState)> timingLogged = [];

    /// <summary>Once per CE and phase, writes the raw timer values to the log, to check the countdown maths.</summary>
    private static void LogTiming(in DynamicEvent ce, long now) {
        if (timingLogged.Add((ce.DynamicEventId, ce.State)))
            Services.Log.Info($"[ce] id={ce.DynamicEventId} state={ce.State} now={now} start={ce.StartTimestamp} left={ce.SecondsLeft} " +
                            $"reg={RegistrationSeconds(in ce)} warmup={WarmupSeconds(in ce)} duration={ce.SecondsDuration}");
    }

    private static string Text(in FFXIVClientStructs.FFXIV.Client.System.String.Utf8String text)
        => !text.StringPtr.HasValue || text.StringLength <= 0 || text.StringLength > MaxNameLength
            ? ""
            : SeString.Parse(text.AsSpan()).TextValue;
}
