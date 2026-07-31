using CS2HanZipLine.Models;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Trace;

namespace CS2HanZipLine.Services;

public sealed class ZiplinePlacementService(ISwiftlyCore core)
{
    private readonly ISwiftlyCore _core = core;
    private ZiplineConfig _config = new();

    public void UpdateConfig(ZiplineConfig config) => _config = config.CloneNormalized();

    public bool TryBuildPlacement(IPlayer player, out AnchorPlacement start, out AnchorPlacement end, out string message)
    {
        start = default;
        end = default;

        if (!TryGetUsablePawn(player, out var pawn))
        {
            message = "Zipline.ErrorPawnUnavailable";
            return false;
        }

        if (!TraceTargetSurface(pawn, out var targetSurface, out var forward))
        {
            message = "Zipline.ErrorAimSurface";
            return false;
        }

        if (!FindStartAnchorSurface(pawn, out var startSurface))
        {
            message = "Zipline.ErrorGroundSurface";
            return false;
        }

        start = CreateAnchorPlacement(startSurface.Position, startSurface.Normal, forward);
        end = CreateAnchorPlacement(targetSurface.Position, targetSurface.Normal, forward);
        message = string.Empty;
        return true;
    }

    public bool TryTraceTargetSurface(IPlayer player, out Vector hitPoint, out string message)
    {
        hitPoint = Vector.Zero;
        if (!TryGetUsablePawn(player, out var pawn) || !TraceTargetSurface(pawn, out var surface, out _))
        {
            message = "Zipline.ErrorAimSurface";
            return false;
        }

        hitPoint = surface.Position;
        message = string.Empty;
        return true;
    }

    public bool TryGetAimRay(IPlayer player, out Vector origin, out Vector direction)
    {
        origin = Vector.Zero;
        direction = Vector.Zero;
        return TryGetUsablePawn(player, out var pawn) && TryGetAimRay(pawn, out origin, out direction);
    }

    public static bool TryCreateSavedPlacement(Vector position, Vector normal, out AnchorPlacement placement)
    {
        placement = default;
        if (!ZiplineMath.IsFinite(position) || !ZiplineMath.TryNormalize(normal, out normal))
        {
            return false;
        }

        placement = CreateAnchorPlacement(position, normal, new Vector(1.0f, 0.0f, 0.0f));
        return true;
    }

    private bool TraceTargetSurface(CCSPlayerPawn pawn, out SurfaceHit surface, out Vector forward)
    {
        surface = default;
        if (!TryGetAimRay(pawn, out var start, out forward))
        {
            return false;
        }

        var target = start + forward * _config.MaxDistance;
        return TryTraceSolid(start, target, out surface);
    }

    private static bool TryGetAimRay(CCSPlayerPawn pawn, out Vector origin, out Vector direction)
    {
        origin = Vector.Zero;
        direction = Vector.Zero;
        if (pawn.EyePosition is not { } eyePosition)
        {
            return false;
        }

        pawn.EyeAngles.ToDirectionVectors(out direction, out _, out _);
        if (!ZiplineMath.TryNormalize(direction, out direction))
        {
            return false;
        }

        origin = new Vector(eyePosition.X, eyePosition.Y, eyePosition.Z);
        return true;
    }

    private bool FindStartAnchorSurface(CCSPlayerPawn pawn, out SurfaceHit surface)
    {
        surface = default;
        var origin = pawn.AbsOrigin;
        if (origin is null)
        {
            return false;
        }

        var start = new Vector(origin.Value.X, origin.Value.Y, origin.Value.Z + 8.0f);
        var end = start - new Vector(0.0f, 0.0f, _config.GroundTraceDistance);
        return TryTraceSolid(start, end, out surface);
    }

    private bool TryTraceSolid(Vector start, Vector end, out SurfaceHit surface)
    {
        surface = default;
        var trace = _core.Trace.TraceShapeLine(
            start,
            end,
            TraceParams.Builder()
                .WithLineRay()
                .WithObjectQuery(RnQueryObjectSet.Static | RnQueryObjectSet.Dynamic)
                .WithInteraction(MaskTrace.Solid)
                .WithCollisionGroup(CollisionGroup.Player)
                .Build());

        if (trace.Fraction is <= 0.0f or >= 0.9999f || trace.StartInSolid)
        {
            return false;
        }

        var normal = trace.HitNormal;
        if (!ZiplineMath.IsFinite(trace.HitPoint) || !ZiplineMath.TryNormalize(normal, out normal))
        {
            return false;
        }

        surface = new SurfaceHit(trace.HitPoint + normal * _config.SurfaceOffset, normal);
        return true;
    }

    private static AnchorPlacement CreateAnchorPlacement(Vector position, Vector normal, Vector playerForward)
    {
        normal = normal.Normalized();
        playerForward = playerForward.Normalized();

        // it_streetlampleg's upright axis is local +Z. Align that axis with the
        // traced surface normal; the mine model's "front faces surface" formula
        // would rotate this pole flat on horizontal ground.
        var horizontalLength = MathF.Sqrt(normal.X * normal.X + normal.Y * normal.Y);
        var pitch = MathF.Atan2(horizontalLength, normal.Z) * 180.0f / MathF.PI;
        var yaw = horizontalLength > 0.0001f
            ? MathF.Atan2(normal.Y, normal.X) * 180.0f / MathF.PI
            : MathF.Atan2(playerForward.Y, playerForward.X) * 180.0f / MathF.PI;

        return new AnchorPlacement(position, normal, new QAngle(pitch, yaw, 0.0f));
    }

    private static bool TryGetUsablePawn(IPlayer player, out CCSPlayerPawn pawn)
    {
        pawn = null!;
        if (player is null || !player.IsValid || player.IsFakeClient || !player.IsAlive)
        {
            return false;
        }

        if (player.PlayerPawn is not { IsValid: true } playerPawn)
        {
            return false;
        }

        pawn = playerPawn;
        return true;
    }

    private readonly record struct SurfaceHit(Vector Position, Vector Normal);
}
