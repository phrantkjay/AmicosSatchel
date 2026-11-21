using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace AmicosSatchel.Modules.FcCommandCenter.Data;

/// <summary>
/// Something officers create and share: an event, announcement, task and so on.
/// </summary>
/// <remarks>
/// Every item carries its own id and last-change time so two copies of a board can be merged:
/// the newer change wins. Deleting only marks the item, so the deletion travels in share codes too.
/// </remarks>
public abstract class BoardItem {
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Who made the last change, as their character name.</summary>
    public string UpdatedBy { get; set; } = "";

    public bool Deleted { get; set; }

    /// <summary>
    /// An id worked out from <paramref name="key"/>, so two people recording the same thing
    /// (the same voyage, or one member's reply to one event) end up with the same item.
    /// </summary>
    public static Guid StableId(string key) => new(MD5.HashData(Encoding.UTF8.GetBytes(key)));

    /// <summary>Records a change made by <paramref name="who"/> just now.</summary>
    public void Touch(string who) {
        UpdatedUtc = DateTime.UtcNow;
        UpdatedBy = who;
    }
}

public enum EventRepeat {
    None,
    Weekly,
    Fortnightly,
}

public sealed class FcEvent : BoardItem {
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Location { get; set; } = "";
    public string Host { get; set; } = "";
    public DateTime StartUtc { get; set; } = DateTime.UtcNow;
    public int DurationMinutes { get; set; } = 60;
    public EventRepeat Repeat { get; set; } = EventRepeat.None;
}

public sealed class Announcement : BoardItem {
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime PostedUtc { get; set; } = DateTime.UtcNow;
    public bool Pinned { get; set; }

    /// <summary>When the announcement stops showing on the overview; null keeps it.</summary>
    public DateTime? ExpiresUtc { get; set; }
}

public enum TaskPriority {
    Low,
    Normal,
    High,
}

public enum TaskState {
    ToDo,
    InProgress,
    Done,
}

public sealed class OfficerTask : BoardItem {
    public string Title { get; set; } = "";
    public string Details { get; set; } = "";
    public string Assignee { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime? DueUtc { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public TaskState State { get; set; } = TaskState.ToDo;
}

/// <summary>One step in the planned company action rotation.</summary>
public sealed class RotationSlot : BoardItem {
    /// <summary>Row in the CompanyAction sheet.</summary>
    public uint ActionId { get; set; }

    /// <summary>Position in the rotation; lower runs first.</summary>
    public int Order { get; set; }

    /// <summary>How many the FC has bought and not used yet, as last counted by an officer.</summary>
    public int Stock { get; set; }

    public string Note { get; set; } = "";
}

/// <summary>An officer's note about an item kept in the FC chest, or about the chest in general.</summary>
public sealed class ChestNote : BoardItem {
    /// <summary>Item the note is about; 0 for a general chest note.</summary>
    public uint ItemId { get; set; }

    public string Note { get; set; } = "";

    /// <summary>Warn when the chest holds fewer than this; 0 for no warning.</summary>
    public int MinQuantity { get; set; }
}

public sealed class WorkshopProject : BoardItem {
    public string Name { get; set; } = "";

    /// <summary>Row in the CompanyCraftSequence sheet, or 0 for a project with a hand-made material list.</summary>
    public uint SequenceId { get; set; }

    public string Assignee { get; set; } = "";
    public string Notes { get; set; } = "";

    /// <summary>Phases finished so far, counting through every part in order.</summary>
    public int PhasesDone { get; set; }

    public bool Completed { get; set; }

    /// <summary>Who's bringing each material, by item id (or by material name for hand-made projects).</summary>
    public Dictionary<string, string> Pledges { get; set; } = [];

    /// <summary>Materials for a hand-made project (SequenceId 0).</summary>
    public List<CustomMaterial> CustomMaterials { get; set; } = [];
}

public enum SignupResponse {
    Going,
    Maybe,
    NotGoing,
}

/// <summary>Party role, from the ClassJob sheet's Role column.</summary>
public enum JobRole {
    None = 0,
    Tank = 1,
    Melee = 2,
    Ranged = 3,
    Healer = 4,
}

/// <summary>A member's reply to one event (one date of it, for repeating events).</summary>
public sealed class EventSignup : BoardItem {
    public Guid EventId { get; set; }
    public DateTime OccurrenceUtc { get; set; }
    public string Member { get; set; } = "";
    public SignupResponse Response { get; set; }

    /// <summary>Job abbreviation when they replied, such as "WHM".</summary>
    public string Job { get; set; } = "";

    public JobRole Role { get; set; }
    public string Comment { get; set; } = "";

    public static Guid IdFor(Guid eventId, DateTime occurrenceUtc, string member) =>
        StableId($"signup|{eventId}|{occurrenceUtc.Ticks}|{member.Trim().ToLowerInvariant()}");
}

/// <summary>Officers' private note about a member, such as how they joined or when to promote them.</summary>
public sealed class MemberNote : BoardItem {
    public string Member { get; set; } = "";
    public string World { get; set; } = "";
    public string Note { get; set; } = "";

    public static Guid IdFor(string member, string world) => StableId($"member|{member.Trim().ToLowerInvariant()}|{world}");
}

/// <summary>Items that went into or out of the chest between two readings.</summary>
public sealed class ChestChange : BoardItem {
    public DateTime SeenUtc { get; set; }
    public uint ItemId { get; set; }

    /// <summary>Positive when items were added, negative when taken.</summary>
    public int Delta { get; set; }

    /// <summary>Who had the chest open when the change was noticed. Not necessarily who made it.</summary>
    public string ReadBy { get; set; } = "";
}

public sealed class LootItem {
    public uint ItemId { get; set; }
    public int Count { get; set; }
    public bool HighQuality { get; set; }
}

/// <summary>What one finished voyage brought back.</summary>
public sealed class VoyageLoot : BoardItem {
    public VesselKind Kind { get; set; }
    public string Vessel { get; set; } = "";
    public DateTime ReturnUtc { get; set; }

    /// <summary>Stops visited, such as "Deep-sea Site 1 → The Ivory Shoals"; empty for airships.</summary>
    public string Route { get; set; } = "";

    public List<LootItem> Items { get; set; } = [];

    /// <summary>One id per voyage, from the vessel and when the voyage was sent.</summary>
    public static Guid IdFor(VesselKind kind, string vessel, DateTime sentUtc) =>
        StableId($"voyage|{kind}|{vessel}|{sentUtc.Ticks}");
}

public sealed class CustomMaterial {
    public string Name { get; set; } = "";
    public int Needed { get; set; }
    public int Have { get; set; }
}
