using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using VRageRender;

// Framework and network types are in the same namespace — no extra usings needed.

namespace TTNAS
{
    // ── Action types (byte values are wire-serialised — never reorder or change) ──
    public enum AnchorActionType : byte
    {
        ToggleAnchor   = 0,
        HoldTight      = 1,
        ToggleClubhaul = 2,
        TogglePark     = 3,
    }

    // No subtype filter — AnchorConfigs is the whitelist. ShouldActivate() gates per-instance.
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), true)]
    public class AnchorBlock : NavalBlockBase
    {
        // ── Config ────────────────────────────────────────────────────────────

        private AnchorBlockConfig _config;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();

            // Config lookup deferred from Init() — external mod registrations arrive
            // in BeforeStart(), after entity Init() runs, so the dict is populated by now.
            if (_config != null) return;
            var block = Entity as IMyTerminalBlock;
            if (block == null) return;
            AnchorBlockConfig cfg;
            if (!AnchorConfigs.TryGet(block.BlockDefinition.SubtypeId, out cfg)) return;
            _config   = cfg;
            _chainTex = MyStringId.GetOrCompute(_config.ChainTextureId);
        }

        // ── NavalBlockBase contract ───────────────────────────────────────────

        protected static readonly Guid STORAGE_GUID =
            new Guid("4d5a2c8e-3a0a-4c1d-9a2f-9f1c4d1e7c1b");

        protected override Guid StorageGuid => STORAGE_GUID;

        // ── Persistence key constants ─────────────────────────────────────────

        private const string K_ANCHORED  = "isAnchored";
        private const string K_LOCKED    = "isLocked";
        private const string K_LOCK_LEN  = "lockedLen";
        private const string K_CLUB      = "clubhaul";
        private const string K_PARKED    = "parked";
        // Network-only ephemeral keys
        private const string K_FALLING   = "falling";
        private const string K_LANDED    = "landed";
        private const string K_HAS_POS   = "hasPos";
        private const string K_AX        = "ax";
        private const string K_AY        = "ay";
        private const string K_AZ        = "az";

        // ── Gameplay state ────────────────────────────────────────────────────

        private bool    _isAnchored      = false;
        private bool    _isLocked        = false;
        private float   _lockedChainLen  = 0f;
        private bool    _clubhauling     = false;
        private bool    _isParked        = false;

        // ── Physics / animation state ─────────────────────────────────────────

        private bool    _noGround        = false;
        private bool    _isFalling       = false;
        private bool    _hasLanded       = false;
        private float   _distToGround    = 0f;
        private Vector3D _anchorPos;
        private bool    _hasPos          = false;
        private float   _fallTime        = 0f;
        private long    _deployedTick    = 0;
        private Vector3D _initialVelocity = Vector3D.Zero;

        // ── Subpart ───────────────────────────────────────────────────────────

        private MyEntitySubpart _subpart;
        private Vector3 _subpartRestPos = Vector3.Zero;

        // ── Cable render ──────────────────────────────────────────────────────

        private MyStringId _chainTex    = MyStringId.GetOrCompute("anchorchain");
        private const float CABLE_WIDTH  = 0.7f;
        private const float CABLE_OFFSET = 0.7f;
        private const float LINK_LENGTH  = 1.2f;

        // ── Constants ─────────────────────────────────────────────────────────

        private const float FALL_G              = 9.8f;
        private const float MAX_RAYCAST         = 400f;
        private const float CHAIN_LENGTH        = 450f;
        private const float MAX_FORCE_G_FACTOR  = 3f;
        private const float FORCE_RAMP_TIME     = 0.2f;
        private const float GYRO_POWER_FACTOR   = 0.5f;

        // ── Gyro ──────────────────────────────────────────────────────────────

        private readonly List<IMyGyro> _gyros = new List<IMyGyro>();
        private readonly Dictionary<IMyGyro, float> _origGyroPower = new Dictionary<IMyGyro, float>();

        // ── NavalBlockBase overrides ──────────────────────────────────────────

        protected override string BuildPersistenceBlob()
        {
            var sb = new StringBuilder();
            NavalPersistence.AppendBool (sb, K_ANCHORED, _isAnchored);
            NavalPersistence.AppendBool (sb, K_LOCKED,   _isLocked);
            NavalPersistence.AppendFloat(sb, K_LOCK_LEN, _lockedChainLen);
            NavalPersistence.AppendBool (sb, K_CLUB,     _clubhauling);
            NavalPersistence.AppendBool (sb, K_PARKED,   _isParked);
            return sb.ToString();
        }

        protected override string BuildNetworkBlob()
        {
            var sb = new StringBuilder();
            NavalPersistence.AppendBool  (sb, K_ANCHORED, _isAnchored);
            NavalPersistence.AppendBool  (sb, K_LOCKED,   _isLocked);
            NavalPersistence.AppendFloat (sb, K_LOCK_LEN, _lockedChainLen);
            NavalPersistence.AppendBool  (sb, K_CLUB,     _clubhauling);
            NavalPersistence.AppendBool  (sb, K_PARKED,   _isParked);
            NavalPersistence.AppendBool  (sb, K_FALLING,  _isFalling);
            NavalPersistence.AppendBool  (sb, K_LANDED,   _hasLanded);
            NavalPersistence.AppendBool  (sb, K_HAS_POS,  _hasPos);
            if (_hasPos)
            {
                NavalPersistence.AppendDouble(sb, K_AX, _anchorPos.X);
                NavalPersistence.AppendDouble(sb, K_AY, _anchorPos.Y);
                NavalPersistence.AppendDouble(sb, K_AZ, _anchorPos.Z);
            }
            return sb.ToString();
        }

        protected override void ParseBlob(string blob)
        {
            _isAnchored    = NavalPersistence.ReadBool (blob, K_ANCHORED);
            _isLocked      = NavalPersistence.ReadBool (blob, K_LOCKED);
            _lockedChainLen= NavalPersistence.ReadFloat(blob, K_LOCK_LEN);
            _clubhauling   = NavalPersistence.ReadBool (blob, K_CLUB);
            _isParked      = NavalPersistence.ReadBool (blob, K_PARKED);
        }

        public override void ApplyNetworkBlob(string blob)
        {
            if (IsServer) return;
            _isAnchored    = NavalPersistence.ReadBool  (blob, K_ANCHORED);
            _isLocked      = NavalPersistence.ReadBool  (blob, K_LOCKED);
            _lockedChainLen= NavalPersistence.ReadFloat (blob, K_LOCK_LEN);
            _clubhauling   = NavalPersistence.ReadBool  (blob, K_CLUB);
            _isParked      = NavalPersistence.ReadBool  (blob, K_PARKED);
            _isFalling     = NavalPersistence.ReadBool  (blob, K_FALLING);
            _hasLanded     = NavalPersistence.ReadBool  (blob, K_LANDED);
            _hasPos        = NavalPersistence.ReadBool  (blob, K_HAS_POS);
            if (_hasPos)
                _anchorPos = new Vector3D(
                    NavalPersistence.ReadDouble(blob, K_AX),
                    NavalPersistence.ReadDouble(blob, K_AY),
                    NavalPersistence.ReadDouble(blob, K_AZ));

            if (!_isAnchored) RetractLocal();
            else if (_hasLanded) AdjustGyros(true);
            SafeRefreshInfo();
        }

        protected override void OnStateLoaded()
        {
            if (_isAnchored)
            {
                // Defer physics disable so doors can set up Havok constraints first
                if (_isParked)
                    MyAPIGateway.Utilities.InvokeOnGameThread(() => { try { ApplyPark(); } catch { } });
                BeginDrop();
            }
        }

        protected override void OnUpdate100()
        {
            // Retry drop if raycast failed on first frame (voxels not loaded yet)
            if (_isAnchored && _noGround && !_isFalling && !_hasLanded)
            {
                BeginDrop();
                if (!_noGround) MarkNeedsSync();
            }
        }

        protected override void OnUpdate()
        {
            if (_config == null) return;
            UpdateAnchorPhysics();
            UpdateCableRender();
        }

        protected override void OnClose()
        {
            AdjustGyros(false);
        }

        public override void HandleAction(byte actionType, ulong senderSteamId)
        {
            if (!IsServer) return;
            switch ((AnchorActionType)actionType)
            {
                case AnchorActionType.ToggleAnchor:   ServerToggleAnchor();   break;
                case AnchorActionType.HoldTight:       ServerHoldTight();      break;
                case AnchorActionType.ToggleClubhaul:  ServerToggleClubhaul(); break;
                case AnchorActionType.TogglePark:      ServerTogglePark();     break;
            }
        }

        // ── Terminal controls ─────────────────────────────────────────────────

        protected override void RegisterControls()
        {
            // ── Toolbar actions ───────────────────────────────────────────────

            var toggleAction = MyAPIGateway.TerminalControls.CreateAction<IMyUpgradeModule>("ToggleAnchor");
            toggleAction.Name         = new StringBuilder("Toggle Anchor");
            toggleAction.Action       = b => ToggleAnchorAction(b);
            toggleAction.ValidForGroups = true;
            toggleAction.Enabled      = b => b?.GameLogic?.GetAs<AnchorBlock>()?._config != null;
            toggleAction.Icon         = @"Textures/GUI/Icons/Actions/Toggle.dds";
            toggleAction.Writer       = (b, sb) => { var l = b.GameLogic?.GetAs<AnchorBlock>(); if (l != null) sb.Append(l._isAnchored ? "Anchor: ON" : "Anchor: OFF"); };
            MyAPIGateway.TerminalControls.AddAction<IMyUpgradeModule>(toggleAction);

            var holdAction = MyAPIGateway.TerminalControls.CreateAction<IMyUpgradeModule>("HoldTight");
            holdAction.Name           = new StringBuilder("Hold Tight");
            holdAction.Action         = b => HoldTightAction(b);
            holdAction.ValidForGroups = true;
            holdAction.Enabled        = b => { var l = b?.GameLogic?.GetAs<AnchorBlock>(); return l?._config != null && l._isAnchored && l._hasLanded; };
            holdAction.Icon           = @"Textures/GUI/Icons/Actions/Lock.dds";
            holdAction.Writer         = (b, sb) => { var l = b.GameLogic?.GetAs<AnchorBlock>(); if (l != null) sb.Append(l._isLocked ? "Hold Tight: ON" : "Hold Tight: OFF"); };
            MyAPIGateway.TerminalControls.AddAction<IMyUpgradeModule>(holdAction);

            var clubAction = MyAPIGateway.TerminalControls.CreateAction<IMyUpgradeModule>("ToggleClubhaul");
            clubAction.Name           = new StringBuilder("Toggle Clubhauling");
            clubAction.Action         = b => ToggleClubhaulAction(b);
            clubAction.ValidForGroups = true;
            clubAction.Enabled        = b => { var l = b?.GameLogic?.GetAs<AnchorBlock>(); return l?._config != null && l._isAnchored && l._hasLanded; };
            clubAction.Icon           = @"Textures/GUI/Icons/Actions/Reverse.dds";
            clubAction.Writer         = (b, sb) => { var l = b.GameLogic?.GetAs<AnchorBlock>(); if (l != null) sb.Append(l._clubhauling ? "Clubhaul: ON" : "Clubhaul: OFF"); };
            MyAPIGateway.TerminalControls.AddAction<IMyUpgradeModule>(clubAction);

            var parkAction = MyAPIGateway.TerminalControls.CreateAction<IMyUpgradeModule>("TogglePark");
            parkAction.Name           = new StringBuilder("Toggle Park");
            parkAction.Action         = b => ToggleParkAction(b);
            parkAction.ValidForGroups = true;
            parkAction.Enabled        = b => { var l = b?.GameLogic?.GetAs<AnchorBlock>(); return l?._config != null && l._isAnchored && l._hasLanded; };
            parkAction.Icon           = @"Textures/GUI/Icons/Actions/LargeShipToggle.dds";
            parkAction.Writer         = (b, sb) => { var l = b.GameLogic?.GetAs<AnchorBlock>(); if (l != null) sb.Append(l._isParked ? "Park: ON" : "Park: OFF"); };
            MyAPIGateway.TerminalControls.AddAction<IMyUpgradeModule>(parkAction);

            // ── Terminal panel switches ────────────────────────────────────────

            var anchorSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyUpgradeModule>("AnchorState");
            anchorSwitch.Title   = MyStringId.GetOrCompute("Anchor");
            anchorSwitch.Tooltip = MyStringId.GetOrCompute("Deploy or retract the anchor");
            anchorSwitch.OnText  = MyStringId.GetOrCompute("DEPLOYED");
            anchorSwitch.OffText = MyStringId.GetOrCompute("RETRACTED");
            anchorSwitch.Getter  = b => b.GameLogic?.GetAs<AnchorBlock>()?._isAnchored ?? false;
            anchorSwitch.Setter  = (b, v) => { var l = b.GameLogic?.GetAs<AnchorBlock>(); if (l != null && l._isAnchored != v) ToggleAnchorAction(b); };
            anchorSwitch.Enabled = b => b?.GameLogic?.GetAs<AnchorBlock>()?._config != null;
            anchorSwitch.Visible = anchorSwitch.Enabled;
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(anchorSwitch);

            var holdSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyUpgradeModule>("HoldTightState");
            holdSwitch.Title   = MyStringId.GetOrCompute("Hold Tight");
            holdSwitch.Tooltip = MyStringId.GetOrCompute("Lock the anchor at the current chain length");
            holdSwitch.OnText  = MyStringId.GetOrCompute("LOCKED");
            holdSwitch.OffText = MyStringId.GetOrCompute("FREE");
            holdSwitch.Getter  = b => b.GameLogic?.GetAs<AnchorBlock>()?._isLocked ?? false;
            holdSwitch.Setter  = (b, v) => { var l = b.GameLogic?.GetAs<AnchorBlock>(); if (l != null && l._isLocked != v) HoldTightAction(b); };
            holdSwitch.Enabled = b => { var l = b?.GameLogic?.GetAs<AnchorBlock>(); return l?._config != null && l._isAnchored && l._hasLanded; };
            holdSwitch.Visible = holdSwitch.Enabled;
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(holdSwitch);

            var clubSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyUpgradeModule>("ClubhaulState");
            clubSwitch.Title   = MyStringId.GetOrCompute("Clubhauling");
            clubSwitch.Tooltip = MyStringId.GetOrCompute("Enable clubhauling manoeuvre mode");
            clubSwitch.OnText  = MyStringId.GetOrCompute("ON");
            clubSwitch.OffText = MyStringId.GetOrCompute("OFF");
            clubSwitch.Getter  = b => b.GameLogic?.GetAs<AnchorBlock>()?._clubhauling ?? false;
            clubSwitch.Setter  = (b, v) => { var l = b.GameLogic?.GetAs<AnchorBlock>(); if (l != null && l._clubhauling != v) ToggleClubhaulAction(b); };
            clubSwitch.Enabled = b => { var l = b?.GameLogic?.GetAs<AnchorBlock>(); return l?._config != null && l._isAnchored && l._hasLanded; };
            clubSwitch.Visible = clubSwitch.Enabled;
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(clubSwitch);

            var parkSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyUpgradeModule>("ParkState");
            parkSwitch.Title   = MyStringId.GetOrCompute("Park Grid");
            parkSwitch.Tooltip = MyStringId.GetOrCompute("Freeze grid physics for server performance");
            parkSwitch.OnText  = MyStringId.GetOrCompute("PARKED");
            parkSwitch.OffText = MyStringId.GetOrCompute("MOBILE");
            parkSwitch.Getter  = b => b.GameLogic?.GetAs<AnchorBlock>()?._isParked ?? false;
            parkSwitch.Setter  = (b, v) => { var l = b.GameLogic?.GetAs<AnchorBlock>(); if (l != null && l._isParked != v) ToggleParkAction(b); };
            parkSwitch.Enabled = b => { var l = b?.GameLogic?.GetAs<AnchorBlock>(); return l?._config != null && l._isAnchored && l._hasLanded; };
            parkSwitch.Visible = parkSwitch.Enabled;
            MyAPIGateway.TerminalControls.AddControl<IMyUpgradeModule>(parkSwitch);
        }

        // ── Client-side action dispatchers ────────────────────────────────────

        private static void ToggleAnchorAction(IMyTerminalBlock block)
        {
            var l = block?.GameLogic?.GetAs<AnchorBlock>();
            if (l == null) return;
            if (MyAPIGateway.Multiplayer.MultiplayerActive && !IsServer)
                NAS_NetworkHandler.Instance?.SendAction(block.EntityId, (byte)AnchorActionType.ToggleAnchor);
            else
                l.ServerToggleAnchor();
        }

        private static void HoldTightAction(IMyTerminalBlock block)
        {
            var l = block?.GameLogic?.GetAs<AnchorBlock>();
            if (l == null || !l._isAnchored || !l._hasLanded) return;
            if (MyAPIGateway.Multiplayer.MultiplayerActive && !IsServer)
                NAS_NetworkHandler.Instance?.SendAction(block.EntityId, (byte)AnchorActionType.HoldTight);
            else
                l.ServerHoldTight();
        }

        private static void ToggleClubhaulAction(IMyTerminalBlock block)
        {
            var l = block?.GameLogic?.GetAs<AnchorBlock>();
            if (l == null || !l._isAnchored || !l._hasLanded) return;
            if (MyAPIGateway.Multiplayer.MultiplayerActive && !IsServer)
                NAS_NetworkHandler.Instance?.SendAction(block.EntityId, (byte)AnchorActionType.ToggleClubhaul);
            else
                l.ServerToggleClubhaul();
        }

        private static void ToggleParkAction(IMyTerminalBlock block)
        {
            var l = block?.GameLogic?.GetAs<AnchorBlock>();
            if (l == null || !l._isAnchored || !l._hasLanded) return;
            if (MyAPIGateway.Multiplayer.MultiplayerActive && !IsServer)
                NAS_NetworkHandler.Instance?.SendAction(block.EntityId, (byte)AnchorActionType.TogglePark);
            else
                l.ServerTogglePark();
        }

        // ── Server mutations ──────────────────────────────────────────────────

        public void ServerToggleAnchor()
        {
            if (!IsServer) return;
            _isAnchored = !_isAnchored;
            if (_isAnchored) BeginDrop(); else Retract();
            SaveState(); MarkNeedsSync(); SafeRefreshInfo();
        }

        public void ServerHoldTight()
        {
            if (!IsServer || !_isAnchored || !_hasLanded) return;
            _isLocked = !_isLocked;
            if (_isLocked)
            {
                _lockedChainLen = (float)(_anchorPos - Block.WorldMatrix.Translation).Length();
                _deployedTick   = DateTime.Now.Ticks;
            }
            SaveState(); MarkNeedsSync(); SafeRefreshInfo();
        }

        public void ServerToggleClubhaul()
        {
            if (!IsServer || !_isAnchored) return;
            _clubhauling = !_clubhauling;
            SaveState(); MarkNeedsSync(); SafeRefreshInfo();
        }

        public void ServerTogglePark()
        {
            if (!IsServer || !_isAnchored || !_hasLanded) return;
            _isParked = !_isParked;
            if (_isParked) ApplyPark(); else UnapplyPark();
            SaveState(); MarkNeedsSync(); SafeRefreshInfo();
        }

        // ── Core anchor mechanics ─────────────────────────────────────────────

        private void BeginDrop()
        {
            if (!_isParked)
            {
                var grid = Block?.CubeGrid;
                if (grid?.Physics != null && !grid.Physics.Enabled)
                    grid.Physics.Enabled = true;
            }

            _distToGround = GetDistToGround();

            if (_distToGround > 0)
            {
                _noGround = false;
                _isFalling = true;
                _hasLanded = false;
                _fallTime  = 0f;
                _hasPos    = false;
            }
            else
            {
                _isFalling = false;
                _hasPos    = false;
                _noGround  = true;
            }
        }

        private void Retract()
        {
            if (_isParked) UnapplyPark();
            _isParked   = false;
            _isFalling  = false;
            _hasLanded  = false;
            _hasPos     = false;
            _isLocked   = false;
            _lockedChainLen = 0f;
            _clubhauling = false;
            _noGround   = false;
            AdjustGyros(false);
        }

        private void RetractLocal()
        {
            _isFalling = false;
            _hasLanded = false;
            _hasPos    = false;
            AdjustGyros(false);
        }

        private float GetDistToGround()
        {
            try
            {
                if (Block == null) return -1f;
                var wm    = Block.WorldMatrix;
                var start = wm.Translation + (wm.Down * 2.5);
                var end   = start + (wm.Down * MAX_RAYCAST);
                IHitInfo hit;
                if (MyAPIGateway.Physics.CastRay(start, end, out hit))
                    if (hit.HitEntity != null && hit.HitEntity != Block.CubeGrid && !hit.HitEntity.Closed)
                        return Math.Max(0, (float)(hit.Position - wm.Translation).Length() - 0.5f);
            }
            catch (Exception e) { Log("GetDistToGround error: " + e); }
            return -1f;
        }

        // ── Physics update (per-frame) ────────────────────────────────────────

        private void UpdateAnchorPhysics()
        {
            if (Entity == null) return;

            if (_subpart == null || _subpart.Closed)
            {
                if (!Entity.TryGetSubpart(_config.SubpartName, out _subpart) || _subpart == null) return;
                _subpartRestPos = _subpart.PositionComp.LocalMatrix.Translation;
                if (!(_isAnchored && (_hasLanded || _isFalling)))
                {
                    _fallTime  = 0f;
                    _hasLanded = false;
                    _hasPos    = false;
                }
            }

            if (_isFalling && !_subpart.Closed)
            {
                if (!_hasLanded)
                {
                    _fallTime += 1f / 60f;
                    float fallDist = 0.5f * FALL_G * _fallTime * _fallTime;
                    float curDist  = GetDistToGround();
                    if (curDist > 0) _distToGround = curDist;

                    if (fallDist >= _distToGround || fallDist >= MAX_RAYCAST)
                    {
                        fallDist = Math.Min(_distToGround, MAX_RAYCAST);
                        var local = _subpart.PositionComp.LocalMatrix;
                        local.Translation = _subpartRestPos + Vector3.Down * fallDist;
                        _subpart.PositionComp.SetLocalMatrix(ref local);

                        _anchorPos = _subpart.WorldMatrix.Translation;
                        _hasPos    = true;
                        _hasLanded = true;

                        MyParticleEffect effect;
                        if (MyParticlesManager.TryCreateParticleEffect("Anchor_Land", out effect))
                        {
                            effect.WorldMatrix = MatrixD.CreateWorld(_anchorPos, Block.WorldMatrix.Forward, Block.WorldMatrix.Up);
                            effect.UserScale   = 1.5f;
                        }

                        AdjustGyros(true);

                        if (IsServer)
                        {
                            if (_isParked) ApplyPark();
                            MarkNeedsSync();
                            SaveState();
                        }

                        SafeRefreshInfo();
                    }
                    else
                    {
                        var local = _subpart.PositionComp.LocalMatrix;
                        local.Translation = _subpartRestPos + Vector3.Down * fallDist;
                        _subpart.PositionComp.SetLocalMatrix(ref local);
                    }
                }
                else if (_hasPos && !_subpart.Closed)
                {
                    // Pin subpart to world anchor position while grid moves
                    try
                    {
                        var gridInv  = MatrixD.Invert(Block.CubeGrid.WorldMatrix);
                        var anchorWM = _subpart.WorldMatrix;
                        anchorWM.Translation = _anchorPos;
                        var localToGrid  = anchorWM * gridInv;
                        var blockInv     = MatrixD.Invert(Block.PositionComp.LocalMatrix);
                        var localToBlock = localToGrid * blockInv;
                        var m = (Matrix)localToBlock;
                        _subpart.PositionComp.SetLocalMatrix(ref m);
                        if (IsServer) ApplyAnchorForces();
                    }
                    catch (Exception e) { Log("Subpart pin error: " + e); }
                }
            }
            else if (!_isFalling && _subpart != null && !_subpart.Closed)
            {
                var local = _subpart.PositionComp.LocalMatrix;
                local.Translation = _subpartRestPos;
                _subpart.PositionComp.SetLocalMatrix(ref local);
                _fallTime  = 0f;
                _hasLanded = false;
                _hasPos    = false;
            }
        }

        // ── Chain forces ──────────────────────────────────────────────────────

        private void ApplyAnchorForces()
        {
            if (_isParked) return;
            if (!_hasPos || Block?.CubeGrid?.Physics == null) return;

            var grid      = Block.CubeGrid;
            float mass    = grid.Physics.Mass;
            var velocity  = grid.Physics.LinearVelocity;

            var toAnchor  = _anchorPos - Block.WorldMatrix.Translation;
            double dist   = toAnchor.Length();
            var dir       = Vector3D.Normalize(toAnchor);

            float effective = _isLocked ? _lockedChainLen : CHAIN_LENGTH;
            if (dist <= effective) return;

            long now = DateTime.Now.Ticks;
            if (_deployedTick == 0) { _deployedTick = now; _initialVelocity = velocity; }

            float t       = Math.Min(1f, (float)TimeSpan.FromTicks(now - _deployedTick).TotalSeconds / FORCE_RAMP_TIME);
            float tension = (float)Math.Min(1.0, (dist - effective) / (effective * 0.2));
            float maxF    = mass * 9.81f * MAX_FORCE_G_FACTOR;
            float vDot    = (float)Vector3D.Dot(velocity, dir);
            float vLen    = (float)velocity.Length();

            var anchorForce = dir * maxF * tension * t;
            if (vDot >= 0)
                grid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, anchorForce, null, null);
            else if (vLen > 0.5f)
            {
                float rf = Math.Max(0.05f, Math.Min(0.2f, Math.Abs(vDot) * 0.1f));
                grid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, anchorForce * rf, null, null);
            }

            grid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, -(Vector3D)velocity * 1.2f * mass, null, null);
            ThrottledRefreshInfo();
        }

        // ── Cable render ──────────────────────────────────────────────────────

        private void UpdateCableRender()
        {
            if (!_isAnchored) return;
            var start = Block.WorldMatrix.Translation + (Block.WorldMatrix.Down * CABLE_OFFSET);
            Vector3D end;
            if (_hasPos)             end = _anchorPos;
            else if (_subpart != null) end = _subpart.WorldMatrix.Translation;
            else return;

            var   dir  = end - start;
            double len = dir.Length();
            if (len <= 0.1) return;
            dir.Normalize();

            int segs    = Math.Max(1, (int)(len / LINK_LENGTH));
            var gravity = Block.WorldMatrix.Down;

            for (int i = 0; i < segs; i++)
            {
                float t1 = (float)i / segs, t2 = (float)(i + 1) / segs;
                float s1 = (float)(Math.Sin(t1 * Math.PI) * 0.08 * len);
                float s2 = (float)(Math.Sin(t2 * Math.PI) * 0.08 * len);
                var p1 = Vector3D.Lerp(start, end, t1) + gravity * s1;
                var p2 = Vector3D.Lerp(start, end, t2) + gravity * s2;
                MySimpleObjectDraw.DrawLine(p1, p2, _chainTex, ref Vector4.One, CABLE_WIDTH, MyBillboard.BlendTypeEnum.LDR);
            }
        }

        // ── Park / unpark ─────────────────────────────────────────────────────

        private void ApplyPark()
        {
            var grid = Block?.CubeGrid;
            if (grid == null) return;

            if (grid.Physics != null)
            {
                grid.Physics.LinearVelocity = Vector3.Zero;
                grid.Physics.AngularVelocity = Vector3.Zero;
            }

            var apiGrid = grid as IMyCubeGrid;
            if (apiGrid != null && !apiGrid.IsStatic)
                apiGrid.IsStatic = true;
        }

        private void UnapplyPark()
        {
            if (IsAnotherAnchorParked()) return;

            var grid = Block?.CubeGrid;
            if (grid == null) return;

            var apiGrid = grid as IMyCubeGrid;
            if (apiGrid != null && apiGrid.IsStatic)
                apiGrid.IsStatic = false;

            if (grid.Physics != null)
            {
                grid.Physics.LinearVelocity = Vector3.Zero;
                grid.Physics.AngularVelocity = Vector3.Zero;

                if (!grid.Physics.Enabled)
                    grid.Physics.Enabled = true;
            }
        }

        private bool IsAnotherAnchorParked()
        {
            try
            {
                var blocks = new List<IMyTerminalBlock>();
                MyAPIGateway.TerminalActionsHelper
                    .GetTerminalSystemForGrid(Block.CubeGrid)
                    .GetBlocksOfType(blocks, b =>
                    {
                        if (b == null || b.EntityId == Block.EntityId) return false;
                        var l = b.GameLogic?.GetAs<AnchorBlock>();
                        return l != null && l._isParked;
                    });
                return blocks.Count > 0;
            }
            catch { return false; }
        }

        // ── Gyros ─────────────────────────────────────────────────────────────

        private void AdjustGyros(bool reduce)
        {
            if (Block?.CubeGrid == null) return;
            var ts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(Block.CubeGrid);
            if (ts == null) return;
            _gyros.Clear();
            ts.GetBlocksOfType(_gyros, g =>
                    g != null && g.CubeGrid?.EntityId == Block.CubeGrid.EntityId &&
                    !g.CubeGrid.IsStatic && g.CubeGrid.Physics != null &&
                    g.IsFunctional && g.IsWorking);

            if (reduce)
            {
                foreach (var g in _gyros)
                {
                    if (!_origGyroPower.ContainsKey(g)) _origGyroPower[g] = g.GyroPower;
                    g.GyroPower = _origGyroPower[g] * GYRO_POWER_FACTOR;
                }
            }
            else
            {
                foreach (var g in _gyros)
                {
                    float v; if (_origGyroPower.TryGetValue(g, out v)) g.GyroPower = v;
                }
                _origGyroPower.Clear();
            }
        }

        // ── Info panel ────────────────────────────────────────────────────────

        protected override void AppendBlockInfo(IMyTerminalBlock block, StringBuilder sb)
        {
            if (_config == null) return;
            sb.AppendLine("=== ANCHOR STATUS ===");
            if (!_isAnchored) { sb.AppendLine("State: RETRACTED"); return; }
            if (_noGround)          sb.AppendLine("State: NO GROUND");
            else if (_isFalling && !_hasLanded)
            {
                sb.AppendLine("State: DROPPING");
                sb.AppendLine($"Drop Distance: {(_distToGround > 0 ? _distToGround.ToString("0") + " m" : "unknown")}");
            }
            else if (_hasLanded)   sb.AppendLine($"State: {(_isLocked ? "LOCKED" : "DEPLOYED")}");
            else                   sb.AppendLine("State: DEPLOYING");

            float eff = _isLocked ? _lockedChainLen : CHAIN_LENGTH;
            if (_hasPos && Block != null)
            {
                double cur = (_anchorPos - Block.WorldMatrix.Translation).Length();
                sb.AppendLine($"Chain: {cur:0.0} m / {eff:0.0} m");
                if (cur > eff)
                {
                    float pct = MathHelper.Clamp((float)((cur - eff) / eff * 100), 0, 100);
                    sb.AppendLine($"Tension: {pct:0}% (tight)");
                }
                else sb.AppendLine("Tension: Slack");
            }
            else if (_distToGround > 0)
                sb.AppendLine($"Drop Distance: {_distToGround:0} m");

            sb.AppendLine($"Hold Tight:   {(_isLocked    ? "YES" : "NO")}");
            sb.AppendLine($"Clubhauling:  {(_clubhauling ? "ON"  : "OFF")}");
            sb.AppendLine($"Parked:       {(_isParked    ? "YES" : "NO")}");
            if (_hasLanded) sb.AppendLine("Gyros: Reduced for realism");
        }
    }
}
