using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace TTNAS
{
    public class WeaponEntry
    {
        public readonly IMyTerminalBlock       Block;
        public readonly Dictionary<string,int> WeaponMap;
        public readonly string                 DisplayName;

        public WeaponEntry(IMyTerminalBlock block, Dictionary<string,int> weaponMap)
        {
            Block       = block;
            WeaponMap   = weaponMap;
            DisplayName = block.CustomName ?? block.DisplayNameText ?? "Unknown";
        }
    }

    public abstract class FireControlBase : MyGameLogicComponent
    {
        protected const int UI_REFRESH_INTERVAL = 30;

        protected IMyTerminalBlock   Block  { get; private set; }
        protected IMyFunctionalBlock FBlock { get; private set; }

        protected readonly List<WeaponEntry> _availableWeapons = new List<WeaponEntry>();
        protected readonly List<ThreatEntry> _availableThreats = new List<ThreatEntry>();
        protected readonly List<WeaponEntry> _selectedWeapons  = new List<WeaponEntry>();
        protected readonly List<ThreatEntry> _selectedTargets  = new List<ThreatEntry>();

        private int  _uiTick;
        private bool _firstUpdate = true;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            Block  = Entity as IMyTerminalBlock;
            FBlock = Entity as IMyFunctionalBlock;
            if (Block == null) return;

            NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
            Block.AppendingCustomInfo += AppendCustomInfo;
            RegisterTerminalControls();
        }

        public override void Close()
        {
            if (Block != null)
                Block.AppendingCustomInfo -= AppendCustomInfo;
            OnClose();
        }

        public override void UpdateAfterSimulation10()
        {
            if (Block == null) return;

            var cic = CICProcessor.GetForGrid(Block.CubeGrid);

            if (cic != null && cic.IsOnline)
            {
                SyncFromCIC(cic);

                if (_firstUpdate)
                {
                    _firstUpdate = false;
                    OnFirstCICSync();
                }
            }

            _uiTick++;
            if (_uiTick >= UI_REFRESH_INTERVAL / 10)
            {
                _uiTick = 0;
                Block.RefreshCustomInfo();
            }

            OnUpdate();
        }

        private void SyncFromCIC(CICProcessor cic)
        {
            var source = SelectWeaponSource(cic.AllWeapons, cic.Statics, cic.Turrets);
            bool weaponsChanged = false;
            if (_availableWeapons.Count != source.Count)
            {
                weaponsChanged = true;
            }
            else
            {
                for (int i = 0; i < source.Count; i++)
                    if (_availableWeapons[i] != source[i]) { weaponsChanged = true; break; }
            }

            if (weaponsChanged)
            {
                _availableWeapons.Clear();
                foreach (var w in source)
                {
                    if (w.Block == null || w.Block.MarkedForClose) continue;
                    if (AcceptWeapon(w)) _availableWeapons.Add(w);
                }
                _selectedWeapons.RemoveAll(w => !_availableWeapons.Contains(w));
                OnWeaponsUpdated();
            }

            bool threatSetChanged = false;
            if (_availableThreats.Count != cic.Threats.Count)
            {
                threatSetChanged = true;
            }
            else
            {
                foreach (var t in cic.Threats)
                {
                    bool found = false;
                    foreach (var a in _availableThreats)
                        if (a.Entity == t.Entity) { found = true; break; }
                    if (!found) { threatSetChanged = true; break; }
                }
            }

            if (threatSetChanged)
            {
                _availableThreats.Clear();
                _availableThreats.AddRange(cic.Threats);
                _selectedTargets.RemoveAll(t => !_availableThreats.Exists(a => a.Entity == t.Entity));
                OnThreatsUpdated();
            }
        }

        protected CICProcessor GetCIC() => CICProcessor.GetForGrid(Block?.CubeGrid);

        protected void RequestWeaponRefresh()
        {
            FCGridCache.I?.RequestWeaponRefresh(Block.CubeGrid);
        }

        protected void RequestThreatRefresh()
        {
            FCGridCache.I?.RequestThreatRefresh(Block.CubeGrid);
        }

        protected bool IsOnline()
        {
            if (FBlock != null && !FBlock.IsWorking) return false;
            if (FCGridCache.I == null || !FCGridCache.I.WcApiReady) return false;
            return true;
        }

        protected static string FormatDist(double metres)
        {
            return metres >= 1000.0
                ? (metres / 1000.0).ToString("F1") + " km"
                : metres.ToString("F0") + " m";
        }

        protected void ShowNotice(string msg)
        {
            MyAPIGateway.Utilities?.ShowNotification(msg, 3000, MyFontEnum.White);
        }

        protected virtual IReadOnlyList<WeaponEntry> SelectWeaponSource(
            IReadOnlyList<WeaponEntry> allWeapons,
            IReadOnlyList<WeaponEntry> staticLaunchers,
            IReadOnlyList<WeaponEntry> turrets)
        {
            return allWeapons;
        }

        protected virtual bool AcceptWeapon(WeaponEntry entry) { return true; }

        protected abstract void RegisterTerminalControls();
        protected abstract void AppendCustomInfo(IMyTerminalBlock block, System.Text.StringBuilder sb);

        protected virtual void OnFirstCICSync()   { }
        protected virtual void OnUpdate()         { }
        protected virtual void OnWeaponsUpdated() { }
        protected virtual void OnThreatsUpdated() { }
        protected virtual void OnClose()          { }
    }
}
