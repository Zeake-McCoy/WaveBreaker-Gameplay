using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using CoreSystems.Api;
using Jakaria.API;
using IMyUserControllableGun = Sandbox.ModAPI.Ingame.IMyUserControllableGun;

namespace TTNAS
{
    // ───────────────────────────────────────────────────────────────────────────
    // IEngSubscriber — implemented by EngineeringProcessor (and future blocks)
    // ───────────────────────────────────────────────────────────────────────────
    public interface IEngSubscriber
    {
        void OnEngDataRefreshed(EngGridCache cache);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // EngGridCache — per-grid engineering data store
    //
    // Scanned every ~1.67 s by NAS_Session. Damage events dirty-flag an
    // "under attack" window. All data is read-only to consumers.
    // ───────────────────────────────────────────────────────────────────────────
    public class EngGridCache
    {
        // ── Identity ───────────────────────────────────────────────────────────
        public string GridName    { get; private set; }
        public bool   IsValid     { get; private set; }
        public int    LastAccessTick;
        public int    SubscriberCount => _subscribers.Count;

        // ── Mass & size ────────────────────────────────────────────────────────
        public float MassKg     { get; private set; }
        public int   BlockCount { get; private set; }

        // ── Power ──────────────────────────────────────────────────────────────
        public float PowerOutputMW     { get; private set; }
        public float PowerDrawMW       { get; private set; }
        public float BatteryChargePct  { get; private set; }
        public int   ReactorTotal      { get; private set; }
        public int   ReactorFunctional { get; private set; }
        public int   BatteryTotal      { get; private set; }

        // ── Propulsion ────────────────────────────────────────────────────────
        public float SpeedMs          { get; private set; }
        public int   ThrusterTotal    { get; private set; }
        public int   ThrusterEnabled  { get; private set; }
        public int   ThrMainTotal     { get; private set; }
        public int   ThrMainEnabled   { get; private set; }
        public float ThrMainTotalN    { get; private set; }
        public float ThrMainAvailN    { get; private set; }
        public int   ThrBrakeTotal    { get; private set; }
        public int   ThrBrakeEnabled  { get; private set; }
        public float ThrBrakeTotalN   { get; private set; }
        public float ThrBrakeAvailN   { get; private set; }
        public int   ThrMnvrTotal     { get; private set; }
        public int   ThrMnvrEnabled   { get; private set; }
        public float ThrMnvrTotalN    { get; private set; }
        public float ThrMnvrAvailN    { get; private set; }
        public int   GyroTotal        { get; private set; }
        public int   GyroFunctional   { get; private set; }

        // ── Weapons ───────────────────────────────────────────────────────────
        public int  WeaponTotal      { get; private set; }
        public int  WeaponFunctional { get; private set; }
        public bool WcDetected       { get; private set; }

        // ── Damage ────────────────────────────────────────────────────────────
        public int   BlockDamaged     { get; private set; }
        public int   BlockCritical    { get; private set; }
        public float HullIntegrityPct { get; private set; }
        public bool  UnderAttack      { get; private set; }

        // ── Water (WaterModAPI, optional) ─────────────────────────────────────
        public bool    WaterAvailable { get; private set; }
        public float   SubmersionPct  { get; private set; }
        public double  FluidDepthM    { get; private set; }
        public Vector3 WaterVelocity  { get; private set; }

        // ── Internals ─────────────────────────────────────────────────────────
        private readonly IMyCubeGrid             _grid;
        private readonly List<IEngSubscriber>    _subscribers = new List<IEngSubscriber>();
        private readonly List<IMySlimBlock>      _slims       = new List<IMySlimBlock>();
        private readonly Dictionary<string, int> _wcMap       = new Dictionary<string, int>();

        private IMyCubeBlock _dirRefBlock;
        private const string DIR_TAG = "[TT_DIRECTION]";

        private int _attackCooldown;
        private const int ATTACK_WINDOW = 120;

        // ── Constructor / Dispose ──────────────────────────────────────────────

        public EngGridCache(IMyCubeGrid grid)
        {
            _grid    = grid;
            GridName = grid.DisplayName ?? "Unknown";
            grid.OnBlockIntegrityChanged += OnIntegrityChanged;
        }

        public void Dispose()
        {
            if (_grid != null)
                _grid.OnBlockIntegrityChanged -= OnIntegrityChanged;
            _subscribers.Clear();
        }

        // ── Direction reference ────────────────────────────────────────────────

        public void SetDirectionReference(IMyCubeBlock block)
        {
            _dirRefBlock = block;
        }

