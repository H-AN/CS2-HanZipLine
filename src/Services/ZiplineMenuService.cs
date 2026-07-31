using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace CS2HanZipLine.Services;

public sealed class ZiplineMenuService(
    ISwiftlyCore core,
    ZiplineService ziplines,
    ZiplineMapStorageService mapStorage)
{
    private readonly ISwiftlyCore _core = core;
    private readonly ZiplineService _ziplines = ziplines;
    private readonly ZiplineMapStorageService _mapStorage = mapStorage;
    private Func<int>? _mapGenerationResolver;

    public void ConfigureRuntime(Func<int> mapGenerationResolver) => _mapGenerationResolver = mapGenerationResolver;

    public void OpenForPlayer(IPlayer player)
    {
        if (player is null || !player.IsValid || player.IsFakeClient)
        {
            return;
        }

        var menu = _core.MenusAPI.CreateBuilder()
            .Design.SetMenuTitle(T(player, "Zipline.MenuTitle"))
            .EnableSound()
            .SetPlayerFrozen(false)
            .Build();

        menu.AddOption(CreateScheduledButton(player, "Zipline.MenuCreate", MenuAction.Create));
        menu.AddOption(CreateScheduledButton(player, "Zipline.MenuRemove", MenuAction.RemoveOwned));

        if (_ziplines.CanManage(player))
        {
            var adminButton = new ButtonMenuOption(T(player, "Zipline.MenuAdmin")) { CloseAfterClick = true };
            adminButton.Click += async (_, args) =>
            {
                if (args.Player is { IsValid: true, IsFakeClient: false } clicker)
                {
                    ScheduleOpenAdminMenu(clicker.SessionId);
                }

                await Task.CompletedTask;
            };
            menu.AddOption(adminButton);
        }

        _core.MenusAPI.OpenMenuForPlayer(player, menu);
    }

    public void OpenAdminForPlayer(IPlayer player)
    {
        if (!_ziplines.CanManage(player))
        {
            return;
        }

        var menu = _core.MenusAPI.CreateBuilder()
            .Design.SetMenuTitle(T(player, "Zipline.AdminMenuTitle"))
            .EnableSound()
            .SetPlayerFrozen(false)
            .Build();

        menu.AddOption(CreateScheduledButton(player, "Zipline.AdminCreate", MenuAction.CreateAdmin));
        menu.AddOption(CreateScheduledButton(player, "Zipline.AdminRemove", MenuAction.RemoveAny));

        var saveButton = new ButtonMenuOption(T(player, "Zipline.AdminSave")) { CloseAfterClick = true };
        saveButton.Click += async (_, args) =>
        {
            if (args.Player is { IsValid: true, IsFakeClient: false } clicker)
            {
                await SaveMapAsync(clicker.SessionId);
            }
        };
        menu.AddOption(saveButton);

        var reloadButton = new ButtonMenuOption(T(player, "Zipline.AdminReload")) { CloseAfterClick = true };
        reloadButton.Click += async (_, args) =>
        {
            if (args.Player is { IsValid: true, IsFakeClient: false } clicker)
            {
                await ReloadMapAsync(clicker.SessionId);
            }
        };
        menu.AddOption(reloadButton);

        menu.AddOption(CreateScheduledButton(player, "Zipline.AdminClear", MenuAction.ClearAll));

        var visionKey = _ziplines.AdminVisionEnabled
            ? "Zipline.AdminVisionDisable"
            : "Zipline.AdminVisionEnable";
        menu.AddOption(CreateScheduledButton(player, visionKey, MenuAction.ToggleVision));

        _core.MenusAPI.OpenMenuForPlayer(player, menu);
    }

    private ButtonMenuOption CreateScheduledButton(IPlayer player, string translationKey, MenuAction action)
    {
        var button = new ButtonMenuOption(T(player, translationKey)) { CloseAfterClick = true };
        button.Click += async (_, args) =>
        {
            if (args.Player is { IsValid: true, IsFakeClient: false } clicker)
            {
                ScheduleAction(clicker.SessionId, action);
            }

            await Task.CompletedTask;
        };
        return button;
    }

    private async Task SaveMapAsync(ulong sessionId)
    {
        if (!TryCaptureAdminMapRequest(sessionId, out var player, out var mapName, out var mapGeneration))
        {
            return;
        }

        var entries = _ziplines.GetMapEntries();
        var result = await _mapStorage.SaveAsync(mapName, entries, CancellationToken.None);
        _core.Scheduler.NextTick(() =>
        {
            if (!IsCurrentMapRequest(mapName, mapGeneration))
            {
                return;
            }

            var currentPlayer = GetPlayer(sessionId);
            if (currentPlayer is null || !_ziplines.CanManage(currentPlayer))
            {
                return;
            }

            var message = result.Success
                ? Format(currentPlayer, "Zipline.AdminMapSaved", entries.Count)
                : T(currentPlayer, "Zipline.AdminMapSaveFailed");
            currentPlayer.SendMessage(MessageType.Chat, message);
        });
    }

    private async Task ReloadMapAsync(ulong sessionId)
    {
        if (!TryCaptureAdminMapRequest(sessionId, out var player, out var mapName, out var mapGeneration))
        {
            return;
        }

        var result = await _mapStorage.LoadAsync(mapName, CancellationToken.None);
        _core.Scheduler.NextTick(() =>
        {
            if (!IsCurrentMapRequest(mapName, mapGeneration))
            {
                return;
            }

            var currentPlayer = GetPlayer(sessionId);
            if (currentPlayer is null || !_ziplines.CanManage(currentPlayer))
            {
                return;
            }

            if (result.Error is not null)
            {
                currentPlayer.SendMessage(MessageType.Chat, T(currentPlayer, "Zipline.AdminMapLoadFailed"));
                return;
            }

            _ziplines.ClearAll(Models.ZiplineDetachReason.PairRemoved);
            var restored = _ziplines.CreateMapPairs(result.Entries);
            currentPlayer.SendMessage(MessageType.Chat, Format(currentPlayer, "Zipline.AdminMapReloaded", restored));
        });
    }

    private bool TryCaptureAdminMapRequest(
        ulong sessionId,
        out IPlayer player,
        out string mapName,
        out int mapGeneration)
    {
        player = GetPlayer(sessionId)!;
        mapName = GetCurrentMapName();
        mapGeneration = _mapGenerationResolver?.Invoke() ?? 0;
        return player is not null
            && _ziplines.CanManage(player)
            && !string.IsNullOrWhiteSpace(mapName);
    }

    private void ScheduleOpenAdminMenu(ulong sessionId)
    {
        _core.Scheduler.NextTick(() =>
        {
            var player = GetPlayer(sessionId);
            if (player is not null)
            {
                OpenAdminForPlayer(player);
            }
        });
    }

    private void ScheduleAction(ulong sessionId, MenuAction action)
    {
        _core.Scheduler.NextTick(() =>
        {
            var player = GetPlayer(sessionId);
            if (player is null)
            {
                return;
            }

            switch (action)
            {
                case MenuAction.Create:
                    _ziplines.TryCreateForPlayer(player, out var createMessage);
                    player.SendMessage(MessageType.Chat, T(player, createMessage));
                    break;
                case MenuAction.RemoveOwned:
                    _ziplines.TryDeleteOwnedAtAim(player, out var removeMessage);
                    player.SendMessage(MessageType.Chat, T(player, removeMessage));
                    break;
                case MenuAction.CreateAdmin:
                    _ziplines.TryCreateForAdministrator(player, out var adminCreateMessage);
                    player.SendMessage(MessageType.Chat, T(player, adminCreateMessage));
                    break;
                case MenuAction.RemoveAny:
                    _ziplines.TryDeleteAnyAtAim(player, out var adminRemoveMessage);
                    player.SendMessage(MessageType.Chat, T(player, adminRemoveMessage));
                    break;
                case MenuAction.ClearAll:
                    if (_ziplines.CanManage(player))
                    {
                        var count = _ziplines.ActivePairCount;
                        _ziplines.ClearAll(Models.ZiplineDetachReason.PairRemoved);
                        player.SendMessage(MessageType.Chat, Format(player, "Zipline.AdminAllRemoved", count));
                    }
                    break;
                case MenuAction.ToggleVision:
                    if (_ziplines.CanManage(player))
                    {
                        _ziplines.SetAdminVisionEnabled(!_ziplines.AdminVisionEnabled);
                        var visionMessage = _ziplines.AdminVisionEnabled
                            ? "Zipline.AdminVisionEnabled"
                            : "Zipline.AdminVisionDisabled";
                        player.SendMessage(MessageType.Chat, T(player, visionMessage));
                    }
                    break;
            }
        });
    }

    private IPlayer? GetPlayer(ulong sessionId)
    {
        var player = _core.PlayerManager.GetPlayerFromSessionId(sessionId);
        return player is { IsValid: true, IsFakeClient: false } && player.SessionId == sessionId ? player : null;
    }

    private bool IsCurrentMapRequest(string mapName, int mapGeneration) => mapGeneration == (_mapGenerationResolver?.Invoke() ?? 0)
        && string.Equals(mapName, GetCurrentMapName(), StringComparison.OrdinalIgnoreCase);

    private string GetCurrentMapName() => _core.Engine.GlobalVars.MapName.Value;

    private string T(IPlayer player, string key) => _core.Translation.GetPlayerLocalizer(player)[key];
    private string Format(IPlayer player, string key, params object[] arguments) => string.Format(T(player, key), arguments);

    private enum MenuAction
    {
        Create,
        RemoveOwned,
        CreateAdmin,
        RemoveAny,
        ClearAll,
        ToggleVision
    }
}
