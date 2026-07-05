using System;
using System.Collections.Generic;
using CoreSystems.Api;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using IMyUserControllableGun = Sandbox.ModAPI.Ingame.IMyUserControllableGun;

namespace TTNAS
{
    // ───────────────────────────────────────────────────────────────────────────
    // ShipMapHelper — shared static utilities for the Top and Side view LCD scripts.
    //
    // Map convention:
    //   TOP  (Y-normal, looking down)     : Z = cols (length), X = rows (width)
    //   SIDE (X-normal, looking from port): Z = cols (length), Y = rows (height, flipped)
    //
    // Block types:
    //   0=empty  1=hull  2=power  3=thrust  4=gyro  5=weapon
    //
    // Zone lines (LCD custom data):
    //   [TT_ZONES=30,65]  — percentages along ship length, default 33/67
    // ───────────────────────────────────────────────────────────────────────────
    public static class ShipMapHelper
    {
        public const string FONT    = "Debug";
        public const float  FS_MAIN = 0.55f;
        public const float  FS_SUB  = 0.46f;
        public const float  FS_XS   = 0.40f;

        // ── Palette ───────────────────────────────────────────────────────────

        public static readonly Color ColBg        = new Color(  3,   8,  18);
        public static readonly Color ColHeader    = new Color(  0, 200, 255);
        public static readonly Color ColLabel     = new Color(110, 130, 150);
        public static readonly Color ColValue     = new Color(200, 215, 230);
        public static readonly Color ColOk        = new Color(  0, 220, 160);
        public static readonly Color ColWarn      = new Color(255, 140,   0);
        public static readonly Color ColError     = new Color(255,  70,  70);
        public static readonly Color ColDim       = new Color( 70,  90, 110);
        public static readonly Color ColSep       = new Color(  0,  45,  80);
        public static readonly Color ColFooter    = new Color( 70,  90, 110);
        public static readonly Color ColRowBg     = new Color(  0,  20,  42);

        public static readonly Color ColMapBg     = new Color(  0,  10,  20);
        public static readonly Color ColMapBorder = new Color(  0,  45,  80);
        public static readonly Color ColZoneLine  = new Color(  0,  90, 130);
        public static readonly Color ColHullMin   = new Color(  8,  35,  65);
        public static readonly Color ColHullMax   = new Color( 40, 160, 230);
        public static readonly Color ColDamaged   = new Color(210, 120,   0);
        public static readonly Color ColCritical  = new Color(210,  30,  30);

        public static readonly Color[] ColType = new Color[]
        {
            new Color(  0,   0,   0),   // 0 empty
            new Color( 30, 160, 230),   // 1 hull
            new Color( 20, 200,  80),   // 2 power
            new Color( 20, 200, 255),   // 3 thrust
            new Color(160,  40, 220),   // 4 gyro
            new Color(230, 210,   0),   // 5 weapon
        };

        private static readonly Dictionary<string, int> _wcBuf = new Dictionary<string, int>();

        // ── Block classification ───────────────────────────────────────────────

        public static byte BlockTypeOf(IMySlimBlock slim, WcApi wc = null)
        {
            var fat = slim.FatBlock;
            if (fat == null) return 1;

            if (wc != null && wc.IsReady)
            {
                var tb = fat as IMyTerminalBlock;
                if (tb != null)
                {
                    _wcBuf.Clear();
                    if (wc.GetBlockWeaponMap(tb, _wcBuf)) return 5;
                }
            }

            if (fat is IMyLargeTurretBase)         return 5;
            if (fat is IMyUserControllableGun)     return 5;
            if (fat is IMyGyro)                    return 4;
            if (fat is IMyThrust)                  return 3;
            if (fat is IMyPowerProducer)            return 2;

            return 1;
        }

        // ── Zone configuration ────────────────────────────────────────────────

