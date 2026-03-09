using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XADatabase.Services;

/// <summary>
/// Automates opening game windows and navigating UI to collect data that
/// requires manual interaction (saddlebag, FC members, FC housing info).
/// Uses a step-based state machine running on IFramework.Update.
///
/// Node indices for FC addon navigation come from Dalamud's Addon Inspector (/xldata):
///   FreeCompany [8]  → Members tab → opens FreeCompanyMember
///   FreeCompany [4]  → Info tab    → opens FreeCompanyStatus
///   FreeCompanyStatus [12] → Housing search button → opens HousingSignBoard
/// </summary>
public sealed class AutoCollectionService : IDisposable
{
    private readonly ICondition condition;
    private readonly IFramework framework;
    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;
    private readonly Action<string> sendCommand;

    // Step-based execution
    private readonly List<CollectionStep> steps = new();
    private int stepIndex = -1;
    private DateTime stepStart;
    private bool stepActionDone;
    private bool running;
    private Action? onFinished;

    public bool IsRunning => running;
    public string StatusText { get; private set; } = string.Empty;

    private class CollectionStep
    {
        public string Name { get; init; } = string.Empty;
        public Action? OnEnter { get; init; }
        public Func<bool> IsComplete { get; init; } = () => true;
        public float TimeoutSec { get; init; } = 5f;
    }

    public AutoCollectionService(ICondition condition, IFramework framework, IObjectTable objectTable, IPluginLog log, Action<string> sendCommand)
    {
        this.condition = condition;
        this.framework = framework;
        this.objectTable = objectTable;
        this.log = log;
        this.sendCommand = sendCommand;
    }

    /// <summary>Check if the character is in a Free Company (InfoProxy FC Id != 0).</summary>
    public unsafe bool IsInFreeCompany()
    {
        try
        {
            var proxy = InfoProxyFreeCompany.Instance();
            return proxy != null && proxy->Id != 0;
        }
        catch { return false; }
    }

    /// <summary>Check if the character is on their home world (required for FC data).</summary>
    public bool IsOnHomeWorld()
    {
        try
        {
            var localPlayer = objectTable.LocalPlayer;
            if (localPlayer == null) return true; // assume home if can't check
            return localPlayer.CurrentWorld.RowId == localPlayer.HomeWorld.RowId;
        }
        catch { return true; }
    }

    /// <summary>Check if the character is in a normal idle state suitable for UI automation.</summary>
    public bool IsNormalCondition()
    {
        return !condition[ConditionFlag.InCombat]
            && !condition[ConditionFlag.BoundByDuty]
            && !condition[ConditionFlag.WatchingCutscene]
            && !condition[ConditionFlag.OccupiedInCutSceneEvent]
            && !condition[ConditionFlag.Occupied]
            && !condition[ConditionFlag.Occupied30]
            && !condition[ConditionFlag.Occupied33]
            && !condition[ConditionFlag.Occupied38]
            && !condition[ConditionFlag.Occupied39]
            && !condition[ConditionFlag.OccupiedInEvent]
            && !condition[ConditionFlag.OccupiedInQuestEvent]
            && !condition[ConditionFlag.OccupiedSummoningBell]
            && !condition[ConditionFlag.BetweenAreas]
            && !condition[ConditionFlag.BetweenAreas51];
    }

