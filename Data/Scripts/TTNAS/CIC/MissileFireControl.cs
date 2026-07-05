using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace TTNAS
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule),
        false, "TT_FireControl_Missile")]
    public class MissileFireControl : FireControlBase
    {
        private const int    FIRE_DELAY_TICKS = 6;
        private const string SUBTYPE          = "TT_FireControl_Missile";

        // ── Grid size filter ──────────────────────────────────────────────────
        // 0 = All, 1 = Large Grid only, 2 = Small Grid only
        private int   _gridSizeFilter = 0;

        // ── Range limit ───────────────────────────────────────────────────────
        // 0 = unlimited.  Slider range 0–100 km (stored as km).
        private float _maxRangeKm = 0f;

        // ── Auto-engage ───────────────────────────────────────────────────────
        // When true, OnUpdate() fires Engage() automatically whenever threats
        // enter range and selected weapons are available — no manual button needed.
        private bool _autoEngage     = false;
        private int  _autoEngageCooldownTick = 0;
        private const int AUTO_ENGAGE_COOLDOWN = 300; // ~5 s between auto volleys

        // ── Public LCD API ────────────────────────────────────────────────────
        public int    GridSizeFilter      => _gridSizeFilter;
        public float  MaxRangeKm          => _maxRangeKm;
        public bool   AutoEngage          => _autoEngage;
        public bool   IsEngaging          => _engaging;
        public int    FireQueueCount      => _fireQueue.Count;
        public int    LastShotsFired      => _lastShotsFired;
        public string LastEngageSummary   => _lastEngagementSummary;
        public int    SalvoCount          => _salvoCount;
        public int    SelectedWeaponCount => _selectedWeapons.Count;
        public IReadOnlyList<ThreatEntry> SelectedTargets => _selectedTargets;

        public bool IsWeaponSelected(WeaponEntry entry) => _selectedWeapons.Contains(entry);

        private struct FireOrder
        {
            public IMyTerminalBlock Weapon;
            public int              WeaponId;
            public MyEntity         Target;
        }

        private readonly Queue<FireOrder> _fireQueue = new Queue<FireOrder>();
        private int    _salvoCount            = 1;
        private int    _fireDelayTick         = 0;
        private bool   _engaging              = false;
        private int    _lastShotsFired        = 0;
        private string _lastEngagementSummary = "";

        private static bool _controlsRegistered = false;

        protected override IReadOnlyList<WeaponEntry> SelectWeaponSource(
            IReadOnlyList<WeaponEntry> allWeapons,
            IReadOnlyList<WeaponEntry> staticLaunchers,
            IReadOnlyList<WeaponEntry> turrets)
        {
            return staticLaunchers.Count > 0 ? staticLaunchers : allWeapons;
        }

        // Returns true if the threat's grid size matches the current filter.
        // Missiles and non-grid entities always pass (only grids are filtered).
        private bool AcceptThreat(ThreatEntry t)
        {
            if (_gridSizeFilter == 0) return true;
            var grid = t.Entity as IMyCubeGrid;
            if (grid == null) return true;
            bool isLG = grid.GridSizeEnum == MyCubeSize.Large;
            if (_gridSizeFilter == 1 && !isLG) return false;
            if (_gridSizeFilter == 2 &&  isLG) return false;
            return true;
        }

        private bool ThreatInRange(ThreatEntry t)
        {
            if (_maxRangeKm <= 0f) return true;
            return t.RangeMetres <= _maxRangeKm * 1000.0;
        }

        private bool ThreatPassesFilters(ThreatEntry t)
            => AcceptThreat(t) && ThreatInRange(t);

        protected override void RegisterTerminalControls()
        {
            if (_controlsRegistered) return;
            _controlsRegistered = true;

            AddSep("FC_M_Sep0");
            AddLabel("FC_M_LblTargets", "--- TARGET SELECTION ---");

            var threatList = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlListbox, IMyUpgradeModule>("FC_M_ThreatList");
            threatList.Title            = MyStringId.GetOrCompute("Targets");
            threatList.Tooltip          = MyStringId.GetOrCompute("WC-sorted threats on this grid");
            threatList.VisibleRowsCount = 6;
            threatList.Multiselect      = true;
            threatList.ListContent = (b, items, selected) =>
            {
                var logic = b.GameLogic.GetAs<MissileFireControl>();
                if (logic == null) return;
                foreach (var threat in logic._availableThreats)
                {
                    if (threat.Entity == null || threat.Entity.MarkedForClose) continue;
                    if (!logic.AcceptThreat(threat)) continue;
                    var item = new MyTerminalControlListBoxItem(
                        MyStringId.GetOrCompute(threat.ListLabel),
                        MyStringId.GetOrCompute(threat.ListLabel),
                        threat);
                    items.Add(item);
                    if (logic._selectedTargets.Contains(threat)) selected.Add(item);
                }
            };
            threatList.ItemSelected = (b, items) =>
            {
                var logic = b.GameLogic.GetAs<MissileFireControl>();
                if (logic == null) return;
                logic._selectedTargets.Clear();
                foreach (var item in items)
                {
                    var t = item.UserData as ThreatEntry;
                    if (t != null) logic._selectedTargets.Add(t);
                }
            };
            threatList.Enabled = _ => true;
            threatList.Visible = b => IsOurBlock(b);
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(threatList);

            AddButton("FC_M_BtnRefreshTargets", "Refresh Targets", b =>
                b.GameLogic.GetAs<MissileFireControl>()?.RequestThreatRefresh());

            AddSep("FC_M_Sep1");
            AddLabel("FC_M_LblWeapons", "--- WEAPON SELECTION ---");

            // ── Grid size filter ──────────────────────────────────────────────
            var sizeFilter = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlCombobox, IMyUpgradeModule>("FC_M_GridSizeFilter");
            sizeFilter.Title   = MyStringId.GetOrCompute("Target Filter");
            sizeFilter.Tooltip = MyStringId.GetOrCompute("Filter targets by grid size");
            sizeFilter.ComboBoxContent = list =>
            {
                list.Add(new MyTerminalControlComboBoxItem { Key = 0, Value = MyStringId.GetOrCompute("All Targets") });
                list.Add(new MyTerminalControlComboBoxItem { Key = 1, Value = MyStringId.GetOrCompute("Large Grid Only") });
                list.Add(new MyTerminalControlComboBoxItem { Key = 2, Value = MyStringId.GetOrCompute("Small Grid Only") });
            };
            sizeFilter.Getter  = b => b.GameLogic.GetAs<MissileFireControl>()?._gridSizeFilter ?? 0;
            sizeFilter.Setter  = (b, v) =>
            {
                var logic = b.GameLogic.GetAs<MissileFireControl>();
                if (logic == null) return;
                logic._gridSizeFilter = (int)v;
                // Clear any selected targets that no longer pass the new filter.
                logic._selectedTargets.RemoveAll(t => !logic.AcceptThreat(t));
            };
            sizeFilter.Enabled = _ => true;
            sizeFilter.Visible = b => IsOurBlock(b);
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(sizeFilter);

            var weaponList = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlListbox, IMyUpgradeModule>("FC_M_WeaponList");
            weaponList.Title            = MyStringId.GetOrCompute("Static Launchers");
            weaponList.Tooltip          = MyStringId.GetOrCompute("WC static launchers on this grid");
            weaponList.VisibleRowsCount = 6;
            weaponList.Multiselect      = true;
            weaponList.ListContent = (b, items, selected) =>
            {
                var logic = b.GameLogic.GetAs<MissileFireControl>();
                if (logic == null) return;
                foreach (var entry in logic._availableWeapons)
                {
                    if (entry.Block == null || entry.Block.MarkedForClose) continue;
                    var item = new MyTerminalControlListBoxItem(
                        MyStringId.GetOrCompute(entry.DisplayName),
                        MyStringId.GetOrCompute(entry.DisplayName),
                        entry);
                    items.Add(item);
                    if (logic._selectedWeapons.Contains(entry)) selected.Add(item);
                }
            };
            weaponList.ItemSelected = (b, items) =>
            {
                var logic = b.GameLogic.GetAs<MissileFireControl>();
                if (logic == null) return;
                logic._selectedWeapons.Clear();
                foreach (var item in items)
                {
                    var entry = item.UserData as WeaponEntry;
                    if (entry != null) logic._selectedWeapons.Add(entry);
                }
            };
            weaponList.Enabled = _ => true;
            weaponList.Visible = b => IsOurBlock(b);
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(weaponList);

            AddButton("FC_M_BtnRefreshWeapons", "Refresh Weapons", b =>
                b.GameLogic.GetAs<MissileFireControl>()?.RequestWeaponRefresh());

            AddSep("FC_M_Sep2");
            AddLabel("FC_M_LblFire", "--- FIRE CONTROL ---");

            // ── Max range slider (0 = unlimited, 1–100 km) ────────────────────
            var rangeSlider = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlSlider, IMyUpgradeModule>("FC_M_RangeSlider");
            rangeSlider.Title   = MyStringId.GetOrCompute("Max Range (km)");
            rangeSlider.Tooltip = MyStringId.GetOrCompute("0 = unlimited; targets beyond this range are ignored by auto-engage");
            rangeSlider.SetLimits(0f, 100f);
            rangeSlider.Getter  = b => b.GameLogic.GetAs<MissileFireControl>()?._maxRangeKm ?? 0f;
            rangeSlider.Setter  = (b, v) =>
            {
                var logic = b.GameLogic.GetAs<MissileFireControl>();
                if (logic != null) logic._maxRangeKm = v < 1f ? 0f : (float)Math.Round(v);
            };
            rangeSlider.Writer = (b, sb) =>
            {
                var logic = b.GameLogic.GetAs<MissileFireControl>();
                float km = logic?._maxRangeKm ?? 0f;
                sb.Append(km <= 0f ? "Unlimited" : km.ToString("F0") + " km");
            };
            rangeSlider.Enabled = _ => true;
            rangeSlider.Visible = b => IsOurBlock(b);
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(rangeSlider);

            // ── Auto-engage toggle ─────────────────────────────────────────────
            var autoToggle = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlOnOffSwitch, IMyUpgradeModule>("FC_M_AutoEngage");
            autoToggle.Title   = MyStringId.GetOrCompute("Auto Engage");
            autoToggle.Tooltip = MyStringId.GetOrCompute("Automatically fire on threats in range using selected weapons");
            autoToggle.OnText  = MyStringId.GetOrCompute("On");
            autoToggle.OffText = MyStringId.GetOrCompute("Off");
            autoToggle.Getter  = b => b.GameLogic.GetAs<MissileFireControl>()?._autoEngage ?? false;
            autoToggle.Setter  = (b, v) =>
            {
                var logic = b.GameLogic.GetAs<MissileFireControl>();
                if (logic != null)
                {
                    logic._autoEngage = v;
                    logic._autoEngageCooldownTick = 0;
                }
            };
            autoToggle.Enabled = _ => true;
            autoToggle.Visible = b => IsOurBlock(b);
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(autoToggle);

            var salvoSlider = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlSlider, IMyUpgradeModule>("FC_M_SalvoSlider");
            salvoSlider.Title   = MyStringId.GetOrCompute("Salvo Size");
            salvoSlider.Tooltip = MyStringId.GetOrCompute("Missiles per target");
            salvoSlider.SetLimits(1, 8);
            salvoSlider.Getter  = b => b.GameLogic.GetAs<MissileFireControl>()?._salvoCount ?? 1;
            salvoSlider.Setter  = (b, v) =>
            {
                var logic = b.GameLogic.GetAs<MissileFireControl>();
                if (logic != null) logic._salvoCount = (int)Math.Round(v);
            };
            salvoSlider.Writer = (b, sb) =>
            {
                var logic = b.GameLogic.GetAs<MissileFireControl>();
                sb.Append(logic?._salvoCount ?? 1);
            };
            salvoSlider.Enabled = _ => true;
            salvoSlider.Visible = b => IsOurBlock(b);
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(salvoSlider);

            AddButton("FC_M_BtnEngage", "ENGAGE", b =>
                b.GameLogic.GetAs<MissileFireControl>()?.Engage());

            AddButton("FC_M_BtnClear", "Clear All Selections", b =>
                b.GameLogic.GetAs<MissileFireControl>()?.ClearSelections());
        }

        private void Engage()
        {
            if (_selectedTargets.Count == 0 || _selectedWeapons.Count == 0)
            {
                ShowNotice("[FC] No targets or weapons selected.");
                return;
            }

            _fireQueue.Clear();
            int weaponIndex = 0;

            foreach (var threat in _selectedTargets)
            {
                if (threat.Entity == null || threat.Entity.MarkedForClose) continue;
                for (int s = 0; s < _salvoCount; s++)
                {
                    var entry = _selectedWeapons[weaponIndex % _selectedWeapons.Count];
                    weaponIndex++;
                    _fireQueue.Enqueue(new FireOrder
                    {
                        Weapon   = entry.Block,
                        WeaponId = 0,
                        Target   = threat.Entity,
                    });
                }
            }

            _engaging       = _fireQueue.Count > 0;
            _lastShotsFired = 0;
            _fireDelayTick  = 0;

            if (_engaging)
                _lastEngagementSummary =
                    _selectedTargets.Count + " target(s), " + _salvoCount + " missile(s) each";
        }

        private void ExecuteOrder(FireOrder order)
        {
            var wc = FCGridCache.I?.WcApi;
            if (wc == null) return;

            var weaponEnt = order.Weapon as MyEntity;
            if (weaponEnt == null || weaponEnt.MarkedForClose) return;
            if (order.Target == null || order.Target.MarkedForClose) return;

            wc.SetWeaponTarget(weaponEnt, order.Target, order.WeaponId);
            wc.SetAiFocus(Block.CubeGrid, order.Target, 0);
            wc.FireWeaponOnce(weaponEnt, false, order.WeaponId);

            _lastShotsFired++;

            NASLog.Info("FC", "Fire: " + order.Weapon.DisplayNameText +
                " -> " + order.Target.DisplayName + " [" + order.Target.EntityId + "]");
        }

        private void ClearSelections()
        {
            _selectedTargets.Clear();
            _selectedWeapons.Clear();
            _fireQueue.Clear();
            _engaging = false;
        }

        protected override void OnUpdate()
        {
            // ── Fire queue drain ──────────────────────────────────────────────
            if (_engaging && _fireQueue.Count > 0)
            {
                _fireDelayTick++;
                if (_fireDelayTick >= FIRE_DELAY_TICKS)
                {
                    _fireDelayTick = 0;
                    ExecuteOrder(_fireQueue.Dequeue());
                    if (_fireQueue.Count == 0) _engaging = false;
                }
                return; // don't also auto-engage while already firing
            }
            _engaging = false;

            // ── Auto-engage ───────────────────────────────────────────────────
            if (!_autoEngage) return;
            if (_selectedWeapons.Count == 0) return;

            _autoEngageCooldownTick++;
            if (_autoEngageCooldownTick < AUTO_ENGAGE_COOLDOWN) return;

            // Find targets in range (from all known threats, not just selected)
            // that haven't been manually deselected. Auto-engage uses the current
            // _availableThreats filtered by range — it does NOT require the operator
            // to have clicked targets in the terminal list.
            bool anyInRange = false;
            foreach (var t in _availableThreats)
            {
                if (t.Entity == null || t.Entity.MarkedForClose) continue;
                if (ThreatPassesFilters(t)) { anyInRange = true; break; }
            }
            if (!anyInRange) return;

            // Build fire queue using filtered in-range threats round-robined across selected weapons.
            _fireQueue.Clear();
            int weaponIndex = 0;
            int targets     = 0;
            foreach (var threat in _availableThreats)
            {
                if (threat.Entity == null || threat.Entity.MarkedForClose) continue;
                if (!ThreatPassesFilters(threat)) continue;

                for (int s = 0; s < _salvoCount; s++)
                {
                    var entry = _selectedWeapons[weaponIndex % _selectedWeapons.Count];
                    weaponIndex++;
                    _fireQueue.Enqueue(new FireOrder
                    {
                        Weapon   = entry.Block,
                        WeaponId = 0,
                        Target   = threat.Entity,
                    });
                }
                targets++;
            }

            if (_fireQueue.Count > 0)
            {
                _engaging                = true;
                _lastShotsFired          = 0;
                _fireDelayTick           = 0;
                _autoEngageCooldownTick  = 0;
                _lastEngagementSummary   = "[AUTO] " + targets + " target(s), " + _salvoCount + " missile(s) each";
                NASLog.Info("FC", "Auto-engage: " + _lastEngagementSummary);
            }
        }

        protected override void AppendCustomInfo(IMyTerminalBlock block, System.Text.StringBuilder sb)
        {
            sb.AppendLine("=== Missile Fire Control ===");
            if (!IsOnline()) { sb.AppendLine("STATUS: OFFLINE"); return; }
            sb.AppendLine(_engaging ? "STATUS: ENGAGING" : "STATUS: READY");
            sb.AppendLine("Tgt Filter: " + (_gridSizeFilter == 0 ? "All" : _gridSizeFilter == 1 ? "LG Only" : "SG Only"));
            sb.AppendLine("Max Range: " + (_maxRangeKm <= 0f ? "Unlimited" : _maxRangeKm.ToString("F0") + " km"));
            sb.AppendLine("Auto Eng : " + (_autoEngage ? "ON" : "off"));
            sb.AppendLine("Targets  : " + _selectedTargets.Count + " selected / " + _availableThreats.Count + " known");
            sb.AppendLine("Weapons  : " + _selectedWeapons.Count + " selected / " + _availableWeapons.Count + " available");
            sb.AppendLine("Salvo    : " + _salvoCount);

            if (_selectedTargets.Count > 0)
            {
                sb.AppendLine("--- Selected Targets ---");
                foreach (var t in _selectedTargets)
                {
                    if (t.Entity == null || t.Entity.MarkedForClose) continue;
                    string incoming = t.IncomingCount > 0 ? " !" + t.IncomingCount : "";
                    sb.AppendLine("  " + t.DisplayName);
                    sb.AppendLine("    Range: " + FormatDist(t.RangeMetres) +
                                  "  Speed: " + t.SpeedMs.ToString("F0") + "m/s" + incoming);
                }
            }

            if (_fireQueue.Count > 0)
                sb.AppendLine("Shots queued : " + _fireQueue.Count);
            if (!string.IsNullOrEmpty(_lastEngagementSummary))
                sb.AppendLine("Last engage  : " + _lastEngagementSummary);
            if (_lastShotsFired > 0)
                sb.AppendLine("Shots fired  : " + _lastShotsFired);
        }

        private static bool IsOurBlock(IMyTerminalBlock b)
            => b.BlockDefinition.SubtypeId == SUBTYPE;

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
            btn.Enabled = _ => true;
            btn.Visible = b => IsOurBlock(b);
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(btn);
        }
    }
}
