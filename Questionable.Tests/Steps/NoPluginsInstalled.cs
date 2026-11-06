using System.Reflection;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using NSubstitute;

namespace Questionable.Tests.Steps;

// EzIPC binds through ECommons' static Svc, so tests that build plugin classes need it set.
// Every IPC call then fails the way Dalamud reports a plugin that isn't installed.
internal static class NoPluginsInstalled
{
    private static readonly Lazy<bool> Installed = new(() =>
    {
        Set(nameof(Svc.PluginInterface), DispatchProxy.Create<IDalamudPluginInterface, NoPluginsInterface>());
        Set(nameof(Svc.Log), Substitute.For<IPluginLog>());
        return true;
    });

    public static void Ensure() => _ = Installed.Value;

    private static void Set(string member, object value)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        if (typeof(Svc).GetProperty(member, flags) is { } property)
            property.SetValue(null, value);
        else
            typeof(Svc).GetField(member, flags)!.SetValue(null, value);
    }
}

// DispatchProxy subclasses its proxy types, so these can't be sealed.
public class NoPluginsInterface : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Type returnType = targetMethod!.ReturnType;
        if (targetMethod.Name == nameof(IDalamudPluginInterface.GetIpcSubscriber))
            return MissingEndpoint.Create(returnType, (string)args![0]!);
        if (targetMethod.Name == "get_InternalName")
            return "Questionable";
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            return Array.CreateInstance(returnType.GetGenericArguments()[0], 0);
        return returnType.IsValueType && returnType != typeof(void) ? Activator.CreateInstance(returnType) : null;
    }
}

public class MissingEndpoint : DispatchProxy
{
    private string _name = "";

    public static object Create(Type subscriberType, string name)
    {
        object proxy = DispatchProxy.Create(subscriberType, typeof(MissingEndpoint));
        ((MissingEndpoint)proxy)._name = name;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod!.Name switch
    {
        "get_HasAction" or "get_HasFunction" => false,
        "Subscribe" or "Unsubscribe" => null,
        _ => throw new IpcNotReadyError(_name),
    };
}
