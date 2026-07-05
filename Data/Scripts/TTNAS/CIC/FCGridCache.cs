using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using CoreSystems.Api;

namespace TTNAS
{
    public enum ContactRelation
    {
        Hostile,    // from GetSortedThreats
        Friendly,   // from GetObstructions (non-hostile entities WC tracks)
    }

    public interface IGridCacheSubscriber
    {
        void OnWeaponsRefreshed(
            List<WeaponEntry> allWeapons,
            List<WeaponEntry> staticLaunchers,
            List<WeaponEntry> turrets);

        void OnThreatsRefreshed(List<ThreatEntry> threats);
        void OnFriendliesRefreshed(List<ThreatEntry> friendlies);
    }

    public class ThreatEntry
    {
        public readonly MyEntity        Entity;
        public readonly string          DisplayName;
        public readonly float           ThreatScore;
        public readonly ContactRelation Relation;
        public readonly string          FactionTag;
        public double  RangeMetres;
        public float   SpeedMs;
        public int     IncomingCount;

        public string ListLabel =>
            "[" + FormatDist(RangeMetres) + " | " + SpeedMs.ToString("F0") + "m/s] " + DisplayName;

        public ThreatEntry(MyEntity entity, float threatScore,
            ContactRelation relation = ContactRelation.Hostile)
        {
            Entity      = entity;
            ThreatScore = threatScore;
            Relation    = relation;
            DisplayName = entity.DisplayName ?? entity.EntityId.ToString();

            FactionTag = "";
            try
            {
                var grid = entity as VRage.Game.ModAPI.IMyCubeGrid;
                if (grid != null && grid.BigOwners != null && grid.BigOwners.Count > 0)
                {
                    var faction = MyAPIGateway.Session?.Factions?
                        .TryGetPlayerFaction(grid.BigOwners[0]);
                    if (faction != null) FactionTag = faction.Tag ?? "";
                }
            }
            catch { }
        }

        private static string FormatDist(double m) =>
            m >= 1000.0 ? (m / 1000.0).ToString("F1") + "km" : m.ToString("F0") + "m";
    }

    public class GridCache
    {
        public readonly List<WeaponEntry>  AllWeapons      = new List<WeaponEntry>();
        public readonly List<WeaponEntry>  StaticLaunchers = new List<WeaponEntry>();
        public readonly List<WeaponEntry>  Turrets         = new List<WeaponEntry>();
        public readonly List<ThreatEntry>  Threats         = new List<ThreatEntry>();
        public readonly List<ThreatEntry>  Friendlies      = new List<ThreatEntry>();

        public bool                    MissilesInbound     = false;
        public int                     MissileInboundCount = 0;
        public readonly List<Vector3D> MissilePositions    = new List<Vector3D>();
        public readonly List<Vector3D> MissileVelocities   = new List<Vector3D>();

        public readonly List<Vector3D> OutboundPositions         = new List<Vector3D>();
        public readonly List<Vector3D> OutboundVelocities        = new List<Vector3D>();
        public readonly List<long>     OutboundFactionIds        = new List<long>();
        public readonly List<Vector3D> FriendlyMissilePositions  = new List<Vector3D>();
        public readonly List<Vector3D> FriendlyMissileVelocities = new List<Vector3D>();
        public readonly List<long>     FriendlyFactionIds        = new List<long>();
        public readonly List<Vector3D> HostileOtherPositions     = new List<Vector3D>();
        public readonly List<Vector3D> HostileOtherVelocities    = new List<Vector3D>();
        public readonly List<long>     HostileFactionIds         = new List<long>();
        public readonly List<long>     InboundFactionIds         = new List<long>();

        private readonly Dictionary<ulong, Vector3D> _prevProjPositions = new Dictionary<ulong, Vector3D>();
        private readonly HashSet<ulong> _ourProjectileIds  = new HashSet<ulong>();
        private readonly HashSet<ulong> _monitoredWeaponIds = new HashSet<ulong>();
        private readonly HashSet<long>  _ourOwnerFactionIds = new HashSet<long>();

