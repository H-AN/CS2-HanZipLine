using SwiftlyS2.Shared.EntitySystem;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace CS2HanZipLine.Models;

public sealed class ZiplinePair
{
    public ZiplinePair(
        int id,
        ulong ownerSteamId,
        ulong ownerSessionId,
        ZiplineAnchor anchorA,
        ZiplineAnchor anchorB,
        float createdAt,
        float expiresAt,
        ZiplineTeam team,
        bool isMapPlaced = false)
    {
        Id = id;
        OwnerSteamId = ownerSteamId;
        OwnerSessionId = ownerSessionId;
        AnchorA = anchorA;
        AnchorB = anchorB;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        Team = team is ZiplineTeam.CT or ZiplineTeam.T ? team : ZiplineTeam.Global;
        IsMapPlaced = isMapPlaced;
    }

    public int Id { get; }
    public ulong OwnerSteamId { get; }
    public ulong OwnerSessionId { get; }
    public ZiplineAnchor AnchorA { get; }
    public ZiplineAnchor AnchorB { get; }
    public CHandle<CBeam> BeamHandle { get; set; }
    public CHandle<CBaseModelEntity> BuildFlightHandle { get; set; }
    public Vector BuildFlightStartPosition { get; set; }
    public Vector BuildFlightEndPosition { get; set; }
    public Vector BuildFlightCablePosition { get; set; }
    public QAngle BuildFlightAngles { get; set; }
    public float BuildFlightStartedAt { get; set; }
    public float BuildFlightDuration { get; set; }
    public bool IsBuildFlightActive { get; set; }
    public float CreatedAt { get; }
    public float ExpiresAt { get; }
    public ZiplineTeam Team { get; }
    public int Uses { get; set; }
    public bool RemoveWhenUnused { get; set; }
    public bool IsMapPlaced { get; }

    public bool IsExpired(float currentTime) => ExpiresAt > 0.0f && currentTime >= ExpiresAt;
}
