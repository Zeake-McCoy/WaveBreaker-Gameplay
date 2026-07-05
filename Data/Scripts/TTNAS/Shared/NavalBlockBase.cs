using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;

namespace TTNAS
{
    /// <summary>
    /// Base class for all TTNAS game-logic block components.
    /// Handles persistence, lifecycle, network sync scheduling,
    /// terminal control dedup, and info panel boilerplate.
    /// </summary>
    public abstract class NavalBlockBase : MyGameLogicComponent
    {
        protected static bool IsServer =>
            MyAPIGateway.Multiplayer == null || MyAPIGateway.Multiplayer.IsServer;

        protected IMyTerminalBlock Block;

        private bool     _stateLoaded;
        private bool     _needsStateSync;
        private int      _lastStateSyncTick;
        private DateTime _lastInfoUpdate = DateTime.MinValue;

        private const int    STATE_SYNC_INTERVAL  = 60;
        private const double INFO_REFRESH_INTERVAL = 0.5;

        private static readonly HashSet<Type> _registeredTypes = new HashSet<Type>();

        // ── Abstract contract ─────────────────────────────────────────────────

        protected abstract Guid   StorageGuid { get; }
        protected abstract string BuildPersistenceBlob();
        protected abstract void   ParseBlob(string blob);
        protected abstract void   OnStateLoaded();
        protected abstract void   RegisterControls();
        public    abstract void   HandleAction(byte actionType, ulong senderSteamId);

        // ── Virtuals ──────────────────────────────────────────────────────────

        protected virtual bool   ShouldActivate()                                 => true;
        protected virtual string BuildNetworkBlob()                               => BuildPersistenceBlob();
        public    virtual void   ApplyNetworkBlob(string blob)                    => ParseBlob(blob);
        protected virtual void   OnUpdate()                                       { }
        protected virtual void   OnUpdate100()                                    { }
        protected virtual void   AppendBlockInfo(IMyTerminalBlock block, StringBuilder sb) { }
        protected virtual void   OnClose()                                        { }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            Block = Entity as IMyTerminalBlock;
            if (Block == null) return;
            if (!ShouldActivate()) return;

            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME
                         | MyEntityUpdateEnum.EACH_100TH_FRAME
                         | MyEntityUpdateEnum.BEFORE_NEXT_FRAME;

            var t = GetType();
            if (!_registeredTypes.Contains(t))
            {
                _registeredTypes.Add(t);
                try { RegisterControls(); }
                catch (Exception e) { Log("RegisterControls error: " + e); }
            }

            Block.AppendingCustomInfo += HandleAppendInfo;
        }

        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                if (Block == null) Block = Entity as IMyTerminalBlock;
                if (IsServer) { TryLoadState(); _needsStateSync = true; }
            }
            catch (Exception e) { Log("UpdateOnceBeforeFrame error: " + e); }
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                if (Block == null || Block.CubeGrid == null || !Block.IsWorking) return;
                OnUpdate();
                if (IsServer) UpdateNetworkSync();
            }
            catch (Exception e) { Log("UpdateBeforeSimulation error: " + e); }
        }

        public override void UpdateAfterSimulation100()
        {
            try
            {
                if (!_stateLoaded && IsServer) { TryLoadState(); if (_stateLoaded) _needsStateSync = true; }
                if (IsServer && _stateLoaded) OnUpdate100();
            }
            catch (Exception e) { Log("UpdateAfterSimulation100 error: " + e); }
        }

        public override bool IsSerialized()
        {
            try { if (IsServer && Block != null) NavalPersistence.Write(Block.EntityId, BuildPersistenceBlob(), GetType()); }
            catch { }
            return base.IsSerialized();
        }

        public override void Close()
        {
            try
            {
                OnClose();
                if (Block != null) Block.AppendingCustomInfo -= HandleAppendInfo;
            }
            catch { }
        }

        // ── Persistence ───────────────────────────────────────────────────────

        private void TryLoadState()
        {
            if (_stateLoaded) return;
            try
            {
                string blob = NavalPersistence.Read(Block.EntityId, GetType());
                if (string.IsNullOrWhiteSpace(blob) && Block.Storage != null)
                {
                    string storageBlob;
                    if (Block.Storage.TryGetValue(StorageGuid, out storageBlob))
                        blob = storageBlob;
                }
                if (!string.IsNullOrWhiteSpace(blob)) ParseBlob(blob);
                OnStateLoaded();
                _stateLoaded = true;
            }
            catch (Exception e) { Log("TryLoadState error: " + e); }
        }

        protected void SaveState()
        {
            if (!IsServer || Block == null) return;
            try
            {
                var blob = BuildPersistenceBlob();
                NavalPersistence.Write(Block.EntityId, blob, GetType());
                if (Block.Storage == null) Block.Storage = new MyModStorageComponent();
                Block.Storage[StorageGuid] = blob;
            }
            catch (Exception e) { Log("SaveState error: " + e); }
        }

        // ── Network sync ──────────────────────────────────────────────────────

        protected void MarkNeedsSync() => _needsStateSync = true;

        private void UpdateNetworkSync()
        {
            var tick = MyAPIGateway.Session?.GameplayFrameCounter ?? 0;
            if (tick - _lastStateSyncTick < STATE_SYNC_INTERVAL && !_needsStateSync) return;
            NAS_NetworkHandler.Instance?.SendState(Block.EntityId, BuildNetworkBlob());
            _lastStateSyncTick = tick;
            _needsStateSync    = false;
        }

        // ── Info panel ────────────────────────────────────────────────────────

        private void HandleAppendInfo(IMyTerminalBlock block, StringBuilder sb)
        {
            try { AppendBlockInfo(block, sb); }
            catch (Exception e) { Log("AppendBlockInfo error: " + e); }
        }

        protected void SafeRefreshInfo()
        {
            try
            {
                Block?.RefreshCustomInfo();
                MyAPIGateway.Utilities.InvokeOnGameThread(() => { try { Block?.RefreshCustomInfo(); } catch { } });
            }
            catch { }
        }

        protected void ThrottledRefreshInfo()
        {
            var now = DateTime.Now;
            if ((now - _lastInfoUpdate).TotalSeconds >= INFO_REFRESH_INTERVAL)
            {
                _lastInfoUpdate = now;
                SafeRefreshInfo();
            }
        }

        // ── ModAPI surface ────────────────────────────────────────────────────

        public string GetPublicState() => BuildNetworkBlob();

        // ── Logging ───────────────────────────────────────────────────────────

        protected void Log(string msg) => NASLog.Info(GetType().Name, msg);
    }
}
