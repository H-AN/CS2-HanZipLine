using CS2HanZipLine.Models;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.EntitySystem;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace CS2HanZipLine.Services;

public sealed class ZiplineEntityService(ISwiftlyCore core, ILogger<ZiplineEntityService> logger)
{
    private const string AnchorDesignerName = "prop_dynamic_override";
    private const string BeamDesignerName = "beam";
    private const float MinimumBuildFlightDuration = 0.12f;

    private readonly ISwiftlyCore _core = core;
    private readonly ILogger<ZiplineEntityService> _logger = logger;
    private ZiplineConfig _config = new();

    public void UpdateConfig(ZiplineConfig config) => _config = config.CloneNormalized();

    public void OnPrecacheResource(IOnPrecacheResourceEvent @event)
    {
        @event.AddItem(_config.AnchorModel);
        @event.AddItem(_config.BuildFlightModel);

        if (!string.IsNullOrWhiteSpace(_config.PrecacheSoundEvent))
        {
            @event.AddItem(_config.PrecacheSoundEvent);
            return;
        }

        _logger.LogWarning("Zipline precache: PrecacheSoundEvent is empty; no sound-event file was added.");
    }

    public bool TryPrepareBuildPairNextTick(
        ZiplinePair pair,
        int mapGeneration,
        Func<int> currentMapGeneration,
        Action<ZiplinePair, bool, string> completed)
    {
        if (!TryCreateAnchor(pair.AnchorA) || !TryCreateAnchor(pair.AnchorB))
        {
            DestroyPairEntities(pair);
            return false;
        }

        _core.Scheduler.NextTick(() =>
        {
            if (mapGeneration != currentMapGeneration())
            {
                DestroyPairEntities(pair);
                return;
            }

            if (!TryResolveAnchor(pair.AnchorA) || !TryResolveAnchor(pair.AnchorB))
            {
                DestroyPairEntities(pair);
                completed(pair, false, "Zipline.ErrorBoundsUnavailable");
                return;
            }

            completed(pair, true, string.Empty);
        });

        return true;
    }

    public bool TryStartBuildFlight(
        ZiplinePair pair,
        int mapGeneration,
        Func<int> currentMapGeneration,
        Action<ZiplinePair, bool> completed)
    {
        if (!pair.AnchorA.IsResolved || !pair.AnchorB.IsResolved || TryGetEntity(pair.AnchorA.EntityHandle) is null)
        {
            return false;
        }

        var startPosition = pair.AnchorA.CablePosition;
        var endPosition = pair.AnchorB.CablePosition;
        var flightVector = endPosition - startPosition;
        if (!ZiplineMath.TryNormalize(flightVector, out var flightDirection))
        {
            return false;
        }

        // The temporary end anchor exists only to measure the exact final cable point.
        DestroyAnchor(pair.AnchorB);
        if (!TryCreateBeam(pair, startPosition))
        {
            return false;
        }

        CBaseModelEntity? entity;
        try
        {
            entity = _core.EntitySystem.CreateEntityByDesignerName<CBaseModelEntity>(AnchorDesignerName);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to allocate zipline build-flight entity.");
            return false;
        }

        if (entity is not { IsValid: true })
        {
            return false;
        }

        try
        {
            var flightAngles = GetFlightAngles(flightDirection);
            entity.Collision.SolidType = SolidType_t.SOLID_NONE;
            entity.Collision.SolidTypeUpdated();
            entity.Teleport(startPosition, flightAngles, Vector.Zero);
            entity.DispatchSpawn();

            var handle = _core.EntitySystem.GetRefEHandle(entity);
            if (!handle.IsValid)
            {
                TryDestroyEntity(entity);
                return false;
            }

            pair.BuildFlightHandle = handle;
            pair.BuildFlightStartPosition = startPosition;
            pair.BuildFlightEndPosition = endPosition;
            pair.BuildFlightCablePosition = startPosition;
            pair.BuildFlightAngles = flightAngles;
            _core.Scheduler.NextTick(() =>
            {
                if (mapGeneration != currentMapGeneration())
                {
                    DestroyBuildFlight(pair);
                    return;
                }

                var flightEntity = TryGetEntity(pair.BuildFlightHandle);
                if (flightEntity is null)
                {
                    ResetBuildFlight(pair);
                    completed(pair, false);
                    return;
                }

                try
                {
                    flightEntity.SetModel(_config.BuildFlightModel);
                    flightEntity.SetScale(_config.BuildFlightModelScale);
                    pair.BuildFlightStartedAt = _core.Engine.GlobalVars.CurrentTime;
                    pair.BuildFlightDuration = Math.Max(
                        MinimumBuildFlightDuration,
                        MathF.Sqrt(ZiplineMath.DistanceSquared(startPosition, endPosition)) / _config.BuildFlightSpeed);
                    pair.IsBuildFlightActive = true;
                    completed(pair, true);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Failed to set the zipline build-flight model.");
                    DestroyBuildFlight(pair);
                    completed(pair, false);
                }
            });
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to initialize zipline build-flight entity.");
            TryDestroyEntity(entity);
            return false;
        }
    }

