using SwiftlyS2.Shared.Natives;
using System.Text.Json.Serialization;

namespace CS2HanZipLine.Models;

public sealed class ZiplineMapDocument
{
    public int Version { get; init; } = 2;
    public List<ZiplineMapEntry> Ziplines { get; init; } = [];
}

public sealed class ZiplineMapEntry
{
    public float[] StartSurface { get; init; } = [];
    public float[] StartNormal { get; init; } = [];
    public float[] EndSurface { get; init; } = [];
    public float[] EndNormal { get; init; } = [];
    [JsonConverter(typeof(JsonStringEnumConverter<ZiplineTeam>))]
    public ZiplineTeam Team { get; init; } = ZiplineTeam.Global;

    public static ZiplineMapEntry FromPair(ZiplinePair pair) => new()
    {
        StartSurface = ToArray(pair.AnchorA.SurfacePosition),
        StartNormal = ToArray(pair.AnchorA.SurfaceNormal),
        EndSurface = ToArray(pair.AnchorB.SurfacePosition),
        EndNormal = ToArray(pair.AnchorB.SurfaceNormal),
        Team = pair.Team
    };

    public bool TryGetSurfaces(out Vector startSurface, out Vector startNormal, out Vector endSurface, out Vector endNormal)
    {
        startSurface = Vector.Zero;
        startNormal = Vector.Zero;
        endSurface = Vector.Zero;
        endNormal = Vector.Zero;

        return TryCreateVector(StartSurface, out startSurface)
            && TryCreateVector(StartNormal, out startNormal)
            && TryCreateVector(EndSurface, out endSurface)
            && TryCreateVector(EndNormal, out endNormal);
    }

    private static float[] ToArray(Vector vector) => [vector.X, vector.Y, vector.Z];

    private static bool TryCreateVector(float[] values, out Vector vector)
    {
        vector = Vector.Zero;
        if (values.Length != 3 || values.Any(value => !float.IsFinite(value)))
        {
            return false;
        }

        vector = new Vector(values[0], values[1], values[2]);
        return true;
    }
}
