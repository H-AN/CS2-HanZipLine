using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace CS2HanZipLine.Services;

public sealed class ZiplineMenuService(ISwiftlyCore core, ZiplineService ziplines)
{
    private readonly ISwiftlyCore _core = core;
    private readonly ZiplineService _ziplines = ziplines;

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

        var createButton = new ButtonMenuOption(T(player, "Zipline.MenuCreate")) { CloseAfterClick = true };
        createButton.Click += async (_, args) =>
        {
            if (args.Player is not { IsValid: true, IsFakeClient: false } clicker)
            {
                return;
            }

            ScheduleAction(clicker.SessionId, create: true);
            await Task.CompletedTask;
        };
        menu.AddOption(createButton);

        var removeButton = new ButtonMenuOption(T(player, "Zipline.MenuRemove")) { CloseAfterClick = true };
        removeButton.Click += async (_, args) =>
        {
            if (args.Player is not { IsValid: true, IsFakeClient: false } clicker)
            {
                return;
            }

            ScheduleAction(clicker.SessionId, create: false);
            await Task.CompletedTask;
        };
        menu.AddOption(removeButton);

        _core.MenusAPI.OpenMenuForPlayer(player, menu);
    }

    private void ScheduleAction(ulong sessionId, bool create)
    {
        _core.Scheduler.NextTick(() =>
        {
            var player = _core.PlayerManager.GetPlayerFromSessionId(sessionId);
            if (player is not { IsValid: true, IsFakeClient: false } || player.SessionId != sessionId)
            {
                return;
            }

            if (create)
            {
                _ziplines.TryCreateForPlayer(player, out var message);
                player.SendMessage(MessageType.Chat, T(player, message));
                return;
            }

            _ziplines.TryDeleteOwnedAtAim(player, out var removeMessage);
            player.SendMessage(MessageType.Chat, T(player, removeMessage));
        });
    }

    private string T(IPlayer player, string key) => _core.Translation.GetPlayerLocalizer(player)[key];
}
