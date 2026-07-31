using CS2HanZipLine.Models;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.EntitySystem;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace CS2HanZipLine.Services;

public sealed class ZiplineRideService(ISwiftlyCore core, ZiplineEntityService entities, ZiplineSoundService sounds)
{
    private readonly ISwiftlyCore _core = core;
    private readonly ZiplineEntityService _entities = entities;
    private readonly ZiplineSoundService _sounds = sounds;
    private readonly Dictionary<ulong, RiderState> _ridersBySessionId = [];
    private readonly Dictionary<int, ulong> _sessionIdByPlayerId = [];
    private readonly List<(ulong SessionId, ZiplineDetachReason Reason)> _detachQueue = [];
    private ZiplineConfig _config = new();

    public Func<int, ZiplinePair?>? PairResolver { get; set; }
    public Func<int>? MapGenerationResolver { get; set; }
    public Action<ZiplinePair>? PairBecameUnused { get; set; }

    public void UpdateConfig(ZiplineConfig config) => _config = config.CloneNormalized();

    public bool IsRiding(IPlayer player)
    {
        return player is not null
            && _sessionIdByPlayerId.TryGetValue(player.PlayerID, out var sessionId)
            && sessionId == player.SessionId
            && _ridersBySessionId.ContainsKey(sessionId);
    }

    public bool TryAttach(IPlayer player, ZiplinePair pair, bool movingFromA, int mapGeneration, out string message)
    {
        if (!TryGetUsablePlayerPawn(player, out var pawn))
        {
            message = "Zipline.ErrorPawnUnavailable";
            return false;
        }

        if (IsRiding(player))
        {
            message = "Zipline.ErrorAlreadyRiding";
            return false;
        }

        if (!_entities.IsPairReady(pair))
        {
            message = "Zipline.ErrorPairUnavailable";
            return false;
        }

        var pawnHandle = _core.EntitySystem.GetRefEHandle(pawn);
        if (!pawnHandle.IsValid)
        {
            message = "Zipline.ErrorPawnUnavailable";
            return false;
        }

        var now = _core.Engine.GlobalVars.CurrentTime;
        var state = new RiderState
        {
            SessionId = player.SessionId,
            PlayerId = player.PlayerID,
            PairId = pair.Id,
            MapGeneration = mapGeneration,
            MovingFromA = movingFromA,
            PawnHandle = pawnHandle,
            OriginalMoveType = pawn.MoveType,
            LastProgress = 0.0f,
            LastProgressTime = now,
            NextLoopSoundTime = now + _config.RideLoopInterval
        };

        ApplyRideMovement(pawn, state, now);
        _ridersBySessionId[state.SessionId] = state;
        _sessionIdByPlayerId[state.PlayerId] = state.SessionId;

        pair.Uses++;
        if (_config.MaxUses > 0 && pair.Uses >= _config.MaxUses)
        {
            pair.RemoveWhenUnused = true;
        }

        _sounds.PlayRideStart(player);
        message = "Zipline.RideStarted";
        return true;
    }

    public bool DetachForPlayer(IPlayer player, ZiplineDetachReason reason)
    {
        if (player is null || !_sessionIdByPlayerId.TryGetValue(player.PlayerID, out var sessionId) || sessionId != player.SessionId)
        {
            return false;
        }

        return DetachAndRestore(sessionId, reason);
    }

    public void DetachForPlayerId(int playerId, ZiplineDetachReason reason)
    {
        if (_sessionIdByPlayerId.TryGetValue(playerId, out var sessionId))
        {
            DetachAndRestore(sessionId, reason);
        }
    }

    public void DetachForPair(int pairId, ZiplineDetachReason reason)
    {
        _detachQueue.Clear();
        foreach (var entry in _ridersBySessionId)
        {
            if (entry.Value.PairId == pairId)
            {
                _detachQueue.Add((entry.Key, reason));
            }
        }

        foreach (var entry in _detachQueue)
        {
            DetachAndRestore(entry.SessionId, entry.Reason);
        }

        _detachQueue.Clear();
    }

