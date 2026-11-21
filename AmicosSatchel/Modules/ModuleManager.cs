using System;
using System.Collections.Generic;
using System.Linq;
using AmicosSatchel.Modules.FcCommandCenter;
using AmicosSatchel.Modules.TailsOfChance;
using AmicosSatchel.Modules.SecondSight;
using Dalamud.Interface.Windowing;

namespace AmicosSatchel.Modules;

/// <summary>
/// Owns every module, starts and stops them, and remembers which ones the player turned on.
/// </summary>
public sealed class ModuleManager : IDisposable {
    private readonly Configuration configuration;
    private readonly ModuleContext context;
    private readonly HashSet<Module> running = [];
    private readonly Dictionary<Module, string> errors = [];

    public ModuleManager(Configuration configuration, WindowSystem windows) {
        this.configuration = configuration;
        context = new ModuleContext(windows, configuration);
    }

    /// <summary>
    /// Every module in the satchel, in the order they are listed. Add new modules here.
    /// </summary>
    public IReadOnlyList<Module> Modules { get; } = [
        new TailsOfChanceModule(),
        new SecondSightModule(),
        new FcCommandCenterModule(),
    ];

    public bool IsEnabled(Module module) =>
        configuration.EnabledModules.TryGetValue(module.Id, out var enabled) ? enabled : module.EnabledByDefault;

    public bool IsRunning(Module module) => running.Contains(module);

    /// <summary>
    /// Why the module failed to start, or null if it didn't fail.
    /// </summary>
    public string? ErrorFor(Module module) => errors.GetValueOrDefault(module);

    /// <summary>
    /// Starts every module the player has turned on. Called once when the plugin loads.
    /// </summary>
    public void StartEnabledModules() {
        foreach (var module in Modules.Where(IsEnabled)) Start(module);
    }

    public void SetEnabled(Module module, bool enabled) {
        configuration.EnabledModules[module.Id] = enabled;
        configuration.Save();

        if (enabled) Start(module);
        else Stop(module);
    }

    public void Dispose() {
        foreach (var module in running.ToArray()) Stop(module);
    }

    private void Start(Module module) {
        if (running.Contains(module)) return;

        try {
            module.Enable(context);
            running.Add(module);
            errors.Remove(module);
        } catch (Exception e) {
            Services.Log.Error(e, $"Couldn't start the {module.Name} module.");
            errors[module] = e.Message;
            TryDisable(module);
        }
    }

    private void Stop(Module module) {
        if (!running.Remove(module)) return;
        TryDisable(module);
    }

    private static void TryDisable(Module module) {
        try {
            module.Disable();
        } catch (Exception e) {
            Services.Log.Error(e, $"Couldn't stop the {module.Name} module cleanly.");
        }
    }
}