        public int MonitoredWeaponCount => _monitoredWeaponIds.Count;
        public int OurProjectileCount  => _ourProjectileIds.Count;

        public void RegisterProjectileMonitors(WcApi wc)
        {
            _ourOwnerFactionIds.Clear();
            foreach (var weapon in AllWeapons)
            {
                if (weapon.Block == null || weapon.Block.MarkedForClose) continue;
                var ent = weapon.Block as VRage.Game.Entity.MyEntity;
                if (ent == null) continue;

                var tb = weapon.Block;
                if (tb.OwnerId != 0)
                {
                    var f = MyAPIGateway.Session.Factions.TryGetPlayerFaction(tb.OwnerId);
                    if (f != null) _ourOwnerFactionIds.Add(f.FactionId);
                }

                if (_monitoredWeaponIds.Contains((ulong)ent.EntityId)) continue;

                foreach (var kv in weapon.WeaponMap)
                {
                    int capturedId = kv.Value;
                    wc.AddProjectileCallback(ent, capturedId, OnProjectileFired);
                }
                _monitoredWeaponIds.Add((ulong)ent.EntityId);
            }
        }

        private void OnProjectileFired(long blockEntityId, int weaponId, ulong projectileId,
            long lastHitId, Vector3D lastPos, bool start)
        {
            if (start) _ourProjectileIds.Add(projectileId);
            else       _ourProjectileIds.Remove(projectileId);
        }

        private readonly IMyCubeGrid                _grid;
        private readonly List<IGridCacheSubscriber> _subscribers = new List<IGridCacheSubscriber>();
        private bool _weaponsDirty = true;

        public int SubscriberCount => _subscribers.Count;
        public int LastAccessTick;

        public GridCache(IMyCubeGrid grid)
        {
            _grid = grid;
            grid.OnBlockAdded   += _ => _weaponsDirty = true;
            grid.OnBlockRemoved += _ => _weaponsDirty = true;
        }

        public void Subscribe(IGridCacheSubscriber sub)
        {
            if (!_subscribers.Contains(sub)) _subscribers.Add(sub);
        }

        public void Unsubscribe(IGridCacheSubscriber sub)
        {
            _subscribers.Remove(sub);
        }

        public void MarkWeaponsDirty() { _weaponsDirty = true; }

        public bool TryRefreshWeapons(WcApi wc,
            List<MyDefinitionId> staticDefs, List<MyDefinitionId> turretDefs)
        {
            if (!_weaponsDirty) return false;
            _weaponsDirty = false;

            AllWeapons.Clear(); StaticLaunchers.Clear(); Turrets.Clear();

            var allBlocks = new List<IMySlimBlock>();
            _grid.GetBlocks(allBlocks);

            foreach (var slim in allBlocks)
            {
                var tb = slim.FatBlock as IMyTerminalBlock;
                if (tb == null) continue;

                var weaponMap = new Dictionary<string, int>();
                if (!wc.GetBlockWeaponMap(tb, weaponMap) || weaponMap.Count == 0) continue;

                var entry = new WeaponEntry(tb, weaponMap);
                var defId = slim.BlockDefinition.Id;
                AllWeapons.Add(entry);

                if (staticDefs.Contains(defId))      StaticLaunchers.Add(entry);
                else if (turretDefs.Contains(defId)) Turrets.Add(entry);
            }

            NotifyWeaponsRefreshed();
            return true;
        }