    public void ClearAll(ZiplineDetachReason reason)
    {
        _detachQueue.Clear();
        foreach (var entry in _ridersBySessionId)
        {
            _detachQueue.Add((entry.Key, reason));
        }

        foreach (var entry in _detachQueue)
        {
            DetachAndRestore(entry.SessionId, entry.Reason);
        }

        _detachQueue.Clear();
        _ridersBySessionId.Clear();
        _sessionIdByPlayerId.Clear();
    }

    public void OnTick()
    {
        if (_ridersBySessionId.Count == 0)
        {
            return;
        }

        _detachQueue.Clear();
        var now = _core.Engine.GlobalVars.CurrentTime;
        foreach (var entry in _ridersBySessionId)
        {
            var reason = DriveRider(entry.Value, now);
            if (reason is { } detachReason)
            {
                _detachQueue.Add((entry.Key, detachReason));
            }
        }

        foreach (var entry in _detachQueue)
        {
            DetachAndRestore(entry.SessionId, entry.Reason);
        }

        _detachQueue.Clear();
    }

    private ZiplineDetachReason? DriveRider(RiderState state, float now)
    {
        if (MapGenerationResolver is null || PairResolver is null || state.MapGeneration != MapGenerationResolver())
        {
            return ZiplineDetachReason.Safety;
        }

        var player = _core.PlayerManager.GetPlayerFromSessionId(state.SessionId);
        if (player is null || !TryGetUsablePlayerPawn(player, out var pawn) || player.SessionId != state.SessionId || player.IsFakeClient)
        {
            return ZiplineDetachReason.Safety;
        }

        if (!TryGetStatePawn(state, pawn))
        {
            return ZiplineDetachReason.Safety;
        }

        var pair = PairResolver(state.PairId);
        if (pair is null || !_entities.IsPairReady(pair))
        {
            return ZiplineDetachReason.PairRemoved;
        }

        if (state.MoveTypeOverridden && now >= state.FlyEndsAt)
        {
            RestoreMoveType(pawn, state);
        }

        var from = state.MovingFromA ? pair.AnchorA.CablePosition : pair.AnchorB.CablePosition;
        var to = state.MovingFromA ? pair.AnchorB.CablePosition : pair.AnchorA.CablePosition;
        var segment = to - from;
        var segmentLengthSquared = ZiplineMath.LengthSquared(segment);
        if (segmentLengthSquared <= 0.01f || !ZiplineMath.TryNormalize(segment, out var direction))
        {
            return ZiplineDetachReason.Safety;
        }

        var eyePosition = pawn.EyePosition;
        if (eyePosition is null)
        {
            return ZiplineDetachReason.Safety;
        }

        // The cable is a head-level attachment. Aligning AbsOrigin would align
        // the player's feet to it and make the rider appear to stand on the rope.
        var position = new Vector(eyePosition.Value.X, eyePosition.Value.Y, eyePosition.Value.Z);
        var progress = ZiplineMath.Dot(position - from, segment) / segmentLengthSquared;
        if (!float.IsFinite(progress) || progress > 1.02f)
        {
            return ZiplineDetachReason.Arrived;
        }

        var closestProgress = Math.Clamp(progress, 0.0f, 1.0f);
        var closestPoint = from + segment * closestProgress;
        if (ZiplineMath.DistanceSquared(position, to) <= _config.ArrivalDistance * _config.ArrivalDistance)
        {
            return ZiplineDetachReason.Arrived;
        }

        if (closestProgress > state.LastProgress + 0.0025f)
        {
            state.LastProgress = closestProgress;
            state.LastProgressTime = now;
        }
        else if (now - state.LastProgressTime >= _config.StallTimeoutSeconds)
        {
            return ZiplineDetachReason.Stalled;
        }

        var alignment = ZiplineMath.ClampLength(closestPoint - position, _config.AlignmentSpeed);
        // Teleport with only velocity calls the engine's movement path. Directly
        // writing AbsVelocity and VelocityUpdated every tick breaks pawn sync.
        pawn.Teleport(null, null, direction * _config.RideSpeed + alignment);

        if (now >= state.NextLoopSoundTime)
        {
            _sounds.PlayRideLoop(player);
            state.NextLoopSoundTime = now + _config.RideLoopInterval;
        }

        return null;
    }

