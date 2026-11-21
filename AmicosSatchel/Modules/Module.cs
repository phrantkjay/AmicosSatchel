using Dalamud.Interface.Windowing;

namespace AmicosSatchel.Modules;

/// <summary>
/// What the satchel gives a module while it is running.
/// </summary>
/// <param name="Windows">Window system the module adds its windows to.</param>
/// <param name="Configuration">Saved settings; a module keeps its own settings here and calls Save.</param>
public sealed record ModuleContext(WindowSystem Windows, Configuration Configuration);

/// <summary>
/// One feature in the satchel that the player can turn on and off.
/// </summary>
/// <remarks>
/// To add a module: create a class that derives from this one, then add it to
/// <see cref="ModuleManager.Modules"/>.
/// </remarks>
public abstract class Module {
    /// <summary>Stable id used to save whether the module is on. Never change it once released.</summary>
    public abstract string Id { get; }

    /// <summary>Name shown in the satchel.</summary>
    public abstract string Name { get; }

    /// <summary>One or two sentences shown under the name.</summary>
    public abstract string Description { get; }

    /// <summary>Short tag shown beside the name, such as the game content it relates to.</summary>
    public virtual string? Category => null;

    /// <summary>
    /// Image shown beside the name, as a file name in <c>images/modules/</c> (for example "tails-of-chance.png").
    /// Use a square 128×128 PNG: larger images lose detail when the window shrinks them. Null shows no image.
    /// </summary>
    public virtual string? ImageFile => null;

    /// <summary>Whether the module starts turned on for someone who has never changed it.</summary>
    public virtual bool EnabledByDefault => false;

    /// <summary>Whether the module draws a settings section in the satchel.</summary>
    public virtual bool HasSettings => false;

    /// <summary>Starts the module: add windows, hook events, and so on.</summary>
    public abstract void Enable(ModuleContext context);

    /// <summary>Stops the module and undoes everything <see cref="Enable"/> did.</summary>
    public abstract void Disable();

    /// <summary>Draws the module's settings. Only called while the module is running.</summary>
    public virtual void DrawSettings() { }
}
