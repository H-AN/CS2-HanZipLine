using SwiftlyS2.Shared.Natives;

namespace CS2HanZipLine.Models;

public readonly record struct AnchorPlacement(Vector SurfacePosition, Vector SurfaceNormal, QAngle Angles);

public enum ZiplineDetachReason
{
    Manual,
    Arrived,
    Stalled,
    Death,
    Disconnect,
    PairRemoved,
    MapUnload,
    PluginUnload,
    Safety
}
