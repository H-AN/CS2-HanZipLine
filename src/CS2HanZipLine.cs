using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    Version = "0.2.0",
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
    private const string AdminCommand = "zipline_admin";
    private const string ClearCommand = "zipline_clear";

    private ServiceProvider? _serviceProvider;
    private IDisposable? _configSubscription;
    private ZiplineService? _ziplineService;
    private ZiplineMenuService? _ziplineMenuService;
    private ZiplineMapStorageService? _ziplineMapStorageService;
    private ZiplineConfig _config = new();
    private ZiplineConfig? _pendingConfig;
    private CancellationTokenSource? _mapLoadCancellation;
    private int _mapGeneration;
    private int _mapLoadRequestId;
    private string _currentMapName = string.Empty;
    private bool _reloadMapZiplinesOnRoundStart;
    private bool _initialMapLoadPending = true;

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
        services.AddSingleton<ZiplineMapStorageService>();
        services.AddSingleton<ZiplineMenuService>();

        _serviceProvider = services.BuildServiceProvider();
        var monitor = _serviceProvider.GetRequiredService<IOptionsMonitor<ZiplineConfig>>();
        _config = monitor.CurrentValue.CloneNormalized();
        _ziplineService = _serviceProvider.GetRequiredService<ZiplineService>();
        _ziplineMenuService = _serviceProvider.GetRequiredService<ZiplineMenuService>();
        _ziplineMapStorageService = _serviceProvider.GetRequiredService<ZiplineMapStorageService>();
        _ziplineService.ConfigureRuntime(() => _mapGeneration);
        _ziplineMenuService.ConfigureRuntime(() => _mapGeneration);
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
        Core.Command.RegisterCommand(AdminCommand, HandleAdminCommand, true);
        Core.Command.RegisterCommand(ClearCommand, HandleClearCommand, true);
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
        Core.Command.UnregisterCommand(AdminCommand);
        Core.Command.UnregisterCommand(ClearCommand);

        CancelMapLoad();
        _ziplineService?.ClearAll(Models.ZiplineDetachReason.PluginUnload);
        _ziplineService = null;
        _ziplineMenuService = null;
        _ziplineMapStorageService = null;
        _serviceProvider?.Dispose();
        _serviceProvider = null;
        _pendingConfig = null;
        _config = new ZiplineConfig();
        _currentMapName = string.Empty;
        _reloadMapZiplinesOnRoundStart = false;
        _initialMapLoadPending = true;
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
        _reloadMapZiplinesOnRoundStart = _ziplineService?.ClearEachRoundEnabled == true;
        _ziplineService?.HandleRoundEnd();
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnRoundStart(EventRoundStart @event)
    {
        if (_reloadMapZiplinesOnRoundStart)
        {
            _reloadMapZiplinesOnRoundStart = false;
            QueueMapLoad(_currentMapName);
        }

        return HookResult.Continue;
    }

    private void OnPrecacheResource(IOnPrecacheResourceEvent @event) => _ziplineService?.OnPrecacheResource(@event);

    private void OnClientKeyStateChanged(IOnClientKeyStateChangedEvent @event) => _ziplineService?.HandleKeyStateChanged(@event);

    private void OnClientDisconnected(IOnClientDisconnectedEvent @event) => _ziplineService?.HandleClientDisconnected(@event.PlayerId);

    private void OnMapLoad(IOnMapLoadEvent @event)
    {
        _mapGeneration++;
        _currentMapName = @event.MapName;
        _reloadMapZiplinesOnRoundStart = false;
        _initialMapLoadPending = false;
        QueueMapLoad(_currentMapName);
    }

    private void OnMapUnload(IOnMapUnloadEvent @event)
    {
        _mapGeneration++;
        CancelMapLoad();
        _currentMapName = string.Empty;
        _reloadMapZiplinesOnRoundStart = false;
        _initialMapLoadPending = false;
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

        TryQueueInitialMapLoad();
        _ziplineService?.OnTick();
    }

    private void TryQueueInitialMapLoad()
    {
        if (!_initialMapLoadPending)
        {
            return;
        }

        try
        {
            var mapName = Core.Engine.GlobalVars.MapName.Value;
            if (string.IsNullOrWhiteSpace(mapName))
            {
                return;
            }

            _initialMapLoadPending = false;
            _currentMapName = mapName;
            QueueMapLoad(_currentMapName);
        }
        catch (InvalidOperationException)
        {
            // The engine has not initialized GlobalVars yet. Wait for a later tick or OnMapLoad.
        }
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
            case "admin":
                ExecuteAdmin(context, player);
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

    private void HandleAdminCommand(ICommandContext context)
    {
        if (!TryGetPlayer(context, out var player))
        {
            context.Reply("[Zipline] This command can only be used by an in-game player.");
            return;
        }

        ExecuteAdmin(context, player);
    }

    private void HandleClearCommand(ICommandContext context)
    {
        if (!TryGetPlayer(context, out var player))
        {
            context.Reply("[Zipline] This command can only be used by an in-game player.");
            return;
        }

        if (_ziplineService is null)
        {
            context.Reply(T(player, "Zipline.ErrorServiceUnavailable"));
            return;
        }

        if (!_ziplineService.CanManage(player))
        {
            context.Reply(T(player, "Zipline.ErrorAdminPermission"));
            return;
        }

        var count = _ziplineService.ActivePairCount;
        _ziplineService.ClearAll(Models.ZiplineDetachReason.PairRemoved);
        context.Reply(string.Format(T(player, "Zipline.AdminAllRemoved"), count));
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

    private void ExecuteAdmin(ICommandContext context, IPlayer player)
    {
        if (_ziplineService is null || _ziplineMenuService is null)
        {
            context.Reply(T(player, "Zipline.ErrorServiceUnavailable"));
            return;
        }

        if (!_ziplineService.CanManage(player))
        {
            context.Reply(T(player, "Zipline.ErrorAdminPermission"));
            return;
        }

        _ziplineMenuService.OpenAdminForPlayer(player);
    }

    private void QueueMapLoad(string mapName)
    {
        if (_ziplineMapStorageService is null || string.IsNullOrWhiteSpace(mapName))
        {
            return;
        }

        CancelMapLoad();
        _mapLoadCancellation = new CancellationTokenSource();
        var cancellationToken = _mapLoadCancellation.Token;
        var mapGeneration = _mapGeneration;
        var requestId = ++_mapLoadRequestId;
        _ = LoadMapZiplinesAsync(mapName, mapGeneration, requestId, cancellationToken);
    }

    private async Task LoadMapZiplinesAsync(string mapName, int mapGeneration, int requestId, CancellationToken cancellationToken)
    {
        if (_ziplineMapStorageService is null)
        {
            return;
        }

        ZiplineMapLoadResult result;
        try
        {
            result = await _ziplineMapStorageService.LoadAsync(mapName, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            Core.Logger.LogWarning(exception, "Unexpected failure while loading zipline map data for {MapName}.", mapName);
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        Core.Scheduler.NextTick(() =>
        {
            if (cancellationToken.IsCancellationRequested
                || mapGeneration != _mapGeneration
                || requestId != _mapLoadRequestId
                || !string.Equals(mapName, _currentMapName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (result.Error is not null)
            {
                Core.Logger.LogWarning("Failed to load zipline map file {MapFile}: {Error}", result.Path, result.Error);
                return;
            }

            var restored = _ziplineService?.CreateMapPairs(result.Entries) ?? 0;
            if (result.Found)
            {
                Core.Logger.LogInformation("Loaded {ZiplineCount} saved ziplines from {MapFile}.", restored, result.Path);
            }
        });
    }

    private void CancelMapLoad()
    {
        _mapLoadCancellation?.Cancel();
        _mapLoadCancellation?.Dispose();
        _mapLoadCancellation = null;
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
