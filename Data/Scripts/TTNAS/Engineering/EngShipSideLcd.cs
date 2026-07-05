using System;
using System.Collections.Generic;
using System.Text;
using CoreSystems.Api;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace TTNAS
{
    // ───────────────────────────────────────────────────────────────────────────
    // EngShipSideLcd — Side-profile ship map (X-normal projection, port view)
    //
    // LCD script ID: TTNB_ShipSide  |  "TT Ship — Side View"
    // Update:        Update100 (~1.67 s)
    // ───────────────────────────────────────────────────────────────────────────
    [MyTextSurfaceScript("TTNB_ShipSide", "TT ENG \u2014 Ship Side")]
    public class EngShipSideLcd : MyTSSCommon
    {
        private readonly IMyTextSurface     _surface;
        private readonly IMyCubeBlock       _block;
        private readonly List<IMySlimBlock> _slims = new List<IMySlimBlock>();

        private byte[,] _typeMap;
        private int[,]  _densMap;
        private int[,]  _dmgMap;

        private const float MARGIN = 8f;

        public EngShipSideLcd(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
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
                              ShipMapHelper.FONT, ShipMapHelper.FS_MAIN).Y + 1f;
            float subH  = _surface.MeasureStringInPixels(new StringBuilder("W"),
                              ShipMapHelper.FONT, ShipMapHelper.FS_SUB).Y  + 1f;
            float xsH   = _surface.MeasureStringInPixels(new StringBuilder("W"),
                              ShipMapHelper.FONT, ShipMapHelper.FS_XS).Y   + 1f;

            var   frame = _surface.DrawFrame();
            float x0    = vp.X + MARGIN;
            float w     = vp.Width - MARGIN * 2f;

            ShipMapHelper.Rect(frame,
                vp.X + vp.Width * 0.5f, vp.Y + vp.Height * 0.5f,
                vp.Width, vp.Height, ShipMapHelper.ColBg);

            float curY = vp.Y + MARGIN;

            // ── Header ────────────────────────────────────────────────────────
            ShipMapHelper.TXT(frame, x0, curY,
                "TT SHIP \u2014 SIDE VIEW", ShipMapHelper.ColHeader,
                ShipMapHelper.FS_MAIN, TextAlignment.LEFT);
            ShipMapHelper.TXT(frame, vp.X + vp.Width - MARGIN, curY,
                DateTime.Now.ToString("HH:mm:ss"), ShipMapHelper.ColHeader,
                ShipMapHelper.FS_MAIN, TextAlignment.RIGHT);
            curY += charH;
            ShipMapHelper.HLine(frame, vp.X + vp.Width * 0.5f, curY, w, ShipMapHelper.ColHeader);
            curY += 4f;

            // ── Stats strip ───────────────────────────────────────────────────
            var eng = EngineeringProcessor.GetForGrid(_block.CubeGrid);
            DrawStatsStrip(frame, x0, w, ref curY, subH, eng);
            ShipMapHelper.HLine(frame, vp.X + vp.Width * 0.5f, curY, w, ShipMapHelper.ColSep);
            curY += 4f;

            float footerH   = subH + MARGIN + 8f;
            float legendH   = xsH * 2f + 12f;
            float mapBottom = vp.Y + vp.Height - footerH - legendH;

            // ── Build + render map ────────────────────────────────────────────
            int cols, rows, maxDens;
            BuildSideMap(out cols, out rows, out maxDens);

            float origX, origY, cell;
            ShipMapHelper.RenderMap(frame, x0, curY, w, mapBottom - curY,
                _typeMap, _densMap, _dmgMap, maxDens, cols, rows,
                out origX, out origY, out cell);

            // ── Zone ticks + labels ───────────────────────────────────────────
            float z1Frac, z2Frac;
            ShipMapHelper.ParseZones(_block, out z1Frac, out z2Frac);

            if (cols > 0 && cell >= 0.5f)
            {
                float mapDrawH = rows * cell;
                float mapFloor = origY + mapDrawH;
                float div1X   = origX + z1Frac * cols * cell;
                float div2X   = origX + z2Frac * cols * cell;

                float lblY    = mapFloor + 8f;
                float tickTop = mapFloor + 1f;
                float tickBot = lblY - 2f;
                if (tickBot > tickTop)
                {
                    float tickCY = (tickTop + tickBot) * 0.5f;
                    float tickH  = tickBot - tickTop;
                    ShipMapHelper.Rect(frame, div1X, tickCY, 1.5f, tickH, ShipMapHelper.ColZoneLine);
                    ShipMapHelper.Rect(frame, div2X, tickCY, 1.5f, tickH, ShipMapHelper.ColZoneLine);
                }

                float mid1 = z1Frac * 0.5f;
                float mid2 = z1Frac + (z2Frac - z1Frac) * 0.5f;
                float mid3 = z2Frac + (1f - z2Frac)     * 0.5f;
                ShipMapHelper.TXT(frame, origX + mid1 * cols * cell, lblY,
                    "BOW",   ShipMapHelper.ColLabel, ShipMapHelper.FS_XS, TextAlignment.CENTER);
                ShipMapHelper.TXT(frame, origX + mid2 * cols * cell, lblY,
                    "MID",   ShipMapHelper.ColLabel, ShipMapHelper.FS_XS, TextAlignment.CENTER);
                ShipMapHelper.TXT(frame, origX + mid3 * cols * cell, lblY,
                    "STERN", ShipMapHelper.ColLabel, ShipMapHelper.FS_XS, TextAlignment.CENTER);

                if (rows > 0)
                {
                    float topY = origY + xsH * 0.5f;
                    float botY = origY + mapDrawH - xsH * 0.5f;
                    ShipMapHelper.TXT(frame, origX - 3f, topY,
                        "\u25b2", ShipMapHelper.ColDim, ShipMapHelper.FS_XS, TextAlignment.RIGHT);
                    ShipMapHelper.TXT(frame, origX - 3f, botY,
                        "\u25bc", ShipMapHelper.ColDim, ShipMapHelper.FS_XS, TextAlignment.RIGHT);
                }
            }

            // ── Legend ────────────────────────────────────────────────────────
            float lgdY = mapBottom + 3f;
            ShipMapHelper.HLine(frame, vp.X + vp.Width * 0.5f, lgdY, w, ShipMapHelper.ColSep);
            lgdY += 3f;
            ShipMapHelper.DrawLegend(frame, x0, lgdY, w, xsH);

            DrawFooter(frame, vp, w, subH);
            frame.Dispose();
        }

        private static void DrawStatsStrip(MySpriteDrawFrame frame, float x, float w,
            ref float y, float charH, EngineeringProcessor eng)
        {
            string hullStr, dmgStr, critStr, atkStr;
            Color  hullCol, dmgCol, critCol, atkCol;

            if (eng != null)
            {
                float hp = eng.HullIntegrityPct * 100f;
                hullStr = "HULL " + hp.ToString("F1") + "%";
                hullCol = hp >= 80f ? ShipMapHelper.ColOk
                        : hp >= 50f ? ShipMapHelper.ColWarn
                        : ShipMapHelper.ColError;
                dmgStr  = "DMG "  + eng.BlockDamaged;
                dmgCol  = eng.BlockDamaged  > 0 ? ShipMapHelper.ColWarn  : ShipMapHelper.ColDim;
                critStr = "CRIT " + eng.BlockCritical;
                critCol = eng.BlockCritical > 0 ? ShipMapHelper.ColError : ShipMapHelper.ColDim;
                atkStr  = eng.UnderAttack ? "\u26a0 UNDER ATTACK" : "OK";
                atkCol  = eng.UnderAttack ? ShipMapHelper.ColError : ShipMapHelper.ColOk;
            }
            else
            {
                hullStr = "HULL --.--%"; hullCol = ShipMapHelper.ColDim;
                dmgStr  = "DMG --";      dmgCol  = ShipMapHelper.ColDim;
                critStr = "CRIT --";     critCol = ShipMapHelper.ColDim;
                atkStr  = "NO PROCESSOR"; atkCol = ShipMapHelper.ColDim;
            }

            float q = w * 0.25f;
            ShipMapHelper.TXT(frame, x,          y, hullStr, hullCol, ShipMapHelper.FS_SUB, TextAlignment.LEFT);
            ShipMapHelper.TXT(frame, x + q,      y, dmgStr,  dmgCol,  ShipMapHelper.FS_SUB, TextAlignment.LEFT);
            ShipMapHelper.TXT(frame, x + q * 2f, y, critStr, critCol, ShipMapHelper.FS_SUB, TextAlignment.LEFT);
            ShipMapHelper.TXT(frame, x + q * 3f, y, atkStr,  atkCol,  ShipMapHelper.FS_SUB, TextAlignment.LEFT);
            y += charH + 3f;
        }

        private void BuildSideMap(out int cols, out int rows, out int maxDens)
        {
            cols = 0; rows = 0; maxDens = 0;

            _slims.Clear();
            try { _block.CubeGrid.GetBlocks(_slims); }
            catch (InvalidOperationException) { cols = 0; rows = 0; maxDens = 0; return; }
            if (_slims.Count == 0) return;

            int minZ = int.MaxValue, maxZ = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;

            foreach (var s in _slims)
            {
                var p = s.Position;
                if (p.Z < minZ) minZ = p.Z;
                if (p.Z > maxZ) maxZ = p.Z;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            cols = maxZ - minZ + 1;
            rows = maxY - minY + 1;

            if (_typeMap == null
             || _typeMap.GetLength(0) != cols
             || _typeMap.GetLength(1) != rows)
            {
                _typeMap = new byte[cols, rows];
                _densMap = new int[cols,  rows];
                _dmgMap  = new int[cols,  rows];
            }
            else
            {
                Array.Clear(_typeMap, 0, _typeMap.Length);
                Array.Clear(_densMap, 0, _densMap.Length);
                Array.Clear(_dmgMap,  0, _dmgMap.Length);
            }

            WcApi wc = (NAS_Session.I != null && NAS_Session.I.WcReady) ? NAS_Session.I.WcApi : null;

            foreach (var slim in _slims)
            {
                var p  = slim.Position;
                int tc = p.Z - minZ;
                int tr = maxY - p.Y;
                if (tc < 0 || tc >= cols || tr < 0 || tr >= rows) continue;

                byte t = ShipMapHelper.BlockTypeOf(slim, wc);
                if (t > _typeMap[tc, tr]) _typeMap[tc, tr] = t;

                _densMap[tc, tr]++;

                float maxI = slim.MaxIntegrity;
                float curI = slim.Integrity;
                int   dmg  = 0;
                if (curI < maxI)                       dmg = 1;
                if (maxI > 0f && curI / maxI < 0.33f) dmg = 2;
                if (dmg > _dmgMap[tc, tr]) _dmgMap[tc, tr] = dmg;
            }

            for (int c = 0; c < cols; c++)
                for (int r = 0; r < rows; r++)
                    if (_densMap[c, r] > maxDens) maxDens = _densMap[c, r];

            ShipMapHelper.ScanlineFill(_typeMap, cols, rows);
        }

        private void DrawFooter(MySpriteDrawFrame frame, RectangleF vp, float w, float subH)
        {
            float y = vp.Y + vp.Height - subH - MARGIN;
            ShipMapHelper.HLine(frame, vp.X + vp.Width * 0.5f, y, w, ShipMapHelper.ColHeader);
            y += 4f;
            ShipMapHelper.TXT(frame, vp.X + MARGIN, y,
                "Terran Titans Naval Advanced Systems", ShipMapHelper.ColFooter,
                ShipMapHelper.FS_SUB, TextAlignment.LEFT);
        }
    }
}
