using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using XADatabase.Core.Policies;

namespace XADatabase.Services;

public sealed class AutoRetainerCharacterVisibilityService : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private const string OfflineCharacterDataChannel = "AutoRetainer.GetOfflineCharacterData";
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICallGateSubscriber<List<ulong>> registeredCharacters;
    private readonly IPluginLog log;
    private object? offlineCharacterDataSubscriber;
    private MethodInfo? offlineCharacterDataInvokeFunc;
    private AutoRetainerCharacterVisibilitySnapshot snapshot = AutoRetainerCharacterVisibilitySnapshot.FailOpen();
    private DateTime nextRefreshUtc = DateTime.MinValue;
    private bool enabled;
    private bool failureLogged;
    private bool disposed;

    public AutoRetainerCharacterVisibilityService(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        registeredCharacters = pluginInterface.GetIpcSubscriber<List<ulong>>("AutoRetainer.GetRegisteredCIDs");
        this.log = log;
    }

    public bool IsFiltering => enabled && snapshot.IsAuthoritative;
    public string StatusText { get; private set; } = "Disabled - all XA Database characters are visible.";

    public bool IsVisible(ulong contentId)
        => !enabled || snapshot.IsVisible(contentId);

    public void RequestRefresh()
        => nextRefreshUtc = DateTime.MinValue;

    public bool Update(bool shouldEnable, DateTime nowUtc)
    {
        if (disposed)
            return false;

        if (!shouldEnable)
        {
            var visibilityChanged = enabled && snapshot.IsAuthoritative;
            enabled = false;
            snapshot = AutoRetainerCharacterVisibilitySnapshot.FailOpen();
            nextRefreshUtc = DateTime.MinValue;
            failureLogged = false;
            StatusText = "Disabled - all XA Database characters are visible.";
            return visibilityChanged;
        }

        if (!enabled)
        {
            enabled = true;
            nextRefreshUtc = DateTime.MinValue;
        }

        if (nowUtc < nextRefreshUtc)
            return false;

        nextRefreshUtc = nowUtc.Add(RefreshInterval);
        var previous = snapshot;
        try
        {
            var registered = registeredCharacters.InvokeFunc();
            if (!AutoRetainerCharacterVisibilitySnapshot.TryCreate(
                    registered,
                    InvokeOfflineCharacterData,
                    out var refreshed))
            {
                throw new InvalidOperationException("AutoRetainer returned incomplete or incompatible character data.");
            }

            snapshot = refreshed;
            failureLogged = false;
            StatusText = $"Active - {snapshot.VisibleCount} visible, {snapshot.ExcludedCount} excluded by AutoRetainer.";
        }
        catch (Exception ex)
        {
            ResetOfflineCharacterDataSubscriber();
            snapshot = AutoRetainerCharacterVisibilitySnapshot.FailOpen();
            StatusText = "Unavailable - showing all characters until AutoRetainer IPC is ready.";
            if (!failureLogged)
            {
                log.Warning(ex, "[XA] Honor AutoRetainer Exclusions could not refresh; failing open and showing all characters.");
                failureLogged = true;
            }
        }

        return !snapshot.HasSameVisibilityAs(previous);
    }

    private object? InvokeOfflineCharacterData(ulong contentId)
    {
        EnsureOfflineCharacterDataSubscriber();

        try
        {
            return offlineCharacterDataInvokeFunc!.Invoke(offlineCharacterDataSubscriber, new object[] { contentId });
        }
        catch (TargetInvocationException ex)
        {
            throw new InvalidOperationException(
                "AutoRetainer offline-character IPC invocation failed.",
                ex.InnerException ?? ex);
        }
    }

    private void EnsureOfflineCharacterDataSubscriber()
    {
        if (offlineCharacterDataSubscriber != null && offlineCharacterDataInvokeFunc != null)
            return;

        var offlineCharacterDataType = AutoRetainerIpcContract.FindOfflineCharacterDataType(
            AppDomain.CurrentDomain.GetAssemblies());
        if (offlineCharacterDataType == null)
        {
            throw new InvalidOperationException(
                $"AutoRetainer IPC type {AutoRetainerIpcContract.OfflineCharacterDataTypeName} is not loaded or is incompatible.");
        }

        var subscriberFactory = typeof(IDalamudPluginInterface)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(method =>
                method.Name == nameof(IDalamudPluginInterface.GetIpcSubscriber)
                && method.IsGenericMethodDefinition
                && method.GetGenericArguments().Length == 2
                && method.GetParameters() is [{ ParameterType: var parameterType }]
                && parameterType == typeof(string));
        if (subscriberFactory == null)
            throw new InvalidOperationException("Dalamud's two-type IPC subscriber factory is unavailable.");

        var closedFactory = subscriberFactory.MakeGenericMethod(typeof(ulong), offlineCharacterDataType);
        var subscriber = closedFactory.Invoke(pluginInterface, new object[] { OfflineCharacterDataChannel });
        if (subscriber == null)
            throw new InvalidOperationException("Dalamud did not create the AutoRetainer offline-character subscriber.");

        var invokeFunc = closedFactory.ReturnType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(method =>
                method.Name == "InvokeFunc"
                && method.GetParameters() is [{ ParameterType: var parameterType }]
                && parameterType == typeof(ulong));
        if (invokeFunc == null)
            throw new InvalidOperationException("AutoRetainer's offline-character subscriber has no compatible InvokeFunc method.");

        offlineCharacterDataSubscriber = subscriber;
        offlineCharacterDataInvokeFunc = invokeFunc;
    }

    private void ResetOfflineCharacterDataSubscriber()
    {
        offlineCharacterDataSubscriber = null;
        offlineCharacterDataInvokeFunc = null;
    }

    public void Dispose()
    {
        disposed = true;
        enabled = false;
        ResetOfflineCharacterDataSubscriber();
        snapshot = AutoRetainerCharacterVisibilitySnapshot.FailOpen();
        StatusText = "Disposed";
    }
}