    /// <summary>
    /// Start the automated collection sequence.
    /// Opens saddlebag and/or FC window, navigates tabs, then calls onFinished.
    /// </summary>
    public void StartCollection(bool doSaddlebag, bool doFc, Action? onFinished = null)
    {
        if (running) return;

        this.onFinished = onFinished;
        steps.Clear();

        // ── Saddlebag collection ──
        // No reliable pre-check for unlock status — container reports loaded even when locked.
        // Just attempt to open; if feature is locked the addon won’t appear and steps skip quickly.
        if (doSaddlebag)
        {
            steps.Add(new CollectionStep { Name = "Open Saddlebag", OnEnter = () => OpenAgentWindow(AgentId.InventoryBuddy, "InventoryBuddy"), IsComplete = () => IsAddonReady("InventoryBuddy"), TimeoutSec = 3f });
            steps.Add(new CollectionStep { Name = "Read Saddlebag", IsComplete = () => !IsAddonReady("InventoryBuddy") || DelayComplete(1.0f), TimeoutSec = 2f });
            steps.Add(new CollectionStep { Name = "Close Saddlebag", OnEnter = () => CloseAddon("InventoryBuddy"), IsComplete = () => !IsAddonReady("InventoryBuddy"), TimeoutSec = 3f });
            steps.Add(new CollectionStep { Name = "Saddlebag Cooldown", IsComplete = () => DelayComplete(0.5f), TimeoutSec = 1f });
        }

        // ── FC collection — requires homeworld first, then FC membership ──
        // Check homeworld BEFORE FC membership — InfoProxy is empty when visiting another world.
        if (doFc)
        {
            if (!IsOnHomeWorld())
            {
                log.Information("[XA] AutoCollection: not on home world, skipping FC steps (FC data only available on homeworld).");
            }
            else if (!IsInFreeCompany())
            {
                log.Information("[XA] AutoCollection: character is not in a Free Company, skipping FC steps.");
            }
            else
            {
                // Open FC window via Agent system
                steps.Add(new CollectionStep { Name = "Open FC Window", OnEnter = () => OpenAgentWindow(AgentId.FreeCompany, "FreeCompany"), IsComplete = () => IsAddonReady("FreeCompany"), TimeoutSec = 5f });
                steps.Add(new CollectionStep { Name = "FC Load Delay", IsComplete = () => DelayComplete(1.0f), TimeoutSec = 2f });

                // Navigate to Members tab — try FireCallback + node click, accept addon or 3s delay
                steps.Add(new CollectionStep { Name = "Click Members Tab", OnEnter = () => { FireAddonCallback("FreeCompany", 1); ClickAddonNode("FreeCompany", 8); }, IsComplete = () => IsAddonReady("FreeCompanyMember") || DelayComplete(3.0f), TimeoutSec = 5f });
                steps.Add(new CollectionStep { Name = "Members Load Delay", IsComplete = () => DelayComplete(1.5f), TimeoutSec = 2f });

                // Navigate to Info/Status tab — try FireCallback + node click
                steps.Add(new CollectionStep { Name = "Click Info Tab", OnEnter = () => { FireAddonCallback("FreeCompany", 3); ClickAddonNode("FreeCompany", 4); }, IsComplete = () => IsAddonReady("FreeCompanyStatus") || DelayComplete(3.0f), TimeoutSec = 5f });
                steps.Add(new CollectionStep { Name = "Status Load Delay", IsComplete = () => DelayComplete(1.0f), TimeoutSec = 2f });

                // Click housing search in FreeCompanyStatus (only if the addon actually opened)
                steps.Add(new CollectionStep { Name = "Click Housing Search", OnEnter = () => { if (IsAddonReady("FreeCompanyStatus")) ClickAddonNode("FreeCompanyStatus", 12); }, IsComplete = () => IsAddonReady("HousingSignBoard") || DelayComplete(3.0f), TimeoutSec = 5f });
                steps.Add(new CollectionStep { Name = "Housing Load Delay", IsComplete = () => DelayComplete(1.5f), TimeoutSec = 2f });

                // Close sub-addons first, then main FC window
                steps.Add(new CollectionStep { Name = "Close Sub-Addons", OnEnter = () => { CloseAddon("HousingSignBoard"); CloseAddon("FreeCompanyStatus"); CloseAddon("FreeCompanyMember"); }, IsComplete = () => DelayComplete(0.5f), TimeoutSec = 2f });
                steps.Add(new CollectionStep { Name = "Close FC Window", OnEnter = () => CloseAddon("FreeCompany"), IsComplete = () => !IsAddonReady("FreeCompany"), TimeoutSec = 5f });
                steps.Add(new CollectionStep { Name = "FC Cooldown", IsComplete = () => DelayComplete(0.5f), TimeoutSec = 1f });
            }
        }

        if (steps.Count == 0)
        {
            onFinished?.Invoke();
            return;
        }

        stepIndex = 0;
        stepStart = DateTime.UtcNow;
        stepActionDone = false;
        running = true;
        StatusText = steps[0].Name;
        framework.Update += OnTick;
        log.Information($"[XA] AutoCollection started with {steps.Count} steps.");
    }

    /// <summary>Cancel the running collection sequence.</summary>
    public void Cancel()
    {
        if (!running) return;
        running = false;
        framework.Update -= OnTick;
        stepIndex = -1;
        StatusText = "Cancelled";
        log.Information("[XA] AutoCollection cancelled.");
    }

    private void OnTick(IFramework fw)
    {
        if (!running || stepIndex < 0 || stepIndex >= steps.Count)
        {
            Finish();
            return;
        }

        // Abort if conditions become abnormal (entered combat, duty, etc.)
        if (!IsNormalCondition())
        {
            log.Warning("[XA] AutoCollection: conditions no longer normal, cancelling.");
            Cancel();
            return;
        }

        var step = steps[stepIndex];
        var elapsed = (float)(DateTime.UtcNow - stepStart).TotalSeconds;

        // Run the step's action once
        if (!stepActionDone)
        {
            if (step.OnEnter != null)
            {
                try
                {
                    step.OnEnter();
                }
                catch (Exception ex)
                {
                    log.Error($"[XA] AutoCollection step '{step.Name}' action error: {ex.Message}");
                }
            }
            stepActionDone = true;
        }

        // Check completion
        try
        {
            if (step.IsComplete())
            {
                log.Information($"[XA] AutoCollection step '{step.Name}' completed.");
                AdvanceStep();
                return;
            }
        }
        catch (Exception ex)
        {
            log.Error($"[XA] AutoCollection step '{step.Name}' check error: {ex.Message}");
        }

        // Timeout — skip to next step
        if (elapsed > step.TimeoutSec)
        {
            log.Warning($"[XA] AutoCollection step '{step.Name}' timed out after {step.TimeoutSec}s, skipping.");
            AdvanceStep();
        }
    }