    public bool TryAdvanceBuildFlight(ZiplinePair pair, float currentTime, out bool arrived)
    {
        arrived = false;
        if (!pair.IsBuildFlightActive)
        {
            return false;
        }

        var entity = TryGetEntity(pair.BuildFlightHandle);
        if (entity is null)
        {
            ResetBuildFlight(pair);
            return false;
        }

        var elapsed = Math.Clamp(currentTime - pair.BuildFlightStartedAt, 0.0f, pair.BuildFlightDuration);
        var progress = elapsed / pair.BuildFlightDuration;
        var position = pair.BuildFlightStartPosition
            + (pair.BuildFlightEndPosition - pair.BuildFlightStartPosition) * progress;
        if (progress < 1.0f)
        {
            position.Z += 0.5f * _config.BuildFlightGravity * elapsed * (pair.BuildFlightDuration - elapsed);
        }

        var velocity = (pair.BuildFlightEndPosition - pair.BuildFlightStartPosition) / pair.BuildFlightDuration;
        velocity.Z += 0.5f * _config.BuildFlightGravity * (pair.BuildFlightDuration - 2.0f * elapsed);
        if (ZiplineMath.TryNormalize(velocity, out var direction))
        {
            pair.BuildFlightAngles = GetFlightAngles(direction);
        }

        try
        {
            entity.Teleport(position, pair.BuildFlightAngles, Vector.Zero);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to update zipline build-flight entity.");
            DestroyBuildFlight(pair);
            return false;
        }

        pair.BuildFlightCablePosition = position;
        if (!TryUpdateBeamEnd(pair, position))
        {
            DestroyBuildFlight(pair);
            return false;
        }

        if (progress < 1.0f)
        {
            return true;
        }

        DestroyBuildFlight(pair);
        arrived = true;
        return true;
    }

    public bool TryCreatePairNextTick(ZiplinePair pair, int mapGeneration, Func<int> currentMapGeneration, Action<ZiplinePair, bool, string> completed)
    {
        if (!TryCreateAnchor(pair.AnchorA) || !TryCreateAnchor(pair.AnchorB))
        {
            DestroyPairEntities(pair);
            completed(pair, false, "Zipline.ErrorEntityCreate");
            return false;
        }

        _core.Scheduler.NextTick(() =>
        {
            if (mapGeneration != currentMapGeneration())
            {
                DestroyPairEntities(pair);
                return;
            }

            if (!TryResolveAnchor(pair.AnchorA) || !TryResolveAnchor(pair.AnchorB))
            {
                DestroyPairEntities(pair);
                completed(pair, false, "Zipline.ErrorBoundsUnavailable");
                return;
            }

            if (!TryCreateBeam(pair, pair.AnchorB.CablePosition))
            {
                DestroyPairEntities(pair);
                completed(pair, false, "Zipline.ErrorBeamCreate");
                return;
            }

            completed(pair, true, string.Empty);
        });

        return true;
    }

    public bool TryCreateCompletedBuildAnchorNextTick(
        ZiplinePair pair,
        int mapGeneration,
        Func<int> currentMapGeneration,
        Action<ZiplinePair, bool, string> completed)
    {
        if (!pair.AnchorA.IsResolved || TryGetEntity(pair.AnchorA.EntityHandle) is null || !TryCreateAnchor(pair.AnchorB))
        {
            DestroyPairEntities(pair);
            return false;
        }

        _core.Scheduler.NextTick(() =>
        {
            if (mapGeneration != currentMapGeneration())
            {
                DestroyPairEntities(pair);
                return;
            }

            if (!TryResolveAnchor(pair.AnchorB))
            {
                DestroyPairEntities(pair);
                completed(pair, false, "Zipline.ErrorBoundsUnavailable");
                return;
            }

            if (!TryUpdateBeamEnd(pair, pair.AnchorB.CablePosition))
            {
                DestroyPairEntities(pair);
                completed(pair, false, "Zipline.ErrorBeamCreate");
                return;
            }

            completed(pair, true, string.Empty);
        });

        return true;
    }