        public void RefreshThreats(WcApi wc)
        {
            Threats.Clear();
            var topGrid = _grid.GetTopMostParent() as MyEntity ?? _grid as MyEntity;
            if (topGrid == null) return;

            // Ensure our faction is cached — RefreshAllProjectiles may not have run yet.
            if (!_factionCached)
            {
                var bigOwners = _grid.BigOwners;
                if (bigOwners != null && bigOwners.Count > 0)
                {
                    var f = MyAPIGateway.Session.Factions.TryGetPlayerFaction(bigOwners[0]);
                    _ourFactionId  = f?.FactionId ?? 0;
                    _factionCached = true;
                }
            }

            var raw    = new List<MyTuple<MyEntity, float>>();
            wc.GetSortedThreats(topGrid, raw);

            var origin = _grid.PositionComp.GetPosition();
            var seen   = new Dictionary<long, float>();
            var resolved = new List<MyTuple<MyEntity, float>>();

            foreach (var t in raw)
            {
                var ent = t.Item1;
                if (ent == null || ent.MarkedForClose) continue;
                if (ent is VRage.Game.ModAPI.IMyCharacter) continue;
                if (ent.EntityId < 0) continue;

                MyEntity topEnt = ent.GetTopMostParent() ?? ent;
                var entGrid = topEnt as VRage.Game.ModAPI.IMyCubeGrid
                           ?? ent    as VRage.Game.ModAPI.IMyCubeGrid;

                long dedupeId = topEnt.EntityId;
                if (entGrid != null)
                {
                    var mechGroup = new List<VRage.Game.ModAPI.IMyCubeGrid>();
                    MyAPIGateway.GridGroups.GetGroup(entGrid, GridLinkTypeEnum.Mechanical, mechGroup);
                    if (mechGroup.Count > 0)
                    {
                        long minId = dedupeId;
                        foreach (var mg in mechGroup)
                            if (mg.EntityId < minId) minId = mg.EntityId;
                        dedupeId = minId;
                    }
                }

                float score = t.Item2;
                float existing;
                if (seen.TryGetValue(dedupeId, out existing) && existing >= score) continue;
                seen[dedupeId] = score;
                resolved.Add(new MyTuple<MyEntity, float>(topEnt, score));
            }

            // Per-call cache: factionId → isHostile. Avoids redundant API calls when
            // multiple grids belong to the same faction in one refresh cycle.
            var relationCache = new Dictionary<long, bool>();

            foreach (var t in resolved)
            {
                var topEnt = t.Item1;
                bool already = false;
                foreach (var ex in Threats)
                    if (ex.Entity != null && ex.Entity.EntityId == topEnt.EntityId)
                    { already = true; break; }
                if (already) continue;

                // Skip grids that are friendly to us — catches rogue subgrids/detached
                // pieces that WC still reports as threats but belong to allied factions.
                if (_ourFactionId != 0)
                {
                    var entGrid = topEnt as VRage.Game.ModAPI.IMyCubeGrid;
                    if (entGrid != null && entGrid.BigOwners != null && entGrid.BigOwners.Count > 0)
                    {
                        var entFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(entGrid.BigOwners[0]);
                        long entFactionId = entFaction != null ? entFaction.FactionId : 0;

                        if (entFactionId != 0 && entFactionId != _ourFactionId)
                        {
                            bool isHostile;
                            if (!relationCache.TryGetValue(entFactionId, out isHostile))
                            {
                                var rel = MyAPIGateway.Session.Factions
                                    .GetRelationBetweenFactions(_ourFactionId, entFactionId);
                                isHostile = rel != VRage.Game.MyRelationsBetweenFactions.Friends;
                                relationCache[entFactionId] = isHostile;
                            }
                            if (!isHostile) continue;
                        }
                    }
                }

                var entry = new ThreatEntry(topEnt, t.Item2);
                entry.RangeMetres   = Vector3D.Distance(origin, topEnt.PositionComp.GetPosition());
                var phys            = topEnt.Physics;
                entry.SpeedMs       = phys != null ? (float)phys.LinearVelocity.Length() : 0f;
                entry.IncomingCount = MissileInboundCount;
                Threats.Add(entry);
            }

            NotifyThreatsRefreshed();
        }

