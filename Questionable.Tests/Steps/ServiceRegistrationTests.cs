using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Questionable.Controller;
using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class ServiceRegistrationTests
{
    [Theory]
    [InlineData(typeof(IArtisanIpc), typeof(ArtisanIpc))]
    [InlineData(typeof(IGatherChillIpc), typeof(GatherChillIpc))]
    [InlineData(typeof(IKnightshopperIpc), typeof(KnightshopperIpc))]
    [InlineData(typeof(IFeeshIpc), typeof(FeeshIpc))]
    public void InterfaceRegisteredAdapters_HaveNoUnresolvableConcreteConsumers(Type contract, Type implementation)
    {
        IServiceCollection services = new ServiceCollection().AddQuestionable();
        Assert.Contains(services, s => !s.IsKeyedService && s.ServiceType == contract &&
            s.ImplementationType == implementation);

        var consumers = services.Select(s => s.IsKeyedService ? s.KeyedImplementationType : s.ImplementationType)
            .OfType<Type>().Distinct()
            .Where(t => t.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == implementation)))
            .ToArray();

        Assert.True(services.Any(s => !s.IsKeyedService && s.ServiceType == implementation) || consumers.Length == 0,
            $"Unregistered {implementation.Name} requested by: {string.Join(", ", consumers.Select(t => t.FullName))}");
    }

    [Fact]
    public void ContextMenu_ResolvesWithNoCompanionPluginsAndUnavailableGatheringIpc()
    {
        var plugin = Substitute.For<IDalamudPluginInterface>();
        plugin.InstalledPlugins.Returns(Array.Empty<IExposedPlugin>());
        var version = Substitute.For<ICallGateSubscriber<int>>();
        var busy = Substitute.For<ICallGateSubscriber<bool>>();
        var canGather = Substitute.For<ICallGateSubscriber<uint, bool>>();
        version.InvokeFunc().Returns(_ => throw new InvalidOperationException("No IPC provider registered"));
        busy.InvokeFunc().Returns(_ => throw new InvalidOperationException("No IPC provider registered"));
        canGather.InvokeFunc(Arg.Any<uint>()).Returns(_ => throw new InvalidOperationException("No IPC provider registered"));
        plugin.GetIpcSubscriber<int>("GatherChill.ApiVersion").Returns(version);
        plugin.GetIpcSubscriber<bool>("GatherChill.IsBusy").Returns(busy);
        plugin.GetIpcSubscriber<uint, bool>("GatherChill.CanGather").Returns(canGather);

        // Use the generated production descriptors for the failing startup path.
        // Unrelated quest/game services are placeholders; their native initialization is not exercised here.
        IServiceCollection registrations = new ServiceCollection().AddQuestionable();
        IServiceCollection services = new ServiceCollection();
        services.Add(registrations.Single(s => s.ServiceType == typeof(ContextMenuController)));
        services.Add(registrations.Single(s => s.ServiceType == typeof(IGatherChillIpc)));
        services.AddSingleton(plugin);
        services.AddSingleton(Substitute.For<IContextMenu>());
        services.AddSingleton(Substitute.For<IClientState>());
        services.AddSingleton(Substitute.For<ICondition>());
        services.AddSingleton<GatheringOperationState>();
        services.AddLogging();
        foreach (var parameter in typeof(ContextMenuController).GetConstructors().Single().GetParameters())
        {
            Type type = parameter.ParameterType;
            if (type == typeof(GatherChillIpc) || type == typeof(IGatherChillIpc) ||
                services.Any(s => s.ServiceType == type) ||
                type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Logging.ILogger<>))
                continue;
            services.AddSingleton(type, _ => null!);
        }

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        Assert.NotNull(provider.GetRequiredService<ContextMenuController>());
        var gather = provider.GetRequiredService<IGatherChillIpc>();
        Assert.Same(gather, provider.GetRequiredService<IGatherChillIpc>());
        Assert.False(gather.IsReady());
        Assert.False(gather.CanGather(42));
        Assert.Null(gather.GetBusy());
        Assert.Equal(GatherStartResult.NotReady, gather.StartRequest(42, 1, out _));
        Assert.False(provider.GetRequiredService<GatheringOperationState>().HasOperation);
    }
}
