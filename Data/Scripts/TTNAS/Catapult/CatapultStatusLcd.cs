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
    // CatapultStatusLcd — TSS showing status of every catapult on the grid.
    //
    // Script name : TTRND_CatapultStatus
    // Display name: "TT Catapult Status"
    // ───────────────────────────────────────────────────────────────────────────
    [MyTextSurfaceScript("TTRND_CatapultStatus", "TT Catapult Status")]
    public class CatapultStatusLcd : MyTSSCommon
    {
        private readonly IMyTextSurface _surface;
        private readonly IMyCubeBlock   _block;

        private const string FONT     = "Debug";
        private const float  FS       = 0.55f;
        private const float  FS_SUB   = 0.46f;
        private const float  FS_XS    = 0.40f;
        private const float  LINE_PAD = 1f;
        private const float  MARGIN   = 10f;

        private static readonly Color ColBg     = new Color(  3,   8,  18);
        private static readonly Color ColRowBg  = new Color(  0,  20,  42);
        private static readonly Color ColHeader = new Color(  0, 200, 255);
        private static readonly Color ColOk     = new Color(  0, 220, 160);
        private static readonly Color ColWarn   = new Color(255, 140,   0);
        private static readonly Color ColError  = new Color(255,  70,  70);
        private static readonly Color ColLabel  = new Color(110, 130, 150);
        private static readonly Color ColValue  = new Color(200, 215, 230);
        private static readonly Color ColDim    = new Color( 70,  90, 110);
        private static readonly Color ColSep    = new Color(  0,  45,  80);
        private static readonly Color ColDiv    = new Color(  0,  60, 100);
        private static readonly Color ColFooter = new Color( 70,  90, 110);

        private readonly List<CatapultBlock> _cats = new List<CatapultBlock>();

        public CatapultStatusLcd(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _surface = surface;
            _block   = block;
        }

        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update10;

        public override void Run()
        {
            try { Draw(); }
            catch { }
        }

        private void Draw()
        {
            _surface.ScriptBackgroundColor = ColBg;

            var vp = new RectangleF(
                (_surface.TextureSize - _surface.SurfaceSize) / 2f,
                _surface.SurfaceSize);

            float charH    = _surface.MeasureStringInPixels(new StringBuilder("W"), FONT, FS).Y     + LINE_PAD;
            float subCharH = _surface.MeasureStringInPixels(new StringBuilder("W"), FONT, FS_SUB).Y + LINE_PAD;
            float xsCharH  = _surface.MeasureStringInPixels(new StringBuilder("W"), FONT, FS_XS).Y  + LINE_PAD;

            var   frame = _surface.DrawFrame();
            float curY  = vp.Y + MARGIN;

            Rect(frame, vp.X + vp.Width * 0.5f, vp.Y + vp.Height * 0.5f, vp.Width, vp.Height, ColBg);

            // ── Header ────────────────────────────────────────────────────────
            TXT(frame, vp.X + MARGIN, curY, "TT CATAPULT STATUS", ColHeader, FS, TextAlignment.LEFT);
            TXT(frame, vp.X + vp.Width - MARGIN, curY,
                DateTime.Now.ToString("HH:mm:ss"), ColHeader, FS, TextAlignment.RIGHT);
            curY += charH;
            HLine(frame, vp, curY, ColHeader); curY += 5f;

            // ── Grid name ─────────────────────────────────────────────────────
            string gridName = _block.CubeGrid?.DisplayName ?? "Unknown Grid";
            Rect(frame, vp.X + vp.Width * 0.5f, curY + subCharH * 0.5f,
                vp.Width - MARGIN * 2f, subCharH + 2f, ColRowBg);
            TXT(frame, vp.X + MARGIN + 6f, curY, gridName, ColValue, FS_SUB, TextAlignment.LEFT);
            curY += subCharH + 2f;
            HLine(frame, vp, curY, ColSep); curY += 5f;

            // ── Collect catapults ─────────────────────────────────────────────
            CatapultBlock.GetOnGrid(_block.CubeGrid, _cats);

            float fullW     = vp.Width - MARGIN * 2f;
            float leftX     = vp.X + MARGIN;
            float rightEdge = leftX + fullW;

            if (_cats.Count == 0)
            {
                TXT(frame, leftX, curY, "No catapults on this grid.", ColDim, FS_SUB, TextAlignment.LEFT);
                DrawFooter(frame, vp);
                frame.Dispose();
                return;
            }

            // ── Summary bar ───────────────────────────────────────────────────
            int nArmed = 0, nReady = 0, nLaunching = 0;
            for (int i = 0; i < _cats.Count; i++)
            {
                var c = _cats[i];
                if (c.IsArmed)            nArmed++;
                if (c.Status == "READY")  nReady++;
                if (c.IsLaunching)        nLaunching++;
            }

            float q3 = fullW / 3f;
            Rect(frame, leftX + fullW * 0.5f, curY + subCharH * 0.5f, fullW, subCharH + 2f, ColRowBg);

            TXT(frame, leftX + q3 * 0.5f,           curY,
                nArmed.ToString()     + " ARMED",
                nArmed     > 0 ? ColHeader : ColDim, FS_SUB, TextAlignment.CENTER);
            TXT(frame, leftX + q3 + q3 * 0.5f,      curY,
                nReady.ToString()     + " READY",
                nReady     > 0 ? ColOk     : ColDim, FS_SUB, TextAlignment.CENTER);
            TXT(frame, leftX + q3 * 2f + q3 * 0.5f, curY,
                nLaunching.ToString() + " LAUNCHING",
                nLaunching > 0 ? ColWarn   : ColDim, FS_SUB, TextAlignment.CENTER);

            curY += subCharH + 2f;
            HLine(frame, vp, curY, ColSep); curY += 4f;

            // ── Column headers ────────────────────────────────────────────────
            float nameW = fullW * 0.40f;

            TXT(frame, leftX + 6f,    curY, "CATAPULT", ColLabel, FS_XS, TextAlignment.LEFT);
            TXT(frame, leftX + nameW, curY, "STATUS",   ColLabel, FS_XS, TextAlignment.LEFT);
            TXT(frame, rightEdge,     curY, "AIRCRAFT",  ColLabel, FS_XS, TextAlignment.RIGHT);
            curY += xsCharH + 2f;
            HLine(frame, vp, curY, ColDiv); curY += 4f;

            // ── Catapult rows ─────────────────────────────────────────────────
            for (int i = 0; i < _cats.Count; i++)
            {
                var cat = _cats[i];

                Color  nameCol   = cat.IsArmed ? ColValue : ColDim;
                Color  statusCol = StatusColor(cat.Status);
                string statusStr = FormatStatus(cat);
                string aircraft  = cat.AircraftName ?? "--";
                Color  acCol     = cat.HasAircraft ? ColValue : ColDim;

                Rect(frame, leftX + fullW * 0.5f, curY + subCharH * 0.5f,
                    fullW, subCharH + 1f, ColRowBg);

                TXT(frame, leftX + 6f,    curY, cat.BlockName, nameCol,   FS_SUB, TextAlignment.LEFT);
                TXT(frame, leftX + nameW, curY, statusStr,     statusCol, FS_SUB, TextAlignment.LEFT);
                TXT(frame, rightEdge,     curY, aircraft,      acCol,     FS_SUB, TextAlignment.RIGHT);

                curY += subCharH + 1f;
                Rect(frame, leftX + fullW * 0.5f, curY, fullW, 1.5f, ColSep);
                curY += 3f;
            }

            DrawFooter(frame, vp);
            frame.Dispose();
        }

        private static Color StatusColor(string status)
        {
            switch (status)
            {
                case "READY":         return ColOk;
                case "LAUNCHING":     return ColWarn;
                case "COOLDOWN":      return ColWarn;
                case "TOO FAST":      return ColWarn;
                case "NOT ALIGNED":   return ColWarn;
                case "DISARMED":      return ColDim;
                case "NO AIRCRAFT":   return ColDim;
                case "INIT":          return ColDim;
                case "OFFLINE":       return ColDim;
                case "BLOCK OFFLINE": return ColError;
                case "NO PHYSICS":    return ColError;
                default:              return ColLabel;
            }
        }

        private static string FormatStatus(CatapultBlock cat)
        {
            if (cat.Status == "COOLDOWN")
                return "COOLDOWN " + cat.CooldownSecondsRemaining.ToString("0.0") + "s";
            return cat.Status;
        }

        private void DrawFooter(MySpriteDrawFrame frame, RectangleF vp)
        {
            float y = vp.Y + vp.Height
                - _surface.MeasureStringInPixels(new StringBuilder("W"), FONT, FS_SUB).Y
                - MARGIN - 4f;
            HLine(frame, vp, y, ColHeader); y += 4f;
            TXT(frame, vp.X + MARGIN, y, "Terran Titans Naval", ColFooter, FS_SUB, TextAlignment.LEFT);
        }

        private static void TXT(MySpriteDrawFrame frame, float x, float y,
            string text, Color color, float scale, TextAlignment align)
        {
            frame.Add(new MySprite(SpriteType.TEXT, text,
                new Vector2(x, y), Vector2.Zero, color, FONT, align, scale));
        }

        private static void HLine(MySpriteDrawFrame frame, RectangleF vp, float y, Color color)
        {
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                new Vector2(vp.X + vp.Width * 0.5f, y),
                new Vector2(vp.Width - MARGIN * 2f, 1.5f), color));
        }

        private static void Rect(MySpriteDrawFrame frame,
            float cx, float cy, float w, float h, Color color)
        {
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                new Vector2(cx, cy), new Vector2(w, h), color));
        }
    }
}
