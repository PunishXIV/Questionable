using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.Operations;
using Questionable.External;
using Questionable.Functions;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class ServiceRegistrationTests
{
    [Theory]
    [InlineData(typeof(IQuestGameState), typeof(GameQuestState))]
    [InlineData(typeof(IQuestCatalog), typeof(QuestCatalog))]
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
    public void ContextMenu_ResolvesWithoutGatherChill()
    {
        NoPluginsInstalled.Ensure();
        IServiceCollection registrations = new ServiceCollection().AddQuestionable();
        IServiceCollection services = new ServiceCollection();
        services.Add(registrations.Single(s => s.ServiceType == typeof(ContextMenuController)));
        services.Add(registrations.Single(s => s.ServiceType == typeof(GatherChillIpc)));
        services.AddSingleton(Substitute.For<IContextMenu>());
        services.AddSingleton(Substitute.For<IClientState>());
        services.AddSingleton(Substitute.For<ICondition>());
        services.AddSingleton<GatheringState>();
        services.AddLogging();
        foreach (var parameter in typeof(ContextMenuController).GetConstructors().Single().GetParameters())
        {
            Type type = parameter.ParameterType;
            if (services.Any(s => s.ServiceType == type) ||
                type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Logging.ILogger<>))
                continue;
            services.AddSingleton(type, _ => null!);
        }

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        Assert.NotNull(provider.GetRequiredService<ContextMenuController>());
        var gather = provider.GetRequiredService<GatherChillIpc>();
        Assert.False(gather.IsReady());
        Assert.False(gather.CanGather(42));
        Assert.Null(gather.GetBusy());
        Assert.Equal(GatherStartResult.NotReady, gather.StartRequest(42, 1, out _));
        Assert.False(provider.GetRequiredService<GatheringState>().HasOperation);
    }
}