        public void RefreshFriendlies(WcApi wc)
        {
            Friendlies.Clear();
            var topGrid = _grid.GetTopMostParent() as MyEntity ?? _grid as MyEntity;
            if (topGrid == null) return;

            var raw    = new List<MyEntity>();
            wc.GetObstructions(topGrid, raw);

            var origin = _grid.PositionComp.GetPosition();
            var ownGridIds = new HashSet<long>();
            var ownGrids   = new List<VRage.Game.ModAPI.IMyCubeGrid>();
            MyAPIGateway.GridGroups.GetGroup(
                _grid as VRage.Game.ModAPI.IMyCubeGrid,
                GridLinkTypeEnum.Mechanical, ownGrids);
            foreach (var g in ownGrids) ownGridIds.Add(g.EntityId);
            ownGridIds.Add(_grid.EntityId);
            ownGridIds.Add(topGrid.EntityId);

            foreach (var ent in raw)
            {
                if (ent == null || ent.MarkedForClose) continue;
                var topEnt = ent.GetTopMostParent() ?? ent;

                if (topEnt.EntityId < 0 || ent.EntityId < 0) continue;
                if (topEnt is VRage.Game.ModAPI.IMyCharacter ||
                    ent    is VRage.Game.ModAPI.IMyCharacter) continue;
                if (ownGridIds.Contains(ent.EntityId)) continue;
                if (ownGridIds.Contains(topEnt.EntityId)) continue;

                var entGrid = topEnt as VRage.Game.ModAPI.IMyCubeGrid
                           ?? ent    as VRage.Game.ModAPI.IMyCubeGrid;
                if (entGrid != null && ownGridIds.Contains(entGrid.EntityId)) continue;

                if (entGrid != null)
                {
                    bool isThreatSubgrid = false;
                    var entMechGroup = new List<VRage.Game.ModAPI.IMyCubeGrid>();
                    MyAPIGateway.GridGroups.GetGroup(entGrid, GridLinkTypeEnum.Mechanical, entMechGroup);
                    foreach (var mg in entMechGroup)
                    {
                        foreach (var threat in Threats)
                        {
                            if (threat.Entity == null) continue;
                            var tGrid = threat.Entity as VRage.Game.ModAPI.IMyCubeGrid
                                     ?? threat.Entity.GetTopMostParent() as VRage.Game.ModAPI.IMyCubeGrid;
                            if (tGrid != null && tGrid.EntityId == mg.EntityId)
                            { isThreatSubgrid = true; break; }
                        }
                        if (isThreatSubgrid) break;
                    }
                    if (isThreatSubgrid) continue;
                }

                var entry = new ThreatEntry(topEnt, 0f, ContactRelation.Friendly);
                entry.RangeMetres = Vector3D.Distance(origin, topEnt.PositionComp.GetPosition());
                var phys = topEnt.Physics;
                entry.SpeedMs = phys != null ? (float)phys.LinearVelocity.Length() : 0f;
                Friendlies.Add(entry);
            }

            NotifyFriendliesRefreshed();
        }

