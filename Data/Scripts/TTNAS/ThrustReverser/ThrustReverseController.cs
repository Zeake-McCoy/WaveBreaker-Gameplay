using System;
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
    public static class ThrustReverserActions
    {
        public const byte SetOn  = 0;
        public const byte SetOff = 1;
    }

    // ───────────────────────────────────────────────────────────────────────────
    // ThrustReverseController — attaches to every thruster; only activates for
    // subtypes registered in ThrustReverserConfigs.
    //
    // State sync:
    //   Client → Server : NavalActionPacket  (ThrustReverserActions.SetOn/SetOff)
    //   Server → Client : NavalStatePacket   (Blob = "1" or "0")
    // ───────────────────────────────────────────────────────────────────────────
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Thrust), false)]
    public class ThrustReverseController : MyGameLogicComponent
    {
        private static bool _controlsRegistered;

        private static bool IsServer =>
            MyAPIGateway.Multiplayer == null || MyAPIGateway.Multiplayer.IsServer;

        private MyThrust            _block;
        private ThrustReverserConfig _config;
        private bool                 _state;
        private bool                 _preState;
        private int                  _ticks;

        private MyEntitySubpart _subpart;
        private bool            _subpartFirstFind = true;
        private Matrix          _subpartLocalMatrix;

        private enum AnimStatus { None, Extend, Retract }
        private AnimStatus _animStatus = AnimStatus.None;

        // ── Public API ─────────────────────────────────────────────────────────

        public bool GetState() => _state;

        public void SetState(bool b)
        {
            if (IsServer)
                ApplyStateServer(b);
            else
                NAS_NetworkHandler.Instance?.SendAction(
                    Entity.EntityId,
                    b ? ThrustReverserActions.SetOn : ThrustReverserActions.SetOff);
        }

        public void HandleAction(byte actionType)
        {
            if (!IsServer) return;
            ApplyStateServer(actionType == ThrustReverserActions.SetOn);
        }

        public void ApplyNetworkBlob(string blob) => _state = blob == "1";

        // ── Lifecycle ──────────────────────────────────────────────────────────

        public override void Init(MyObjectBuilder_EntityBase ob)
        {
            base.Init(ob);
            _block = Entity as MyThrust;
            if (_block == null) return;

            // Defer config lookup to UpdateOnceBeforeFrame() — same reason as
            // AfterburnerController: Init() runs before BeforeStart().
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (_block == null) return;

            ThrustReverserConfig cfg;
            if (!ThrustReverserConfigs.TryGet(_block.BlockDefinition.Id.SubtypeName, out cfg))
            {
                RegisterTerminalControls();
                return;
            }
            _config = cfg;

            if (_block.CubeGrid?.Physics == null) return;

            RegisterTerminalControls();
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
        }

        public override void UpdateBeforeSimulation()
        {
            if (_config == null) return;
            try
            {
                MyEntitySubpart found;
                if (Entity.TryGetSubpart(_config.SubpartName, out found))
                {
                    if (_subpartFirstFind)
                    {
                        _subpartFirstFind   = false;
                        _subpart            = found;
                        _subpartLocalMatrix = found.PositionComp.LocalMatrixRef;
                    }
                    else
                    {
                        _subpart = found;
                    }
                }
                AnimateReverser();
            }
            catch (Exception e) { NASLog.Error("ThrustReverser", "UpdateBefore error: " + e); }
        }

        public override void UpdateAfterSimulation()
        {
            if (_config == null) return;
            try
            {
                if (!_block.IsFunctional) return;
                if (!_state) return;

                var grid = _block.CubeGrid;
                if (grid?.Physics == null) return;

                Vector3D force = _block.WorldMatrix.Forward
                    * _block.BlockDefinition.ForceMagnitude
                    * _block.CurrentStrength
                    * _config.ForceMultiplier;
                grid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, force,
                    grid.Physics.CenterOfMassWorld, null);
            }
            catch (Exception e) { NASLog.Error("ThrustReverser", "UpdateAfter error: " + e); }
        }

        // ── Internals ──────────────────────────────────────────────────────────

        private void ApplyStateServer(bool b)
        {
            _state = b;
            NAS_NetworkHandler.Instance?.SendState(Entity.EntityId, b ? "1" : "0");
        }

        private void AnimateReverser()
        {
            if (_subpart == null || _config == null) return;

            if (_state != _preState)
                _animStatus = _state ? AnimStatus.Extend : AnimStatus.Retract;

            if (_ticks > _config.AnimationLength)
            {
                _ticks      = 0;
                _animStatus = AnimStatus.None;
            }

            if (_animStatus != AnimStatus.None)
            {
                _ticks++;
                float progress = (float)_ticks / _config.AnimationLength;

                Matrix localMatrix = _subpart.PositionComp.LocalMatrixRef;
                Vector3 translation = localMatrix.Translation;

                translation.Z = _animStatus == AnimStatus.Extend
                    ? -_config.MovementDistance * progress
                    : -_config.MovementDistance + (_config.MovementDistance * progress);

                localMatrix.Translation = translation;
                _subpart.PositionComp.SetLocalMatrix(ref localMatrix);
            }

            _preState = _state;
        }

        // ── Terminal controls (registered once) ────────────────────────────────

        private static void RegisterTerminalControls()
        {
            if (_controlsRegistered) return;
            _controlsRegistered = true;

            var toggle = MyAPIGateway.TerminalControls
                .CreateControl<IMyTerminalControlOnOffSwitch, Sandbox.ModAPI.Ingame.IMyThrust>(
                    "TT_ThrustReverserOnOff");
            toggle.Getter  = b =>
            {
                var l = b.GameLogic?.GetAs<ThrustReverseController>();
                return l != null && l.GetState();
            };
            toggle.Setter  = (b, v) => b.GameLogic?.GetAs<ThrustReverseController>()?.SetState(v);
            toggle.Title   = MyStringId.GetOrCompute("Thrust Reverser");
            toggle.OnText  = MyStringId.GetOrCompute("On");
            toggle.OffText = MyStringId.GetOrCompute("Off");
            toggle.Visible = b =>
            {
                var l = b?.GameLogic?.GetAs<ThrustReverseController>();
                return l != null && l._config != null && l._animStatus == AnimStatus.None;
            };
            toggle.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyThrust>(toggle);

            var action = MyAPIGateway.TerminalControls
                .CreateAction<Sandbox.ModAPI.Ingame.IMyThrust>("TT_ThrustReverserToggle");
            action.Action  = b =>
            {
                var l = b.GameLogic?.GetAs<ThrustReverseController>();
                l?.SetState(!l.GetState());
            };
            action.Name   = new StringBuilder("Thrust Reverser On/Off");
            action.Writer = (b, sb) =>
            {
                sb.Clear();
                var l = b?.GameLogic?.GetAs<ThrustReverseController>();
                sb.Append(l != null && l.GetState() ? "On" : "Off");
            };
            action.Enabled        = b => b?.GameLogic?.GetAs<ThrustReverseController>()?._config != null;
            action.ValidForGroups = true;
            MyAPIGateway.TerminalControls.AddAction<Sandbox.ModAPI.Ingame.IMyThrust>(action);
        }
    }
}
