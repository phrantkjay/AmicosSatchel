using AmicosSatchel.Modules;
using AmicosSatchel.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;

namespace AmicosSatchel;

public sealed class Plugin : IDalamudPlugin {
    private const string Command = "/satchel";

    private readonly WindowSystem windowSystem = new("AmicosSatchel");
    private readonly ModuleManager modules;
    private readonly SatchelWindow satchelWindow;

    public Plugin(IDalamudPluginInterface pluginInterface) {
        pluginInterface.Create<Services>();

        var configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        modules = new ModuleManager(configuration, windowSystem);

        satchelWindow = new SatchelWindow(modules, configuration);
        windowSystem.AddWindow(satchelWindow);

        pluginInterface.UiBuilder.Draw += windowSystem.Draw;
        pluginInterface.UiBuilder.OpenMainUi += ToggleSatchel;
        pluginInterface.UiBuilder.OpenConfigUi += ToggleSatchel;
        Services.Commands.AddHandler(Command, new CommandInfo((_, _) => ToggleSatchel()) {
            HelpMessage = "Open Amico's Satchel to turn its features on or off.",
        });

        modules.StartEnabledModules();
    }

    public void Dispose() {
        Services.Commands.RemoveHandler(Command);
        Services.PluginInterface.UiBuilder.OpenConfigUi -= ToggleSatchel;
        Services.PluginInterface.UiBuilder.OpenMainUi -= ToggleSatchel;
        Services.PluginInterface.UiBuilder.Draw -= windowSystem.Draw;

        modules.Dispose();
        satchelWindow.Dispose();
        windowSystem.RemoveAllWindows();
    }

    private void ToggleSatchel() => satchelWindow.Toggle();
}