        public void RefreshMissiles(WcApi wc, float dtSeconds)
        {
            var topGrid = _grid.GetTopMostParent() as MyEntity ?? _grid as MyEntity;
            if (topGrid == null) return;

            var locked = wc.GetProjectilesLockedOn(topGrid);
            MissilesInbound     = locked.Item1;
            MissileInboundCount = locked.Item2;

            var allPositions = new List<Vector3D>();
            if (MissilesInbound) wc.GetProjectilesLockedOnPos(topGrid, allPositions);

            var allSmart = new List<MyTuple<ulong, Vector3D, int, long>>();
            wc.GetAllSmartProjectiles(allSmart);

            const double DEDUP_DIST_SQ = 500.0 * 500.0;

            MissilePositions.Clear(); MissileVelocities.Clear(); InboundFactionIds.Clear();

            foreach (var pos in allPositions)
            {
                bool isOwnOrFriendly = false;
                foreach (var op in OutboundPositions)
                    if (Vector3D.DistanceSquared(pos, op) < DEDUP_DIST_SQ) { isOwnOrFriendly = true; break; }
                if (!isOwnOrFriendly)
                    foreach (var fp in FriendlyMissilePositions)
                        if (Vector3D.DistanceSquared(pos, fp) < DEDUP_DIST_SQ) { isOwnOrFriendly = true; break; }
                if (isOwnOrFriendly) continue;

                MissilePositions.Add(pos);
                InboundFactionIds.Add(0);

                Vector3D vel     = Vector3D.Zero;
                double   bestDist = double.MaxValue;
                ulong    bestId   = 0;
                foreach (var sp in allSmart)
                {
                    double d = Vector3D.DistanceSquared(pos, sp.Item2);
                    if (d < bestDist) { bestDist = d; bestId = sp.Item1; }
                }
                if (bestDist < DEDUP_DIST_SQ && bestId != 0)
                {
                    Vector3D prevPos;
                    if (_prevProjPositions.TryGetValue(bestId, out prevPos))
                    {
                        float dt2 = dtSeconds > 0f ? dtSeconds : 1f;
                        foreach (var sp in allSmart)
                            if (sp.Item1 == bestId) { vel = (sp.Item2 - prevPos) / dt2; break; }
                    }
                }
                MissileVelocities.Add(vel);
            }

            MissilesInbound     = MissilePositions.Count > 0;
            MissileInboundCount = MissilePositions.Count;
        }

        public void RefreshAllProjectiles(WcApi wc, float dtSeconds)
        {
            RegisterProjectileMonitors(wc);

            OutboundPositions.Clear();  OutboundVelocities.Clear();  OutboundFactionIds.Clear();
            FriendlyMissilePositions.Clear(); FriendlyMissileVelocities.Clear(); FriendlyFactionIds.Clear();
            HostileOtherPositions.Clear(); HostileOtherVelocities.Clear(); HostileFactionIds.Clear();

            if (!_factionCached)
            {
                var topGrid   = _grid.GetTopMostParent() as VRage.Game.ModAPI.IMyCubeGrid ?? _grid;
                var bigOwners = topGrid.BigOwners;
                if (bigOwners != null && bigOwners.Count > 0)
                {
                    var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(bigOwners[0]);
                    _ourFactionId  = faction?.FactionId ?? 0;
                    _factionCached = true;
                }
            }

            var raw = new List<MyTuple<ulong, Vector3D, int, long>>();
            wc.GetAllSmartProjectiles(raw);

            var inboundSet = new HashSet<Vector3D>();
            foreach (var p in MissilePositions) inboundSet.Add(p);

            float dt = dtSeconds > 0f ? dtSeconds : 1f;

            foreach (var proj in raw)
            {
                ulong  id        = proj.Item1;
                var    pos       = proj.Item2;
                long   factionId = proj.Item4;

                Vector3D vel = Vector3D.Zero;
                Vector3D prevPos;
                if (_prevProjPositions.TryGetValue(id, out prevPos))
                    vel = (pos - prevPos) / dt;

                ProjectileFaction pfac;
                if (_ourProjectileIds.Contains(id))
                {
                    pfac = ProjectileFaction.Ours;
                }
                else if (factionId != 0 && _ourOwnerFactionIds.Contains(factionId))
                {
                    pfac = ProjectileFaction.Ours;
                }
                else
                {
                    pfac = ClassifyFactionRelation(factionId);
                }

                if (pfac == ProjectileFaction.Ours)
                {
                    OutboundPositions.Add(pos);  OutboundVelocities.Add(vel);  OutboundFactionIds.Add(factionId);
                }
                else if (pfac == ProjectileFaction.Friendly)
                {
                    FriendlyMissilePositions.Add(pos); FriendlyMissileVelocities.Add(vel); FriendlyFactionIds.Add(factionId);
                }
                else
                {
                    if (!inboundSet.Contains(pos))
                    {
                        HostileOtherPositions.Add(pos); HostileOtherVelocities.Add(vel); HostileFactionIds.Add(factionId);
                    }
                }
            }

            _prevProjPositions.Clear();
            foreach (var proj in raw)
                _prevProjPositions[proj.Item1] = proj.Item2;
        }