    private bool DetachAndRestore(ulong sessionId, ZiplineDetachReason reason)
    {
        if (!_ridersBySessionId.Remove(sessionId, out var state))
        {
            return false;
        }

        if (_sessionIdByPlayerId.TryGetValue(state.PlayerId, out var mappedSessionId) && mappedSessionId == sessionId)
        {
            _sessionIdByPlayerId.Remove(state.PlayerId);
        }

        IPlayer? player = null;
        var currentPlayer = _core.PlayerManager.GetPlayerFromSessionId(sessionId);
        if (currentPlayer is not null
            && TryGetUsablePlayerPawn(currentPlayer, out var pawn)
            && currentPlayer.SessionId == sessionId
            && TryGetStatePawn(state, pawn))
        {
            RestoreMovement(pawn, state, reason);
            player = currentPlayer;
        }

        if (player is not null && reason is not ZiplineDetachReason.Disconnect and not ZiplineDetachReason.MapUnload and not ZiplineDetachReason.PluginUnload)
        {
            _sounds.PlayRideEnd(player);
        }

        if (PairResolver?.Invoke(state.PairId) is { RemoveWhenUnused: true } pair && !HasRiderForPair(pair.Id))
        {
            PairBecameUnused?.Invoke(pair);
        }

        return true;
    }

    private bool HasRiderForPair(int pairId)
    {
        foreach (var state in _ridersBySessionId.Values)
        {
            if (state.PairId == pairId)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetUsablePlayerPawn(IPlayer? player, out CCSPlayerPawn pawn)
    {
        pawn = null!;
        if (player is null || !player.IsValid || player.IsFakeClient || !player.IsAlive || player.PlayerPawn is not { IsValid: true } playerPawn)
        {
            return false;
        }

        pawn = playerPawn;
        return true;
    }

    private static bool TryGetStatePawn(RiderState state, CCSPlayerPawn currentPawn)
    {
        if (!state.PawnHandle.IsValid || state.PawnHandle.Value is not { IsValid: true } storedPawn)
        {
            return false;
        }

        return storedPawn.Index == currentPawn.Index;
    }

    private void ApplyRideMovement(CCSPlayerPawn pawn, RiderState state, float now)
    {
        if (_config.RideFlyDurationSeconds > 0.0f)
        {
            pawn.MoveType = MoveType_t.MOVETYPE_FLY;
            pawn.MoveTypeUpdated();
            state.MoveTypeOverridden = true;
            state.FlyEndsAt = now + _config.RideFlyDurationSeconds;
        }

        pawn.Teleport(null, null, Vector.Zero);
    }

    private static void RestoreMovement(CCSPlayerPawn pawn, RiderState state, ZiplineDetachReason reason)
    {
        RestoreMoveType(pawn, state);
        if (reason is not ZiplineDetachReason.Arrived and not ZiplineDetachReason.Manual)
        {
            pawn.Teleport(null, null, Vector.Zero);
        }
    }

    private static void RestoreMoveType(CCSPlayerPawn pawn, RiderState state)
    {
        if (!state.MoveTypeOverridden)
        {
            return;
        }

        pawn.MoveType = state.OriginalMoveType;
        pawn.MoveTypeUpdated();
        state.MoveTypeOverridden = false;
        state.FlyEndsAt = 0.0f;
    }
}
