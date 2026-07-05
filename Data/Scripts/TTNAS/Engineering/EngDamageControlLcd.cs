using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace TTNAS
{
    // ───────────────────────────────────────────────────────────────────────────
    // EngDamageControlLcd — Hull integrity and per-zone damage breakdown.
    //
    // Sections:
    //   STATUS   — NOMINAL / UNDER ATTACK, overall hull integrity %, bar
    //   ZONES    — Fore / Midship / Aft breakdown:
    //                block count, damaged count, critical count, integrity bar
    //   SYSTEMS  — Reactor, thruster and weapon readiness from EngGridCache
    //
    // Zone boundaries are read from LCD custom data: [TT_ZONES=33,67]
    // (percentages along the ship's forward axis; defaults to 33 / 67).
    //
    // LCD script ID: TTNB_EngDamage  |  "TT Damage Control"
    // Update:        Update100 (~1.67 s)
    // ───────────────────────────────────────────────────────────────────────────
    [MyTextSurfaceScript("TTNB_EngDamage", "TT ENG \u2014 Damage Control")]
    public class EngDamageControlLcd : MyTSSCommon
    {
        private readonly IMyTextSurface     _surface;
        private readonly IMyCubeBlock       _block;
        private readonly List<IMySlimBlock> _slims = new List<IMySlimBlock>();

        private const float MARGIN   = 8f;
        private const float LINE_PAD = 1f;

        private struct ZoneData
        {
            public string Label;
            public int    Total;
            public int    Damaged;
            public int    Critical;
            public float  TotalIntegrity;
            public float  CurrentIntegrity;
        }

        public EngDamageControlLcd(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _surface = surface;
            _block   = block;
        }

        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update100;

        public override void Run()
        {
            try { Draw(); }
            catch { }
        }

        private void Draw()
        {
            _surface.ScriptBackgroundColor = ShipMapHelper.ColBg;

            var vp = new RectangleF(
                (_surface.TextureSize - _surface.SurfaceSize) / 2f,
                _surface.SurfaceSize);

            float charH = _surface.MeasureStringInPixels(new StringBuilder("W"),
                              ShipMapHelper.FONT, ShipMapHelper.FS_MAIN).Y + LINE_PAD;
            float subH  = _surface.MeasureStringInPixels(new StringBuilder("W"),
                              ShipMapHelper.FONT, ShipMapHelper.FS_SUB).Y  + LINE_PAD;

            var   frame = _surface.DrawFrame();
            float x0    = vp.X + MARGIN;
            float w     = vp.Width - MARGIN * 2f;

            ShipMapHelper.Rect(frame,
                vp.X + vp.Width * 0.5f, vp.Y + vp.Height * 0.5f,
                vp.Width, vp.Height, ShipMapHelper.ColBg);

            float curY = vp.Y + MARGIN;

            // ── Header ────────────────────────────────────────────────────────
            ShipMapHelper.TXT(frame, x0, curY,
                "TT DAMAGE CONTROL", ShipMapHelper.ColHeader,
                ShipMapHelper.FS_MAIN, TextAlignment.LEFT);
            ShipMapHelper.TXT(frame, x0 + w, curY,
                DateTime.Now.ToString("HH:mm:ss"), ShipMapHelper.ColHeader,
                ShipMapHelper.FS_MAIN, TextAlignment.RIGHT);
            curY += charH;
            ShipMapHelper.HLine(frame, x0 + w * 0.5f, curY, w, ShipMapHelper.ColHeader);
            curY += 4f;

            // ── Grid name ─────────────────────────────────────────────────────
            ShipMapHelper.Rect(frame, x0 + w * 0.5f, curY + subH * 0.5f,
                w, subH + 2f, ShipMapHelper.ColRowBg);
            ShipMapHelper.TXT(frame, x0 + 4f, curY,
                _block.CubeGrid.DisplayName ?? "Unknown",
                ShipMapHelper.ColValue, ShipMapHelper.FS_SUB, TextAlignment.LEFT);
            curY += subH + 2f;
            ShipMapHelper.HLine(frame, x0 + w * 0.5f, curY, w, ShipMapHelper.ColSep);
            curY += 5f;

            // ── Scan blocks ────────────────────────────────────────────────────
            _slims.Clear();
            try { _block.CubeGrid.GetBlocks(_slims); }
            catch (InvalidOperationException) { frame.Dispose(); return; }

            // Find forward axis from cockpit or TT_DIRECTION tag
            const string DIR_TAG = "[TT_DIRECTION]";
            IMyCubeBlock dirRef  = null;
            IMyCubeBlock mainCoc = null;
            foreach (var s in _slims)
            {
                if (s.FatBlock == null) continue;
                var tb = s.FatBlock as IMyTerminalBlock;
                if (tb != null && tb.CustomData.IndexOf(DIR_TAG,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    dirRef = s.FatBlock as IMyCubeBlock;
                var sc = s.FatBlock as IMyShipController;
                if (sc != null && sc.IsMainCockpit)
                    mainCoc = s.FatBlock as IMyCubeBlock;
            }
            IMyCubeBlock      refBlock = dirRef ?? mainCoc;
            Base6Directions.Direction fwdDir = refBlock != null
                ? refBlock.Orientation.Forward
                : Base6Directions.Direction.Forward;

            // World-space unit vector pointing toward the ship's nose
            var  worldFwd = _block.CubeGrid.WorldMatrix.GetDirectionVector(fwdDir);

            // Find extent along forward axis (world-space positions)
            float minProj = float.MaxValue;
            float maxProj = float.MinValue;
            foreach (var s in _slims)
            {
                Vector3D pos = _block.CubeGrid.GridIntegerToWorld(s.Position);
                float    p   = (float)pos.Dot(worldFwd);
                if (p < minProj) minProj = p;
                if (p > maxProj) maxProj = p;
            }
            float range = maxProj - minProj;

            // Parse zone fractions
            float z1Frac, z2Frac;
            ShipMapHelper.ParseZones(_block, out z1Frac, out z2Frac);

            // Build three zones: Fore [0, z1), Midship [z1, z2), Aft [z2, 1]
            var zones = new ZoneData[3];
            zones[0].Label = "FORE";
            zones[1].Label = "MIDSHIP";
            zones[2].Label = "AFT";

            float totalI   = 0f;
            float currentI = 0f;
            bool  underAtk = false;

            var eng = EngineeringProcessor.GetForGrid(_block.CubeGrid);
            if (eng != null) underAtk = eng.UnderAttack;

            foreach (var slim in _slims)
            {
                float maxSI = slim.MaxIntegrity;
                float curSI = slim.Integrity;
                totalI   += maxSI;
                currentI += curSI;

                // Zone assignment
                int zoneIdx = 2; // default Aft
                if (range > 0f)
                {
                    Vector3D pos  = _block.CubeGrid.GridIntegerToWorld(slim.Position);
                    float    proj = (float)pos.Dot(worldFwd);
                    // norm: 0 = aft end (min projection), 1 = fore end (max projection)
                    float    norm = (proj - minProj) / range;
                    if      (norm >= z2Frac) zoneIdx = 0; // FORE
                    else if (norm >= z1Frac) zoneIdx = 1; // MIDSHIP
                    // else zoneIdx = 2 (AFT, already set)
                }

                zones[zoneIdx].Total++;
                zones[zoneIdx].TotalIntegrity   += maxSI;
                zones[zoneIdx].CurrentIntegrity += curSI;
                if (curSI < maxSI)                      zones[zoneIdx].Damaged++;
                if (maxSI > 0f && curSI / maxSI < 0.33f) zones[zoneIdx].Critical++;
            }

            float hullPct = totalI > 0f ? currentI / totalI : 1f;

            // ── STATUS ────────────────────────────────────────────────────────
            string statusStr = underAtk ? "UNDER ATTACK" : "NOMINAL";
            Color  statusCol = underAtk ? ShipMapHelper.ColError : ShipMapHelper.ColOk;

            ShipMapHelper.TXT(frame, x0, curY, "STATUS", ShipMapHelper.ColHeader,
                ShipMapHelper.FS_MAIN, TextAlignment.LEFT);
            ShipMapHelper.TXT(frame, x0 + w, curY, statusStr,
                statusCol, ShipMapHelper.FS_MAIN, TextAlignment.RIGHT);
            curY += charH;

            // Hull integrity bar
            Color hullCol = hullPct > 0.6f ? ShipMapHelper.ColOk
                          : hullPct > 0.3f ? ShipMapHelper.ColWarn
                          : ShipMapHelper.ColError;
            DrawBar(frame, x0, curY, w, subH, hullPct, hullCol,
                "HULL  " + (hullPct * 100f).ToString("F1") + "%");
            curY += subH + 3f;
            ShipMapHelper.HLine(frame, x0 + w * 0.5f, curY, w, ShipMapHelper.ColSep);
            curY += 5f;

            // ── ZONES ─────────────────────────────────────────────────────────
            ShipMapHelper.TXT(frame, x0, curY, "ZONES", ShipMapHelper.ColHeader,
                ShipMapHelper.FS_MAIN, TextAlignment.LEFT);
            curY += charH;

            foreach (var zone in zones)
            {
                float zFrac = zone.TotalIntegrity > 0f
                    ? zone.CurrentIntegrity / zone.TotalIntegrity
                    : 1f;
                Color zCol  = zone.Critical > 0 ? ShipMapHelper.ColError
                            : zone.Damaged  > 0 ? ShipMapHelper.ColWarn
                            : ShipMapHelper.ColOk;

                // Zone label row
                ShipMapHelper.Rect(frame, x0 + w * 0.5f, curY + subH * 0.5f,
                    w, subH, ShipMapHelper.ColRowBg);

                // Label + block count
                ShipMapHelper.TXT(frame, x0 + 4f, curY, zone.Label,
                    ShipMapHelper.ColLabel, ShipMapHelper.FS_SUB, TextAlignment.LEFT);

                // Stats: Dmg / Crit counts (right side)
                string dmgStr = zone.Critical > 0
                    ? zone.Damaged + " dmg  " + zone.Critical + " crit"
                    : zone.Damaged > 0
                        ? zone.Damaged + " dmg"
                        : "intact";
                ShipMapHelper.TXT(frame, x0 + w, curY, dmgStr,
                    zCol, ShipMapHelper.FS_SUB, TextAlignment.RIGHT);

                curY += subH;

                // Mini integrity bar spanning full width
                DrawBar(frame, x0, curY, w, subH - 2f, zFrac, zCol,
                    (zFrac * 100f).ToString("F0") + "%");
                curY += subH;

                ShipMapHelper.Rect(frame, x0 + w * 0.5f, curY, w, 1f, ShipMapHelper.ColSep);
                curY += 4f;
            }

            ShipMapHelper.HLine(frame, x0 + w * 0.5f, curY, w, ShipMapHelper.ColSep);
            curY += 5f;

            // ── SYSTEMS ───────────────────────────────────────────────────────
            ShipMapHelper.TXT(frame, x0, curY, "SYSTEMS", ShipMapHelper.ColHeader,
                ShipMapHelper.FS_MAIN, TextAlignment.LEFT);
            curY += charH;

            if (eng != null)
            {
                DrawSystemRow(frame, x0, w, ref curY, subH,
                    "Reactors",
                    eng.ReactorFunctional + " / " + eng.ReactorTotal,
                    eng.ReactorFunctional >= eng.ReactorTotal ? ShipMapHelper.ColOk
                    : eng.ReactorFunctional > 0               ? ShipMapHelper.ColWarn
                    : ShipMapHelper.ColError);

                int thrE = eng.ThrMainEnabled + eng.ThrBrakeEnabled + eng.ThrMnvrEnabled;
                int thrT = eng.ThrMainTotal  + eng.ThrBrakeTotal  + eng.ThrMnvrTotal;
                DrawSystemRow(frame, x0, w, ref curY, subH,
                    "Thrusters",
                    thrE + " / " + thrT,
                    thrE >= thrT            ? ShipMapHelper.ColOk
                    : thrE > 0              ? ShipMapHelper.ColWarn
                    : ShipMapHelper.ColError);

                DrawSystemRow(frame, x0, w, ref curY, subH,
                    "Weapons",
                    eng.WeaponFunctional + " / " + eng.WeaponTotal,
                    eng.WeaponFunctional >= eng.WeaponTotal ? ShipMapHelper.ColOk
                    : eng.WeaponFunctional > 0              ? ShipMapHelper.ColWarn
                    : ShipMapHelper.ColError);

                DrawSystemRow(frame, x0, w, ref curY, subH,
                    "Gyros",
                    eng.GyroFunctional + " / " + eng.GyroTotal,
                    eng.GyroFunctional >= eng.GyroTotal ? ShipMapHelper.ColOk
                    : eng.GyroFunctional > 0            ? ShipMapHelper.ColWarn
                    : ShipMapHelper.ColError);
            }
            else
            {
                ShipMapHelper.TXT(frame, x0 + 4f, curY,
                    "No Engineering Processor on grid",
                    ShipMapHelper.ColDim, ShipMapHelper.FS_SUB, TextAlignment.LEFT);
                curY += subH;
            }

            // ── Footer ────────────────────────────────────────────────────────
            DrawFooter(frame, vp, w, subH);

            frame.Dispose();
        }

        private void DrawSystemRow(MySpriteDrawFrame frame, float x0, float w,
            ref float curY, float subH, string label, string value, Color valueCol)
        {
            ShipMapHelper.Rect(frame, x0 + w * 0.5f, curY + subH * 0.5f,
                w, subH, ShipMapHelper.ColRowBg);
            ShipMapHelper.TXT(frame, x0 + 4f, curY, label,
                ShipMapHelper.ColLabel, ShipMapHelper.FS_SUB, TextAlignment.LEFT);
            ShipMapHelper.TXT(frame, x0 + w, curY, value,
                valueCol, ShipMapHelper.FS_SUB, TextAlignment.RIGHT);
            curY += subH;
            ShipMapHelper.Rect(frame, x0 + w * 0.5f, curY, w, 1f, ShipMapHelper.ColSep);
            curY += 2f;
        }

        private void DrawBar(MySpriteDrawFrame frame, float x, float y,
            float w, float h, float frac, Color fillCol, string label)
        {
            float cx = x + w * 0.5f;
            float cy = y + h * 0.5f;
            ShipMapHelper.Rect(frame, cx, cy, w, h, ShipMapHelper.ColMapBg);
            if (frac > 0f)
            {
                float fw = Math.Max(1f, w * Math.Min(1f, frac));
                ShipMapHelper.Rect(frame, x + fw * 0.5f, cy, fw, h, fillCol);
            }
            ShipMapHelper.TXT(frame, cx, y, label,
                ShipMapHelper.ColValue, ShipMapHelper.FS_XS, TextAlignment.CENTER);
        }

        private void DrawFooter(MySpriteDrawFrame frame, RectangleF vp, float w, float subH)
        {
            float y = vp.Y + vp.Height - subH - MARGIN;
            ShipMapHelper.HLine(frame, vp.X + MARGIN + w * 0.5f, y, w, ShipMapHelper.ColHeader);
            y += 4f;
            ShipMapHelper.TXT(frame, vp.X + MARGIN, y,
                "Terran Titans Naval Advanced Systems", ShipMapHelper.ColFooter,
                ShipMapHelper.FS_SUB, TextAlignment.LEFT);
        }
    }
}
