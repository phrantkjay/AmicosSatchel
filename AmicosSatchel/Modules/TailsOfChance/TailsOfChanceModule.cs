using Dalamud.Interface.Windowing;

namespace AmicosSatchel.Modules.TailsOfChance;

/// <summary>
/// Shows Wondrous Tails line odds inside the journal.
/// </summary>
public sealed class TailsOfChanceModule : Module {
    private WindowSystem? windows;
    private OddsWindow? window;

    public override string Id => "TailsOfChance";
    public override string Name => "Tails of Chance";

    public override string Description =>
        "Shows the exact chance of finishing Wondrous Tails with one, two or three lines, " +
        "right inside the journal, and compares it with what a shuffle would give you.";

    public override string Category => "Wondrous Tails";
    public override string ImageFile => "tails-of-chance.png";

    public override bool EnabledByDefault => true;

    public override void Enable(ModuleContext context) {
        windows = context.Windows;
        window = new OddsWindow(Services.GameGui);
        windows.AddWindow(window);
    }

    public override void Disable() {
        if (window is not null) windows?.RemoveWindow(window);
        window = null;
        windows = null;
    }
}
