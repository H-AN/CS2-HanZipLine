using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using CS2HanZipLine.Services;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;

namespace CS2HanZipLine;

[PluginMetadata(
    Id = "CS2.HanZipLine",
    Version = "0.1.0",
    Name = "CS2-HanZipLine",
    Author = "H-AN",
    Description = "Player-created two-way ziplines for CS2."
)]
public sealed class CS2HanZipLine : BasePlugin
{
    private const string ConfigFileName = "CS2-HanZipLine.jsonc";
    private const string ConfigSectionName = "CS2HanZipLine";
    private const string MainCommand = "zipline";
    private const string CreateCommand = "zipline_create";
    private const string RemoveCommand = "zipline_remove";

    private ServiceProvider? _serviceProvider;
    private IDisposable? _configSubscription;
    private ZiplineService? _ziplineService;
    private ZiplineMenuService? _ziplineMenuService;
    private ZiplineConfig _config = new();
    private ZiplineConfig? _pendingConfig;
    private int _mapGeneration;

    public CS2HanZipLine(ISwiftlyCore core) : base(core)
    {
    }

    public override void Load(bool hotReload)
    {
        Core.Configuration.InitializeJsonWithModel<ZiplineConfig>(ConfigFileName, ConfigSectionName)
            .Configure(builder => builder.AddJsonFile(ConfigFileName, optional: false, reloadOnChange: true));

        var services = new ServiceCollection();
        services.AddSwiftly(Core);
        services.AddOptionsWithValidateOnStart<ZiplineConfig>().BindConfiguration(ConfigSectionName);
        services.AddSingleton<ZiplinePlacementService>();
        services.AddSingleton<ZiplineSoundService>();
        services.AddSingleton<ZiplineEntityService>();
        services.AddSingleton<ZiplineRideService>();
        services.AddSingleton<ZiplineService>();
        services.AddSingleton<ZiplineMenuService>();

        _serviceProvider = services.BuildServiceProvider();
        var monitor = _serviceProvider.GetRequiredService<IOptionsMonitor<ZiplineConfig>>();
        _config = monitor.CurrentValue.CloneNormalized();
        _ziplineService = _serviceProvider.GetRequiredService<ZiplineService>();
        _ziplineMenuService = _serviceProvider.GetRequiredService<ZiplineMenuService>();
        _ziplineService.ConfigureRuntime(() => _mapGeneration);
        _ziplineService.UpdateConfig(_config);
        _configSubscription = monitor.OnChange(config => Interlocked.Exchange(ref _pendingConfig, config.CloneNormalized()));

        Core.Event.OnPrecacheResource += OnPrecacheResource;
        Core.Event.OnClientKeyStateChanged += OnClientKeyStateChanged;
        Core.Event.OnClientDisconnected += OnClientDisconnected;
        Core.Event.OnMapLoad += OnMapLoad;
        Core.Event.OnMapUnload += OnMapUnload;
        Core.Event.OnTick += OnTick;

        Core.Command.RegisterCommand(MainCommand, HandleMainCommand, true);
        Core.Command.RegisterCommand(CreateCommand, HandleCreateCommand, true);
        Core.Command.RegisterCommand(RemoveCommand, HandleRemoveCommand, true);
    }