        public static void ParseZones(IMyCubeBlock block,
            out float z1Frac, out float z2Frac)
        {
            z1Frac = 1f / 3f;
            z2Frac = 2f / 3f;

            var tb = block as IMyTerminalBlock;
            if (tb == null) return;

            int idx = tb.CustomData.IndexOf("[TT_ZONES=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return;
            int eq  = tb.CustomData.IndexOf('=', idx);
            int end = tb.CustomData.IndexOf(']', idx);
            if (eq < 0 || end <= eq) return;

            var parts = tb.CustomData.Substring(eq + 1, end - eq - 1).Split(',');
            if (parts.Length < 2) return;

            float v1, v2;
            if (float.TryParse(parts[0].Trim(), out v1)
             && float.TryParse(parts[1].Trim(), out v2))
            {
                z1Frac = Math.Max(0f, Math.Min(1f, v1 / 100f));
                z2Frac = Math.Max(0f, Math.Min(1f, v2 / 100f));
            }
        }

        // ── Scanline fill ─────────────────────────────────────────────────────

        public static void ScanlineFill(byte[,] map, int cols, int rows)
        {
            for (int c = 0; c < cols; c++)
            {
                int first = -1, last = -1;
                for (int r = 0; r < rows; r++)
                {
                    if (map[c, r] == 0) continue;
                    if (first < 0) first = r;
                    last = r;
                }
                if (first < 0) continue;
                for (int r = first; r <= last; r++)
                    if (map[c, r] == 0) map[c, r] = 1;
            }
        }

        public static void ScanlineFillByRow(byte[,] map, int cols, int rows)
        {
            for (int r = 0; r < rows; r++)
            {
                int first = -1, last = -1;
                for (int c = 0; c < cols; c++)
                {
                    if (map[c, r] == 0) continue;
                    if (first < 0) first = c;
                    last = c;
                }
                if (first < 0) continue;
                for (int c = first; c <= last; c++)
                    if (map[c, r] == 0) map[c, r] = 1;
            }
        }

        // ── Cell colour ───────────────────────────────────────────────────────

        public static Color CellColor(byte type, int dens, int maxDens, int dmg)
        {
            if (dmg >= 2) return ColCritical;
            if (dmg >= 1) return ColDamaged;
            if (type == 1)
            {
                float t = maxDens > 0 ? (float)dens / maxDens : 0f;
                return LerpColor(ColHullMin, ColHullMax, t);
            }
            if (type < ColType.Length) return ColType[type];
            return ColType[1];
        }

        public static Color LerpColor(Color a, Color b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            return new Color(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        // ── Map renderer ──────────────────────────────────────────────────────

        public static void RenderMap(
            MySpriteDrawFrame frame,
            float panelX, float panelY, float panelW, float panelH,
            byte[,] typeMap, int[,] densMap, int[,] dmgMap,
            int maxDens, int cols, int rows,
            out float originX, out float originY, out float cell)
        {
            originX = panelX;
            originY = panelY;
            cell    = 0f;

            if (cols <= 0 || rows <= 0 || panelH <= 4f) return;

            Rect(frame, panelX + panelW * 0.5f, panelY + panelH * 0.5f,
                panelW, panelH, ColMapBorder);
            Rect(frame, panelX + panelW * 0.5f, panelY + panelH * 0.5f,
                panelW - 2f, panelH - 2f, ColMapBg);

            float innerW = panelW - 4f;
            float innerH = panelH - 4f;
            cell = Math.Min(innerW / cols, innerH / rows);
            if (cell < 0.5f) return;

            originX = panelX + 2f + (innerW - cell * cols) * 0.5f;
            originY = panelY + 2f + (innerH - cell * rows) * 0.5f;

            for (int r = 0; r < rows; r++)
            {
                float rowY   = originY + r * cell + cell * 0.5f;
                int   runS   = -1;
                Color runCol = new Color(0, 0, 0, 0);

                for (int c = 0; c <= cols; c++)
                {
                    byte  t;
                    Color col;
                    if (c < cols)
                    {
                        t   = typeMap[c, r];
                        col = t > 0
                            ? CellColor(t, densMap[c, r], maxDens, dmgMap[c, r])
                            : new Color(0, 0, 0, 0);
                    }
                    else { t = 0; col = new Color(0, 0, 0, 0); }

                    if (runS >= 0 && (t == 0 || col != runCol))
                    {
                        float runW  = (c - runS) * cell;
                        float runCX = originX + runS * cell + runW * 0.5f;
                        Rect(frame, runCX, rowY, runW, cell, runCol);
                        runS = -1;
                    }
                    if (t > 0 && runS < 0) { runS = c; runCol = col; }
                }
            }
        }

        // ── Legend ────────────────────────────────────────────────────────────

        public static float DrawLegend(MySpriteDrawFrame frame,
            float x, float y, float w, float lineH)
        {
            float dot  = 5f;
            float pad  = 3f;
            float lblW = 68f;
            float row2 = y + lineH + 3f;
            float iw   = w - lblW;
            float q4   = iw / 4f;

            TXT(frame, x, y, "INTEGRITY", ColDim, FS_XS, TextAlignment.LEFT);
            DrawLegendItem(frame, x + lblW + q4 * 0f, y, dot, pad, "HULL",     ColHullMax);
            DrawLegendItem(frame, x + lblW + q4 * 1f, y, dot, pad, "DAMAGED",  ColDamaged);
            DrawLegendItem(frame, x + lblW + q4 * 2f, y, dot, pad, "CRITICAL", ColCritical);

            TXT(frame, x, row2, "SYSTEMS", ColDim, FS_XS, TextAlignment.LEFT);
            DrawLegendItem(frame, x + lblW + q4 * 0f, row2, dot, pad, "WEAPON", ColType[5]);
            DrawLegendItem(frame, x + lblW + q4 * 1f, row2, dot, pad, "POWER",  ColType[2]);
            DrawLegendItem(frame, x + lblW + q4 * 2f, row2, dot, pad, "GYRO",   ColType[4]);
            DrawLegendItem(frame, x + lblW + q4 * 3f, row2, dot, pad, "THRUST", ColType[3]);

            return lineH * 2f + 3f;
        }

        private static void DrawLegendItem(MySpriteDrawFrame frame,
            float x, float y, float dot, float pad, string label, Color color)
        {
            Rect(frame, x + dot * 0.5f, y + dot * 0.5f, dot, dot, color);
            TXT(frame, x + dot + pad, y, label, color, FS_XS, TextAlignment.LEFT);
        }

        // ── Sprite primitives ─────────────────────────────────────────────────

        public static void TXT(MySpriteDrawFrame frame, float x, float y,
            string text, Color color, float scale, TextAlignment align)
        {
            frame.Add(new MySprite(SpriteType.TEXT, text,
                new Vector2(x, y), Vector2.Zero, color, FONT, align, scale));
        }

        public static void HLine(MySpriteDrawFrame frame,
            float cx, float y, float w, Color color)
        {
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                new Vector2(cx, y), new Vector2(w, 1.5f), color));
        }

        public static void Rect(MySpriteDrawFrame frame,
            float cx, float cy, float w, float h, Color color)
        {
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                new Vector2(cx, cy), new Vector2(w, h), color));
        }

        public static string Trunc(string s, int max)
            => s == null ? "" : s.Length <= max ? s : s.Substring(0, max - 1) + "~";
    }
}