    private void AdvanceStep()
    {
        stepIndex++;
        if (stepIndex >= steps.Count)
        {
            Finish();
            return;
        }
        stepStart = DateTime.UtcNow;
        stepActionDone = false;
        StatusText = steps[stepIndex].Name;
    }

    private void Finish()
    {
        running = false;
        framework.Update -= OnTick;
        stepIndex = -1;
        StatusText = "Complete";
        log.Information("[XA] AutoCollection finished.");
        onFinished?.Invoke();
    }

    // ── Helpers ──

    private bool DelayComplete(float seconds)
    {
        return (float)(DateTime.UtcNow - stepStart).TotalSeconds >= seconds;
    }

    /// <summary>
    /// Open a game window via the Agent system. This is the correct way to open
    /// native game windows like Saddlebag and FC — text commands don't exist for these.
    /// </summary>
    private unsafe void OpenAgentWindow(AgentId agentId, string addonName)
    {
        try
        {
            var agent = AgentModule.Instance()->GetAgentByInternalId(agentId);
            if (agent == null)
            {
                log.Warning($"[XA] OpenAgentWindow: agent {agentId} not found.");
                return;
            }

            if (!agent->IsAgentActive())
            {
                agent->Show();
                log.Information($"[XA] OpenAgentWindow: opened {addonName} via agent {agentId}.");
            }
            else
            {
                log.Debug($"[XA] OpenAgentWindow: agent {agentId} already active.");
            }
        }
        catch (Exception ex)
        {
            log.Error($"[XA] OpenAgentWindow error for {agentId}: {ex.Message}");
        }
    }

    private unsafe AtkUnitBase* GetAddon(string name)
    {
        try
        {
            return AtkStage.Instance()->RaptureAtkUnitManager->GetAddonByName(name);
        }
        catch
        {
            return null;
        }
    }

    private unsafe bool IsAddonReady(string name)
    {
        var addon = GetAddon(name);
        return addon != null && addon->IsVisible;
    }

    private unsafe void CloseAddon(string name)
    {
        var addon = GetAddon(name);
        if (addon != null && addon->IsVisible)
        {
            try { addon->Close(true); }
            catch (Exception ex) { log.Warning($"[XA] CloseAddon '{name}' error: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Click a component node in an addon by its node list index.
    /// Node indices come from Dalamud's Addon Inspector (/xldata).
    /// Fires the first event attached to the node's AtkEventManager.
    /// </summary>
    private unsafe void ClickAddonNode(string addonName, int nodeListIndex)
    {
        var addon = GetAddon(addonName);
        if (addon == null)
        {
            log.Warning($"[XA] ClickAddonNode: addon '{addonName}' not found.");
            return;
        }

        if (!addon->IsVisible || nodeListIndex >= addon->UldManager.NodeListCount)
        {
            log.Warning($"[XA] ClickAddonNode: addon '{addonName}' not visible or index {nodeListIndex} out of range (max {addon->UldManager.NodeListCount}).");
            return;
        }

        var node = addon->UldManager.NodeList[nodeListIndex];
        if (node == null)
        {
            log.Warning($"[XA] ClickAddonNode: node at index {nodeListIndex} is null.");
            return;
        }

        // Try to fire the node's click event via AtkEventManager
        try
        {
            var evt = node->AtkEventManager.Event;
            if (evt != null)
            {
                // Fire ButtonClick event (type 25) using the node's event param
                addon->ReceiveEvent((AtkEventType)25, (int)evt->Param, evt);
                log.Information($"[XA] ClickAddonNode: clicked node {nodeListIndex} in '{addonName}' (param: {evt->Param}).");
                return;
            }
            log.Warning($"[XA] ClickAddonNode: node {nodeListIndex} in '{addonName}' has no events.");
        }
        catch (Exception ex)
        {
            log.Error($"[XA] ClickAddonNode error for '{addonName}' node {nodeListIndex}: {ex.Message}");
        }
    }

    /// <summary>
    /// Fire a callback on an addon with integer values.
    /// More reliable than ReceiveEvent for tab switches — this is how the game
    /// internally processes most UI interactions.
    /// </summary>
    private unsafe void FireAddonCallback(string addonName, params int[] callbackValues)
    {
        var addon = GetAddon(addonName);
        if (addon == null || !addon->IsVisible)
        {
            log.Warning($"[XA] FireAddonCallback: addon '{addonName}' not found or not visible.");
            return;
        }

        try
        {
            AtkValue* atkValues = stackalloc AtkValue[callbackValues.Length];
            for (int i = 0; i < callbackValues.Length; i++)
            {
                atkValues[i].Type = (FFXIVClientStructs.FFXIV.Component.GUI.ValueType)3;
                atkValues[i].Int = callbackValues[i];
            }

            addon->FireCallback((uint)callbackValues.Length, atkValues);
            log.Information($"[XA] FireAddonCallback: fired on '{addonName}' with values [{string.Join(", ", callbackValues)}].");
        }
        catch (Exception ex)
        {
            log.Error($"[XA] FireAddonCallback error for '{addonName}': {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (running)
        {
            running = false;
            framework.Update -= OnTick;
        }
    }
}
