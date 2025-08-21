using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Questionable.Controller.Steps;

namespace Questionable;

internal static class ServiceCollectionExtensions
{
    public static void AddTaskFactory<
        [MeansImplicitUse(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
        TFactory>(
        this IServiceCollection serviceCollection)
        where TFactory : class, ITaskFactory
    {
        serviceCollection.AddSingleton<ITaskFactory, TFactory>();
        serviceCollection.AddSingleton<TFactory>();
    }

    public static void AddTaskExecutor<T,
        [MeansImplicitUse(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
        TExecutor>(
        this IServiceCollection serviceCollection)
        where T : class, ITask
        where TExecutor : TaskExecutor<T>
    {
        serviceCollection.AddKeyedTransient<ITaskExecutor, TExecutor>(typeof(T));
        serviceCollection.AddTransient<TExecutor>();
    }

    public static void AddTaskFactoryAndExecutor<T,
        [MeansImplicitUse(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
        TFactory,
        [MeansImplicitUse(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
        TExecutor>(
        this IServiceCollection serviceCollection)
        where TFactory : class, ITaskFactory
        where T : class, ITask
        where TExecutor : TaskExecutor<T>
    {
        serviceCollection.AddTaskFactory<TFactory>();
        serviceCollection.AddTaskExecutor<T, TExecutor>();
    }
}
