using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace TTNAS
{
    public static class AfterburnerActions
    {
        public const byte SetOn  = 0;
        public const byte SetOff = 1;
    }

    // ───────────────────────────────────────────────────────────────────────────
    // AfterburnerController — attaches to every thruster; only activates for
    // subtypes registered in AfterburnerConfigs.
    //
    // State sync:
    //   Client → Server : NavalActionPacket  (AfterburnerActions.SetOn/SetOff)
    //   Server → Client : NavalStatePacket   (Blob = "1" or "0")
    // ───────────────────────────────────────────────────────────────────────────
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Thrust), false)]
    public class AfterburnerController : MyGameLogicComponent
    {
        private static readonly Guid STORAGE_KEY =
            new Guid("A8F3C21D-5E7B-4A2F-9C6D-1B3E8F4A7D2C");

        private static Dictionary<long, AfterburnerController> _instances;
        private static bool _controlsRegistered;

        static AfterburnerController()
        {
            _instances = new Dictionary<long, AfterburnerController>();
        }

        private static bool IsServer =>
            MyAPIGateway.Multiplayer == null || MyAPIGateway.Multiplayer.IsServer;

        private MyThrust         _block;
        private AfterburnerConfig _config;
        private bool              _state;
        private bool              _isActuallyBurning;
        private bool              _particleCreateFailed;

        private MatrixD          _particleMatrix   = MatrixD.Identity;
        private Vector3D         _particlePosition = Vector3D.Zero;
        private MyParticleEffect _particle;

        private MySoundPair          _audio;
        private MyEntity3DSoundEmitter _engineSound;

        // ── Public API ─────────────────────────────────────────────────────────

        public static AfterburnerController GetByEntityId(long id)
        {
            if (_instances == null) return null;
            AfterburnerController inst;
            return _instances.TryGetValue(id, out inst) ? inst : null;
        }

        public bool GetState() => _state;

        /// <summary>
        /// Called from terminal controls (any side).  Routes through network when on client.
        /// </summary>
        public void SetState(bool b)
        {
            if (IsServer)
                ApplyStateServer(b);
            else
                NAS_NetworkHandler.Instance?.SendAction(
                    Entity.EntityId,
                    b ? AfterburnerActions.SetOn : AfterburnerActions.SetOff);
        }

        /// <summary>Server-side: apply action received from a client.</summary>
        public void HandleAction(byte actionType)
        {
            if (!IsServer) return;
            ApplyStateServer(actionType == AfterburnerActions.SetOn);
        }

        /// <summary>Client-side: apply state blob received from server.</summary>
        public void ApplyNetworkBlob(string blob) => _state = blob == "1";

        // ── Lifecycle ──────────────────────────────────────────────────────────

        public override void Init(MyObjectBuilder_EntityBase ob)
        {
            base.Init(ob);
            _block = Entity as MyThrust;
            if (_block == null) return;

            // Don't look up config here — Init() runs before BeforeStart(), so
            // external mod registrations haven't arrived yet.  Defer to
            // UpdateOnceBeforeFrame() which fires on the first simulation tick,
            // after all BeforeStart() calls have completed.
            // Use _block.EntityId (not Entity.EntityId) — Entity property resolves
            // through the container which may not be set yet during Init.
            if (_instances == null) _instances = new Dictionary<long, AfterburnerController>();
            try { _instances[_block.EntityId] = this; }
            catch { _instances = new Dictionary<long, AfterburnerController> { { _block.EntityId, this } }; }
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (_block == null) return;

            // Config lookup deferred from Init() — registrations are now in the dict.
            AfterburnerConfig cfg;
            if (!AfterburnerConfigs.TryGet(_block.BlockDefinition.Id.SubtypeName, out cfg))
            {
                // Not a registered afterburner subtype — still register controls once
                // so the visibility lambda fires correctly for any subtype that might
                // be registered later (e.g. hot-reload scenarios).
                RegisterTerminalControls();
                return;
            }
            _config      = cfg;
            _audio       = new MySoundPair(_config.SoundEffect);
            _engineSound = new MyEntity3DSoundEmitter(_block);

            // Restore persisted state
            try
            {
                string saved;
                if (Entity.Storage != null && Entity.Storage.TryGetValue(STORAGE_KEY, out saved))
                    _state = saved == "1";
            }
            catch { }

            RegisterTerminalControls();
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
        }

        public override void UpdateAfterSimulation()
        {
            if (_config == null) return;
            if (_block == null || _block.MarkedForClose || _block.CubeGrid == null) return;

            ((IMyThrust)_block).PowerConsumptionMultiplier = 1f;

            // IsWorking = IsFunctional && IsEnabled && has fuel/power.
            // This correctly gates afterburner when hydrogen tanks are empty.
            bool currentlyBurning = _state && _block.IsWorking;
            if (currentlyBurning)
            {
                ((IMyThrust)_block).PowerConsumptionMultiplier = _config.PowerMultiplier;
                var grid = _block.CubeGrid;
                if (grid.Physics != null)
                {
                    Vector3D force = _block.WorldMatrix.Backward
                        * _block.BlockDefinition.ForceMagnitude
                        * _block.CurrentStrength
                        * _config.ThrustMultiplier;
                    grid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, force,
                        grid.Physics.CenterOfMassWorld, null);
                }
            }

            if (_isActuallyBurning != currentlyBurning)
            {
                _isActuallyBurning = currentlyBurning;
                AfterburnerManager.Instance?.SetThrusterActive(_block.CubeGrid, _isActuallyBurning);
            }

            HandleParticle(currentlyBurning);
            HandleSound(currentlyBurning);
        }

        public override void Close()
        {
            try
            {
                _instances?.Remove(_block?.EntityId ?? Entity.EntityId);

                if (_isActuallyBurning)
                {
                    _isActuallyBurning = false;
                    var grid = _block?.CubeGrid;
                    if (grid != null && !grid.MarkedForClose)
                        AfterburnerManager.Instance?.SetThrusterActive(grid, false);
                }

                _engineSound?.StopSound(true);
                _engineSound?.Cleanup();

                if (_particle != null)
                {
                    _particle.Stop(false);
                    _particle.StopEmitting();
                    _particle.StopLights();
                    _particle = null;
                }
            }
            catch (Exception e) { NASLog.Error("AfterburnerController", "Close error: " + e); }

            base.Close();
        }

        // ── Internals ──────────────────────────────────────────────────────────

        private void ApplyStateServer(bool b)
        {
            _state = b;
            try { if (Entity.Storage != null) Entity.Storage[STORAGE_KEY] = b ? "1" : "0"; }
            catch { }
            NAS_NetworkHandler.Instance?.SendState(Entity.EntityId, b ? "1" : "0");
        }

        private void HandleParticle(bool shouldBurn)
        {
            if (_particle == null)
            {
                if (shouldBurn && !_particleCreateFailed)
                {
                    _particleMatrix   = _block.WorldMatrix;
                    _particlePosition = _particleMatrix.Translation;
                    bool ok = MyParticlesManager.TryCreateParticleEffect(
                        _config.ParticleEffect, ref _particleMatrix, ref _particlePosition,
                        uint.MaxValue, out _particle);
                    if (!ok) _particleCreateFailed = true;
                }
            }
            else
            {
                if (shouldBurn)
                {
                    _particle.WorldMatrix        = _block.WorldMatrix;
                    _particle.UserLifeMultiplier = 0.5f;
                    _particle.UserScale          = _config.ParticleScale;
                    _particle.Update();
                    _particle.Play();
                }
                else
                {
                    _particle.StopEmitting();
                    _particle.StopLights();
                }
            }
        }

        private void HandleSound(bool shouldBurn)
        {
            if (shouldBurn)
            {
                if (!_engineSound.IsPlaying)
                {
                    _engineSound.VolumeMultiplier  = 1f;
                    _engineSound.CustomMaxDistance = 5000f;
                    _engineSound.PlaySoundWithDistance(_audio.SoundId, false);
                }
            }
            else
            {
                _engineSound.StopSound(true, true);
            }
        }

        // ── Terminal controls (registered once) ────────────────────────────────

        private static void RegisterTerminalControls()
        {
            if (_controlsRegistered) return;
            _controlsRegistered = true;

            var toggle = MyAPIGateway.TerminalControls
                .CreateControl<IMyTerminalControlOnOffSwitch, Sandbox.ModAPI.Ingame.IMyThrust>(
                    "TT_AfterburnerOnOff");
            toggle.Getter  = b => { var l = GetByEntityId(b.EntityId); return l != null && l.GetState(); };
            toggle.Setter  = (b, v) => GetByEntityId(b.EntityId)?.SetState(v);
            toggle.Title   = MyStringId.GetOrCompute("Afterburner");
            toggle.OnText  = MyStringId.GetOrCompute("On");
            toggle.OffText = MyStringId.GetOrCompute("Off");
            toggle.Visible = b => AfterburnerConfigs.Contains(b?.BlockDefinition.SubtypeId);
            toggle.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyThrust>(toggle);

            var action = MyAPIGateway.TerminalControls
                .CreateAction<Sandbox.ModAPI.Ingame.IMyThrust>("TT_AfterburnerToggle");
            action.Action  = b => { var l = GetByEntityId(b.EntityId); l?.SetState(!l.GetState()); };
            action.Name    = new StringBuilder("Afterburner On/Off");
            action.Writer  = (b, sb) =>
            {
                sb.Clear();
                var l = GetByEntityId(b.EntityId);
                sb.Append(l != null && l.GetState() ? "On" : "Off");
            };
            action.Enabled = b => AfterburnerConfigs.Contains(b?.BlockDefinition.SubtypeId);
            action.ValidForGroups = true;
            MyAPIGateway.TerminalControls.AddAction<Sandbox.ModAPI.Ingame.IMyThrust>(action);
        }
    }
}
