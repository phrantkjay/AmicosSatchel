using System;
using System.Collections.Generic;
using AmicosSatchel.Modules.FcCommandCenter;
using AmicosSatchel.Windows;
using Dalamud.Configuration;

namespace AmicosSatchel;

/// <summary>
/// Saved settings for the whole satchel.
/// </summary>
[Serializable]
public sealed class Configuration : IPluginConfiguration {
    public int Version { get; set; } = 0;

    /// <summary>
    /// Whether each module is turned on, by module id. Modules missing from here use their default.
    /// </summary>
    public Dictionary<string, bool> EnabledModules { get; set; } = [];

    /// <summary>
    /// Look of the satchel window.
    /// </summary>
    public SatchelThemeId Theme { get; set; } = SatchelThemeId.CrystalBoard;

    /// <summary>
    /// Where the Amico's Second Sight desktop map is, for the module's "Open Second Sight" button.
    /// Learned automatically the first time the map connects; null until then.
    /// </summary>
    public string? SecondSightAppPath { get; set; }

    /// <summary>Print a chat line (only you see it) when reaching a vista you haven't logged, with the emote to use.</summary>
    public bool SecondSightVistaChat { get; set; } = true;

    /// <summary>
    /// Let the Second Sight map ask for a teleport when you click an aetheryte there and confirm.
    /// Off by default: with it off the module only reads the game.
    /// </summary>
    public bool SecondSightAllowTeleport { get; set; }

    /// <summary>Let the Second Sight map put the game's map flag where you right-click (to share with &lt;flag&gt;).</summary>
    public bool SecondSightAllowFlag { get; set; } = true;

    /// <summary>After setting the flag from the map, put &lt;flag&gt; in the chat box ready to send (never sent for you).</summary>
    public bool SecondSightFlagToChat { get; set; } = true;

    /// <summary>Print a chat line with a clickable map link when you decipher a treasure map.</summary>
    public bool SecondSightTreasureChat { get; set; } = true;

    /// <summary>Draw a ring and label at A Realm Reborn vistas you haven't logged.</summary>
    public bool SecondSightVistaMarkers { get; set; } = true;

    /// <summary>Download new versions of the map in the background and put them in when the map is closed.</summary>
    public bool SecondSightAutoUpdate { get; set; } = true;

    /// <summary>The same, from when the map was called XIV Map. Moved to SecondSightAppPath on load.</summary>
    public string? XivMapAppPath { get; set; }

    /// <summary>Settings for the FC Command Center module. Its boards are saved in their own files.</summary>
    public FcCommandCenterSettings FcCommandCenter { get; set; } = new();

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}
