using SwiftlyS2.Shared.EntitySystem;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace CS2HanZipLine.Models;

public sealed class RiderState
{
    public required ulong SessionId { get; init; }
    public required int PlayerId { get; init; }
    public required int PairId { get; init; }
    public required int MapGeneration { get; init; }
    public required bool MovingFromA { get; init; }
    public required CHandle<CCSPlayerPawn> PawnHandle { get; init; }
    public required MoveType_t OriginalMoveType { get; init; }
    public bool MoveTypeOverridden { get; set; }
    public float FlyEndsAt { get; set; }
    public float LastProgress { get; set; }
    public float LastProgressTime { get; set; }
    public float NextLoopSoundTime { get; set; }
}