        private Base6Directions.Direction GetRefForwardDir(IMyCubeBlock taggedOverride)
        {
            var refBlock = taggedOverride ?? _dirRefBlock;
            if (refBlock != null)
                return refBlock.Orientation.Forward;
            return Base6Directions.Direction.Forward;
        }

        // ── Subscriber management ──────────────────────────────────────────────

        public void Subscribe(IEngSubscriber sub)
        {
            if (!_subscribers.Contains(sub))
                _subscribers.Add(sub);
        }

        public void Unsubscribe(IEngSubscriber sub)
        {
            _subscribers.Remove(sub);
        }

        // ── Damage event ───────────────────────────────────────────────────────

        private void OnIntegrityChanged(IMySlimBlock slim)
        {
            _attackCooldown = ATTACK_WINDOW;
        }

        // ── Main refresh — called by NAS_Session every SCAN_INTERVAL ticks ──────

        public void Refresh(WcApi wc, bool waterReady)
        {
            if (_grid == null || _grid.MarkedForClose)
            {
                IsValid = false;
                return;
            }

            IsValid  = true;
            GridName = _grid.DisplayName ?? "Unknown";

            if (_attackCooldown > 0) _attackCooldown -= 100;
            UnderAttack = _attackCooldown > 0;

            _slims.Clear();
            try { _grid.GetBlocks(_slims); }
            catch (InvalidOperationException) { return; }
            BlockCount = _slims.Count;

            IMyCubeBlock dirTagged   = null;
            IMyCubeBlock mainCockpit = null;
            foreach (var s in _slims)
            {
                if (s.FatBlock == null) continue;
                var tb = s.FatBlock as IMyTerminalBlock;
                if (tb != null && tb.CustomData.IndexOf(DIR_TAG, StringComparison.OrdinalIgnoreCase) >= 0)
                    dirTagged = s.FatBlock as IMyCubeBlock;
                var sc = s.FatBlock as IMyShipController;
                if (sc != null && sc.IsMainCockpit)
                    mainCockpit = s.FatBlock as IMyCubeBlock;
            }
            IMyCubeBlock refBlock = dirTagged ?? _dirRefBlock ?? mainCockpit;
            Base6Directions.Direction fwdDir = GetRefForwardDir(refBlock);
            Base6Directions.Direction bkwDir = Base6Directions.GetOppositeDirection(fwdDir);

            int   reactorTotal = 0, reactorFunc  = 0;
            int   battTotal    = 0;
            int   thrTotal     = 0, thrEnabled   = 0;
            int   thrMainT     = 0, thrMainE     = 0;
            float thrMainTN    = 0f, thrMainAN   = 0f;
            int   thrBrakeT    = 0, thrBrakeE    = 0;
            float thrBrakeTN   = 0f, thrBrakeAN  = 0f;
            int   thrMnvrT     = 0, thrMnvrE     = 0;
            float thrMnvrTN    = 0f, thrMnvrAN   = 0f;
            int   gyroTotal    = 0, gyroFunc     = 0;
            int   wpnTotal     = 0, wpnFunc      = 0;
            int   blockDmg     = 0, blockCrit    = 0;
            bool  wcDetected   = false;
            float powerOut     = 0f, powerDraw   = 0f;
            float battCharge   = 0f;
            int   battCount    = 0;
            float totalI       = 0f, currentI    = 0f;

            bool wcReady = wc != null && wc.IsReady;

            foreach (var slim in _slims)
            {
                float maxI = slim.MaxIntegrity;
                float curI = slim.Integrity;
                totalI   += maxI;
                currentI += curI;
                if (curI < maxI)                       blockDmg++;
                if (maxI > 0f && curI / maxI < 0.33f) blockCrit++;

                var fat = slim.FatBlock;
                if (fat == null) continue;

                var fb = fat as IMyFunctionalBlock;

                var reactor = fat as IMyReactor;
                if (reactor != null)
                {
                    reactorTotal++;
                    if (fb != null && fb.IsWorking)
                    {
                        reactorFunc++;
                        powerOut  += reactor.MaxOutput;
                        powerDraw += reactor.CurrentOutput;
                    }
                    continue;
                }

                var battery = fat as IMyBatteryBlock;
                if (battery != null)
                {
                    battTotal++;
                    battCharge += battery.MaxStoredPower > 0f
                        ? battery.CurrentStoredPower / battery.MaxStoredPower
                        : 0f;
                    battCount++;
                    if (fb != null && fb.IsWorking)
                    {
                        powerOut  += battery.MaxOutput;
                        powerDraw += battery.CurrentOutput;
                    }
                    continue;
                }

                var power = fat as IMyPowerProducer;
                if (power != null)
                {
                    if (fb != null && fb.IsWorking)
                    {
                        powerOut  += power.MaxOutput;
                        powerDraw += power.CurrentOutput;
                    }
                    continue;
                }

                var thr = fat as IMyThrust;
                if (thr != null)
                {
                    thrTotal++;
                    bool  thrOn = fb != null && fb.Enabled;
                    float maxN  = thr.MaxThrust;
                    if (thrOn) thrEnabled++;

                    Base6Directions.Direction thrPush =
                        Base6Directions.GetOppositeDirection(thr.Orientation.Forward);

                    if (thrPush == fwdDir)
                    {
                        thrMainT++; thrMainTN += maxN;
                        if (thrOn) { thrMainE++; thrMainAN += maxN; }
                    }
                    else if (thrPush == bkwDir)
                    {
                        thrBrakeT++; thrBrakeTN += maxN;
                        if (thrOn) { thrBrakeE++; thrBrakeAN += maxN; }
                    }
                    else
                    {
                        thrMnvrT++; thrMnvrTN += maxN;
                        if (thrOn) { thrMnvrE++; thrMnvrAN += maxN; }
                    }
                    continue;
                }

                if (fat is IMyGyro)
                {
                    gyroTotal++;
                    if (fb != null && fb.IsWorking) gyroFunc++;
                    continue;
                }

                bool isWeapon;
                if (wcReady)
                {
                    var tb = fat as IMyTerminalBlock;
                    _wcMap.Clear();
                    isWeapon = tb != null && wc.GetBlockWeaponMap(tb, _wcMap);
                    if (isWeapon) wcDetected = true;
                }
                else
                {
                    isWeapon = fat is IMyLargeTurretBase || fat is IMyUserControllableGun;
                }

                if (isWeapon)
                {
                    wpnTotal++;
                    if (fb != null && fb.IsWorking) wpnFunc++;
                }
            }

            BlockDamaged      = blockDmg;
            BlockCritical     = blockCrit;
            HullIntegrityPct  = totalI > 0f ? currentI / totalI : 1f;

            ReactorTotal      = reactorTotal;
            ReactorFunctional = reactorFunc;
            BatteryTotal      = battTotal;
            BatteryChargePct  = battCount > 0 ? battCharge / battCount : 0f;
            PowerOutputMW     = powerOut;
            PowerDrawMW       = powerDraw;

            ThrusterTotal     = thrTotal;
            ThrusterEnabled   = thrEnabled;
            ThrMainTotal      = thrMainT;
            ThrMainEnabled    = thrMainE;
            ThrMainTotalN     = thrMainTN;
            ThrMainAvailN     = thrMainAN;
            ThrBrakeTotal     = thrBrakeT;
            ThrBrakeEnabled   = thrBrakeE;
            ThrBrakeTotalN    = thrBrakeTN;
            ThrBrakeAvailN    = thrBrakeAN;
            ThrMnvrTotal      = thrMnvrT;
            ThrMnvrEnabled    = thrMnvrE;
            ThrMnvrTotalN     = thrMnvrTN;
            ThrMnvrAvailN     = thrMnvrAN;
            GyroTotal         = gyroTotal;
            GyroFunctional    = gyroFunc;

            WeaponTotal       = wpnTotal;
            WeaponFunctional  = wpnFunc;
            WcDetected        = wcDetected;

            var gridEnt = _grid as MyEntity;
            if (gridEnt != null && gridEnt.Physics != null)
            {
                SpeedMs = (float)gridEnt.Physics.LinearVelocity.Length();
                MassKg  = gridEnt.Physics.Mass;
            }
            else
            {
                SpeedMs = 0f;
                MassKg  = 0f;
            }

            if (waterReady)
            {
                WaterAvailable = true;
                var ent = _grid as MyEntity;
                if (ent != null)
                {
                    SubmersionPct = WaterModAPI.Entity_PercentUnderwater(ent);
                    FluidDepthM   = WaterModAPI.Entity_FluidDepth(ent);
                    WaterVelocity = WaterModAPI.Entity_FluidVelocity(ent);
                }
            }
            else
            {
                WaterAvailable = false;
                SubmersionPct  = 0f;
                FluidDepthM    = 0.0;
                WaterVelocity  = Vector3.Zero;
            }

            NotifySubscribers();
        }

        private void NotifySubscribers()
        {
            var snap = new List<IEngSubscriber>(_subscribers);
            foreach (var sub in snap)
            {
                try { sub.OnEngDataRefreshed(this); }
                catch (Exception e)
                {
                    NASLog.Error("Eng", "Subscriber notify: " + e.Message);
                }
            }
        }
    }
}
