using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Globalization;

using VRage.ModAPI;
using VRage.Game.Entity;
using VRageMath;
using VRageRender;
using VRage.Utils;

namespace TTNAS
{
    public class CableRendererInstance
    {
        private readonly IMyEntity _entity;
        private readonly CableBlockConfig _cfg;

        private MyEntitySubpart _movingSubpart;
        private int _nextSubpartRetryTick;

        private readonly MatrixD[] _startLocal;
        private readonly MatrixD[] _endLocal;

        private readonly string[] _startNames;
        private readonly string[] _endNames;

        private bool _cacheValid;
        private MyEntitySubpart _cachedSubpartRef;

        private readonly Dictionary<string, IMyModelDummy> _dummyDict =
            new Dictionary<string, IMyModelDummy>(StringComparer.OrdinalIgnoreCase);

        private readonly MyStringId _material;

        public CableRendererInstance(IMyEntity entity, CableBlockConfig cfg)
        {
            _entity = entity;
            _cfg    = cfg;

            _material = MyStringId.GetOrCompute(
                string.IsNullOrWhiteSpace(cfg.Material) ? "GizmoDrawLine" : cfg.Material);

            int n = cfg.CableCount < 1 ? 1 : cfg.CableCount;

            _startLocal = new MatrixD[n];
            _endLocal   = new MatrixD[n];
            _startNames = new string[n];
            _endNames   = new string[n];

            string sp = string.IsNullOrEmpty(cfg.StartPrefix) ? "cablestart_" : cfg.StartPrefix;
            string ep = string.IsNullOrEmpty(cfg.EndPrefix)   ? "cableend_"   : cfg.EndPrefix;

            for (int i = 0; i < n; i++)
            {
                _startNames[i] = sp + (i + 1).ToString(CultureInfo.InvariantCulture);
                _endNames[i]   = ep + (i + 1).ToString(CultureInfo.InvariantCulture);
            }
        }

        public void Update()
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;
            EnsureSubpart(false);
        }

        public void Draw()
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;
            if (_entity == null || _entity.Closed) return;

            EnsureSubpart(false);

            IMyEntity endEnt = GetEndEntity();
            if (endEnt == null || endEnt.Closed) return;

            if (!_cacheValid || _cachedSubpartRef != _movingSubpart)
                RebuildCache(endEnt);

            if (!_cacheValid) return;

            MatrixD baseWorld = _entity.WorldMatrix;
            MatrixD endWorld  = endEnt.WorldMatrix;

            Vector3D sagDir = endWorld.Down;
            Vector4 color   = new Vector4(_cfg.ColorR, _cfg.ColorG, _cfg.ColorB, _cfg.ColorA);

