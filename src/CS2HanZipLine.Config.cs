namespace CS2HanZipLine;

public sealed class ZiplineConfig
{
    public bool Enable { get; set; } = true;
    public bool AdminOnlyCreate { get; set; }
    public string AdminPermissions { get; set; } = "hanzipline.admin.manage";
    public int MaxActivePairs { get; set; } = 64;
    public int MaxPerPlayer { get; set; } = 10;
    public float CreateCooldownSeconds { get; set; } = 8.0f;
    public float MinDistance { get; set; } = 128.0f;
    public float MaxDistance { get; set; } = 5000.0f;
    public float AnchorSeparation { get; set; } = 96.0f;
    public float UseRadius { get; set; } = 96.0f;
    public bool BotAllowUse { get; set; }
    public float BotUseRange { get; set; } = 300.0f;
    public float BotUseCooldownSeconds { get; set; } = 20.0f;
    public float BotTargetTimeoutSeconds { get; set; } = 5.0f;
    public float BotApproachSpeed { get; set; } = 450.0f;
    public float RideSpeed { get; set; } = 700.0f;
    public float ArrivalDistance { get; set; } = 48.0f;
    public float AlignmentSpeed { get; set; } = 240.0f;
    public float RideFlyDurationSeconds { get; set; } = 1.0f;
    public float StallTimeoutSeconds { get; set; } = 1.5f;
    public int MaxUses { get; set; }
    public float LifetimeSeconds { get; set; }
    public bool ClearEachRound { get; set; } = true;
    public string AnchorModel { get; set; } = "models/props/cs_italy/it_streetlampleg.vmdl";
    public float AnchorModelScale { get; set; } = 0.65f;
    public string BuildFlightModel { get; set; } = "weapons/models/grenade/decoy/weapon_decoy.vmdl";
    public float BuildFlightModelScale { get; set; } = 1.0f;
    public float BuildFlightSpeed { get; set; } = 1800.0f;
    public float BuildFlightGravity { get; set; } = 800.0f;
    public float CableAttachmentHeightFallback { get; set; } = 144.0f;
    public float BeamWidth { get; set; } = 0.5f;
    public float BeamEndWidth { get; set; } = 0.5f;
    public float BeamHaloScale { get; set; } = 3.0f;
    public string BeamColor { get; set; } = "255 255 255 255";
    public string AdminVisionGlowColor { get; set; } = "255 210 35 255";
    public int AdminVisionGlowRange { get; set; } = 5000;

    public string PrecacheSoundEvent { get; set; } = "soundevents/game_sounds_ui.vsndevts";

    public string CreateSound { get; set; } = "Music.Match.LastRoundHalf";

    public string BuildSound { get; set; } = "UI.ContractSeal";

    public string RideStartSound { get; set; } = "UIPanorama.container_weapon_ticker";

    public string RideLoopSound { get; set; } = "UI.StickerScratch";

    public float RideLoopInterval { get; set; } = 0.5f;

    public string RideEndSound { get; set; } = "UI.CrateOpen";

    public float SoundVolume { get; set; } = 1.0f;

    public float SoundPitch { get; set; } = 1.0f;

    public bool RealisticBuild { get; set; }
    public float SurfaceOffset { get; set; } = 2.0f;
    public float GroundTraceDistance { get; set; } = 256.0f;

    public ZiplineConfig CloneNormalized()
    {
        var clone = (ZiplineConfig)MemberwiseClone();
        clone.MaxActivePairs = Math.Max(1, clone.MaxActivePairs);
        clone.MaxPerPlayer = Math.Max(1, clone.MaxPerPlayer);
        clone.CreateCooldownSeconds = Math.Max(0.0f, clone.CreateCooldownSeconds);
        clone.MinDistance = Math.Max(1.0f, clone.MinDistance);
        clone.MaxDistance = Math.Max(clone.MinDistance, clone.MaxDistance);
        clone.AnchorSeparation = Math.Max(1.0f, clone.AnchorSeparation);
        clone.UseRadius = Math.Max(1.0f, clone.UseRadius);
        clone.BotUseRange = Math.Max(clone.UseRadius, clone.BotUseRange);
        clone.BotUseCooldownSeconds = Math.Max(0.0f, clone.BotUseCooldownSeconds);
        clone.BotTargetTimeoutSeconds = Math.Max(0.0f, clone.BotTargetTimeoutSeconds);
        clone.BotApproachSpeed = Math.Max(1.0f, clone.BotApproachSpeed);
        clone.RideSpeed = Math.Max(1.0f, clone.RideSpeed);
        clone.ArrivalDistance = Math.Max(1.0f, clone.ArrivalDistance);
        clone.AlignmentSpeed = Math.Max(0.0f, clone.AlignmentSpeed);
        clone.RideFlyDurationSeconds = Math.Max(0.0f, clone.RideFlyDurationSeconds);
        clone.StallTimeoutSeconds = Math.Max(0.1f, clone.StallTimeoutSeconds);
        clone.LifetimeSeconds = Math.Max(0.0f, clone.LifetimeSeconds);
        clone.AnchorModelScale = Math.Max(0.01f, clone.AnchorModelScale);
        clone.BuildFlightModelScale = Math.Max(0.01f, clone.BuildFlightModelScale);
        clone.BuildFlightSpeed = Math.Max(1.0f, clone.BuildFlightSpeed);
        clone.BuildFlightGravity = Math.Max(0.0f, clone.BuildFlightGravity);
        clone.CableAttachmentHeightFallback = Math.Max(0.0f, clone.CableAttachmentHeightFallback);
        clone.BeamWidth = Math.Max(0.01f, clone.BeamWidth);
        clone.BeamEndWidth = Math.Max(0.01f, clone.BeamEndWidth);
        clone.BeamHaloScale = Math.Max(0.0f, clone.BeamHaloScale);
        clone.AdminVisionGlowRange = Math.Max(0, clone.AdminVisionGlowRange);
        clone.RideLoopInterval = Math.Max(0.05f, clone.RideLoopInterval);
        clone.SoundVolume = Math.Max(0.0f, clone.SoundVolume);
        clone.SoundPitch = Math.Max(0.01f, clone.SoundPitch);
        clone.SurfaceOffset = Math.Max(0.0f, clone.SurfaceOffset);
        clone.GroundTraceDistance = Math.Max(32.0f, clone.GroundTraceDistance);
        clone.AdminPermissions = clone.AdminPermissions?.Trim() ?? string.Empty;
        clone.AnchorModel = string.IsNullOrWhiteSpace(clone.AnchorModel)
            ? "models/props/cs_italy/it_streetlampleg.vmdl"
            : clone.AnchorModel.Trim();
        clone.BuildFlightModel = string.IsNullOrWhiteSpace(clone.BuildFlightModel)
            ? "weapons/models/grenade/decoy/weapon_decoy.vmdl"
            : clone.BuildFlightModel.Trim();
        clone.AdminVisionGlowColor = string.IsNullOrWhiteSpace(clone.AdminVisionGlowColor)
            ? "255 210 35 255"
            : clone.AdminVisionGlowColor.Trim();
        return clone;
    }
}