        private enum ProjectileFaction { Ours, Friendly, Hostile }

        private ProjectileFaction ClassifyFactionRelation(long missileFactionId)
        {
            if (!_factionCached)
            {
                var topGrid   = _grid.GetTopMostParent() as VRage.Game.ModAPI.IMyCubeGrid ?? _grid;
                var bigOwners = topGrid.BigOwners;
                if (bigOwners != null && bigOwners.Count > 0)
                {
                    var f = MyAPIGateway.Session.Factions.TryGetPlayerFaction(bigOwners[0]);
                    _ourFactionId  = f?.FactionId ?? 0;
                    _factionCached = true;
                }
            }

            if (missileFactionId == 0)
                return _ourFactionId == 0 ? ProjectileFaction.Ours : ProjectileFaction.Hostile;

            if (_ourFactionId == 0) return ProjectileFaction.Hostile;
            if (missileFactionId == _ourFactionId) return ProjectileFaction.Friendly;

            var relation = MyAPIGateway.Session.Factions
                .GetRelationBetweenFactions(_ourFactionId, missileFactionId);

            return relation == VRage.Game.MyRelationsBetweenFactions.Friends
                ? ProjectileFaction.Friendly
                : ProjectileFaction.Hostile;
        }

        private long _ourFactionId  = 0;
        private bool _factionCached = false;

        private void NotifyWeaponsRefreshed()
        {
            var wc = NAS_Session.I?.WcApi;
            if (wc != null) RegisterProjectileMonitors(wc);

            var snap = new List<IGridCacheSubscriber>(_subscribers);
            foreach (var sub in snap)
            {
                try { sub.OnWeaponsRefreshed(AllWeapons, StaticLaunchers, Turrets); }
                catch (Exception e)
                {
                    NASLog.Error("FC", "WeaponsRefreshed notify: " + e.Message);
                }
            }
        }

        private void NotifyThreatsRefreshed()
        {
            var snap = new List<IGridCacheSubscriber>(_subscribers);
            foreach (var sub in snap)
            {
                try { sub.OnThreatsRefreshed(Threats); }
                catch (Exception e)
                {
                    NASLog.Error("FC", "ThreatsRefreshed notify: " + e.Message);
                }
            }
        }

        private void NotifyFriendliesRefreshed()
        {
            var snap = new List<IGridCacheSubscriber>(_subscribers);
            foreach (var sub in snap)
            {
                try { sub.OnFriendliesRefreshed(Friendlies); }
                catch (Exception e)
                {
                    NASLog.Error("FC", "FriendliesRefreshed notify: " + e.Message);
                }
            }
        }
    }