    public bool IsPairReady(ZiplinePair pair)
    {
        return pair.AnchorA.IsResolved
            && pair.AnchorB.IsResolved
            && TryGetEntity(pair.AnchorA.EntityHandle) is not null
            && TryGetEntity(pair.AnchorB.EntityHandle) is not null
            && TryGetEntity(pair.BeamHandle) is not null;
    }

    public void DestroyPairEntities(ZiplinePair pair)
    {
        DestroyBuildFlight(pair);
        TryDestroyEntity(TryGetEntity(pair.BeamHandle));
        DestroyAnchor(pair.AnchorA);
        DestroyAnchor(pair.AnchorB);
        pair.BeamHandle = default;
    }

    private void DestroyBuildFlight(ZiplinePair pair)
    {
        TryDestroyEntity(TryGetEntity(pair.BuildFlightHandle));
        ResetBuildFlight(pair);
    }

    private static void ResetBuildFlight(ZiplinePair pair)
    {
        pair.BuildFlightHandle = default;
        pair.BuildFlightStartPosition = Vector.Zero;
        pair.BuildFlightEndPosition = Vector.Zero;
        pair.BuildFlightCablePosition = Vector.Zero;
        pair.BuildFlightAngles = new QAngle();
        pair.BuildFlightStartedAt = 0.0f;
        pair.BuildFlightDuration = 0.0f;
        pair.IsBuildFlightActive = false;
    }

    private static void DestroyAnchor(ZiplineAnchor anchor)
    {
        TryDestroyEntity(TryGetEntity(anchor.EntityHandle));
        anchor.EntityHandle = default;
        anchor.IsResolved = false;
    }

    private bool TryCreateAnchor(ZiplineAnchor anchor)
    {
        CBaseModelEntity? entity;
        try
        {
            entity = _core.EntitySystem.CreateEntityByDesignerName<CBaseModelEntity>(AnchorDesignerName);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to allocate zipline anchor entity.");
            return false;
        }

        if (entity is not { IsValid: true })
        {
            return false;
        }

        try
        {
            entity.Collision.SolidType = SolidType_t.SOLID_NONE;
            entity.Collision.SolidTypeUpdated();
            entity.Teleport(anchor.SurfacePosition, anchor.Angles, Vector.Zero);
            entity.DispatchSpawn();
            entity.SetModel(_config.AnchorModel);
            entity.SetScale(_config.AnchorModelScale);

            var handle = _core.EntitySystem.GetRefEHandle(entity);
            if (!handle.IsValid)
            {
                TryDestroyEntity(entity);
                return false;
            }

            anchor.EntityHandle = handle;
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to initialize zipline anchor entity.");
            TryDestroyEntity(entity);
            return false;
        }
    }

    private bool TryResolveAnchor(ZiplineAnchor anchor)
    {
        var entity = TryGetEntity(anchor.EntityHandle);
        if (entity is null)
        {
            return false;
        }

        var mins = entity.Collision.Mins;
        var maxs = entity.Collision.Maxs;
        if (!IsUsableBounds(mins, maxs))
        {
            if (_config.CableAttachmentHeightFallback <= 0.0f)
            {
                return false;
            }

            _logger.LogWarning(
                "Zipline anchor bounds are unavailable for {AnchorModel}; using the configured fallback height.",
                _config.AnchorModel);
            anchor.BasePosition = anchor.SurfacePosition;
            anchor.CablePosition = anchor.SurfacePosition
                + anchor.SurfaceNormal * (_config.CableAttachmentHeightFallback * _config.AnchorModelScale);
            anchor.IsResolved = true;
            return true;
        }

        // Collision bounds are in model-local units. SetScale changes the model
        // visual size, so apply the same uniform scale before deriving endpoints.
        var scaledMins = mins * _config.AnchorModelScale;
        var scaledMaxs = maxs * _config.AnchorModelScale;
        var centerX = (scaledMins.X + scaledMaxs.X) * 0.5f;
        var centerY = (scaledMins.Y + scaledMaxs.Y) * 0.5f;
        var localBottom = new Vector(centerX, centerY, scaledMins.Z);
        var localTop = new Vector(centerX, centerY, scaledMaxs.Z);
        var bottomOffset = TransformLocalVector(anchor.Angles, localBottom);
        var modelOrigin = anchor.SurfacePosition - bottomOffset;

        entity.Teleport(modelOrigin, anchor.Angles, null);
        anchor.BasePosition = anchor.SurfacePosition;
        anchor.CablePosition = modelOrigin + TransformLocalVector(anchor.Angles, localTop);
        anchor.IsResolved = ZiplineMath.IsFinite(anchor.CablePosition);
        return anchor.IsResolved;
    }

