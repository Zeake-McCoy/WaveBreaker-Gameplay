using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using CoreSystems.Api;
using Jakaria.API;

namespace TTNAS
{
    // ─────────────────────────────────────────────────────────────────────────────
    // NAS_Session — unified singleton session component for TTNavalAdvancedSystems.
    //
    // Owns:
    //   • WcApi (WeaponCore, optional)
    //   • WaterModAPI (optional)
    //   • EngGridCache dictionary + 100-tick engineering scan
    //   • CableRenderer list + per-frame draw pass
    //
    // Access pattern:
    //   NAS_Session.I              — singleton
    //   NAS_Session.I.WcReady      — true if WC is loaded
    //   NAS_Session.I.WcApi        — WC API handle
    //   NAS_Session.I.WaterReady   — true if WaterMod is loaded
    //   NAS_Session.I.GetOrCreate(grid)  — get/create EngGridCache
    //   NAS_Session.I.Peek(grid)         — read-only EngGridCache access
    // ─────────────────────────────────────────────────────────────────────────────
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation | MyUpdateOrder.BeforeSimulation)]
    public class NAS_Session : MySessionComponentBase
    {
        public static NAS_Session I { get; private set; }

        // ── Optional APIs ─────────────────────────────────────────────────────

        private WcApi _wc;
        public bool  WcReady    => _wc != null && _wc.IsReady;
        public WcApi WcApi      => _wc;
        public bool  WaterReady => WaterModAPI.Registered;

        // ── Engineering grid caches ───────────────────────────────────────────

        private readonly Dictionary<long, EngGridCache> _engCaches = new Dictionary<long, EngGridCache>();

        private int _tick;
        private const int SCAN_INTERVAL    = 100;
        private const int PRUNE_IDLE_TICKS = 300;

        // ── Cable renderers ───────────────────────────────────────────────────

        private readonly List<CableRendererInstance> _cableRenderers = new List<CableRendererInstance>();

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public override void LoadData()
        {
            I = this;
        }

        public override void BeforeStart()
        {
            try { _wc = new WcApi(); _wc.Load(); }
            catch { _wc = null; }
        }

        protected override void UnloadData()
        {
            if (_wc != null) { try { _wc.Unload(); } catch { } _wc = null; }

            foreach (var kv in _engCaches) kv.Value.Dispose();
            _engCaches.Clear();
            _cableRenderers.Clear();

            I = null;
        }

        // ── Engineering cache access ──────────────────────────────────────────

        /// <summary>Returns (or creates) the EngGridCache for this grid.</summary>
        public EngGridCache GetOrCreate(IMyCubeGrid grid)
        {
            EngGridCache cache;
            if (!_engCaches.TryGetValue(grid.EntityId, out cache))
            {
                cache = new EngGridCache(grid);
                _engCaches[grid.EntityId] = cache;
            }
            cache.LastAccessTick = _tick;
            return cache;
        }

        /// <summary>Returns the EngGridCache if it exists, otherwise null.</summary>
        public EngGridCache Peek(IMyCubeGrid grid)
        {
            EngGridCache cache;
            if (!_engCaches.TryGetValue(grid.EntityId, out cache)) return null;
            cache.LastAccessTick = _tick;
            return cache;
        }

        // ── Cable renderer registration ───────────────────────────────────────

        public void RegisterCableRenderer(CableRendererInstance r)
        {
            if (r != null && !_cableRenderers.Contains(r)) _cableRenderers.Add(r);
        }

        public void UnregisterCableRenderer(CableRendererInstance r)
        {
            if (r != null) _cableRenderers.Remove(r);
        }

        // ── Update ────────────────────────────────────────────────────────────

        public override void UpdateAfterSimulation()
        {
            _tick++;

            // Prune idle caches
            if (_tick % PRUNE_IDLE_TICKS == 0)
            {
                var remove = new List<long>();
                foreach (var kv in _engCaches)
                    if (kv.Value.SubscriberCount == 0 &&
                        _tick - kv.Value.LastAccessTick > PRUNE_IDLE_TICKS)
                        remove.Add(kv.Key);

                foreach (long id in remove)
                {
                    _engCaches[id].Dispose();
                    _engCaches.Remove(id);
                }
            }

            // Engineering scan
            if (_tick % SCAN_INTERVAL == 0)
                foreach (var kv in _engCaches)
                    kv.Value.Refresh(_wc, WaterReady);
        }

        // ── Draw (cable renderers) ────────────────────────────────────────────

        public override void Draw()
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;
            for (int i = 0; i < _cableRenderers.Count; i++)
                _cableRenderers[i]?.Draw();
        }
    }
}
