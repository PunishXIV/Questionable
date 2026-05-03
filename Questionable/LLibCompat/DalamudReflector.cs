using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
namespace LLib;

public sealed class DalamudReflector : IDisposable
{
    private readonly IFramework _framework;
    private readonly Dictionary<string, IDalamudPlugin> _pluginCache = new();
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IPluginLog _pluginLog;
    private bool _pluginsChanged;

    public DalamudReflector(IDalamudPluginInterface pluginInterface, IFramework framework, IPluginLog pluginLog)
    {
        _pluginInterface = pluginInterface;
        _framework = framework;
        _pluginLog = pluginLog;
        object pm = GetPluginManager();
        pm.GetType().GetEvent("OnInstalledPluginsChanged")!.AddEventHandler(pm, OnInstalledPluginsChanged);

        _framework.Update += FrameworkUpdate;
    }

    public void Dispose()
    {
        _framework.Update -= FrameworkUpdate;

        object pm = GetPluginManager();
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
            object pluginManager = GetPluginManager();
            IList installedPlugins =
                (IList)pluginManager.GetType().GetProperty("InstalledPlugins")!.GetValue(
                    pluginManager)!;

            foreach(object? t in installedPlugins)
            {
                if ((string?)t.GetType().GetProperty("Name")!.GetValue(t) == internalName)
                {
                    Type? type = t.GetType().Name == "LocalDevPlugin" ? t.GetType().BaseType : t.GetType();
                    IDalamudPlugin? plugin = (IDalamudPlugin?)type!
                        .GetField("instance", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(t);
                    if (plugin == null)
                    {
                        if (!suppressErrors)
                        {
                            _pluginLog.Warning($"[DalamudReflector] Found requested plugin {internalName} but it was null");
                        }
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
        catch(Exception e)
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
