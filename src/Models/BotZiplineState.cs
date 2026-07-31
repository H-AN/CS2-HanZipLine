namespace CS2HanZipLine.Models;

internal sealed class BotZiplineState
{
    public required int PlayerId { get; init; }
    public int TargetPairId { get; set; } = -1;
    public bool MovingFromA { get; set; }
    public float TargetStartedAt { get; set; }
    public float NextEligibleAt { get; set; }
}
