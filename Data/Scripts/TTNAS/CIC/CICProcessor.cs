using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;

namespace TTNAS
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule),
        true,
        new string[] { "TT_CIC_Processor", "TT_CIC_Processor_SG" })]
    public class CICProcessor : MyGameLogicComponent, IGridCacheSubscriber
    {
        private static readonly Dictionary<long, CICProcessor> _registry
            = new Dictionary<long, CICProcessor>();

        public static CICProcessor GetForGrid(IMyCubeGrid grid)
        {
            if (grid == null) return null;
            var topGrid = grid.GetTopMostParent() as IMyCubeGrid ?? grid;
            CICProcessor cic;
            return _registry.TryGetValue(topGrid.EntityId, out cic) ? cic : null;
        }

        public IReadOnlyList<ThreatEntry>  Threats    => _threats;
        public IReadOnlyList<ThreatEntry>  Friendlies => _friendlies;
        public IReadOnlyList<WeaponEntry>  AllWeapons => _allWeapons;
        public IReadOnlyList<WeaponEntry>  Statics    => _statics;
        public IReadOnlyList<WeaponEntry>  Turrets    => _turrets;

        public bool            IsOnline            => _fblock?.IsWorking ?? false;
        public bool            MissilesInbound     => _cache?.MissilesInbound     ?? false;
        public int             MissileInboundCount => _cache?.MissileInboundCount ?? 0;
        public List<Vector3D>  MissilePositions         => _cache != null ? _cache.MissilePositions         : _emptyVec3;
        public List<Vector3D>  MissileVelocities        => _cache != null ? _cache.MissileVelocities        : _emptyVec3;
        public List<Vector3D>  OutboundPositions        => _cache != null ? _cache.OutboundPositions        : _emptyVec3;
        public List<Vector3D>  OutboundVelocities       => _cache != null ? _cache.OutboundVelocities       : _emptyVec3;
        public List<long>      OutboundFactionIds       => _cache != null ? _cache.OutboundFactionIds       : _emptyLong;
        public List<long>      FriendlyFactionIds       => _cache != null ? _cache.FriendlyFactionIds       : _emptyLong;
        public List<long>      HostileFactionIds        => _cache != null ? _cache.HostileFactionIds        : _emptyLong;
        public List<long>      InboundFactionIds        => _cache != null ? _cache.InboundFactionIds        : _emptyLong;
        public List<Vector3D>  FriendlyMissilePositions  => _cache != null ? _cache.FriendlyMissilePositions  : _emptyVec3;
        public List<Vector3D>  FriendlyMissileVelocities => _cache != null ? _cache.FriendlyMissileVelocities : _emptyVec3;
        public List<Vector3D>  HostileOtherPositions     => _cache != null ? _cache.HostileOtherPositions     : _emptyVec3;
        public List<Vector3D>  HostileOtherVelocities    => _cache != null ? _cache.HostileOtherVelocities    : _emptyVec3;

        private static readonly List<Vector3D> _emptyVec3 = new List<Vector3D>();
        private static readonly List<long>     _emptyLong = new List<long>();

        private readonly List<ThreatEntry> _threats    = new List<ThreatEntry>();
        private readonly List<ThreatEntry> _friendlies = new List<ThreatEntry>();
        private readonly List<WeaponEntry> _allWeapons = new List<WeaponEntry>();
        private readonly List<WeaponEntry> _statics    = new List<WeaponEntry>();
        private readonly List<WeaponEntry> _turrets    = new List<WeaponEntry>();

        private IMyTerminalBlock   _block;
        private IMyFunctionalBlock _fblock;

        private GridCache _cache;
        private bool      _subscribed         = false;
        private bool      _initialRefreshDone = false;
        private int       _uiTick             = 0;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            _block  = Entity as IMyTerminalBlock;
            _fblock = Entity as IMyFunctionalBlock;
            if (_block == null) return;

            NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
            _block.AppendingCustomInfo += AppendCustomInfo;

            TryRegisterAndSubscribe();
        }

        public override void Close()
        {
            Unregister();
            if (_block != null)
                _block.AppendingCustomInfo -= AppendCustomInfo;
        }

        private void TryRegisterAndSubscribe()
        {
            if (_subscribed) return;

            var session = FCGridCache.I;
            if (session == null || !session.WcApiReady) return;

            var topGrid = _block.CubeGrid.GetTopMostParent() as IMyCubeGrid ?? _block.CubeGrid;
            if (_registry.ContainsKey(topGrid.EntityId)) return;

            _cache = session.GetOrCreateCache(_block.CubeGrid);
            _cache.Subscribe(this);
            _subscribed = true;
            _registry[topGrid.EntityId] = this;
        }

        private void Unregister()
        {
            if (_cache != null)
            {
                _cache.Unsubscribe(this);
                _cache = null;
            }

            if (_block != null)
            {
                var topGrid = _block.CubeGrid.GetTopMostParent() as IMyCubeGrid ?? _block.CubeGrid;
                CICProcessor existing;
                if (_registry.TryGetValue(topGrid.EntityId, out existing) && existing == this)
                    _registry.Remove(topGrid.EntityId);
            }

            _subscribed = false;
        }

        public override void UpdateAfterSimulation10()
        {
            if (_block == null) return;

            if (!_subscribed) { TryRegisterAndSubscribe(); return; }

            if (!_initialRefreshDone)
            {
                _initialRefreshDone = true;
                FCGridCache.I?.RequestWeaponRefresh(_block.CubeGrid);
            }

            _uiTick++;
            if (_uiTick >= 3)
            {
                _uiTick = 0;
                _block.RefreshCustomInfo();
            }
        }

        public void OnWeaponsRefreshed(
            List<WeaponEntry> allWeapons,
            List<WeaponEntry> staticLaunchers,
            List<WeaponEntry> turrets)
        {
            _allWeapons.Clear(); _allWeapons.AddRange(allWeapons);
            _statics.Clear();    _statics.AddRange(staticLaunchers);
            _turrets.Clear();    _turrets.AddRange(turrets);
        }

        public void OnThreatsRefreshed(List<ThreatEntry> threats)
        {
            _threats.Clear();
            _threats.AddRange(threats);
        }

        public void OnFriendliesRefreshed(List<ThreatEntry> friendlies)
        {
            _friendlies.Clear();
            _friendlies.AddRange(friendlies);
        }

        private void AppendCustomInfo(IMyTerminalBlock block, StringBuilder sb)
        {
            sb.AppendLine("=== TT CIC PROCESSOR ===");
            sb.AppendLine("Status   : " + (IsOnline ? "ONLINE" : "OFFLINE"));
            sb.AppendLine("Threats  : " + _threats.Count);
            sb.AppendLine("Missiles : " + (_cache?.MissileInboundCount ?? 0) + " inbound");
            sb.AppendLine("Friendlies: " + _friendlies.Count);
            sb.AppendLine("Weapons  : " + _allWeapons.Count + " total");
            sb.AppendLine("  Static : " + _statics.Count);
            sb.AppendLine("  Turrets: " + _turrets.Count);
        }
    }
}