    public override void Unload()
    {
        _configSubscription?.Dispose();
        _configSubscription = null;

        Core.Event.OnPrecacheResource -= OnPrecacheResource;
        Core.Event.OnClientKeyStateChanged -= OnClientKeyStateChanged;
        Core.Event.OnClientDisconnected -= OnClientDisconnected;
        Core.Event.OnMapLoad -= OnMapLoad;
        Core.Event.OnMapUnload -= OnMapUnload;
        Core.Event.OnTick -= OnTick;

        Core.Command.UnregisterCommand(MainCommand);
        Core.Command.UnregisterCommand(CreateCommand);
        Core.Command.UnregisterCommand(RemoveCommand);

        _ziplineService?.ClearAll(Models.ZiplineDetachReason.PluginUnload);
        _ziplineService = null;
        _ziplineMenuService = null;
        _serviceProvider?.Dispose();
        _serviceProvider = null;
        _pendingConfig = null;
        _config = new ZiplineConfig();
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnPlayerDeath(EventPlayerDeath @event)
    {
        _ziplineService?.HandlePlayerDeath(@event.UserId);
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnRoundEnd(EventRoundEnd @event)
    {
        _ziplineService?.HandleRoundEnd();
        return HookResult.Continue;
    }

    private void OnPrecacheResource(IOnPrecacheResourceEvent @event) => _ziplineService?.OnPrecacheResource(@event);

    private void OnClientKeyStateChanged(IOnClientKeyStateChangedEvent @event) => _ziplineService?.HandleKeyStateChanged(@event);

    private void OnClientDisconnected(IOnClientDisconnectedEvent @event) => _ziplineService?.HandleClientDisconnected(@event.PlayerId);

    private void OnMapLoad(IOnMapLoadEvent @event)
    {
        _mapGeneration++;
    }

    private void OnMapUnload(IOnMapUnloadEvent @event)
    {
        _mapGeneration++;
        _ziplineService?.HandleMapUnload();
    }

    private void OnTick()
    {
        var pendingConfig = Interlocked.Exchange(ref _pendingConfig, null);
        if (pendingConfig is not null)
        {
            _config = pendingConfig;
            _ziplineService?.UpdateConfig(_config);
        }

        _ziplineService?.OnTick();
    }

    private void HandleMainCommand(ICommandContext context)
    {
        if (!TryGetPlayer(context, out var player))
        {
            context.Reply("[Zipline] This command can only be used by an in-game player.");
            return;
        }

        if (context.Args.Length == 0)
        {
            _ziplineMenuService?.OpenForPlayer(player);
            return;
        }

        if (context.Args.Length != 1)
        {
            context.Reply(T(player, "Zipline.CommandUsage"));
            return;
        }

        switch (context.Args[0].Trim().ToLowerInvariant())
        {
            case "create":
            case "build":
                ExecuteCreate(context, player);
                break;
            case "remove":
            case "delete":
                ExecuteRemove(context, player);
                break;
            default:
                context.Reply(T(player, "Zipline.CommandUsage"));
                break;
        }
    }

    private void HandleCreateCommand(ICommandContext context)
    {
        if (!TryGetPlayer(context, out var player))
        {
            context.Reply("[Zipline] This command can only be used by an in-game player.");
            return;
        }

        ExecuteCreate(context, player);
    }

    private void HandleRemoveCommand(ICommandContext context)
    {
        if (!TryGetPlayer(context, out var player))
        {
            context.Reply("[Zipline] This command can only be used by an in-game player.");
            return;
        }

        ExecuteRemove(context, player);
    }

    private void ExecuteCreate(ICommandContext context, IPlayer player)
    {
        if (_ziplineService is null)
        {
            context.Reply(T(player, "Zipline.ErrorServiceUnavailable"));
            return;
        }

        _ziplineService.TryCreateForPlayer(player, out var message);
        context.Reply(T(player, message));
    }

    private void ExecuteRemove(ICommandContext context, IPlayer player)
    {
        if (_ziplineService is null)
        {
            context.Reply(T(player, "Zipline.ErrorServiceUnavailable"));
            return;
        }

        _ziplineService.TryDeleteOwnedAtAim(player, out var message);
        context.Reply(T(player, message));
    }

    private static bool TryGetPlayer(ICommandContext context, out IPlayer player)
    {
        player = null!;
        if (context.Sender is not { IsValid: true, IsFakeClient: false } sender)
        {
            return false;
        }

        player = sender;
        return true;
    }

    private string T(IPlayer player, string key) => Core.Translation.GetPlayerLocalizer(player)[key];
}
