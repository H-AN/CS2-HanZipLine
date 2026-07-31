using CS2HanZipLine.Models;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace CS2HanZipLine.Services;

public sealed class ZiplineService(
    ISwiftlyCore core,
    ZiplinePlacementService placement,
    ZiplineEntityService entities,
    ZiplineRideService rides,
    ZiplineSoundService sounds)
{
    private readonly ISwiftlyCore _core = core;
    private readonly ZiplinePlacementService _placement = placement;
    private readonly ZiplineEntityService _entities = entities;
    private readonly ZiplineRideService _rides = rides;
    private readonly ZiplineSoundService _sounds = sounds;
    private readonly Dictionary<int, ZiplinePair> _pairsById = [];
    private readonly Dictionary<int, ZiplinePair> _pendingPairsById = [];
    private readonly Dictionary<ulong, float> _lastCreateTimeBySteamId = [];
    private readonly Dictionary<ulong, BotZiplineState> _botStatesBySessionId = [];
    private readonly List<int> _pairIdsToRemove = [];
    private readonly List<int> _pendingPairIdsToStart = [];
    private readonly List<int> _pendingPairIdsToCancel = [];
    private ZiplineConfig _config = new();
    private int _nextPairId = 1;
    private bool _adminVisionEnabled;
    private string[] _adminPermissions = [];

    public Func<int>? MapGenerationResolver { get; set; }
    public bool ClearEachRoundEnabled => _config.ClearEachRound;
    public bool AdminVisionEnabled => _adminVisionEnabled;
    public int ActivePairCount => _pairsById.Count + _pendingPairsById.Count;

    public void ConfigureRuntime(Func<int> mapGenerationResolver)
    {
        MapGenerationResolver = mapGenerationResolver;
        _rides.MapGenerationResolver = mapGenerationResolver;
        _rides.PairResolver = TryGetPair;
        _rides.CanUsePair = CanUsePair;
        _rides.PairBecameUnused = pair => RemovePair(pair.Id, ZiplineDetachReason.PairRemoved);
        _rides.RiderDetached = HandleRiderDetached;
    }

    public void UpdateConfig(ZiplineConfig config)
    {
        _config = config.CloneNormalized();
        _adminPermissions = ParseAdminPermissions(_config.AdminPermissions);
        _placement.UpdateConfig(_config);
        _entities.UpdateConfig(_config);
        _rides.UpdateConfig(_config);
        _sounds.UpdateConfig(_config);
        RefreshAdminVision();
    }

    public void OnPrecacheResource(SwiftlyS2.Shared.Events.IOnPrecacheResourceEvent @event) => _entities.OnPrecacheResource(@event);

    public bool CanManage(IPlayer player)
    {
        if (!TryGetHumanPlayer(player, requireAlive: false))
        {
            return false;
        }

        foreach (var permission in _adminPermissions)
        {
            if (_core.Permission.PlayerHasPermission(player.SteamID, permission))
            {
                return true;
            }
        }

        return false;
    }

    public bool TryCreateForPlayer(IPlayer player, out string message)
    {
        if (!_config.Enable)
        {
            message = "Zipline.ErrorDisabled";
            return false;
        }

        if (player is null || !TryGetHumanPlayer(player, requireAlive: true))
        {
            message = "Zipline.ErrorPlayerUnavailable";
            return false;
        }

        if (_config.AdminOnlyCreate && !CanManage(player))
        {
            message = "Zipline.ErrorCreatePermission";
            return false;
        }

        if (HasPendingOwner(player.SessionId))
        {
            message = "Zipline.ErrorCreatePending";
            return false;
        }

        var now = _core.Engine.GlobalVars.CurrentTime;
        if (_lastCreateTimeBySteamId.TryGetValue(player.SteamID, out var lastCreateTime)
            && now - lastCreateTime < _config.CreateCooldownSeconds)
        {
            message = "Zipline.ErrorCreateCooldown";
            return false;
        }

        if (CountOwnedPairs(player.SteamID) >= _config.MaxPerPlayer)
        {
            message = "Zipline.ErrorPlayerLimit";
            return false;
        }

        if (!_placement.TryBuildPlacement(player, out var start, out var end, out message))
        {
            return false;
        }

        if (!TryGetPlayerZiplineTeam(player, out var team))
        {
            message = "Zipline.ErrorPlayerUnavailable";
            return false;
        }

        if (!TryCreatePair(player.SteamID, player.SessionId, start, end, team, isMapPlaced: false, useRealisticBuild: _config.RealisticBuild, out message))
        {
            return false;
        }

        _lastCreateTimeBySteamId[player.SteamID] = now;
        return true;
    }

    public bool TryCreateForAdministrator(IPlayer player, ZiplineTeam team, out string message)
    {
        if (!_config.Enable)
        {
            message = "Zipline.ErrorDisabled";
            return false;
        }

        if (!CanManage(player))
        {
            message = "Zipline.ErrorAdminPermission";
            return false;
        }

        if (!TryGetHumanPlayer(player, requireAlive: true))
        {
            message = "Zipline.ErrorPlayerUnavailable";
            return false;
        }

        if (!_placement.TryBuildPlacement(player, out var start, out var end, out message))
        {
            return false;
        }

        return TryCreatePair(
            player.SteamID,
            player.SessionId,
            start,
            end,
            team,
            isMapPlaced: false,
            useRealisticBuild: _config.RealisticBuild,
            out message);
    }

    public bool TryDeleteOwnedAtAim(IPlayer player, out string message)
    {
        if (!TryGetHumanPlayer(player, requireAlive: true))
        {
            message = "Zipline.ErrorPlayerUnavailable";
            return false;
        }

        if (!_placement.TryTraceTargetSurface(player, out var aimedPoint, out _))
        {
            message = "Zipline.ErrorAimSurface";
            return false;
        }

        ZiplinePair? nearest = null;
        var deletionRadiusSquared = _config.UseRadius * _config.UseRadius;
        var nearestDistanceSquared = float.MaxValue;
        foreach (var pair in _pairsById.Values)
        {
            if (pair.OwnerSteamId != player.SteamID)
            {
                continue;
            }

            var distanceA = ZiplineMath.DistanceSquared(aimedPoint, pair.AnchorA.BasePosition);
            var distanceB = ZiplineMath.DistanceSquared(aimedPoint, pair.AnchorB.BasePosition);
            var distance = Math.Min(distanceA, distanceB);
            if (distance <= deletionRadiusSquared && distance < nearestDistanceSquared)
            {
                nearest = pair;
                nearestDistanceSquared = distance;
            }
        }

        if (nearest is null)
        {
            message = "Zipline.ErrorNoOwnedPairAtAim";
            return false;
        }

        RemovePair(nearest.Id, ZiplineDetachReason.PairRemoved);
        message = "Zipline.PairRemoved";
        return true;
    }

    public bool TryDeleteAnyAtAim(IPlayer player, out string message)
    {
        if (!CanManage(player))
        {
            message = "Zipline.ErrorAdminPermission";
            return false;
        }

        if (!TryGetHumanPlayer(player, requireAlive: true))
        {
            message = "Zipline.ErrorPlayerUnavailable";
            return false;
        }

        if (!_placement.TryGetAimRay(player, out var eyePosition, out var direction))
        {
            message = "Zipline.ErrorAimSurface";
            return false;
        }

        var nearest = FindPairAtAim(eyePosition, direction);
        if (nearest is null)
        {
            message = "Zipline.ErrorNoPairAtAim";
            return false;
        }

        RemovePair(nearest.Id, ZiplineDetachReason.PairRemoved);
        message = "Zipline.PairRemoved";
        return true;
    }

    public IReadOnlyList<ZiplineMapEntry> GetMapEntries()
    {
        var entries = new List<ZiplineMapEntry>(_pairsById.Count);
        foreach (var pair in _pairsById.Values)
        {
            entries.Add(ZiplineMapEntry.FromPair(pair));
        }

        return entries;
    }

    public int CreateMapPairs(IEnumerable<ZiplineMapEntry> entries)
    {
        var created = 0;
        foreach (var entry in entries)
        {
            if (!entry.TryGetSurfaces(out var startPosition, out var startNormal, out var endPosition, out var endNormal)
                || !ZiplinePlacementService.TryCreateSavedPlacement(startPosition, startNormal, out var start)
                || !ZiplinePlacementService.TryCreateSavedPlacement(endPosition, endNormal, out var end))
            {
                continue;
            }

            if (!TryCreatePair(0, 0, start, end, entry.Team, isMapPlaced: true, useRealisticBuild: false, out var message))
            {
                if (message == "Zipline.ErrorGlobalLimit")
                {
                    break;
                }

                continue;
            }

            created++;
        }

        return created;
    }

    public void SetAdminVisionEnabled(bool enabled)
    {
        _adminVisionEnabled = enabled;
        _entities.SetAdminVisionEnabled(enabled);
        RefreshAdminVision();
    }

    public void HandleKeyStateChanged(SwiftlyS2.Shared.Events.IOnClientKeyStateChangedEvent @event)
    {
        if (!_config.Enable || !@event.Pressed || !string.Equals(@event.Key.ToString(), "F", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var player = _core.PlayerManager.GetPlayer(@event.PlayerId);
        if (player is null || !TryGetHumanPlayer(player, requireAlive: true))
        {
            return;
        }

        if (_rides.IsRiding(player))
        {
            _rides.DetachForPlayer(player, ZiplineDetachReason.Manual);
            return;
        }

        if (!TryFindNearestUsableAnchor(player, _config.UseRadius, out var pair, out var movingFromA))
        {
            return;
        }

        _rides.TryAttach(player, pair, movingFromA, ResolveMapGeneration(), out _);
    }

    public void HandlePlayerDeath(int playerId) => _rides.DetachForPlayerId(playerId, ZiplineDetachReason.Death);

    public void HandleClientDisconnected(int playerId)
    {
        _rides.DetachForPlayerId(playerId, ZiplineDetachReason.Disconnect);
        RemoveBotStateForPlayerId(playerId);
    }

    public void HandleRoundEnd()
    {
        if (_config.ClearEachRound)
        {
            ClearAll(ZiplineDetachReason.PairRemoved);
        }
    }

    public void HandleMapUnload() => ClearAll(ZiplineDetachReason.MapUnload);

    public void OnTick()
    {
        if (!_config.Enable)
        {
            if (_pairsById.Count > 0 || _pendingPairsById.Count > 0)
            {
                ClearAll(ZiplineDetachReason.Safety);
            }

            return;
        }

        _pairIdsToRemove.Clear();
        var now = _core.Engine.GlobalVars.CurrentTime;
        AdvanceBuildFlights(now);
        foreach (var pair in _pairsById.Values)
        {
            if (pair.IsExpired(now) || !_entities.IsPairReady(pair))
            {
                _pairIdsToRemove.Add(pair.Id);
            }
        }

        foreach (var pairId in _pairIdsToRemove)
        {
            RemovePair(pairId, ZiplineDetachReason.PairRemoved);
        }

        _pairIdsToRemove.Clear();
        DriveBots(now);
        _rides.OnTick();
    }

    public void ClearAll(ZiplineDetachReason reason)
    {
        _pairIdsToRemove.Clear();
        foreach (var pair in _pairsById.Values)
        {
            _pairIdsToRemove.Add(pair.Id);
        }

        foreach (var pairId in _pairIdsToRemove)
        {
            RemovePair(pairId, reason);
        }

        _pairIdsToRemove.Clear();
        foreach (var pendingPair in _pendingPairsById.Values)
        {
            _entities.DestroyPairEntities(pendingPair);
        }

        _pendingPairsById.Clear();
        _pendingPairIdsToStart.Clear();
        _pendingPairIdsToCancel.Clear();
        _lastCreateTimeBySteamId.Clear();
        _rides.ClearAll(reason);
        _botStatesBySessionId.Clear();
    }

    private bool TryCreatePair(
        ulong ownerSteamId,
        ulong ownerSessionId,
        AnchorPlacement start,
        AnchorPlacement end,
        ZiplineTeam team,
        bool isMapPlaced,
        bool useRealisticBuild,
        out string message)
    {
        if (!_config.Enable)
        {
            message = "Zipline.ErrorDisabled";
            return false;
        }

        if (ActivePairCount >= _config.MaxActivePairs)
        {
            message = "Zipline.ErrorGlobalLimit";
            return false;
        }

        if (!ValidateEstimatedPair(start, end, out message))
        {
            return false;
        }

        var now = _core.Engine.GlobalVars.CurrentTime;
        var expiresAt = !isMapPlaced && _config.LifetimeSeconds > 0.0f
            ? now + _config.LifetimeSeconds
            : 0.0f;
        var pair = new ZiplinePair(
            _nextPairId++,
            ownerSteamId,
            ownerSessionId,
            new ZiplineAnchor(start.SurfacePosition, start.SurfaceNormal, start.Angles),
            new ZiplineAnchor(end.SurfacePosition, end.SurfaceNormal, end.Angles),
            now,
            expiresAt,
            team,
            isMapPlaced);

        _pendingPairsById[pair.Id] = pair;
        if (useRealisticBuild)
        {
            if (_entities.TryPrepareBuildPairNextTick(pair, ResolveMapGeneration(), ResolveMapGeneration, OnBuildPairPrepared))
            {
                message = "Zipline.Creating";
                return true;
            }
        }
        else if (_entities.TryCreatePairNextTick(pair, ResolveMapGeneration(), ResolveMapGeneration, OnPairCreated))
        {
            message = "Zipline.Creating";
            return true;
        }

        _pendingPairsById.Remove(pair.Id);
        _entities.DestroyPairEntities(pair);
        message = "Zipline.ErrorEntityCreate";
        return false;
    }

    private ZiplinePair? FindPairAtAim(Vector eyePosition, Vector direction)
    {
        ZiplinePair? nearest = null;
        var bestPerpendicularDistanceSquared = _config.UseRadius * _config.UseRadius;
        var bestAlongDistance = float.MaxValue;
        foreach (var pair in _pairsById.Values)
        {
            ConsiderAdminAimPoint(pair, pair.AnchorA.BasePosition, eyePosition, direction, ref nearest, ref bestPerpendicularDistanceSquared, ref bestAlongDistance);
            ConsiderAdminAimPoint(pair, pair.AnchorA.CablePosition, eyePosition, direction, ref nearest, ref bestPerpendicularDistanceSquared, ref bestAlongDistance);
            ConsiderAdminAimPoint(pair, pair.AnchorB.BasePosition, eyePosition, direction, ref nearest, ref bestPerpendicularDistanceSquared, ref bestAlongDistance);
            ConsiderAdminAimPoint(pair, pair.AnchorB.CablePosition, eyePosition, direction, ref nearest, ref bestPerpendicularDistanceSquared, ref bestAlongDistance);
        }

        return nearest;
    }

    private static void ConsiderAdminAimPoint(
        ZiplinePair candidate,
        Vector point,
        Vector eyePosition,
        Vector direction,
        ref ZiplinePair? nearest,
        ref float bestPerpendicularDistanceSquared,
        ref float bestAlongDistance)
    {
        var toPoint = point - eyePosition;
        var alongDistance = ZiplineMath.Dot(toPoint, direction);
        if (alongDistance < 0.0f)
        {
            return;
        }

        var perpendicularDistanceSquared = ZiplineMath.DistanceSquared(point, eyePosition + direction * alongDistance);
        if (perpendicularDistanceSquared > bestPerpendicularDistanceSquared
            || (perpendicularDistanceSquared >= bestPerpendicularDistanceSquared && alongDistance >= bestAlongDistance))
        {
            return;
        }

        nearest = candidate;
        bestPerpendicularDistanceSquared = perpendicularDistanceSquared;
        bestAlongDistance = alongDistance;
    }

    private void RefreshAdminVision()
    {
        _entities.SetAdminVisionEnabled(_adminVisionEnabled);
        foreach (var pair in _pairsById.Values)
        {
            _entities.ApplyAdminVision(pair);
        }

        foreach (var pair in _pendingPairsById.Values)
        {
            _entities.ApplyAdminVision(pair);
        }
    }

    private void AdvanceBuildFlights(float currentTime)
    {
        _pendingPairIdsToStart.Clear();
        _pendingPairIdsToCancel.Clear();

        foreach (var pair in _pendingPairsById.Values)
        {
            if (!pair.IsBuildFlightActive)
            {
                continue;
            }

            if (!_entities.TryAdvanceBuildFlight(pair, currentTime, out var arrived))
            {
                _pendingPairIdsToCancel.Add(pair.Id);
                continue;
            }

            if (arrived)
            {
                _pendingPairIdsToStart.Add(pair.Id);
            }
        }

        foreach (var pairId in _pendingPairIdsToCancel)
        {
            if (!_pendingPairsById.Remove(pairId, out var pair))
            {
                continue;
            }

            _entities.DestroyPairEntities(pair);
            NotifyOwner(pair.OwnerSessionId, "Zipline.ErrorEntityCreate");
        }

        foreach (var pairId in _pendingPairIdsToStart)
        {
            if (!_pendingPairsById.TryGetValue(pairId, out var pair))
            {
                continue;
            }

            if (_entities.TryCreateCompletedBuildAnchorNextTick(pair, ResolveMapGeneration(), ResolveMapGeneration, OnPairCreated))
            {
                continue;
            }

            _pendingPairsById.Remove(pair.Id);
            _entities.DestroyPairEntities(pair);
            NotifyOwner(pair.OwnerSessionId, "Zipline.ErrorEntityCreate");
        }
    }

    private void OnPairCreated(ZiplinePair pair, bool success, string failureMessage)
    {
        if (!_pendingPairsById.Remove(pair.Id))
        {
            if (success)
            {
                _entities.DestroyPairEntities(pair);
            }

            return;
        }

        if (!success)
        {
            _entities.DestroyPairEntities(pair);
            NotifyOwner(pair.OwnerSessionId, failureMessage);
            return;
        }

        var finalDistanceSquared = ZiplineMath.DistanceSquared(pair.AnchorA.CablePosition, pair.AnchorB.CablePosition);
        if (!IsWithinDistanceRange(finalDistanceSquared))
        {
            _entities.DestroyPairEntities(pair);
            NotifyOwner(pair.OwnerSessionId, "Zipline.ErrorDistance");
            return;
        }

        _pairsById[pair.Id] = pair;
        var owner = _core.PlayerManager.GetPlayerFromSessionId(pair.OwnerSessionId);
        if (owner is not null && TryGetHumanPlayer(owner, requireAlive: false))
        {
            _sounds.PlayCreate(owner);
            NotifyOwner(pair.OwnerSessionId, "Zipline.PairCreated");
        }
    }

    private void OnBuildPairPrepared(ZiplinePair pair, bool success, string failureMessage)
    {
        if (!_pendingPairsById.ContainsKey(pair.Id))
        {
            if (success)
            {
                _entities.DestroyPairEntities(pair);
            }

            return;
        }

        if (!success)
        {
            _pendingPairsById.Remove(pair.Id);
            _entities.DestroyPairEntities(pair);
            NotifyOwner(pair.OwnerSessionId, failureMessage);
            return;
        }

        var finalDistanceSquared = ZiplineMath.DistanceSquared(pair.AnchorA.CablePosition, pair.AnchorB.CablePosition);
        if (!IsWithinDistanceRange(finalDistanceSquared))
        {
            _pendingPairsById.Remove(pair.Id);
            _entities.DestroyPairEntities(pair);
            NotifyOwner(pair.OwnerSessionId, "Zipline.ErrorDistance");
            return;
        }

        if (_entities.TryStartBuildFlight(pair, ResolveMapGeneration(), ResolveMapGeneration, OnBuildFlightInitialized))
        {
            return;
        }

        _pendingPairsById.Remove(pair.Id);
        _entities.DestroyPairEntities(pair);
        NotifyOwner(pair.OwnerSessionId, "Zipline.ErrorEntityCreate");
    }

    private void OnBuildFlightInitialized(ZiplinePair pair, bool success)
    {
        if (!_pendingPairsById.ContainsKey(pair.Id))
        {
            if (success)
            {
                _entities.DestroyPairEntities(pair);
            }

            return;
        }

        if (!success)
        {
            _pendingPairsById.Remove(pair.Id);
            _entities.DestroyPairEntities(pair);
            NotifyOwner(pair.OwnerSessionId, "Zipline.ErrorEntityCreate");
            return;
        }

        var owner = _core.PlayerManager.GetPlayerFromSessionId(pair.OwnerSessionId);
        if (owner is not null && TryGetHumanPlayer(owner, requireAlive: false))
        {
            _sounds.PlayBuild(owner);
        }
    }

    private void DriveBots(float now)
    {
        if (!_config.BotAllowUse)
        {
            _botStatesBySessionId.Clear();
            return;
        }

        foreach (var bot in _core.PlayerManager.GetBots())
        {
            if (!TryGetLiveBotPawn(bot, out var pawn))
            {
                continue;
            }

            if (!_botStatesBySessionId.TryGetValue(bot.SessionId, out var state))
            {
                state = new BotZiplineState { PlayerId = bot.PlayerID };
                _botStatesBySessionId[bot.SessionId] = state;
            }

            if (_rides.IsRiding(bot) || now < state.NextEligibleAt)
            {
                continue;
            }

            if (!TryFindNearestUsableAnchor(bot, _config.BotUseRange, out var pair, out var movingFromA))
            {
                ResetBotTarget(state);
                continue;
            }

            if (state.TargetPairId != pair.Id || state.MovingFromA != movingFromA)
            {
                state.TargetPairId = pair.Id;
                state.MovingFromA = movingFromA;
                state.TargetStartedAt = now;
            }

            if (_config.BotTargetTimeoutSeconds > 0.0f
                && now - state.TargetStartedAt >= _config.BotTargetTimeoutSeconds)
            {
                state.NextEligibleAt = now + _config.BotUseCooldownSeconds;
                ResetBotTarget(state);
                continue;
            }

            var target = movingFromA ? pair.AnchorA.BasePosition : pair.AnchorB.BasePosition;
            if (pawn.AbsOrigin is not { } pawnPosition)
            {
                continue;
            }

            if (ZiplineMath.DistanceSquared(pawnPosition, target) <= _config.UseRadius * _config.UseRadius)
            {
                if (_rides.TryAttach(bot, pair, movingFromA, ResolveMapGeneration(), out _))
                {
                    state.NextEligibleAt = now + _config.BotUseCooldownSeconds;
                    ResetBotTarget(state);
                }

                continue;
            }

            DriveBotToAnchor(pawn, target);
        }
    }

    private void DriveBotToAnchor(CCSPlayerPawn pawn, Vector target)
    {
        if (pawn.AbsOrigin is not { } pawnPosition)
        {
            return;
        }

        var horizontalDirection = new Vector(
            target.X - pawnPosition.X,
            target.Y - pawnPosition.Y,
            0.0f);
        if (!ZiplineMath.TryNormalize(horizontalDirection, out horizontalDirection))
        {
            return;
        }

        // Keep the bot's view untouched; only the engine movement path supplies an approach velocity.
        pawn.Teleport(null, null, horizontalDirection * _config.BotApproachSpeed);
    }

    private void HandleRiderDetached(RiderState rider, ZiplineDetachReason _)
    {
        if (_botStatesBySessionId.TryGetValue(rider.SessionId, out var state))
        {
            state.NextEligibleAt = _core.Engine.GlobalVars.CurrentTime + _config.BotUseCooldownSeconds;
            ResetBotTarget(state);
        }
    }

    private void RemoveBotStateForPlayerId(int playerId)
    {
        ulong? sessionIdToRemove = null;
        foreach (var entry in _botStatesBySessionId)
        {
            if (entry.Value.PlayerId == playerId)
            {
                sessionIdToRemove = entry.Key;
                break;
            }
        }

        if (sessionIdToRemove is { } sessionId)
        {
            _botStatesBySessionId.Remove(sessionId);
        }
    }

    private static void ResetBotTarget(BotZiplineState state)
    {
        state.TargetPairId = -1;
        state.MovingFromA = false;
        state.TargetStartedAt = 0.0f;
    }

    private static bool TryGetLiveBotPawn(IPlayer player, out CCSPlayerPawn pawn)
    {
        pawn = null!;
        if (player is not { IsValid: true, IsFakeClient: true, IsAlive: true, PlayerPawn: { IsValid: true } playerPawn })
        {
            return false;
        }

        pawn = playerPawn;
        return true;
    }

    private bool TryFindNearestUsableAnchor(IPlayer player, float radius, out ZiplinePair pair, out bool movingFromA)
    {
        pair = null!;
        movingFromA = true;
        if (player.PlayerPawn?.AbsOrigin is not { } pawnPosition)
        {
            return false;
        }

        var now = _core.Engine.GlobalVars.CurrentTime;
        var radiusSquared = radius * radius;
        var nearestDistanceSquared = float.MaxValue;
        foreach (var candidate in _pairsById.Values)
        {
            if (candidate.IsExpired(now)
                || candidate.RemoveWhenUnused
                || (_config.MaxUses > 0 && candidate.Uses >= _config.MaxUses)
                || !_entities.IsPairReady(candidate)
                || !CanUsePair(player, candidate))
            {
                continue;
            }

            var distanceA = ZiplineMath.DistanceSquared(pawnPosition, candidate.AnchorA.BasePosition);
            if (distanceA <= radiusSquared && distanceA < nearestDistanceSquared)
            {
                pair = candidate;
                movingFromA = true;
                nearestDistanceSquared = distanceA;
            }

            var distanceB = ZiplineMath.DistanceSquared(pawnPosition, candidate.AnchorB.BasePosition);
            if (distanceB <= radiusSquared && distanceB < nearestDistanceSquared)
            {
                pair = candidate;
                movingFromA = false;
                nearestDistanceSquared = distanceB;
            }
        }

        return pair is not null;
    }

    private void RemovePair(int pairId, ZiplineDetachReason reason)
    {
        if (!_pairsById.Remove(pairId, out var pair))
        {
            return;
        }

        _rides.DetachForPair(pairId, reason);
        _entities.DestroyPairEntities(pair);
    }

    private bool ValidateEstimatedPair(AnchorPlacement start, AnchorPlacement end, out string message)
    {
        var distanceSquared = ZiplineMath.DistanceSquared(start.SurfacePosition, end.SurfacePosition);
        if (!IsWithinDistanceRange(distanceSquared))
        {
            message = "Zipline.ErrorDistance";
            return false;
        }

        var separationSquared = _config.AnchorSeparation * _config.AnchorSeparation;
        foreach (var pair in _pairsById.Values)
        {
            if (IsPlacementTooClose(start.SurfacePosition, pair.AnchorA.BasePosition, separationSquared)
                || IsPlacementTooClose(start.SurfacePosition, pair.AnchorB.BasePosition, separationSquared)
                || IsPlacementTooClose(end.SurfacePosition, pair.AnchorA.BasePosition, separationSquared)
                || IsPlacementTooClose(end.SurfacePosition, pair.AnchorB.BasePosition, separationSquared))
            {
                message = "Zipline.ErrorAnchorSeparation";
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    private bool IsWithinDistanceRange(float distanceSquared)
    {
        return distanceSquared >= _config.MinDistance * _config.MinDistance
            && distanceSquared <= _config.MaxDistance * _config.MaxDistance;
    }

    private int CountOwnedPairs(ulong steamId)
    {
        var count = 0;
        foreach (var pair in _pairsById.Values)
        {
            if (pair.OwnerSteamId == steamId)
            {
                count++;
            }
        }

        foreach (var pair in _pendingPairsById.Values)
        {
            if (pair.OwnerSteamId == steamId)
            {
                count++;
            }
        }

        return count;
    }

    private bool HasPendingOwner(ulong sessionId)
    {
        foreach (var pair in _pendingPairsById.Values)
        {
            if (pair.OwnerSessionId == sessionId)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPlacementTooClose(Vector candidate, Vector existing, float separationSquared)
    {
        return ZiplineMath.DistanceSquared(candidate, existing) < separationSquared;
    }

    private int ResolveMapGeneration() => MapGenerationResolver?.Invoke() ?? 0;

    private bool CanUsePair(IPlayer player, ZiplinePair pair)
    {
        if (_config.AllowAllTeamsUseZiplines || pair.Team == ZiplineTeam.Global)
        {
            return true;
        }

        return TryGetPlayerZiplineTeam(player, out var playerTeam) && playerTeam == pair.Team;
    }

    private static bool TryGetPlayerZiplineTeam(IPlayer player, out ZiplineTeam team)
    {
        team = ZiplineTeam.Global;
        if (player.PlayerPawn is not { IsValid: true } pawn)
        {
            return false;
        }

        team = pawn.TeamNum switch
        {
            (int)Team.CT => ZiplineTeam.CT,
            (int)Team.T => ZiplineTeam.T,
            _ => ZiplineTeam.Global
        };

        return team is ZiplineTeam.CT or ZiplineTeam.T;
    }

    private static string[] ParseAdminPermissions(string? rawPermissions)
    {
        if (string.IsNullOrWhiteSpace(rawPermissions))
        {
            return [];
        }

        return rawPermissions
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static permission => permission.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private ZiplinePair? TryGetPair(int pairId)
    {
        return _pairsById.TryGetValue(pairId, out var pair) ? pair : null;
    }

    private void NotifyOwner(ulong sessionId, string messageKey)
    {
        var player = _core.PlayerManager.GetPlayerFromSessionId(sessionId);
        if (player is null || !TryGetHumanPlayer(player, requireAlive: false))
        {
            return;
        }

        player.SendMessage(MessageType.Chat, _core.Translation.GetPlayerLocalizer(player)[messageKey]);
    }

    private static bool TryGetHumanPlayer(IPlayer? player, bool requireAlive)
    {
        return player is { IsValid: true, IsFakeClient: false }
            && player.SteamID != 0
            && (!requireAlive || (player.IsAlive && player.PlayerPawn is { IsValid: true }));
    }
}
