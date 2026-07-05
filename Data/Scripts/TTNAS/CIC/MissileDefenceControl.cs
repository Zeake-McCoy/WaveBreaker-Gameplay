using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace TTNAS
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule),
        false, "TT_MissileDefence")]
    public class MissileDefenceControl : MyGameLogicComponent
    {
        internal enum WeaponRole { Intercept, ShortRange, CIWS, Countermeasure }

        private static readonly string[] ROLE_TAGS = {
            "INTERCEPT", "SHORTRANGE", "CIWS", "COUNTERMEASURE"
        };

        private static readonly string[] ROLE_LABELS = {
            "Long Range Interceptor",
            "Short Range Interceptor",
            "CIWS",
            "Countermeasures"
        };

        internal class RoleConfig
        {
            public bool  Enabled             = true;
            public float TriggerRangeM       = 8000f;
            public int   MissilesPerIncoming = 1;
            public int   MinMissileCount     = 1;
            public int   SalvoDelayTicks     = 60;
            public int   _cooldownTicks      = 0;
        }

        private readonly Dictionary<WeaponRole, List<WeaponEntry>> _roleWeapons
            = new Dictionary<WeaponRole, List<WeaponEntry>>();

        internal readonly Dictionary<WeaponRole, RoleConfig> _roleConfig
            = new Dictionary<WeaponRole, RoleConfig>();

        private IMyTerminalBlock   _block;
        private IMyFunctionalBlock _fblock;

        private bool _initialised    = false;
        private int  _uiTick         = 0;
        private int  _weaponScanTick = 0;
        private const int WEAPON_SCAN_INTERVAL_TICKS = 12;

        private const string INI_SECTION = "TT Missile Defence";
        private readonly MyIni _ini = new MyIni();

        private static bool _controlsRegistered = false;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            _block  = Entity as IMyTerminalBlock;
            _fblock = Entity as IMyFunctionalBlock;
            if (_block == null) return;

            NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
            _block.AppendingCustomInfo += AppendCustomInfo;

            foreach (WeaponRole role in Enum.GetValues(typeof(WeaponRole)))
            {
                _roleWeapons[role] = new List<WeaponEntry>();
                _roleConfig[role]  = DefaultConfig(role);
            }

            RegisterTerminalControls();
        }

        public override void Close()
        {
            if (_block != null)
                _block.AppendingCustomInfo -= AppendCustomInfo;
        }

        private static RoleConfig DefaultConfig(WeaponRole role)
        {
            switch (role)
            {
                case WeaponRole.Intercept:
                    return new RoleConfig { TriggerRangeM = 15000f, MinMissileCount = 1, MissilesPerIncoming = 1, SalvoDelayTicks = 90 };
                case WeaponRole.ShortRange:
                    return new RoleConfig { TriggerRangeM = 6000f,  MinMissileCount = 1, MissilesPerIncoming = 1, SalvoDelayTicks = 60 };
                case WeaponRole.CIWS:
                    return new RoleConfig { TriggerRangeM = 2000f,  MinMissileCount = 1, MissilesPerIncoming = 1, SalvoDelayTicks = 10 };
                case WeaponRole.Countermeasure:
                    return new RoleConfig { TriggerRangeM = 3000f,  MinMissileCount = 4, MissilesPerIncoming = 1, SalvoDelayTicks = 120 };
                default:
                    return new RoleConfig();
            }
        }

        public override void UpdateAfterSimulation10()
        {
            if (_block == null || !(_fblock?.IsWorking ?? true)) return;

            var cic = CICProcessor.GetForGrid(_block.CubeGrid);
            if (cic == null || !cic.IsOnline) return;

            if (!_initialised) { ReadSettings(); _initialised = true; }

            _weaponScanTick++;
            if (_weaponScanTick >= WEAPON_SCAN_INTERVAL_TICKS)
            {
                _weaponScanTick = 0;
                ScanWeaponGroups();
            }

            foreach (var cfg in _roleConfig.Values)
                if (cfg._cooldownTicks > 0) cfg._cooldownTicks--;

            if (cic.MissilesInbound) EvaluateEngagement(cic);

            _uiTick++;
            if (_uiTick >= 3) { _uiTick = 0; _block.RefreshCustomInfo(); }
        }

        private void ScanWeaponGroups()
        {
            var wc = FCGridCache.I?.WcApi;
            if (wc == null) return;

            foreach (var role in _roleWeapons.Keys) _roleWeapons[role].Clear();

            var allGroups = new List<IMyBlockGroup>();
            MyAPIGateway.TerminalActionsHelper
                ?.GetTerminalSystemForGrid(_block.CubeGrid)
                ?.GetBlockGroups(allGroups);

            foreach (var group in allGroups)
            {
                string gName = group.Name ?? "";
                WeaponRole? matchedRole = null;

                for (int i = 0; i < ROLE_TAGS.Length; i++)
                {
                    if (gName.IndexOf(ROLE_TAGS[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    { matchedRole = (WeaponRole)i; break; }
                }
                if (matchedRole == null) continue;

                var blocks = new List<IMyTerminalBlock>();
                group.GetBlocksOfType<IMyTerminalBlock>(blocks);

                foreach (var tb in blocks)
                {
                    if (tb.CubeGrid.EntityId != _block.CubeGrid.EntityId) continue;
                    var weaponMap = new Dictionary<string, int>();
                    if (!wc.GetBlockWeaponMap(tb, weaponMap) || weaponMap.Count == 0) continue;
                    _roleWeapons[matchedRole.Value].Add(new WeaponEntry(tb, weaponMap));
                }
            }
        }

        private void EvaluateEngagement(CICProcessor cic)
        {
            var wc = FCGridCache.I?.WcApi;
            if (wc == null) return;

            var positions = cic.MissilePositions;
            int count     = positions.Count;
            if (count == 0) return;

            var gridPos = _block.GetPosition();

            var missileRangeSq = new float[count];
            var missileOrder   = new int[count];
            for (int i = 0; i < count; i++)
            {
                missileRangeSq[i] = (float)Vector3D.DistanceSquared(gridPos, positions[i]);
                missileOrder[i]   = i;
            }
            for (int i = 1; i < count; i++)
            {
                float kd = missileRangeSq[i]; int ki = missileOrder[i]; int j = i - 1;
                while (j >= 0 && missileRangeSq[j] > kd)
                {
                    missileRangeSq[j + 1] = missileRangeSq[j];
                    missileOrder[j + 1]   = missileOrder[j];
                    j--;
                }
                missileRangeSq[j + 1] = kd;
                missileOrder[j + 1]   = ki;
            }

            float closestRange = (float)Math.Sqrt(missileRangeSq[0]);

            foreach (WeaponRole role in Enum.GetValues(typeof(WeaponRole)))
            {
                var cfg     = _roleConfig[role];
                var weapons = _roleWeapons[role];

                if (!cfg.Enabled || cfg._cooldownTicks > 0) continue;
                if (weapons.Count == 0) continue;
                if (count < cfg.MinMissileCount) continue;
                if (closestRange > cfg.TriggerRangeM) continue;

                bool fired     = false;
                int  weaponIdx = 0;

                for (int mi = 0; mi < count; mi++)
                {
                    float range = (float)Math.Sqrt(missileRangeSq[mi]);
                    if (range > cfg.TriggerRangeM) break;
                    if (weaponIdx >= weapons.Count) weaponIdx = 0;

                    int missileIdx = missileOrder[mi];

                    for (int shot = 0; shot < cfg.MissilesPerIncoming; shot++)
                    {
                        if (weaponIdx >= weapons.Count) break;
                        var weapon    = weapons[weaponIdx];
                        var weaponEnt = weapon.Block as MyEntity;
                        if (weaponEnt == null) { weaponIdx++; continue; }

                        int weaponId = GetFirstWeaponId(weapon);

                        if (role == WeaponRole.Countermeasure)
                        {
                            wc.FireWeaponOnce(weaponEnt, false, weaponId);
                        }
                        else
                        {
                            var target = FindNearestThreatEntity(positions[missileIdx], cic);
                            if (target != null)
                            {
                                wc.SetWeaponTarget(weaponEnt, target, weaponId);
                                wc.SetAiFocus(_block.CubeGrid, target, 0);
                            }
                            wc.FireWeaponOnce(weaponEnt, false, weaponId);
                        }

                        weaponIdx++;
                        fired = true;
                    }
                }

                if (fired) cfg._cooldownTicks = cfg.SalvoDelayTicks;
            }
        }

        private void RegisterTerminalControls()
        {
            if (_controlsRegistered) return;
            _controlsRegistered = true;

            AddSep("MD_Sep0");
            AddLabel("MD_Lbl0",  "--- LONG RANGE INTERCEPTOR ---");
            AddLabel("MD_Lbl0b", "Group tag: INTERCEPT");
            AddToggle("MD_Int_Enable", "Enable", WeaponRole.Intercept);
            AddSlider("MD_Int_Range", "Trigger Range (km)", WeaponRole.Intercept,
                1f, 50f,
                cfg => cfg.TriggerRangeM / 1000f,
                (cfg, v) => cfg.TriggerRangeM = v * 1000f,
                (cfg, sb) => sb.Append(cfg.TriggerRangeM / 1000f + " km"));
            AddSlider("MD_Int_MPI", "Missiles per Incoming", WeaponRole.Intercept,
                1f, 6f,
                cfg => cfg.MissilesPerIncoming,
                (cfg, v) => cfg.MissilesPerIncoming = (int)Math.Round(v),
                (cfg, sb) => sb.Append(cfg.MissilesPerIncoming));

            AddSep("MD_Sep1");
            AddLabel("MD_Lbl1",  "--- SHORT RANGE INTERCEPTOR ---");
            AddLabel("MD_Lbl1b", "Group tag: SHORTRANGE");
            AddToggle("MD_SR_Enable", "Enable", WeaponRole.ShortRange);
            AddSlider("MD_SR_Range", "Trigger Range (km)", WeaponRole.ShortRange,
                0.5f, 15f,
                cfg => cfg.TriggerRangeM / 1000f,
                (cfg, v) => cfg.TriggerRangeM = v * 1000f,
                (cfg, sb) => sb.Append(cfg.TriggerRangeM / 1000f + " km"));
            AddSlider("MD_SR_MPI", "Missiles per Incoming", WeaponRole.ShortRange,
                1f, 6f,
                cfg => cfg.MissilesPerIncoming,
                (cfg, v) => cfg.MissilesPerIncoming = (int)Math.Round(v),
                (cfg, sb) => sb.Append(cfg.MissilesPerIncoming));

            AddSep("MD_Sep2");
            AddLabel("MD_Lbl2",  "--- CIWS ---");
            AddLabel("MD_Lbl2b", "Group tag: CIWS");
            AddToggle("MD_CW_Enable", "Enable", WeaponRole.CIWS);
            AddSlider("MD_CW_Range", "Trigger Range (km)", WeaponRole.CIWS,
                0.1f, 5f,
                cfg => cfg.TriggerRangeM / 1000f,
                (cfg, v) => cfg.TriggerRangeM = v * 1000f,
                (cfg, sb) => sb.Append(cfg.TriggerRangeM / 1000f + " km"));

            AddSep("MD_Sep3");
            AddLabel("MD_Lbl3",  "--- COUNTERMEASURES ---");
            AddLabel("MD_Lbl3b", "Group tag: COUNTERMEASURE");
            AddToggle("MD_CM_Enable", "Enable", WeaponRole.Countermeasure);
            AddSlider("MD_CM_Range", "Max Range (km)", WeaponRole.Countermeasure,
                0.5f, 10f,
                cfg => cfg.TriggerRangeM / 1000f,
                (cfg, v) => cfg.TriggerRangeM = v * 1000f,
                (cfg, sb) => sb.Append(cfg.TriggerRangeM / 1000f + " km"));
            AddSlider("MD_CM_MinMsl", "Min Incoming Missiles", WeaponRole.Countermeasure,
                1f, 20f,
                cfg => cfg.MinMissileCount,
                (cfg, v) => cfg.MinMissileCount = (int)Math.Round(v),
                (cfg, sb) => sb.Append(cfg.MinMissileCount));

            AddSep("MD_Sep4");
            AddButton("MD_BtnSave", "Save Settings", b =>
                b.GameLogic.GetAs<MissileDefenceControl>()?.WriteDefaultSettings());
        }

        private void AddToggle(string id, string title, WeaponRole role)
        {
            var ctrl = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlOnOffSwitch, IMyUpgradeModule>(id);
            ctrl.Title   = MyStringId.GetOrCompute(title);
            ctrl.OnText  = MyStringId.GetOrCompute("ON");
            ctrl.OffText = MyStringId.GetOrCompute("OFF");
            ctrl.Getter  = b =>
            {
                var l = b.GameLogic.GetAs<MissileDefenceControl>();
                return l != null && l._roleConfig[role].Enabled;
            };
            ctrl.Setter  = (b, v) =>
            {
                var l = b.GameLogic.GetAs<MissileDefenceControl>();
                if (l != null) l._roleConfig[role].Enabled = v;
            };
            ctrl.Visible = b => IsOurBlock(b);
            ctrl.Enabled = _ => true;
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(ctrl);
        }

        private void AddSlider(
            string id, string title, WeaponRole role,
            float min, float max,
            Func<RoleConfig, float> getter,
            Action<RoleConfig, float> setter,
            Action<RoleConfig, StringBuilder> writer)
        {
            var ctrl = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlSlider, IMyUpgradeModule>(id);
            ctrl.Title   = MyStringId.GetOrCompute(title);
            ctrl.SetLimits(min, max);
            ctrl.Getter  = b =>
            {
                var l = b.GameLogic.GetAs<MissileDefenceControl>();
                return l != null ? getter(l._roleConfig[role]) : min;
            };
            ctrl.Setter  = (b, v) =>
            {
                var l = b.GameLogic.GetAs<MissileDefenceControl>();
                if (l != null) setter(l._roleConfig[role], v);
            };
            ctrl.Writer  = (b, sb) =>
            {
                var l = b.GameLogic.GetAs<MissileDefenceControl>();
                if (l != null) writer(l._roleConfig[role], sb);
            };
            ctrl.Visible = b => IsOurBlock(b);
            ctrl.Enabled = b =>
            {
                var l = b.GameLogic.GetAs<MissileDefenceControl>();
                return l != null && l._roleConfig[role].Enabled;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(ctrl);
        }

        private static void AddSep(string id)
        {
            var sep = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlSeparator, IMyUpgradeModule>(id);
            sep.Visible = b => IsOurBlock(b);
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(sep);
        }

        private static void AddLabel(string id, string text)
        {
            var lbl = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlLabel, IMyUpgradeModule>(id);
            lbl.Label   = MyStringId.GetOrCompute(text);
            lbl.Visible = b => IsOurBlock(b);
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(lbl);
        }

        private static void AddButton(string id, string text, Action<IMyTerminalBlock> action)
        {
            var btn = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlButton, IMyUpgradeModule>(id);
            btn.Title   = MyStringId.GetOrCompute(text);
            btn.Action  = action;
            btn.Visible = b => IsOurBlock(b);
            btn.Enabled = _ => true;
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(btn);
        }

        private static bool IsOurBlock(IMyTerminalBlock b)
            => b.BlockDefinition.SubtypeId == "TT_MissileDefence";

        private void ReadSettings()
        {
            string cd = _block.CustomData ?? "";
            MyIniParseResult result;
            if (!_ini.TryParse(cd, out result) || !_ini.ContainsSection(INI_SECTION))
            { WriteDefaultSettings(); return; }

            foreach (WeaponRole role in Enum.GetValues(typeof(WeaponRole)))
            {
                var    cfg = _roleConfig[role];
                string p   = role.ToString();
                cfg.Enabled             = _ini.Get(INI_SECTION, p + "_Enabled").ToBoolean(cfg.Enabled);
                float km                = _ini.Get(INI_SECTION, p + "_TriggerKm").ToSingle(cfg.TriggerRangeM / 1000f);
                cfg.TriggerRangeM       = Math.Max(0.1f, km) * 1000f;
                cfg.MinMissileCount     = Math.Max(1, _ini.Get(INI_SECTION, p + "_MinMissiles").ToInt32(cfg.MinMissileCount));
                cfg.MissilesPerIncoming = Math.Max(1, _ini.Get(INI_SECTION, p + "_MissilesPerIncoming").ToInt32(cfg.MissilesPerIncoming));
                cfg.SalvoDelayTicks     = Math.Max(1, _ini.Get(INI_SECTION, p + "_SalvoDelay").ToInt32(cfg.SalvoDelayTicks));
            }
        }

        internal void WriteDefaultSettings()
        {
            _ini.Clear();
            _ini.Set(INI_SECTION, "_Groups", "INTERCEPT | SHORTRANGE | CIWS | COUNTERMEASURE");

            foreach (WeaponRole role in Enum.GetValues(typeof(WeaponRole)))
            {
                var    cfg = _roleConfig[role];
                string p   = role.ToString();
                _ini.Set(INI_SECTION, p + "_Enabled",             cfg.Enabled);
                _ini.Set(INI_SECTION, p + "_TriggerKm",           cfg.TriggerRangeM / 1000f);
                _ini.Set(INI_SECTION, p + "_MinMissiles",         cfg.MinMissileCount);
                _ini.Set(INI_SECTION, p + "_MissilesPerIncoming", cfg.MissilesPerIncoming);
                _ini.Set(INI_SECTION, p + "_SalvoDelay",          cfg.SalvoDelayTicks);
            }

            _block.CustomData = _ini.ToString();
        }

        private void AppendCustomInfo(IMyTerminalBlock block, StringBuilder sb)
        {
            var cic = CICProcessor.GetForGrid(_block.CubeGrid);
            sb.AppendLine("=== TT MISSILE DEFENCE ===");
            sb.AppendLine("CIC     : " + (cic?.IsOnline == true ? "ONLINE" : "OFFLINE"));
            sb.AppendLine("Inbound : " + (cic?.MissileInboundCount ?? 0) + " missiles");
            sb.AppendLine();

            foreach (WeaponRole role in Enum.GetValues(typeof(WeaponRole)))
            {
                var cfg   = _roleConfig[role];
                var wep   = _roleWeapons[role];
                string state = !cfg.Enabled ? "OFF"
                             : cfg._cooldownTicks > 0 ? "CD:" + cfg._cooldownTicks
                             : "RDY";
                sb.AppendLine(ROLE_LABELS[(int)role]);
                sb.AppendLine("  Wpns:" + wep.Count +
                              "  Range:" + (cfg.TriggerRangeM / 1000f).ToString("F0") + "km" +
                              "  [" + state + "]");
            }
        }

        private static int GetFirstWeaponId(WeaponEntry weapon)
        {
            foreach (var kv in weapon.WeaponMap) return kv.Value;
            return 0;
        }

        private static MyEntity FindNearestThreatEntity(Vector3D pos, CICProcessor cic)
        {
            MyEntity best     = null;
            double   bestDist = double.MaxValue;
            foreach (var t in cic.Threats)
            {
                if (t.Entity == null || t.Entity.MarkedForClose) continue;
                double d = Vector3D.DistanceSquared(pos, t.Entity.PositionComp.GetPosition());
                if (d < bestDist) { bestDist = d; best = t.Entity as MyEntity; }
            }
            return best;
        }
    }
}