            for (int i = 0; i < _startLocal.Length; i++)
            {
                Vector3D startW = Vector3D.Transform(_startLocal[i].Translation, baseWorld);
                Vector3D endW   = Vector3D.Transform(_endLocal[i].Translation,   endWorld);

                if (_cfg.Mode == CableRenderMode.Line)
                    DrawSegmentedLine(startW, endW, sagDir, ref color);
                else
                    DrawSegmentedTube(startW, endW, sagDir, ref color);
            }
        }

        private IMyEntity GetEndEntity()
        {
            if (!string.IsNullOrWhiteSpace(_cfg.MovingSubpartName))
            {
                if (_movingSubpart == null || _movingSubpart.Closed) return null;
                return _movingSubpart;
            }
            return _entity;
        }

        private void EnsureSubpart(bool force)
        {
            if (_entity == null) return;
            if (string.IsNullOrWhiteSpace(_cfg.MovingSubpartName)) return;

            if (_movingSubpart != null && !_movingSubpart.Closed) return;

            int tick = MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : 0;
            if (!force && tick < _nextSubpartRetryTick) return;
            _nextSubpartRetryTick = tick + 60;

            MyEntitySubpart sp;
            if (_entity.TryGetSubpart(_cfg.MovingSubpartName, out sp) && sp != null && !sp.Closed)
            {
                _movingSubpart = sp;
                _cacheValid    = false;
            }
        }

        private void RebuildCache(IMyEntity endEnt)
        {
            _cacheValid       = false;
            _cachedSubpartRef = _movingSubpart;

            for (int i = 0; i < _startLocal.Length; i++)
            {
                MatrixD m;
                if (!TryGetDummyLocal(_entity, _startNames[i], out m)) return;
                _startLocal[i] = m;

                if (!TryGetDummyLocal(endEnt, _endNames[i], out m)) return;
                _endLocal[i] = m;
            }

            _cacheValid = true;
        }

        private bool TryGetDummyLocal(IMyEntity ent, string name, out MatrixD local)
        {
            local = MatrixD.Identity;
            if (ent == null || ent.Closed || ent.Model == null) return false;

            _dummyDict.Clear();
            ent.Model.GetDummies(_dummyDict);

            IMyModelDummy dummy;
            if (!_dummyDict.TryGetValue(name, out dummy) || dummy == null) return false;

            local = (MatrixD)dummy.Matrix;
            return true;
        }

        private void DrawSegmentedLine(Vector3D start, Vector3D end, Vector3D sagDir, ref Vector4 color)
        {
            Vector3D delta = end - start;
            double dist    = delta.Length();
            if (dist < 0.05) return;

            double segLen  = _cfg.SegmentLen <= 0.01 ? 0.25 : _cfg.SegmentLen;
            int segments   = Math.Max(1, (int)Math.Ceiling(dist / segLen));
            double sagScale = Math.Max(0, _cfg.SagFactor) * dist;

            for (int i = 0; i < segments; i++)
            {
                double t1 = (double)i / segments;
                double t2 = (double)(i + 1) / segments;

                Vector3D p1 = Vector3D.Lerp(start, end, t1) + sagDir * (Math.Sin(t1 * Math.PI) * sagScale);
                Vector3D p2 = Vector3D.Lerp(start, end, t2) + sagDir * (Math.Sin(t2 * Math.PI) * sagScale);

                Vector3D d = p2 - p1;
                double l = d.Length();
                if (l < 0.01) continue;

                MyTransparentGeometry.AddLineBillboard(
                    _material, color,
                    p1,
                    (Vector3)(d / l),
                    (float)l,
                    _cfg.Width,
                    MyBillboard.BlendTypeEnum.Standard);
            }
        }

        private void DrawSegmentedTube(Vector3D start, Vector3D end, Vector3D sagDir, ref Vector4 color)
        {
            Vector3D delta = end - start;
            double dist    = delta.Length();
            if (dist < 0.05) return;

            double segLen  = _cfg.SegmentLen <= 0.01 ? 0.25 : _cfg.SegmentLen;
            int segments   = Math.Max(1, (int)Math.Ceiling(dist / segLen));
            double sagScale = Math.Max(0, _cfg.SagFactor) * dist;

            Vector3D camPos = (MyAPIGateway.Session != null && MyAPIGateway.Session.Camera != null)
                ? MyAPIGateway.Session.Camera.WorldMatrix.Translation
                : start;

            for (int i = 0; i < segments; i++)
            {
                double t1 = (double)i / segments;
                double t2 = (double)(i + 1) / segments;

                Vector3D p1 = Vector3D.Lerp(start, end, t1) + sagDir * (Math.Sin(t1 * Math.PI) * sagScale);
                Vector3D p2 = Vector3D.Lerp(start, end, t2) + sagDir * (Math.Sin(t2 * Math.PI) * sagScale);

                DrawTubeBillboardSegment(p1, p2, camPos, ref color);
            }
        }

        private void DrawTubeBillboardSegment(Vector3D p1, Vector3D p2, Vector3D camPos, ref Vector4 color)
        {
            Vector3D dirD = p2 - p1;
            double lenD = dirD.Length();
            if (lenD < 0.01) return;

            Vector3D dirN = dirD / lenD;
            Vector3 dirF = (Vector3)dirN;

            MyTransparentGeometry.AddLineBillboard(
                _material, color, p1, dirF, (float)lenD, _cfg.Width,
                MyBillboard.BlendTypeEnum.Standard);

            Vector3D mid   = (p1 + p2) * 0.5;
            Vector3D toCam = camPos - mid;
            if (toCam.LengthSquared() < 1e-6) toCam = Vector3D.Up;
            toCam.Normalize();

            Vector3D right = Vector3D.Cross(dirN, toCam);
            if (right.LengthSquared() < 1e-6)
                right = Vector3D.Cross(dirN, Vector3D.Up);
            right.Normalize();

            double   offsetAmt = _cfg.Width * 0.6;
            Vector3D q1 = p1 + right * offsetAmt;
            Vector3D q2 = p2 + right * offsetAmt;

            Vector3D dir2D = q2 - q1;
            double   len2D = dir2D.Length();
            if (len2D < 0.01) return;

            MyTransparentGeometry.AddLineBillboard(
                _material, color, q1, (Vector3)(dir2D / len2D), (float)len2D, _cfg.Width,
                MyBillboard.BlendTypeEnum.Standard);
        }
    }
}
