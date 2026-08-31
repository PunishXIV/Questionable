using PunishLib;
using Questionable.AutoGen;
using WrathCombo.API;
using WrathError = WrathCombo.API.WrathIPCWrapper.ErrorType;

namespace Questionable;

public sealed class QuestionablePlugin(
    IDalamudPluginInterface pluginInterface,
    IClientState clientState,
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
    private ServiceProvider? _serviceProvider;
    private bool _ecommonsInitialized;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pluginInterface);
        ArgumentNullException.ThrowIfNull(chatGui);

        try
        {
            // Off the thread-pool thread LoadAsync runs on; Dalamud warns against blocking there.
            _serviceProvider = await Task.Run(BuildAndInitialize, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            chatGui.PrintError(_L("Unable to load plugin, check /xllog for details"), _L("Questionable"));
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var serviceProvider = Interlocked.Exchange(ref _serviceProvider, value: null);

        // Unhook Dalamud event sources (Framework.Update, UiBuilder callbacks, toast hooks, ...)
        // before disposing the container. MS.DI marks the root scope as disposed at the *start* of
        // Dispose/DisposeAsync, so any GetService call after that point throws ObjectDisposedException.
        // Under IAsyncDalamudPlugin.DisposeAsync the framework thread can tick between "scope flagged
        // disposed" and DalamudInitializer being reached in the disposal walk, and other singletons'
        // per-frame container access would blow up. Disposing DalamudInitializer up-front removes the
        // event subscriptions before that window opens; its Dispose is idempotent so MS.DI's later
        // disposal pass is a no-op.
        try
        {
            serviceProvider?.GetService<DalamudInitializer>()?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Container already torn down elsewhere — nothing to unhook.
        }

        if (serviceProvider is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else
            serviceProvider?.Dispose();

        if (_ecommonsInitialized)
        {
            _ecommonsInitialized = false;
            ECommonsMain.Dispose();
        }
    }

    private ServiceProvider BuildAndInitialize()
    {
        ECommonsMain.Init(pluginInterface, this, Module.DalamudReflector);
        _ecommonsInitialized = true;
        WrathIPCWrapper.Init(pluginInterface, WrathError.IPCNotReady | WrathError.Unexpected);
        PunishLibMain.Init(pluginInterface, "Questionable", new AboutPlugin()
        {
            Developer = "alydev",
            Sponsor = "https://ko-fi.com/alydev"
        });

        ServiceCollection serviceCollection = [];
        serviceCollection.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace)
            .ClearProviders()
            .AddDalamudLogger(pluginLog, t => t[(t.LastIndexOf('.') + 1)..]));

        // Dalamud services supplied to the plugin constructor - Injectio can't discover these.
        serviceCollection.AddSingleton<IAsyncDalamudPlugin>(this);
        serviceCollection.AddSingleton(pluginInterface);
        serviceCollection.AddSingleton(clientState);
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
            // Backup config when version changes
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

        var serviceProvider = serviceCollection.BuildServiceProvider();
        Initialize(serviceProvider);
        return serviceProvider;
    }

    // Task factories and executors are now registered in ServiceCollectionExtensions:AddTaskRegistrations

    private static void Initialize(IServiceProvider serviceProvider)
    {
        // Resolve before the registry loads — its constructor discards a bundle left by an older
        // plugin version, so the registry doesn't pick up a stale one.
        PathDataUpdater pathDataUpdater = serviceProvider.GetRequiredService<PathDataUpdater>();
        serviceProvider.GetRequiredService<QuestRegistry>().Reload();
        serviceProvider.GetRequiredService<GatheringPointRegistry>().Reload();
        serviceProvider.GetRequiredService<SinglePlayerDutyConfigComponent>().Reload();
        serviceProvider.GetRequiredService<CommandHandler>();
        serviceProvider.GetRequiredService<ContextMenuController>();
        serviceProvider.GetRequiredService<CraftworksSupplyController>();
        serviceProvider.GetRequiredService<CreditsController>();
        serviceProvider.GetRequiredService<HelpUiController>();
        serviceProvider.GetRequiredService<PointMenuHandler>();
        serviceProvider.GetRequiredService<HousingSelectBlockHandler>();
        serviceProvider.GetRequiredService<YesNoChoiceHandler>();
        serviceProvider.GetRequiredService<DialogueChoiceHandler>();
        serviceProvider.GetRequiredService<ShopController>();
        serviceProvider.GetRequiredService<GrandCompanyExchangeController>();
        serviceProvider.GetRequiredService<ChocoboNamingController>();
        serviceProvider.GetRequiredService<QuestionableIpc>();
        serviceProvider.GetRequiredService<DalamudInitializer>();
        serviceProvider.GetRequiredService<TextAdvanceIpc>();
        serviceProvider.GetRequiredService<YesAlreadyIpc>();

        pathDataUpdater.CheckForUpdates();
    }
}
