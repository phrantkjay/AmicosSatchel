using System;
using System.Collections.Generic;

namespace AmicosSatchel.Modules.FcCommandCenter.Data;

// Snapshots are copies of what the game showed, read when someone opened the chest, the
// workshop console or the member list. They are replaced as a whole, newest capture wins.

public sealed class ChestItem {
    public uint ItemId { get; set; }
    public bool HighQuality { get; set; }
    public int Quantity { get; set; }
}

public sealed class ChestPage {
    public DateTime CapturedUtc { get; set; }
    public List<ChestItem> Items { get; set; } = [];
}

public sealed class ChestSnapshot {
    /// <summary>Item pages by number (1 to 5). Each page is captured on its own, when it was viewed.</summary>
    public Dictionary<int, ChestPage> Pages { get; set; } = [];

    public long Gil { get; set; }
    public DateTime? GilCapturedUtc { get; set; }

    /// <summary>Gil each time it changed, oldest first.</summary>
    public List<GilReading> GilHistory { get; set; } = [];

    public List<ChestItem> Crystals { get; set; } = [];
    public DateTime? CrystalsCapturedUtc { get; set; }

    public string CapturedBy { get; set; } = "";
}

public sealed class GilReading {
    public DateTime Utc { get; set; }
    public long Gil { get; set; }
}

public enum VesselKind {
    Submersible,
    Airship,
}

public sealed class Vessel {
    public VesselKind Kind { get; set; }

    /// <summary>Slot in the workshop, from 0.</summary>
    public int Slot { get; set; }

    public string Name { get; set; } = "";
    public int Rank { get; set; }

    /// <summary>When the current voyage ends; null when the vessel is docked.</summary>
    public DateTime? ReturnUtc { get; set; }
}

public sealed class VoyageSnapshot {
    public DateTime CapturedUtc { get; set; }
    public string CapturedBy { get; set; } = "";
    public List<Vessel> Vessels { get; set; } = [];
}

public sealed class Member {
    public string Name { get; set; } = "";
    public string World { get; set; } = "";

    /// <summary>Job abbreviation, such as "WHM".</summary>
    public string Job { get; set; } = "";

    public DateTime LastSeenUtc { get; set; }

    /// <summary>First time this member showed up in the list.</summary>
    public DateTime FirstSeenUtc { get; set; }

    /// <summary>Last time the member list showed them online; null if never seen online.</summary>
    public DateTime? LastOnlineUtc { get; set; }
}

public sealed class RosterSnapshot {
    public DateTime CapturedUtc { get; set; }
    public List<Member> Members { get; set; } = [];
}