    // ───────────────────────────────────────────────────────────────────────────
    // FCGridCache — session component that owns all GridCaches.
    // WcApi is provided by NAS_Session; this component does not load its own.
    // ───────────────────────────────────────────────────────────────────────────
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class FCGridCache : MySessionComponentBase
    {
        public static FCGridCache I;

        // Pass-through to NAS_Session — single WcApi instance for the whole mod.
        public WcApi WcApi      => NAS_Session.I?.WcApi;
        public bool  WcApiReady => NAS_Session.I?.WcReady ?? false;

        private readonly List<MyDefinitionId>        _staticDefs = new List<MyDefinitionId>();
        private readonly List<MyDefinitionId>        _turretDefs = new List<MyDefinitionId>();
        private bool _defsCached = false;

        private readonly ConcurrentDictionary<long, GridCache> _gridCaches = new ConcurrentDictionary<long, GridCache>();

        public override void LoadData()
        {
            I = this;
        }

        protected override void UnloadData()
        {
            _gridCaches.Clear();
            _staticDefs.Clear();
            _turretDefs.Clear();
            _defsCached = false;
            I = null;
        }

        private void TryCacheDefs()
        {
            if (_defsCached) return;
            var wc = NAS_Session.I?.WcApi;
            if (wc == null || !wc.IsReady) return;

            _staticDefs.Clear(); _turretDefs.Clear();
            wc.GetAllCoreStaticLaunchers(_staticDefs);
            wc.GetAllCoreTurrets(_turretDefs);
            _defsCached = true;

            NASLog.Info("FC", "Defs cached — " + _staticDefs.Count + " static, " + _turretDefs.Count + " turrets.");

            var cachedValues = new List<GridCache>(_gridCaches.Values);
            foreach (var cache in cachedValues)
                cache.MarkWeaponsDirty();
        }

        public GridCache PeekCache(IMyCubeGrid grid)
        {
            GridCache cache;
            if (!_gridCaches.TryGetValue(grid.EntityId, out cache))
            {
                cache = new GridCache(grid);
                _gridCaches[grid.EntityId] = cache;
            }
            cache.LastAccessTick = _totalTicks;
            return cache;
        }

        public GridCache GetOrCreateCache(IMyCubeGrid grid)
        {
            GridCache cache;
            if (!_gridCaches.TryGetValue(grid.EntityId, out cache))
            {
                cache = new GridCache(grid);
                _gridCaches[grid.EntityId] = cache;
            }
            return cache;
        }

        public void RequestWeaponRefresh(IMyCubeGrid grid)
        {
            if (!WcApiReady || !_defsCached) return;
            GridCache cache;
            if (!_gridCaches.TryGetValue(grid.EntityId, out cache))
                cache = GetOrCreateCache(grid);
            cache.MarkWeaponsDirty();
            cache.TryRefreshWeapons(WcApi, _staticDefs, _turretDefs);
        }

        public void RequestThreatRefresh(IMyCubeGrid grid)
        {
            if (!WcApiReady) return;
            GridCache cache;
            if (_gridCaches.TryGetValue(grid.EntityId, out cache))
                cache.RefreshThreats(WcApi);
        }

        private const int CONTACT_REFRESH_TICKS = 60;
        private const int CACHE_IDLE_PRUNE_TICKS = 300;
        private int _ticksSinceRefresh = 0;
        private int _totalTicks        = 0;

        public override void UpdateAfterSimulation()
        {
            if (!WcApiReady) return;

            if (!_defsCached) { TryCacheDefs(); return; }

            _totalTicks++;

            var toRemove = new List<long>();
            foreach (var kv in _gridCaches)
                if (kv.Value.SubscriberCount == 0 &&
                    _totalTicks - kv.Value.LastAccessTick > CACHE_IDLE_PRUNE_TICKS)
                    toRemove.Add(kv.Key);
            foreach (long id in toRemove) { GridCache _removed; _gridCaches.TryRemove(id, out _removed); }

            _ticksSinceRefresh++;
            if (_ticksSinceRefresh >= CONTACT_REFRESH_TICKS)
            {
                _ticksSinceRefresh = 0;
                var snapshot = new List<GridCache>(_gridCaches.Values);
                foreach (var cache in snapshot)
                {
                    cache.RefreshThreats(WcApi);
                    cache.RefreshFriendlies(WcApi);
                    cache.RefreshAllProjectiles(WcApi, CONTACT_REFRESH_TICKS / 60f);
                    cache.RefreshMissiles(WcApi, CONTACT_REFRESH_TICKS / 60f);
                }
            }
        }
    }
}
