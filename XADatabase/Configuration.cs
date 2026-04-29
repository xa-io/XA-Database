using Dalamud.Configuration;
using System;

namespace XADatabase;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool OpenPluginOnLoad { get; set; } = false;
    public bool ShowVersionInWindowTitle { get; set; } = true;
    public bool ShowVersionInWindowTitleDefaultApplied { get; set; } = false;
    public bool SearchHoverTooltipEnabled { get; set; } = true;
    public int SearchHoverTooltipCharacterLimit { get; set; } = 3;
    public bool SearchItemContextMenuEnabled { get; set; } = true;

    // Auto-save interval in minutes (0 = disabled, only manual/login/logout saves)
    public int AutoSaveIntervalMinutes { get; set; } = 0;

    // Addon watcher: auto-save when game windows close (inventory, retainer, saddlebag, market)
    public bool AddonWatcherEnabled { get; set; } = true;

    // Echo notification: show "/echo XA Database has been saved" in chat on save
    public bool EchoOnSave { get; set; } = false;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
