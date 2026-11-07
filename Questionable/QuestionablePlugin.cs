using PunishLib;
using Questionable.AutoGen;
using WrathCombo.API;

namespace Questionable;

public sealed class QuestionablePlugin(
    IDalamudPluginInterface pluginInterface,
    IClientState clientState,
    IDutyState dutyState,
    ITargetManager targetManager,
    IFramework framework,
    IGameGui gameGui,
    IDataManager dataManager,
    ISigScanner sigScanner,
    IObjectTable objectTable,
    IPluginLog pluginLog,
    ICondition condition,
    IChatGui chatGui,
    ICommandManager commandManager,
    IAddonLifecycle addonLifecycle,
    IKeyState keyState,
    IContextMenu contextMenu,
    IToastGui toastGui,
    IGameInteropProvider gameInteropProvider,
    ITextureProvider textureProvider) : IAsyncDalamudPlugin
{
    private readonly PluginLifetime _lifetime = new();
#pragma warning disable CA2213 // These captured references are owned and disposed by PluginLifetime's DI container.
    private DalamudInitializer? _initializer;
    private QuestController? _questController;
    private PathDataUpdater? _pathDataUpdater;
#pragma warning restore CA2213
    private Configuration? _runtimeConfiguration;
    private bool _ecommonsInitialized;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pluginInterface);
        ArgumentNullException.ThrowIfNull(chatGui);

        try
        {
            // Off the thread-pool thread LoadAsync runs on; Dalamud warns against blocking there.
            await _lifetime.InitializeAsync(
                () => Task.Run(BuildAndInitialize, cancellationToken), StopEventSourcesAsync, ReleaseGlobals,
                e => pluginLog.Error(e, "Unable to finish plugin cleanup"), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            try
            {
                chatGui.PrintError(_L("Unable to load plugin, check /xllog for details"), _L("Questionable"));
            }
            catch
            {
                // Reporting a load failure must not replace its original exception.
            }
            throw;
        }
    }

    public ValueTask DisposeAsync() => _lifetime.DisposeAsync();

    private async ValueTask StopEventSourcesAsync()
    {
        var initializer = Interlocked.Exchange(ref _initializer, null);
        var questController = Interlocked.Exchange(ref _questController, null);
        try
        {
            // Stop background mutations before the container starts disposing their dependencies.
#pragma warning disable CA1849 // Cancel now; the DI container awaits updater disposal after event sources are stopped.
            Interlocked.Exchange(ref _pathDataUpdater, null)?.Dispose();
#pragma warning restore CA1849
        }
        finally
        {
            if (!framework.IsFrameworkUnloading && (initializer != null || questController != null))
            {
                try
                {
                    await framework.RunOnFrameworkThread(() =>
                    {
                        try
                        {
                            initializer?.Dispose();
                        }
                        finally
                        {
                            questController?.Shutdown();
                        }
                    }).ConfigureAwait(false);
                }
                catch
                {
                    // Dispatch can fail during framework shutdown; still detach the event sources.
                    initializer?.Dispose();
#pragma warning disable CA2219 // PluginLifetime logs this failure and still runs container/global cleanup.
                    throw;
#pragma warning restore CA2219
                }
            }
            else
                initializer?.Dispose();
        }
    }

    private void ReleaseGlobals()
    {
        try
        {
            if (Interlocked.Exchange(ref _runtimeConfiguration, null) is { } configuration)
                Configuration.ClearRuntime(configuration);
        }
        finally
        {
            if (_ecommonsInitialized)
            {
                _ecommonsInitialized = false;
                ECommonsMain.Dispose();
            }
        }
    }

    private void BuildAndInitialize()
    {
        ECommonsMain.Init(pluginInterface, this, Module.DalamudReflector);
        _ecommonsInitialized = true;
        WrathIPCWrapper.Init(pluginInterface);
        PunishLibMain.Init(pluginInterface, "Questionable", new AboutPlugin()
        {
            Developer = "alydev",
            Sponsor = "https://ko-fi.com/alydev"
        });

        HashSet<string> namespaces = typeof(QuestionablePlugin).Assembly.GetTypes()
            .Select(x => x.Namespace).OfType<string>().ToHashSet();
        ServiceCollection serviceCollection = [];
        serviceCollection.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace)
            .ClearProviders()
            .AddDalamudLogger(pluginLog, t => LogCategory(t, namespaces)));

        // Dalamud services supplied to the plugin constructor - Injectio can't discover these.
        serviceCollection.AddSingleton<IAsyncDalamudPlugin>(this);
        serviceCollection.AddSingleton(pluginInterface);
        serviceCollection.AddSingleton(clientState);
        serviceCollection.AddSingleton(dutyState);
        serviceCollection.AddSingleton(targetManager);
        serviceCollection.AddSingleton(framework);
        serviceCollection.AddSingleton(gameGui);
        serviceCollection.AddSingleton(dataManager);
        serviceCollection.AddSingleton(sigScanner);
        serviceCollection.AddSingleton(objectTable);
        serviceCollection.AddSingleton(pluginLog);
        serviceCollection.AddSingleton(condition);
        serviceCollection.AddSingleton(chatGui);
        serviceCollection.AddSingleton(commandManager);
        serviceCollection.AddSingleton(addonLifecycle);
        serviceCollection.AddSingleton(keyState);
        serviceCollection.AddSingleton(contextMenu);
        serviceCollection.AddSingleton(toastGui);
        serviceCollection.AddSingleton(gameInteropProvider);
        serviceCollection.AddSingleton(textureProvider);
        serviceCollection.AddSingleton(new WindowSystem(nameof(Questionable)));

        var savedConfig = (Configuration?)pluginInterface.GetPluginConfig();
        if (savedConfig != null && savedConfig.Version != Configuration.PluginConfigVersion)
        {
            pluginInterface.ConfigFile.CopyTo(Path.ChangeExtension(pluginInterface.ConfigFile.FullName, ".json.bak"), overwrite: true);
            savedConfig.Version = Configuration.PluginConfigVersion;
        }

        var configuration = savedConfig ?? new Configuration();
        if (!configuration.AutoRedeemOffResetApplied)
        {
            configuration.ApplyAutoRedeemRewardItemsInitialReset();
            configuration.AutoRedeemOffResetApplied = true;
            pluginInterface.SavePluginConfig(configuration);
        }

        serviceCollection.AddSingleton(configuration);
        _runtimeConfiguration = configuration;
        Configuration.InitializeRuntime(configuration);
        Questionable.Utils.LocalizeShortcut.Initialize(configuration, dataManager, clientState);
        Windows.Common.Ui.QstTheme.Initialize(configuration);

        // Injectio-discovered registrations: every class carrying [RegisterSingleton]/[RegisterTransient],
        // and the [RegisterServices] task-registration module in ServiceCollectionExtensions.
        serviceCollection.AddQuestionable();

        // Questpath auto-generation (Questionable/AutoGen): reads game data through Dalamud's Lumina
        // instance, which QuestGameData borrows without disposing.
        serviceCollection.AddSingleton(sp =>
            new QuestGameData(sp.GetRequiredService<IDataManager>().GameData));

        // Same-instance forwards: AetheryteData and JsonSchemaValidator each satisfy an additional
        // service registration that must resolve to the singleton already registered above, not to
        // a second independently-constructed instance.
        serviceCollection.AddSingleton<IAetheryteTerritoryProvider>(sp => sp.GetRequiredService<AetheryteData>());
        serviceCollection.AddSingleton<IQuestValidator>(sp => sp.GetRequiredService<JsonSchemaValidator>());

        // Breaks the QuestController <-> MovementController ctor cycle without handing MovementController
        // the whole IServiceProvider. Once .Value is evaluated the container isn't touched again, so a
        // framework tick during shutdown can't hit a disposed scope through this path.
        serviceCollection.AddSingleton(sp => new Lazy<QuestController>(sp.GetRequiredService<QuestController>));
        // Same for MovementController <-> operation providers (combat cleanup needs CombatController, which moves).
        serviceCollection.AddSingleton(sp =>
            new Lazy<OperationCoordinator>(sp.GetRequiredService<OperationCoordinator>));

        var serviceProvider = serviceCollection.BuildServiceProvider();
        _lifetime.OwnServices(serviceProvider);
        Initialize(serviceProvider);
    }


    private void Initialize(IServiceProvider serviceProvider)
    {
        // Resolve before the registry loads — its constructor discards a bundle left by an older
        // plugin version, so the registry doesn't pick up a stale one.
        _pathDataUpdater = serviceProvider.GetRequiredService<PathDataUpdater>();
        serviceProvider.GetRequiredService<QuestRegistry>().Reload();
        serviceProvider.GetRequiredService<SinglePlayerDutyConfigComponent>().Reload();
        serviceProvider.GetRequiredService<ProfileController>();
        _questController = serviceProvider.GetRequiredService<QuestController>();
        // MovementController.Dispose reads this; resolve it now so unloading never reaches into a disposed container.
        _ = serviceProvider.GetRequiredService<Lazy<OperationCoordinator>>().Value;
        serviceProvider.GetRequiredService<CommandHandler>();
        serviceProvider.GetRequiredService<ContextMenuController>();
        serviceProvider.GetRequiredService<CraftworksSupplyController>();
        serviceProvider.GetRequiredService<CreditsController>();
        serviceProvider.GetRequiredService<HelpUiController>();
        serviceProvider.GetRequiredService<PointMenuHandler>();
        serviceProvider.GetRequiredService<HousingSelectBlockHandler>();
        serviceProvider.GetRequiredService<YesNoChoiceHandler>();
        serviceProvider.GetRequiredService<DialogueChoiceHandler>();
        serviceProvider.GetRequiredService<GrandCompanyExchangeController>();
        serviceProvider.GetRequiredService<ChocoboNamingController>();
        serviceProvider.GetRequiredService<QuestionableIpc>();
        _initializer = serviceProvider.GetRequiredService<DalamudInitializer>();
        serviceProvider.GetRequiredService<TextAdvanceIpc>();
        serviceProvider.GetRequiredService<YesAlreadyIpc>();

        _pathDataUpdater.CheckForUpdates();
    }

    // Categories use dots for nested types too, so cut only the namespace: Craft.Executor, not Executor.
    private static string LogCategory(string category, HashSet<string> namespaces)
    {
        for (int i = category.LastIndexOf('.'); i > 0; i = category.LastIndexOf('.', i - 1))
        {
            if (namespaces.Contains(category[..i]))
                return category[(i + 1)..];
        }

        return category[(category.LastIndexOf('.') + 1)..];
    }
}
