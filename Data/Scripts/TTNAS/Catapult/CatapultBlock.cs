using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace TTNAS
{
    // ───────────────────────────────────────────────────────────────────────────
    // CatapultBlock — game-logic component for catapult blocks.
    //
    // Uses no subtype filter in the descriptor — CatapultConfigs is the whitelist.
    // Init returns early if the block's subtype is not registered.
    // ───────────────────────────────────────────────────────────────────────────
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_AdvancedDoor), false)]
    public class CatapultBlock : MyGameLogicComponent
    {
        // ── Config ────────────────────────────────────────────────────────────

        private CatapultConfig _cfg;

        // ── Block references ──────────────────────────────────────────────────

        private IMyDoor            _door;
        private IMyTerminalBlock   _term;

        // ── Runtime state ─────────────────────────────────────────────────────

        private bool   _armed;
        private bool   _debugDraw;
        private string _status = "INIT";
        private long   _cooldownUntilTick;
        private int    _u10Counter;

        // ── Detection ─────────────────────────────────────────────────────────

        private IMyCubeGrid _candidateAny;
        private IMyCubeGrid _candidateMain;

        // ── Launch ────────────────────────────────────────────────────────────

        private bool        _launching;
        private IMyCubeGrid _launchGrid;
        private int         _launchFramesLeft;
        private Vector3D    _launchDir;
        private double      _launchDeltaVPerFrame;

        // ── Hold ──────────────────────────────────────────────────────────────

        private bool        _holding;
        private IMyCubeGrid _holdGrid;
        private Vector3D    _holdTargetLocal;

        // ── Reusable buffers ──────────────────────────────────────────────────

        private readonly HashSet<IMyEntity>            _nearbyEnts      = new HashSet<IMyEntity>();
        private readonly List<IMyCubeGrid>             _nearbySmallGrids= new List<IMyCubeGrid>(128);
        private readonly Dictionary<long, List<long>>  _mechAdj         = new Dictionary<long, List<long>>(256);
        private readonly Dictionary<long, IMyCubeGrid> _gridById        = new Dictionary<long, IMyCubeGrid>(256);
        private readonly List<IMySlimBlock>            _slimsBuf        = new List<IMySlimBlock>(4096);
        private readonly HashSet<long>                 _bfsVisited      = new HashSet<long>();
        private readonly Queue<long>                   _bfsQueue        = new Queue<long>();

        // ── Persistence ───────────────────────────────────────────────────────

        private static readonly Guid STORAGE_GUID =
            new Guid("7C4F2A8B-5E3D-4A1C-9B7F-2E8D1C5F3A4E");

        // ── Static instance registry ──────────────────────────────────────────

        private static readonly List<CatapultBlock> _allInstances = new List<CatapultBlock>();

        public static void GetOnGrid(IMyCubeGrid grid, List<CatapultBlock> output)
        {
            output.Clear();
            if (grid == null) return;
            var top = grid.GetTopMostParent() as IMyCubeGrid ?? grid;
            foreach (var cb in _allInstances)
            {
                if (cb._door == null || cb._door.MarkedForClose) continue;
                var cbTop = cb._door.CubeGrid.GetTopMostParent() as IMyCubeGrid ?? cb._door.CubeGrid;
                if (cbTop.EntityId == top.EntityId)
                    output.Add(cb);
            }
        }

        // ── Exposed state (read-only, for LCD) ────────────────────────────────

        public string BlockName    => _term?.CustomName ?? "Catapult";
        public string Status       => _status;
        public bool   IsArmed      => _armed;
        public bool   IsHolding    => _holding;
        public bool   IsLaunching  => _launching;
        public bool   HasAircraft  => _candidateMain != null || _candidateAny != null;
        public string AircraftName
        {
            get
            {
                if (_candidateMain != null) return _candidateMain.DisplayName;
                if (_candidateAny  != null) return _candidateAny.DisplayName;
                return null;
            }
        }
        public float CooldownSecondsRemaining
        {
            get { long r = _cooldownUntilTick - NowTick(); return r > 0 ? (float)r / 60f : 0f; }
        }

        // ── Static dedup ──────────────────────────────────────────────────────

        private static readonly HashSet<string> _registeredSubtypes = new HashSet<string>();
        private static bool _controlFilterHooked;

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            _door = Entity as IMyDoor;
            _term = Entity as IMyTerminalBlock;
            if (_door == null || _term == null) return;

            if (!CatapultConfigs.TryGet(_door.BlockDefinition.SubtypeName, out _cfg)) return;

            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME
                         | MyEntityUpdateEnum.EACH_10TH_FRAME
                         | MyEntityUpdateEnum.EACH_FRAME;

            string sub = _door.BlockDefinition.SubtypeName;
            if (!_registeredSubtypes.Contains(sub))
            {
                _registeredSubtypes.Add(sub);
                RegisterTerminalControls();
                RegisterTerminalActions();
            }

            EnsureHideVanillaDoorControls();
        }

        public override void Close()
        {
            base.Close();
            _allInstances.Remove(this);
            if (_term != null)
                _term.AppendingCustomInfo -= OnAppendCustomInfo;
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();
            if (_term == null || _cfg == null) return;

            _allInstances.Add(this);
            _term.AppendingCustomInfo += OnAppendCustomInfo;

            if (MyAPIGateway.Multiplayer.IsServer)
                LoadState();

            _status = "OFFLINE";
            _term.RefreshCustomInfo();
        }

        public override void UpdateAfterSimulation()
        {
            base.UpdateAfterSimulation();
            if (_door == null || _cfg == null) return;

            bool isDedicated = MyAPIGateway.Utilities?.IsDedicated ?? false;

            if (MyAPIGateway.Multiplayer.IsServer)
            {
                TickReturnClose();
                if (_cfg.HoldEnabled) TickHoldForce();
                TickLaunchForce();
            }

            if (!isDedicated && _debugDraw)
                DrawDebug();
        }

        public override void UpdateAfterSimulation10()
        {
            base.UpdateAfterSimulation10();
            if (_door == null || _term == null || _cfg == null) return;

            if (!MyAPIGateway.Multiplayer.IsServer)
            {
                _term.RefreshCustomInfo();
                return;
            }

            if (++_u10Counter < _cfg.DetectEveryNUpdates10) return;
            _u10Counter = 0;

            UpdateDetectionAndStatus();
            _term.RefreshCustomInfo();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Terminal custom info
        // ─────────────────────────────────────────────────────────────────────

        private void OnAppendCustomInfo(IMyTerminalBlock block, StringBuilder sb)
        {
            sb.AppendLine("=== TT Carrier Catapult ===");
            sb.Append("Armed:      ").AppendLine(_armed ? "YES" : "NO");
            sb.Append("Status:     ").AppendLine(_status);
            sb.Append("Hold:       ").AppendLine(_holding ? "ON" : "OFF");
            sb.Append("Debug Draw: ").AppendLine(_debugDraw ? "ON" : "OFF");

            if (_candidateMain != null)
                sb.Append("Aircraft:   ").AppendLine(_candidateMain.DisplayName);
            else if (_candidateAny != null)
                sb.Append("Aircraft:   ").AppendLine(_candidateAny.DisplayName);

            if (_launching)
                sb.AppendLine("Launching:  YES");

            long now = NowTick();
            if (now < _cooldownUntilTick)
                sb.Append("Cooldown:   ").AppendLine(
                    ((_cooldownUntilTick - now) / 60.0).ToString("0.0") + "s");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Persistence
        // ─────────────────────────────────────────────────────────────────────

        private void SaveState()
        {
            if (!MyAPIGateway.Multiplayer.IsServer || _term == null) return;
            try
            {
                if (_term.Storage == null)
                    _term.Storage = new MyModStorageComponent();
                _term.Storage[STORAGE_GUID] = _armed ? "1" : "0";
            }
            catch (Exception e)
            {
                NASLog.Error("Catapult", "SaveState error: " + e);
            }
        }

        private void LoadState()
        {
            if (_term?.Storage == null) return;
            try
            {
                string val;
                if (_term.Storage.TryGetValue(STORAGE_GUID, out val))
                    _armed = val == "1";
            }
            catch (Exception e)
            {
                NASLog.Error("Catapult", "LoadState error: " + e);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Arm / disarm
        // ─────────────────────────────────────────────────────────────────────

        public void ApplyArmed(bool value)
        {
            _armed = value;
            if (!value)
            {
                ClearHold();
            }
            else if (MyAPIGateway.Multiplayer.IsServer)
            {
                UpdateDetectionAndStatus();
            }

            _term?.RefreshCustomInfo();
            if (MyAPIGateway.Multiplayer.IsServer) SaveState();
        }

        private void RequestArm(bool value)
        {
            if (MyAPIGateway.Multiplayer.IsServer)
                ApplyArmed(value);
            else
                NAS_NetworkHandler.Instance?.SendAction(_door.EntityId,
                    value ? CatapultActions.ArmOn : CatapultActions.ArmOff);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Launch request
        // ─────────────────────────────────────────────────────────────────────

        private void RequestLaunch()
        {
            if (_door == null) return;
            if (MyAPIGateway.Multiplayer.IsServer)
                TryLaunchServer();
            else
                NAS_NetworkHandler.Instance?.SendAction(_door.EntityId, CatapultActions.Launch);
        }

        // Called by NavalActionPacket.Received when NavalBlockBase is not present.
        public void HandleCatapultAction(byte actionType)
        {
            switch (actionType)
            {
                case CatapultActions.Launch: TryLaunchServer();   break;
                case CatapultActions.ArmOn:  ApplyArmed(true);    break;
                case CatapultActions.ArmOff: ApplyArmed(false);   break;
            }
        }

        public void TryLaunchServer()
        {
            if (!MyAPIGateway.Multiplayer.IsServer) return;

            UpdateDetectionAndStatus();
            if (_status != "READY" || _candidateMain == null) return;

            var aircraft = _candidateMain;
            if (aircraft.MarkedForClose || aircraft.Physics == null) return;

            _holding  = false;
            _holdGrid = null;
            try { _door.OpenDoor(); } catch { }

            BeginLaunchForce(aircraft);
            _cooldownUntilTick  = NowTick() + _cfg.CooldownTicks;
            _returnCloseAtTick  = NowTick() + _cfg.ReturnAfterLaunchTicks;
        }

        private long _returnCloseAtTick;

        // ─────────────────────────────────────────────────────────────────────
        // Detection + status
        // ─────────────────────────────────────────────────────────────────────

        private void UpdateDetectionAndStatus()
        {
            _candidateAny  = null;
            _candidateMain = null;

            if (!_door.IsFunctional || !_door.Enabled)
            {
                SetStatus("BLOCK OFFLINE", clearHold: true); return;
            }
            if (NowTick() < _cooldownUntilTick)
            {
                SetStatus("COOLDOWN", clearHold: true); return;
            }
            if (!_armed)
            {
                SetStatus("DISARMED", clearHold: true); return;
            }
            if (_launching)
            {
                SetStatus("LAUNCHING", clearHold: true); return;
            }

            var g = FindCandidateSmallGridInBox();
            if (g == null)
            {
                SetStatus("NO AIRCRAFT", clearHold: true); return;
            }

            _candidateAny = g;

            var main = ResolveMainGridByMechanicalLinks(g) ?? g;
            _candidateMain = main;

            if (main.Physics == null)
            {
                SetStatus("NO PHYSICS", clearHold: true); return;
            }
            if (main.Physics.LinearVelocity.Length() > _cfg.MaxReadySpeedMps)
            {
                SetStatus("TOO FAST", clearHold: true); return;
            }
            if (AlignmentDotAbs(main) < _cfg.MinAlignDotAbs)
            {
                SetStatus("NOT ALIGNED", clearHold: true); return;
            }

            if (_cfg.HoldEnabled)
            {
                bool wasHolding = _holding && _holdGrid == main;
                _holding  = true;
                _holdGrid = main;
                if (!wasHolding)
                {
                    var carrierOriInv = MatrixD.Transpose(_door.CubeGrid.WorldMatrix.GetOrientation());
                    _holdTargetLocal  = Vector3D.TransformNormal(
                        main.Physics.CenterOfMassWorld - _door.CubeGrid.GetPosition(),
                        carrierOriInv);
                }
            }

            _status = "READY";
        }

        private void SetStatus(string s, bool clearHold)
        {
            _status = s;
            if (clearHold) { _holding = false; _holdGrid = null; }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Detection helpers
        // ─────────────────────────────────────────────────────────────────────

        private IMyCubeGrid FindCandidateSmallGridInBox()
        {
            var wm     = _door.WorldMatrix;
            var center = _door.GetPosition()
                       - wm.Forward * _cfg.ShuttleOffsetM
                       + wm.Up      * _cfg.DetectUpOffsetM;

            double halfLen = _cfg.EffectiveDetectHalfLengthM;
            var rot = Quaternion.CreateFromRotationMatrix((Matrix)wm);
            var obb = new MyOrientedBoundingBoxD(center,
                new Vector3D(_cfg.DetectHalfWidthM, _cfg.DetectHalfHeightM, halfLen),
                rot);

            double r = Math.Sqrt(
                _cfg.DetectHalfWidthM  * _cfg.DetectHalfWidthM  +
                _cfg.DetectHalfHeightM * _cfg.DetectHalfHeightM +
                halfLen * halfLen) + 10.0;

            var sphere = new BoundingSphereD(center, r);
            _nearbyEnts.Clear();
            MyAPIGateway.Entities.GetEntities(_nearbyEnts, e =>
                e != null && !e.MarkedForClose &&
                sphere.Contains(e.WorldAABB) != ContainmentType.Disjoint);

            IMyCubeGrid best      = null;
            double      bestScore = double.MinValue;

            foreach (var ent in _nearbyEnts)
            {
                var g = ent as IMyCubeGrid;
                if (g == null || g == _door.CubeGrid) continue;
                if (g.MarkedForClose || g.Physics == null) continue;
                if (g.GridSizeEnum != MyCubeSize.Small) continue;

                var aabb = g.WorldAABB;
                if (obb.Contains(ref aabb) == ContainmentType.Disjoint) continue;

                double dotAbs = AlignmentDotAbs(g);
                double distSq = Vector3D.DistanceSquared(g.GetPosition(), center);
                double score  = dotAbs * 10.0 - distSq * 0.0001;

                if (score > bestScore) { bestScore = score; best = g; }
            }

            return best;
        }

        private double AlignmentDotAbs(IMyCubeGrid g)
        {
            return Math.Abs(Vector3D.Dot(
                Vector3D.Normalize(g.WorldMatrix.Forward),
                Vector3D.Normalize(_door.WorldMatrix.Forward)));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Mechanical-link BFS
        // ─────────────────────────────────────────────────────────────────────

        private IMyCubeGrid ResolveMainGridByMechanicalLinks(IMyCubeGrid seed)
        {
            if (seed == null) return null;

            CollectNearbySmallGrids(seed.GetPosition());

            _gridById.Clear();
            for (int i = 0; i < _nearbySmallGrids.Count; i++)
            {
                var g = _nearbySmallGrids[i];
                if (g != null) _gridById[g.EntityId] = g;
            }

            BuildMechanicalAdjacency();

            _bfsVisited.Clear();
            _bfsQueue.Clear();
            _bfsVisited.Add(seed.EntityId);
            _bfsQueue.Enqueue(seed.EntityId);

            var   best     = seed;
            float bestMass = seed.Physics != null ? seed.Physics.Mass : 0f;

            while (_bfsQueue.Count > 0)
            {
                long id = _bfsQueue.Dequeue();

                IMyCubeGrid g;
                if (_gridById.TryGetValue(id, out g) && g?.Physics != null)
                {
                    float m = g.Physics.Mass;
                    if (m > bestMass) { bestMass = m; best = g; }
                }

                List<long> neigh;
                if (!_mechAdj.TryGetValue(id, out neigh) || neigh == null) continue;
                for (int i = 0; i < neigh.Count; i++)
                {
                    long nid = neigh[i];
                    if (_bfsVisited.Add(nid))
                        _bfsQueue.Enqueue(nid);
                }
            }

            return best;
        }

        private void CollectNearbySmallGrids(Vector3D center)
        {
            _nearbySmallGrids.Clear();
            var sphere = new BoundingSphereD(center, _cfg.MechGroupScanRadiusM);

            _nearbyEnts.Clear();
            MyAPIGateway.Entities.GetEntities(_nearbyEnts, e =>
                e != null && !e.MarkedForClose &&
                sphere.Contains(e.WorldAABB) != ContainmentType.Disjoint);

            foreach (var ent in _nearbyEnts)
            {
                var g = ent as IMyCubeGrid;
                if (g == null || g.MarkedForClose || g.Physics == null) continue;
                if (g.GridSizeEnum != MyCubeSize.Small || g == _door.CubeGrid) continue;
                _nearbySmallGrids.Add(g);
            }
        }

        private void BuildMechanicalAdjacency()
        {
            _mechAdj.Clear();

            for (int i = 0; i < _nearbySmallGrids.Count; i++)
            {
                var g = _nearbySmallGrids[i];
                if (g == null) continue;

                _slimsBuf.Clear();
                try { g.GetBlocks(_slimsBuf, null); }
                catch { continue; }

                for (int s = 0; s < _slimsBuf.Count; s++)
                {
                    var slim = _slimsBuf[s];
                    if (slim == null) continue;

                    var mech = slim.FatBlock as IMyMechanicalConnectionBlock;
                    if (mech == null) continue;

                    IMyCubeGrid top = null;
                    try { top = mech.TopGrid; } catch { }
                    if (top == null || top.MarkedForClose) continue;
                    if (top.GridSizeEnum != MyCubeSize.Small) continue;

                    AddEdge(g.EntityId, top.EntityId);
                    AddEdge(top.EntityId, g.EntityId);
                }
            }
        }

        private void AddEdge(long a, long b)
        {
            List<long> list;
            if (!_mechAdj.TryGetValue(a, out list))
            {
                list = new List<long>(8);
                _mechAdj[a] = list;
            }
            for (int i = 0; i < list.Count; i++)
                if (list[i] == b) return;
            list.Add(b);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Hold force
        // ─────────────────────────────────────────────────────────────────────

        private void TickHoldForce()
        {
            if (!_holding || _launching) return;
            if (_door?.CubeGrid?.Physics == null)                       { ClearHold(); return; }
            if (_holdGrid?.Physics == null || _holdGrid.MarkedForClose) { ClearHold(); return; }
            if (!_armed || NowTick() < _cooldownUntilTick)              { ClearHold(); return; }

            var carrierOri = _door.CubeGrid.WorldMatrix.GetOrientation();
            var liftDir    = _door.WorldMatrix.Up;
            var targetPos  = _door.CubeGrid.GetPosition()
                           + Vector3D.TransformNormal(_holdTargetLocal, carrierOri)
                           + liftDir * _cfg.HoldLiftOffsetM;
            var com        = _holdGrid.Physics.CenterOfMassWorld;

            var carrierLin   = _door.CubeGrid.Physics.LinearVelocity;
            var carrierOmega = _door.CubeGrid.Physics.AngularVelocity;
            var r            = targetPos - _door.CubeGrid.Physics.CenterOfMassWorld;
            var carrierPtVel = carrierLin + Vector3D.Cross(carrierOmega, r);

            var posErr     = targetPos - com;
            var posCorrVel = posErr * _cfg.HoldPosCorrectionKp;
            double pvLen   = posCorrVel.Length();
            if (pvLen > _cfg.HoldMaxPosCorrection && pvLen > 1e-6)
                posCorrVel *= _cfg.HoldMaxPosCorrection / pvLen;

            try
            {
                _holdGrid.Physics.LinearVelocity  = (Vector3)(carrierPtVel + posCorrVel);

                var alignTarget = carrierOmega;
                var catFwd  = Vector3D.Normalize(_door.WorldMatrix.Forward);
                var acFwd   = Vector3D.Normalize(_holdGrid.WorldMatrix.Forward);
                var alignAxis = Vector3D.Cross(acFwd, catFwd);
                double sinErr = alignAxis.Length();
                double angErr = Math.Asin(Math.Min(1.0, sinErr));
                if (angErr > 0.005 && angErr < _cfg.HoldAlignGateRad && sinErr > 1e-6)
                {
                    double rate = Math.Min(angErr * _cfg.HoldAlignKp, _cfg.HoldAlignMaxOmega);
                    alignTarget += (alignAxis / sinErr) * rate;
                }
                _holdGrid.Physics.AngularVelocity = alignTarget;
            }
            catch
            {
                ClearHold();
            }
        }

        private void ClearHold() { _holding = false; _holdGrid = null; }

        // ─────────────────────────────────────────────────────────────────────
        // Launch force
        // ─────────────────────────────────────────────────────────────────────

        private void BeginLaunchForce(IMyCubeGrid aircraft)
        {
            _launching = false; _launchGrid = null; _launchFramesLeft = 0;
            if (aircraft?.Physics == null) return;

            _launchDir = Vector3D.Normalize(_door.WorldMatrix.Forward);

            int frames = (int)Math.Ceiling(_cfg.EffectiveLaunchDurationS * 60f);
            if (frames < 1) frames = 1;

            _launchDeltaVPerFrame = _cfg.LaunchAddSpeedMps / frames;

            _launchGrid       = aircraft;
            _launchFramesLeft = frames;
            _launching        = true;

            try { aircraft.Physics.AngularVelocity *= 0.6f; } catch { }
        }

        private void TickLaunchForce()
        {
            if (!_launching) return;
            if (_launchGrid == null || _launchGrid.MarkedForClose || _launchGrid.Physics == null)
            {
                _launching = false; _launchGrid = null; _launchFramesLeft = 0; return;
            }
            if (_launchFramesLeft <= 0)
            {
                _launching = false; _launchGrid = null; return;
            }

            try
            {
                var liftDir    = (Vector3D)_door.WorldMatrix.Up;
                var carrierOri = _door.CubeGrid.WorldMatrix.GetOrientation();

                var targetPos  = _door.CubeGrid.GetPosition()
                               + Vector3D.TransformNormal(_holdTargetLocal, carrierOri)
                               + liftDir * _cfg.HoldLiftOffsetM;
                var com        = _launchGrid.Physics.CenterOfMassWorld;

                var vertErr    = Vector3D.Dot(targetPos - com, liftDir);
                var carrierVtV = _door.CubeGrid.Physics != null
                               ? Vector3D.Dot(_door.CubeGrid.Physics.LinearVelocity, liftDir)
                               : 0.0;
                var corrVtV    = carrierVtV + Math.Max(-(double)_cfg.HoldMaxPosCorrection,
                                 Math.Min((double)_cfg.HoldMaxPosCorrection,
                                          vertErr * _cfg.HoldPosCorrectionKp));

                var vel        = (Vector3D)_launchGrid.Physics.LinearVelocity;
                vel           += _launchDir * _launchDeltaVPerFrame;
                var curVtV     = Vector3D.Dot(vel, liftDir);
                vel           += liftDir * (corrVtV - curVtV);

                _launchGrid.Physics.LinearVelocity  = (Vector3)vel;
                var carrierOmegaL = _door.CubeGrid.Physics != null
                                  ? _door.CubeGrid.Physics.AngularVelocity
                                  : Vector3.Zero;
                _launchGrid.Physics.AngularVelocity = carrierOmegaL;
            }
            catch
            {
                _launching = false; _launchGrid = null; _launchFramesLeft = 0; return;
            }

            _launchFramesLeft--;
        }

        private void TickReturnClose()
        {
            if (_returnCloseAtTick <= 0 || NowTick() < _returnCloseAtTick) return;
            _returnCloseAtTick = 0;
            try { _door?.CloseDoor(); } catch { }
            ApplyArmed(false);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Terminal controls
        // ─────────────────────────────────────────────────────────────────────

        private static void RegisterTerminalControls()
        {
            if (MyAPIGateway.Utilities?.IsDedicated ?? false) return;

            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
            {
                if (MyAPIGateway.TerminalControls == null) return;

                var armed = MyAPIGateway.TerminalControls
                    .CreateControl<IMyTerminalControlOnOffSwitch, IMyTerminalBlock>("TT_CAT_Armed");
                armed.Title   = MyStringId.GetOrCompute("Catapult Armed");
                armed.Tooltip = MyStringId.GetOrCompute("Arms/disarms the catapult.");
                armed.OnText  = MyStringId.GetOrCompute("ARMED");
                armed.OffText = MyStringId.GetOrCompute("SAFE");
                armed.Getter  = b => GetLogic(b)?._armed ?? false;
                armed.Setter  = (b, v) => GetLogic(b)?.RequestArm(v);
                armed.Visible = b => IsOurBlock(b);
                MyAPIGateway.TerminalControls.AddControl<IMyTerminalBlock>(armed);

                var dbg = MyAPIGateway.TerminalControls
                    .CreateControl<IMyTerminalControlOnOffSwitch, IMyTerminalBlock>("TT_CAT_DebugDraw");
                dbg.Title   = MyStringId.GetOrCompute("Show Debug");
                dbg.Tooltip = MyStringId.GetOrCompute("Draws the detection box (client-side).");
                dbg.OnText  = MyStringId.GetOrCompute("ON");
                dbg.OffText = MyStringId.GetOrCompute("OFF");
                dbg.Getter  = b => GetLogic(b)?._debugDraw ?? false;
                dbg.Setter  = (b, v) =>
                {
                    var l = GetLogic(b);
                    if (l == null) return;
                    l._debugDraw = v;
                    l._term?.RefreshCustomInfo();
                };
                dbg.Visible = b => IsOurBlock(b);
                MyAPIGateway.TerminalControls.AddControl<IMyTerminalBlock>(dbg);

                var btn = MyAPIGateway.TerminalControls
                    .CreateControl<IMyTerminalControlButton, IMyTerminalBlock>("TT_CAT_Launch");
                btn.Title   = MyStringId.GetOrCompute("Launch");
                btn.Tooltip = MyStringId.GetOrCompute("Launch the detected aircraft (server-validated).");
                btn.Action  = b => GetLogic(b)?.RequestLaunch();
                btn.Enabled = b =>
                {
                    var l = GetLogic(b);
                    if (l == null) return false;
                    var d = b as IMyDoor;
                    return l._armed && d != null && d.IsFunctional && d.Enabled;
                };
                btn.Visible = b => IsOurBlock(b);
                MyAPIGateway.TerminalControls.AddControl<IMyTerminalBlock>(btn);
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // Terminal actions
        // ─────────────────────────────────────────────────────────────────────

        private static void RegisterTerminalActions()
        {
            if (MyAPIGateway.Utilities?.IsDedicated ?? false) return;

            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
            {
                if (MyAPIGateway.TerminalControls == null) return;

                var aLaunch = MyAPIGateway.TerminalControls
                    .CreateAction<IMyTerminalBlock>("TT_CAT_Action_Launch");
                aLaunch.Name    = new StringBuilder("Catapult: Launch");
                aLaunch.Icon    = @"Textures\GUI\Icons\Actions\Toggle.dds";
                aLaunch.Enabled = b => IsOurBlock(b) && (GetLogic(b)?._armed ?? false);
                aLaunch.Action  = b => GetLogic(b)?.RequestLaunch();
                aLaunch.Writer  = (b, sb) => sb.Append(GetLogic(b)?._armed == true ? "ARMED" : "SAFE");
                MyAPIGateway.TerminalControls.AddAction<IMyTerminalBlock>(aLaunch);

                var aToggle = MyAPIGateway.TerminalControls
                    .CreateAction<IMyTerminalBlock>("TT_CAT_Action_ArmToggle");
                aToggle.Name    = new StringBuilder("Catapult: Arm Toggle");
                aToggle.Icon    = @"Textures\GUI\Icons\Actions\SwitchOn.dds";
                aToggle.Enabled = b => IsOurBlock(b);
                aToggle.Action  = b =>
                {
                    var l = GetLogic(b);
                    if (l != null) l.RequestArm(!l._armed);
                };
                aToggle.Writer = (b, sb) => sb.Append(GetLogic(b)?._armed == true ? "ARMED" : "SAFE");
                MyAPIGateway.TerminalControls.AddAction<IMyTerminalBlock>(aToggle);

                var aOn = MyAPIGateway.TerminalControls
                    .CreateAction<IMyTerminalBlock>("TT_CAT_Action_ArmOn");
                aOn.Name    = new StringBuilder("Catapult: Arm");
                aOn.Icon    = @"Textures\GUI\Icons\Actions\SwitchOn.dds";
                aOn.Enabled = b => IsOurBlock(b);
                aOn.Action  = b => GetLogic(b)?.RequestArm(true);
                aOn.Writer = (b, sb) => sb.Append("ARMED");
                MyAPIGateway.TerminalControls.AddAction<IMyTerminalBlock>(aOn);

                var aOff = MyAPIGateway.TerminalControls
                    .CreateAction<IMyTerminalBlock>("TT_CAT_Action_ArmOff");
                aOff.Name    = new StringBuilder("Catapult: Safe");
                aOff.Icon    = @"Textures\GUI\Icons\Actions\SwitchOff.dds";
                aOff.Enabled = b => IsOurBlock(b);
                aOff.Action  = b => GetLogic(b)?.RequestArm(false);
                aOff.Writer = (b, sb) => sb.Append("SAFE");
                MyAPIGateway.TerminalControls.AddAction<IMyTerminalBlock>(aOff);

                var aDbg = MyAPIGateway.TerminalControls
                    .CreateAction<IMyTerminalBlock>("TT_CAT_Action_DebugToggle");
                aDbg.Name    = new StringBuilder("Catapult: Debug Toggle");
                aDbg.Icon    = @"Textures\GUI\Icons\Actions\Toggle.dds";
                aDbg.Enabled = b => IsOurBlock(b);
                aDbg.Action  = b =>
                {
                    var l = GetLogic(b);
                    if (l == null) return;
                    l._debugDraw = !l._debugDraw;
                    l._term?.RefreshCustomInfo();
                };
                aDbg.Writer = (b, sb) => sb.Append(GetLogic(b)?._debugDraw == true ? "ON" : "OFF");
                MyAPIGateway.TerminalControls.AddAction<IMyTerminalBlock>(aDbg);
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // Hide vanilla door Open/Close controls
        // ─────────────────────────────────────────────────────────────────────

        private static void EnsureHideVanillaDoorControls()
        {
            if (_controlFilterHooked) return;
            if (MyAPIGateway.Utilities?.IsDedicated ?? false) return;

            _controlFilterHooked = true;

            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
            {
                if (MyAPIGateway.TerminalControls == null) return;
                MyAPIGateway.TerminalControls.CustomControlGetter +=
                    (IMyTerminalBlock block, List<IMyTerminalControl> controls) =>
                    {
                        if (!IsOurBlock(block)) return;
                        for (int i = controls.Count - 1; i >= 0; i--)
                        {
                            var id = controls[i]?.Id;
                            if (string.IsNullOrEmpty(id)) continue;
                            if (id.IndexOf("Open",    StringComparison.OrdinalIgnoreCase) >= 0 ||
                                id.IndexOf("Reverse", StringComparison.OrdinalIgnoreCase) >= 0)
                                controls.RemoveAt(i);
                        }
                    };
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // Debug draw
        // ─────────────────────────────────────────────────────────────────────

        private void DrawDebug()
        {
            if (_door == null || (MyAPIGateway.Utilities?.IsDedicated ?? false)) return;

            var wm     = _door.WorldMatrix;

            var detCenter = _door.GetPosition()
                          - wm.Forward * _cfg.ShuttleOffsetM
                          + wm.Up      * _cfg.DetectUpOffsetM;
            var dm = wm; dm.Translation = detCenter;
            var dHalf = new Vector3D(_cfg.DetectHalfWidthM, _cfg.DetectHalfHeightM, _cfg.EffectiveDetectHalfLengthM);
            var dBb   = new BoundingBoxD(-dHalf, dHalf);
            var dFace = new Color(0, 255, 255, 25);
            var dWire = new Color(0, 255, 255, 200);
            MySimpleObjectDraw.DrawTransparentBox(ref dm, ref dBb, ref dFace, MySimpleObjectRasterizer.Solid,     1, 0.02f, null, null, false);
            MySimpleObjectDraw.DrawTransparentBox(ref dm, ref dBb, ref dWire, MySimpleObjectRasterizer.Wireframe, 1, 0.04f, null, null, false);

            if (_holding && _holdGrid != null)
            {
                var carrierOri = _door.CubeGrid.WorldMatrix.GetOrientation();
                var holdPos    = _door.CubeGrid.GetPosition()
                               + Vector3D.TransformNormal(_holdTargetLocal, carrierOri);
                var hm = wm; hm.Translation = holdPos;
                var hHalf = new Vector3D(1.0, 1.0, 1.0);
                var hBb   = new BoundingBoxD(-hHalf, hHalf);
                var hFace = new Color(255, 220, 0, 60);
                var hWire = new Color(255, 220, 0, 255);
                MySimpleObjectDraw.DrawTransparentBox(ref hm, ref hBb, ref hFace, MySimpleObjectRasterizer.Solid,     1, 0.02f, null, null, false);
                MySimpleObjectDraw.DrawTransparentBox(ref hm, ref hBb, ref hWire, MySimpleObjectRasterizer.Wireframe, 1, 0.06f, null, null, false);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static bool IsOurBlock(IMyTerminalBlock b)
        {
            return b != null && CatapultConfigs.IsKnown(b.BlockDefinition.SubtypeName);
        }

        private static CatapultBlock GetLogic(IMyTerminalBlock b)
        {
            var door = b as IMyDoor;
            return door?.GameLogic.GetAs<CatapultBlock>();
        }

        private static long NowTick()
        {
            return (long)(MyAPIGateway.Session?.GameplayFrameCounter ?? 0);
        }
    }
}
