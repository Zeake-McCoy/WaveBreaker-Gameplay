using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace TTNAS
{
    // ───────────────────────────────────────────────────────────────────────────
    // EngineeringProcessor — Engineering data hub block
    //
    // One per grid. Registers with NAS_Session, owns the EngGridCache subscription.
    // LCD scripts call EngineeringProcessor.GetForGrid(grid).
    //
    // Block subtypes: "TT_Eng_Processor" (LG), "TT_Eng_Processor_SG" (SG)
    // ───────────────────────────────────────────────────────────────────────────
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), true,
        new string[] { "TT_Eng_Processor", "TT_Eng_Processor_SG" })]
    public class EngineeringProcessor : MyGameLogicComponent, IEngSubscriber
    {
        // ── Static registry ────────────────────────────────────────────────────

        private static readonly Dictionary<long, EngineeringProcessor> _registry
            = new Dictionary<long, EngineeringProcessor>();

        public static EngineeringProcessor GetForGrid(IMyCubeGrid grid)
        {
            if (grid == null) return null;
            var top = grid.GetTopMostParent() as IMyCubeGrid ?? grid;
            EngineeringProcessor ep;
            return _registry.TryGetValue(top.EntityId, out ep) ? ep : null;
        }

        // ── Exposed engineering state ──────────────────────────────────────────

        public bool   IsOnline          => _fblock?.IsWorking ?? false;

        public string GridName          { get; private set; }
        public float  MassKg            { get; private set; }
        public int    BlockCount        { get; private set; }

        public float  PowerOutputMW     { get; private set; }
        public float  PowerDrawMW       { get; private set; }
        public float  BatteryChargePct  { get; private set; }
        public int    ReactorTotal      { get; private set; }
        public int    ReactorFunctional { get; private set; }
        public int    BatteryTotal      { get; private set; }

        public float  SpeedMs           { get; private set; }
        public int    ThrusterTotal     { get; private set; }
        public int    ThrusterEnabled   { get; private set; }
        public int    ThrMainTotal      { get; private set; }
        public int    ThrMainEnabled    { get; private set; }
        public float  ThrMainTotalN     { get; private set; }
        public float  ThrMainAvailN     { get; private set; }
        public int    ThrBrakeTotal     { get; private set; }
        public int    ThrBrakeEnabled   { get; private set; }
        public float  ThrBrakeTotalN    { get; private set; }
        public float  ThrBrakeAvailN    { get; private set; }
        public int    ThrMnvrTotal      { get; private set; }
        public int    ThrMnvrEnabled    { get; private set; }
        public float  ThrMnvrTotalN     { get; private set; }
        public float  ThrMnvrAvailN     { get; private set; }
        public int    GyroTotal         { get; private set; }
        public int    GyroFunctional    { get; private set; }

        public int    WeaponTotal       { get; private set; }
        public int    WeaponFunctional  { get; private set; }
        public bool   WcDetected        { get; private set; }

        public int    BlockDamaged      { get; private set; }
        public int    BlockCritical     { get; private set; }
        public float  HullIntegrityPct  { get; private set; }
        public bool   UnderAttack       { get; private set; }

        public bool    WaterAvailable   { get; private set; }
        public float   SubmersionPct    { get; private set; }
        public double  FluidDepthM      { get; private set; }
        public Vector3 WaterVelocity    { get; private set; }

        // ── Block references ───────────────────────────────────────────────────

        private IMyTerminalBlock   _block;
        private IMyFunctionalBlock _fblock;

        private EngGridCache _cache;
        private bool         _subscribed;
        private int          _uiTick;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            _block  = Entity as IMyTerminalBlock;
            _fblock = Entity as IMyFunctionalBlock;
            if (_block == null) return;

            NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
            _block.AppendingCustomInfo += AppendCustomInfo;
        }

        public override void Close()
        {
            Unregister();
            if (_block != null)
                _block.AppendingCustomInfo -= AppendCustomInfo;
        }

        // ── Registry + cache subscription ─────────────────────────────────────

        private void TryRegister()
        {
            if (_subscribed || NAS_Session.I == null) return;

            var top = _block.CubeGrid.GetTopMostParent() as IMyCubeGrid ?? _block.CubeGrid;
            if (_registry.ContainsKey(top.EntityId)) return;

            _cache = NAS_Session.I.GetOrCreate(_block.CubeGrid);
            _cache.SetDirectionReference(Entity as IMyCubeBlock);
            _cache.Subscribe(this);
            _subscribed = true;
            _registry[top.EntityId] = this;
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
                var top = _block.CubeGrid.GetTopMostParent() as IMyCubeGrid ?? _block.CubeGrid;
                EngineeringProcessor existing;
                if (_registry.TryGetValue(top.EntityId, out existing) && existing == this)
                    _registry.Remove(top.EntityId);
            }

            _subscribed = false;
        }

        // ── Update ─────────────────────────────────────────────────────────────

        public override void UpdateAfterSimulation10()
        {
            if (_block == null) return;

            if (!_subscribed)
            {
                TryRegister();
                return;
            }

            if (++_uiTick >= 3)
            {
                _uiTick = 0;
                _block.RefreshCustomInfo();
            }
        }

        // ── IEngSubscriber ─────────────────────────────────────────────────────

        public void OnEngDataRefreshed(EngGridCache cache)
        {
            GridName         = cache.GridName;
            MassKg           = cache.MassKg;
            BlockCount       = cache.BlockCount;
            PowerOutputMW    = cache.PowerOutputMW;
            PowerDrawMW      = cache.PowerDrawMW;
            BatteryChargePct = cache.BatteryChargePct;
            ReactorTotal     = cache.ReactorTotal;
            ReactorFunctional= cache.ReactorFunctional;
            BatteryTotal     = cache.BatteryTotal;
            SpeedMs          = cache.SpeedMs;
            ThrusterTotal    = cache.ThrusterTotal;
            ThrusterEnabled  = cache.ThrusterEnabled;
            ThrMainTotal     = cache.ThrMainTotal;
            ThrMainEnabled   = cache.ThrMainEnabled;
            ThrMainTotalN    = cache.ThrMainTotalN;
            ThrMainAvailN    = cache.ThrMainAvailN;
            ThrBrakeTotal    = cache.ThrBrakeTotal;
            ThrBrakeEnabled  = cache.ThrBrakeEnabled;
            ThrBrakeTotalN   = cache.ThrBrakeTotalN;
            ThrBrakeAvailN   = cache.ThrBrakeAvailN;
            ThrMnvrTotal     = cache.ThrMnvrTotal;
            ThrMnvrEnabled   = cache.ThrMnvrEnabled;
            ThrMnvrTotalN    = cache.ThrMnvrTotalN;
            ThrMnvrAvailN    = cache.ThrMnvrAvailN;
            GyroTotal        = cache.GyroTotal;
            GyroFunctional   = cache.GyroFunctional;
            WeaponTotal      = cache.WeaponTotal;
            WeaponFunctional = cache.WeaponFunctional;
            WcDetected       = cache.WcDetected;
            BlockDamaged     = cache.BlockDamaged;
            BlockCritical    = cache.BlockCritical;
            HullIntegrityPct = cache.HullIntegrityPct;
            UnderAttack      = cache.UnderAttack;
            WaterAvailable   = cache.WaterAvailable;
            SubmersionPct    = cache.SubmersionPct;
            FluidDepthM      = cache.FluidDepthM;
            WaterVelocity    = cache.WaterVelocity;
        }

        // ── Terminal custom info ────────────────────────────────────────────────

        private void AppendCustomInfo(IMyTerminalBlock block, StringBuilder sb)
        {
            sb.AppendLine("=== TT ENGINEERING PROCESSOR ===");
            sb.AppendLine("Status    : " + (IsOnline ? "ONLINE" : "OFFLINE"));
            sb.AppendLine("WC API    : " + (WcDetected ? "YES" : "NO / vanilla"));
            sb.AppendLine("Water API : " + (WaterAvailable ? "YES" : "NO"));
            sb.AppendLine("Mass      : " + FormatMass(MassKg));
            sb.AppendLine("Speed     : " + SpeedMs.ToString("F1") + " m/s");
            sb.AppendLine("Blocks    : " + BlockCount +
                          " (" + BlockDamaged + " dmg / " + BlockCritical + " crit)");
            sb.AppendLine("Hull      : " + (HullIntegrityPct * 100f).ToString("F1") + "%" +
                          (UnderAttack ? "  [UNDER ATTACK]" : ""));
            sb.AppendLine("Power     : " + PowerOutputMW.ToString("F1") + " MW out / " +
                                           PowerDrawMW.ToString("F1") + " MW draw");
            sb.AppendLine("Batteries : " + BatteryTotal +
                          " (" + (BatteryChargePct * 100f).ToString("F0") + "% charge)");
            sb.AppendLine("Thrusters : " + ThrusterEnabled + "/" + ThrusterTotal + " enabled");
            sb.AppendLine("Gyros     : " + GyroFunctional + "/" + GyroTotal + " functional");
            sb.AppendLine("Weapons   : " + WeaponFunctional + "/" + WeaponTotal + " functional");
            if (WaterAvailable)
            {
                sb.AppendLine("Submersion: " + (SubmersionPct * 100f).ToString("F0") + "%");
                sb.AppendLine("Depth     : " + FluidDepthM.ToString("F1") + " m");
            }
        }

        private static string FormatMass(float kg)
        {
            if (kg >= 1000000f) return (kg / 1000000f).ToString("F1") + " Mt";
            if (kg >= 1000f)    return (kg / 1000f).ToString("F1") + " t";
            return kg.ToString("F0") + " kg";
        }
    }
}
