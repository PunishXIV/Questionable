using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Dalamud.Plugin.Services;

namespace LLib;

/// <summary>
/// Originally part of ECommons by NightmareXIV.
///
/// https://github.com/NightmareXIV/ECommons/blob/master/ECommons/Reflection/DalamudReflector.cs
/// </summary>
public sealed class DalamudReflector : IDisposable
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IFramework _framework;
    private readonly IPluginLog _pluginLog;
    private readonly Dictionary<string, IDalamudPlugin> _pluginCache = new();
    private bool _pluginsChanged;

    public DalamudReflector(IDalamudPluginInterface pluginInterface, IFramework framework, IPluginLog pluginLog)
    {
        _pluginInterface = pluginInterface;
        _framework = framework;
        _pluginLog = pluginLog;
        var pm = GetPluginManager();
        pm.GetType().GetEvent("OnInstalledPluginsChanged")!.AddEventHandler(pm, OnInstalledPluginsChanged);

        _framework.Update += FrameworkUpdate;
    }

    public void Dispose()
    {
        _framework.Update -= FrameworkUpdate;

        var pm = GetPluginManager();
        pm.GetType().GetEvent("OnInstalledPluginsChanged")!.RemoveEventHandler(pm, OnInstalledPluginsChanged);
    }

    private void FrameworkUpdate(IFramework framework)
    {
        if (_pluginsChanged)
        {
            _pluginsChanged = false;
            _pluginCache.Clear();
        }
    }

    private object GetPluginManager()
    {
        return _pluginInterface.GetType().Assembly.GetType("Dalamud.Service`1", true)!
            .MakeGenericType(
                _pluginInterface.GetType().Assembly.GetType("Dalamud.Plugin.Internal.PluginManager", true)!)
            .GetMethod("Get")!.Invoke(null, BindingFlags.Default, null, Array.Empty<object>(), null)!;
    }

    public bool TryGetDalamudPlugin(string internalName, [MaybeNullWhen(false)] out IDalamudPlugin instance,
        bool suppressErrors = false,
        bool ignoreCache = false)
    {
        if (!ignoreCache && _pluginCache.TryGetValue(internalName, out instance))
        {
            return true;
        }

        try
        {
            var pluginManager = GetPluginManager();
            var installedPlugins =
                (System.Collections.IList)pluginManager.GetType().GetProperty("InstalledPlugins")!.GetValue(
                    pluginManager)!;

            foreach (var t in installedPlugins)
            {
                if ((string?)t.GetType().GetProperty("Name")!.GetValue(t) == internalName)
                {
                    var type = t.GetType().Name == "LocalDevPlugin" ? t.GetType().BaseType : t.GetType();
                    var plugin = (IDalamudPlugin?)type!
                        .GetField("instance", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(t);
                    if (plugin == null)
                    {
                        if (!suppressErrors)
                            _pluginLog.Warning($"[DalamudReflector] Found requested plugin {internalName} but it was null");
                    }
                    else
                    {
                        instance = plugin;
                        _pluginCache[internalName] = plugin;
                        return true;
                    }
                }
            }

            instance = null;
            return false;
        }
        catch (Exception e)
        {
            if (!suppressErrors)
            {
                _pluginLog.Error(e, $"Can't find {internalName} plugin: {e.Message}");
            }

            instance = null;
            return false;
        }
    }

    private void OnInstalledPluginsChanged()
    {
        _pluginLog.Verbose("Installed plugins changed event fired");
        _pluginsChanged = true;
    }
}
