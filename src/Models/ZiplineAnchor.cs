using SwiftlyS2.Shared.EntitySystem;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace CS2HanZipLine.Models;

public sealed class ZiplineAnchor
{
    public ZiplineAnchor(Vector surfacePosition, Vector surfaceNormal, QAngle angles)
    {
        SurfacePosition = surfacePosition;
        SurfaceNormal = surfaceNormal;
        Angles = angles;
        BasePosition = surfacePosition;
        CablePosition = surfacePosition;
    }

    public Vector SurfacePosition { get; }
    public Vector SurfaceNormal { get; }
    public QAngle Angles { get; }
    public Vector BasePosition { get; set; }
    public Vector CablePosition { get; set; }
    public CHandle<CBaseModelEntity> EntityHandle { get; set; }
    public bool IsResolved { get; set; }
}