    private bool TryCreateBeam(ZiplinePair pair, Vector endPosition)
    {
        CBeam? beam;
        try
        {
            beam = _core.EntitySystem.CreateEntityByDesignerName<CBeam>(BeamDesignerName);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to allocate zipline beam entity.");
            return false;
        }

        if (beam is not { IsValid: true } || !TryParseColor(_config.BeamColor, out var color))
        {
            TryDestroyEntity(beam);
            return false;
        }

        try
        {
            beam.Render = color;
            beam.Width = _config.BeamWidth;
            beam.EndWidth = _config.BeamEndWidth;
            beam.HaloScale = _config.BeamHaloScale;
            beam.Teleport(pair.AnchorA.CablePosition, new QAngle(), Vector.Zero);
            beam.EndPos.X = endPosition.X;
            beam.EndPos.Y = endPosition.Y;
            beam.EndPos.Z = endPosition.Z;
            beam.DispatchSpawn();
            beam.EndPosUpdated();

            var handle = _core.EntitySystem.GetRefEHandle(beam);
            if (!handle.IsValid)
            {
                TryDestroyEntity(beam);
                return false;
            }

            pair.BeamHandle = handle;
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to initialize zipline beam entity.");
            TryDestroyEntity(beam);
            return false;
        }
    }

    private bool TryUpdateBeamEnd(ZiplinePair pair, Vector endPosition)
    {
        var beam = TryGetEntity(pair.BeamHandle);
        if (beam is null)
        {
            return false;
        }

        try
        {
            beam.EndPos.X = endPosition.X;
            beam.EndPos.Y = endPosition.Y;
            beam.EndPos.Z = endPosition.Z;
            beam.EndPosUpdated();
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to update zipline build beam endpoint.");
            return false;
        }
    }

    private static CBaseModelEntity? TryGetEntity(CHandle<CBaseModelEntity> handle)
    {
        if (!handle.IsValid)
        {
            return null;
        }

        var entity = handle.Value;
        return entity is { IsValid: true } ? entity : null;
    }

    private static CBeam? TryGetEntity(CHandle<CBeam> handle)
    {
        if (!handle.IsValid)
        {
            return null;
        }

        var entity = handle.Value;
        return entity is { IsValid: true } ? entity : null;
    }

    private static void TryDestroyEntity(CEntityInstance? entity)
    {
        if (entity is not { IsValid: true })
        {
            return;
        }

        try
        {
            entity.Despawn();
        }
        catch
        {
            try
            {
                entity.AcceptInput("Kill", string.Empty);
            }
            catch
            {
            }
        }
    }

    private static bool IsUsableBounds(Vector mins, Vector maxs)
    {
        return ZiplineMath.IsFinite(mins)
            && ZiplineMath.IsFinite(maxs)
            && maxs.Z > mins.Z + 0.01f;
    }

    private static Vector TransformLocalVector(QAngle angles, Vector local)
    {
        angles.ToDirectionVectors(out var forward, out var right, out var up);
        return forward * local.X + right * local.Y + up * local.Z;
    }

    private static QAngle GetFlightAngles(Vector direction)
    {
        var horizontalLength = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        var pitch = -MathF.Atan2(direction.Z, horizontalLength) * 180.0f / MathF.PI;
        var yaw = MathF.Atan2(direction.Y, direction.X) * 180.0f / MathF.PI;
        return new QAngle(pitch, yaw, 0.0f);
    }

    private static bool TryParseColor(string input, out Color color)
    {
        color = new Color(255, 255, 255, 255);
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 3 or > 4
            || !byte.TryParse(parts[0], out var red)
            || !byte.TryParse(parts[1], out var green)
            || !byte.TryParse(parts[2], out var blue))
        {
            return false;
        }

        var alpha = parts.Length == 4 && byte.TryParse(parts[3], out var parsedAlpha) ? parsedAlpha : byte.MaxValue;
        color = new Color(red, green, blue, alpha);
        return true;
    }
}
