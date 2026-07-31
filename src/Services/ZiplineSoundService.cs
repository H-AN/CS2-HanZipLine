using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Sounds;

namespace CS2HanZipLine.Services;

public sealed class ZiplineSoundService(ISwiftlyCore core, ILogger<ZiplineSoundService> logger)
{
    private readonly ISwiftlyCore _core = core;
    private readonly ILogger<ZiplineSoundService> _logger = logger;
    private ZiplineConfig _config = new();

    public void UpdateConfig(ZiplineConfig config)
    {
        _config = config.CloneNormalized();
    }

    public void PlayCreate(IPlayer player) => PlayForPlayer(player, _config.CreateSound);

    public void PlayBuild(IPlayer player) => PlayForPlayer(player, _config.BuildSound);

    public void PlayRideStart(IPlayer player) => PlayForPlayer(player, _config.RideStartSound);

    public void PlayRideLoop(IPlayer player) => PlayForPlayer(player, _config.RideLoopSound);

    public void PlayRideEnd(IPlayer player) => PlayForPlayer(player, _config.RideEndSound);

    private void PlayForPlayer(IPlayer player, string soundName)
    {
        if (string.IsNullOrWhiteSpace(soundName) || player is null || !player.IsValid || player.PlayerID < 0)
        {
            return;
        }

        try
        {
            using var sound = new SoundEvent(soundName, _config.SoundVolume, _config.SoundPitch);
            sound.SourceEntityIndex = player.PlayerPawn is { IsValid: true } pawn
                ? (int)pawn.Index
                : -1;
            // Send to every client; the source entity lets CS2 apply normal positional attenuation.
            sound.Recipients.AddAllPlayers();

            if (sound.Recipients.GetRecipientCount() > 0)
            {
                sound.Emit();
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to emit zipline sound event {SoundEvent}.", soundName);
        }
    }
}
