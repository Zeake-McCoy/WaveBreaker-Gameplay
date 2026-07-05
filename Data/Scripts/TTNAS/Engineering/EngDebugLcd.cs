using System;
using System.Text;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace TTNAS
{
    [MyTextSurfaceScript("TTNB_EngDebug", "TT ENG \u2014 Debug")]
    public class EngDebugLcd : MyTSSCommon
    {
        private readonly IMyTextSurface _surface;
        private readonly IMyCubeBlock   _block;

        private const string FONT     = "Debug";
        private const float  FS       = 0.55f;
        private const float  FS_SUB   = 0.46f;
        private const float  LINE_PAD = 1f;
        private const float  MARGIN   = 10f;
        private const float  COL_GAP  = 6f;

        private static readonly Color ColBg      = new Color(  3,   8,  18);
        private static readonly Color ColRowBg   = new Color(  0,  20,  42);
        private static readonly Color ColHeader  = new Color(  0, 200, 255);
        private static readonly Color ColSection = new Color(  0, 170, 210);
        private static readonly Color ColOk      = new Color(  0, 220, 160);
        private static readonly Color ColWarn    = new Color(255, 140,   0);
        private static readonly Color ColError   = new Color(255,  70,  70);
        private static readonly Color ColLabel   = new Color(110, 130, 150);
        private static readonly Color ColValue   = new Color(200, 215, 230);
        private static readonly Color ColDim     = new Color( 70,  90, 110);
        private static readonly Color ColSep     = new Color(  0,  45,  80);
        private static readonly Color ColDiv     = new Color(  0,  60, 100);
        private static readonly Color ColFooter  = new Color( 70,  90, 110);

        public EngDebugLcd(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
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

            var   frame = _surface.DrawFrame();
            float curY  = vp.Y + MARGIN;

            Rect(frame, vp.X + vp.Width * 0.5f, vp.Y + vp.Height * 0.5f,
                vp.Width, vp.Height, ColBg);

            // ── Header ────────────────────────────────────────────────────────
            TXT(frame, vp.X + MARGIN, curY, "TT ENGINEERING DEBUG", ColHeader, FS, TextAlignment.LEFT);
            TXT(frame, vp.X + vp.Width - MARGIN, curY,
                DateTime.Now.ToString("HH:mm:ss"), ColHeader, FS, TextAlignment.RIGHT);
            curY += charH;
            FullHLine(frame, vp, curY, ColHeader); curY += 5f;

            var eng = EngineeringProcessor.GetForGrid(_block.CubeGrid);

            string gridName = eng != null ? eng.GridName : "No Engineering Processor on grid";
            Color  nameCol  = eng != null ? ColValue : ColError;
            Rect(frame, vp.X + vp.Width * 0.5f, curY + subCharH * 0.5f,
                vp.Width - MARGIN * 2f, subCharH + 2f, ColRowBg);
            TXT(frame, vp.X + MARGIN + 6f, curY, gridName, nameCol, FS_SUB, TextAlignment.LEFT);
            curY += subCharH + 2f;
            FullHLine(frame, vp, curY, ColSep); curY += 5f;

            float colW   = (vp.Width - MARGIN * 2f - COL_GAP) * 0.5f;
            float leftX  = vp.X + MARGIN;
            float rightX = leftX + colW + COL_GAP;

            float leftY  = curY;
            float rightY = curY;

            if (eng == null)
            {
                DrawFooter(frame, vp, curY);
                frame.Dispose();
                return;
            }

            // ── Left: SYSTEM ──────────────────────────────────────────────────
            DrawSection(frame, leftX, ref leftY, charH, "System");
            ColRow(frame, leftX, colW, ref leftY, charH,
                "Processor",  eng.IsOnline ? "ONLINE" : "OFFLINE",
                ColLabel,     eng.IsOnline ? ColOk : ColError);
            ColRow(frame, leftX, colW, ref leftY, charH,
                "WeaponCore", NAS_Session.I?.WcReady    == true ? "READY" : "NOT READY",
                ColLabel,     NAS_Session.I?.WcReady    == true ? ColOk   : ColWarn);
            ColRow(frame, leftX, colW, ref leftY, charH,
                "WaterMod",   NAS_Session.I?.WaterReady == true ? "READY" : "NOT READY",
                ColLabel,     NAS_Session.I?.WaterReady == true ? ColOk   : ColWarn);
            DrawDivider(frame, leftX, colW, ref leftY);

            // ── Left: POWER ───────────────────────────────────────────────────
            DrawSection(frame, leftX, ref leftY, charH, "Power");
            float balance = eng.PowerOutputMW - eng.PowerDrawMW;
            ColRow(frame, leftX, colW, ref leftY, charH,
                "Output",    eng.PowerOutputMW.ToString("F1") + " MW", ColLabel, ColValue);
            ColRow(frame, leftX, colW, ref leftY, charH,
                "Draw",      eng.PowerDrawMW.ToString("F1")   + " MW", ColLabel, ColValue);
            ColRow(frame, leftX, colW, ref leftY, charH,
                "Balance",   (balance >= 0f ? "+" : "") + balance.ToString("F1") + " MW",
                ColLabel,    balance >= 0f ? ColOk : ColError);
            ColRow(frame, leftX, colW, ref leftY, charH,
                "Reactors",  eng.ReactorFunctional + "/" + eng.ReactorTotal,
                ColLabel,    eng.ReactorFunctional == eng.ReactorTotal ? ColOk : ColWarn);
            ColRow(frame, leftX, colW, ref leftY, charH,
                "Batteries", eng.BatteryTotal + " (" + (eng.BatteryChargePct * 100f).ToString("F0") + "%)",
                ColLabel,    eng.BatteryChargePct > 0.2f ? ColValue : ColWarn);
            DrawDivider(frame, leftX, colW, ref leftY);

            // ── Left: WEAPONS ─────────────────────────────────────────────────
            DrawSection(frame, leftX, ref leftY, charH, "Weapons");
            ColRow(frame, leftX, colW, ref leftY, charH,
                "Detection",  eng.WcDetected ? "WeaponCore" : "Vanilla",
                ColLabel,     eng.WcDetected ? ColOk : ColDim);
            ColRow(frame, leftX, colW, ref leftY, charH,
                "Functional", eng.WeaponFunctional + "/" + eng.WeaponTotal,
                ColLabel,     eng.WeaponFunctional == eng.WeaponTotal ? ColOk : ColWarn);
            DrawDivider(frame, leftX, colW, ref leftY);

            // ── Right: GRID ───────────────────────────────────────────────────
            DrawSection(frame, rightX, ref rightY, charH, "Grid");
            ColRow(frame, rightX, colW, ref rightY, charH,
                "Mass",   FormatMass(eng.MassKg),               ColLabel, ColValue);
            ColRow(frame, rightX, colW, ref rightY, charH,
                "Speed",  eng.SpeedMs.ToString("F1") + " m/s",  ColLabel, ColValue);
            ColRow(frame, rightX, colW, ref rightY, charH,
                "Blocks", eng.BlockCount.ToString(),             ColLabel, ColValue);
            DrawDivider(frame, rightX, colW, ref rightY);

            // ── Right: PROPULSION ─────────────────────────────────────────────
            DrawSection(frame, rightX, ref rightY, charH, "Propulsion");
            ColRow(frame, rightX, colW, ref rightY, charH,
                "Main (rear)",
                FormatThrust(eng.ThrMainAvailN) + " / " + FormatThrust(eng.ThrMainTotalN),
                ColLabel,
                eng.ThrMainEnabled == eng.ThrMainTotal ? ColOk : ColWarn);
            ColRow(frame, rightX, colW, ref rightY, charH,
                "Brake (fwd)",
                FormatThrust(eng.ThrBrakeAvailN) + " / " + FormatThrust(eng.ThrBrakeTotalN),
                ColLabel,
                eng.ThrBrakeEnabled == eng.ThrBrakeTotal ? ColOk : ColWarn);
            ColRow(frame, rightX, colW, ref rightY, charH,
                "Maneuver",
                FormatThrust(eng.ThrMnvrAvailN) + " / " + FormatThrust(eng.ThrMnvrTotalN),
                ColLabel,
                eng.ThrMnvrEnabled == eng.ThrMnvrTotal ? ColOk : ColWarn);
            ColRow(frame, rightX, colW, ref rightY, charH,
                "Gyroscopes", eng.GyroFunctional + "/" + eng.GyroTotal + " ok",
                ColLabel,     eng.GyroFunctional == eng.GyroTotal      ? ColOk : ColWarn);
            DrawDivider(frame, rightX, colW, ref rightY);

            // ── Right: DAMAGE ─────────────────────────────────────────────────
            DrawSection(frame, rightX, ref rightY, charH, "Damage");
            float hullPct = eng.HullIntegrityPct * 100f;
            Color hullCol = hullPct >= 80f ? ColOk : hullPct >= 50f ? ColWarn : ColError;
            ColRow(frame, rightX, colW, ref rightY, charH,
                "Hull",       hullPct.ToString("F1") + "%", ColLabel, hullCol);
            ColRow(frame, rightX, colW, ref rightY, charH,
                "Damaged",    eng.BlockDamaged.ToString(),
                ColLabel,     eng.BlockDamaged  == 0 ? ColDim : ColWarn);
            ColRow(frame, rightX, colW, ref rightY, charH,
                "Critical",   eng.BlockCritical.ToString(),
                ColLabel,     eng.BlockCritical == 0 ? ColDim : ColError);
            ColRow(frame, rightX, colW, ref rightY, charH,
                "Under Attack", eng.UnderAttack ? "YES" : "NO",
                ColLabel,       eng.UnderAttack  ? ColError : ColDim);
            DrawDivider(frame, rightX, colW, ref rightY);

            // ── Vertical divider ──────────────────────────────────────────────
            float divTop    = curY;
            float divBottom = Math.Max(leftY, rightY);
            float divX      = leftX + colW + COL_GAP * 0.5f;
            Rect(frame, divX, (divTop + divBottom) * 0.5f, 1.5f, divBottom - divTop, ColDiv);

            float bodyBottom = divBottom;

            // ── Water — full width (optional) ─────────────────────────────────
            if (eng.WaterAvailable)
            {
                float wy    = bodyBottom;
                float fullW = vp.Width - MARGIN * 2f;
                DrawSection(frame, leftX, ref wy, charH, "Water");
                float subPct = eng.SubmersionPct * 100f;
                Color subCol = subPct > 80f ? ColError : subPct > 10f ? ColWarn : ColDim;
                ColRow(frame, leftX, fullW, ref wy, charH,
                    "Submersion", subPct.ToString("F0") + "%",               ColLabel, subCol);
                ColRow(frame, leftX, fullW, ref wy, charH,
                    "Depth",      eng.FluidDepthM.ToString("F1") + " m",     ColLabel, ColValue);
                ColRow(frame, leftX, fullW, ref wy, charH,
                    "Current",    eng.WaterVelocity.Length().ToString("F1") + " m/s", ColLabel, ColValue);
                DrawDivider(frame, leftX, fullW, ref wy);
                bodyBottom = wy;
            }

            DrawFooter(frame, vp, bodyBottom);
            frame.Dispose();
        }

        private void DrawSection(MySpriteDrawFrame frame, float x,
            ref float y, float charH, string title)
        {
            TXT(frame, x, y, title.ToUpper(), ColSection, FS, TextAlignment.LEFT);
            y += charH + 1f;
        }

        private void ColRow(MySpriteDrawFrame frame, float x, float w,
            ref float y, float charH,
            string label, string value, Color labelCol, Color valueCol)
        {
            Rect(frame, x + w * 0.5f, y + charH * 0.5f, w, charH + 1f, ColRowBg);
            TXT(frame, x + 6f, y, label, labelCol, FS_SUB, TextAlignment.LEFT);
            TXT(frame, x + w,  y, value, valueCol, FS_SUB, TextAlignment.RIGHT);
            y += charH + 1f;
            Rect(frame, x + w * 0.5f, y, w, 1.5f, ColSep);
            y += 3f;
        }

        private static void DrawDivider(MySpriteDrawFrame frame, float x, float w, ref float y)
        {
            y += 3f;
            Rect(frame, x + w * 0.5f, y, w, 1.5f, ColDiv);
            y += 5f;
        }

        private void DrawFooter(MySpriteDrawFrame frame, RectangleF vp, float bodyBottom)
        {
            float y = vp.Y + vp.Height
                - _surface.MeasureStringInPixels(new StringBuilder("W"), FONT, FS_SUB).Y
                - MARGIN - 4f;
            FullHLine(frame, vp, y, ColHeader); y += 4f;
            TXT(frame, vp.X + MARGIN, y, "Terran Titans Naval Advanced Systems", ColFooter, FS_SUB, TextAlignment.LEFT);
        }

        private static void TXT(MySpriteDrawFrame frame, float x, float y,
            string text, Color color, float scale, TextAlignment align)
        {
            frame.Add(new MySprite(SpriteType.TEXT, text,
                new Vector2(x, y), Vector2.Zero, color, FONT, align, scale));
        }

        private static void FullHLine(MySpriteDrawFrame frame, RectangleF vp, float y, Color color)
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

        private static string FormatMass(float kg)
        {
            if (kg >= 1000000f) return (kg / 1000000f).ToString("F1") + " Mt";
            if (kg >= 1000f)    return (kg / 1000f).ToString("F1")    + " t";
            return kg.ToString("F0") + " kg";
        }

        private static string FormatThrust(float n)
        {
            if (n >= 1000000f) return (n / 1000000f).ToString("F1") + " MN";
            if (n >= 1000f)    return (n / 1000f).ToString("F1")    + " kN";
            return n.ToString("F0") + " N";
        }
    }
}
