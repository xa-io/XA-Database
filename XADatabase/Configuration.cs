using Dalamud.Configuration;
using System;

namespace XADatabase;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool OpenPluginOnLoad { get; set; } = false;

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
