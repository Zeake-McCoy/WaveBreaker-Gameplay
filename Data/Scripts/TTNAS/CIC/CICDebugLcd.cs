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
    [MyTextSurfaceScript("TTNW_Debug", "TT CIC \u2014 Debug")]
    public class CICDebugLcd : MyTSSCommon
    {
        private readonly IMyTextSurface _surface;
        private readonly IMyCubeBlock   _block;

        private const string FONT     = "Debug";
        private const float  FS       = 0.55f;
        private const float  FS_SUB   = 0.46f;
        private const float  LINE_PAD = 1f;
        private const float  MARGIN   = 10f;

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
        private static readonly Color ColFooter  = new Color( 70,  90, 110);

        public CICDebugLcd(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
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

            float charH = _surface.MeasureStringInPixels(
                new StringBuilder("W"), FONT, FS).Y + LINE_PAD;

            var   frame = _surface.DrawFrame();
            float curY  = vp.Y + MARGIN;

            Rect(frame, vp.X + vp.Width * 0.5f, vp.Y + vp.Height * 0.5f,
                vp.Width, vp.Height, ColBg);

            // ── Header ────────────────────────────────────────────────────────
            TXT(frame, vp.X + MARGIN, curY, "TT CIC DEBUG", ColHeader, FS, TextAlignment.LEFT);
            TXT(frame, vp.X + vp.Width - MARGIN, curY,
                DateTime.Now.ToString("HH:mm:ss"), ColHeader, FS, TextAlignment.RIGHT);
            curY += charH;
            HLine(frame, vp, curY, ColHeader); curY += 5f;

            var cic   = CICProcessor.GetForGrid(_block.CubeGrid);
            var cache = FCGridCache.I?.PeekCache(_block.CubeGrid);

            // ── System APIs ───────────────────────────────────────────────────
            Section(frame, vp, ref curY, charH, "System");
            Row(frame, vp, ref curY, charH,
                "CIC",
                cic != null ? (cic.IsOnline ? "ONLINE" : "OFFLINE") : "NULL",
                ColLabel,
                cic?.IsOnline == true ? ColOk : ColError);
            Row(frame, vp, ref curY, charH,
                "WeaponCore",
                FCGridCache.I?.WcApiReady == true ? "READY" : "NOT READY",
                ColLabel,
                FCGridCache.I?.WcApiReady == true ? ColOk : ColWarn);
            curY += 3f;
            HLine(frame, vp, curY, ColSep); curY += 5f;

            if (cic == null || cache == null)
            {
                Row(frame, vp, ref curY, charH, "CIC / Cache", "unavailable", ColLabel, ColError);
                DrawFooter(frame, vp, curY);
                frame.Dispose();
                return;
            }

            // ── Grid Cache ────────────────────────────────────────────────────
            Section(frame, vp, ref curY, charH, "Grid Cache");
            Row(frame, vp, ref curY, charH,
                "Weapons",
                cache.AllWeapons.Count + "  (" + cache.StaticLaunchers.Count + " static / " + cache.Turrets.Count + " turret)",
                ColLabel, ColValue);
            Row(frame, vp, ref curY, charH,
                "Threats",    cache.Threats.Count.ToString(),
                ColLabel, cache.Threats.Count > 0 ? ColError : ColDim);
            Row(frame, vp, ref curY, charH,
                "Friendlies", cache.Friendlies.Count.ToString(),
                ColLabel, cache.Friendlies.Count > 0 ? ColOk : ColDim);
            Row(frame, vp, ref curY, charH,
                "Monitors",   cache.MonitoredWeaponCount.ToString(),
                ColLabel, cache.MonitoredWeaponCount > 0 ? ColOk : ColWarn);
            Row(frame, vp, ref curY, charH,
                "Projectiles", cache.OurProjectileCount.ToString(),
                ColLabel, cache.OurProjectileCount > 0 ? ColOk : ColDim);
            curY += 3f;
            HLine(frame, vp, curY, ColSep); curY += 5f;

            // ── Missile Lists ─────────────────────────────────────────────────
            Section(frame, vp, ref curY, charH, "Missiles");
            Row(frame, vp, ref curY, charH,
                "Inbound (locked)",  cic.MissilePositions.Count.ToString(),
                ColLabel, cic.MissilePositions.Count > 0 ? ColError : ColDim);
            Row(frame, vp, ref curY, charH,
                "Outbound (ours)",   cic.OutboundPositions.Count.ToString(),
                ColLabel, cic.OutboundPositions.Count > 0 ? ColOk : ColDim);
            Row(frame, vp, ref curY, charH,
                "Friendly missiles", cic.FriendlyMissilePositions.Count.ToString(),
                ColLabel, cic.FriendlyMissilePositions.Count > 0 ? ColOk : ColDim);
            Row(frame, vp, ref curY, charH,
                "Hostile other",     cic.HostileOtherPositions.Count.ToString(),
                ColLabel, cic.HostileOtherPositions.Count > 0 ? ColWarn : ColDim);

            // ── Inbound detail ────────────────────────────────────────────────
            var mPos = cic.MissilePositions;
            var mVel = cic.MissileVelocities;
            if (mPos.Count > 0)
            {
                curY += 3f;
                HLine(frame, vp, curY, ColSep); curY += 5f;
                Section(frame, vp, ref curY, charH, "Inbound Detail");
                int show = Math.Min(mPos.Count, 4);
                for (int i = 0; i < show; i++)
                {
                    float dist = (float)Vector3D.Distance(_block.GetPosition(), mPos[i]);
                    float spd  = i < mVel.Count ? (float)mVel[i].Length() : 0f;
                    Row(frame, vp, ref curY, charH,
                        "  [" + i + "]",
                        (dist / 1000f).ToString("F1") + "km  " + spd.ToString("F0") + "m/s",
                        ColError, ColError);
                }
            }

            // ── Outbound detail ───────────────────────────────────────────────
            var oPos = cic.OutboundPositions;
            var oVel = cic.OutboundVelocities;
            var oFac = cic.OutboundFactionIds;
            if (oPos.Count > 0)
            {
                curY += 3f;
                HLine(frame, vp, curY, ColSep); curY += 5f;
                Section(frame, vp, ref curY, charH, "Outbound Detail");
                int show = Math.Min(oPos.Count, 4);
                for (int i = 0; i < show; i++)
                {
                    float dist = (float)Vector3D.Distance(_block.GetPosition(), oPos[i]);
                    float spd  = i < oVel.Count ? (float)oVel[i].Length() : 0f;
                    long  fid  = i < oFac.Count ? oFac[i] : 0;
                    Row(frame, vp, ref curY, charH,
                        "  [" + i + "]",
                        (dist / 1000f).ToString("F1") + "km  " + spd.ToString("F0") + "m/s  fac=" + fid,
                        ColOk, ColOk);
                }
            }

            DrawFooter(frame, vp, vp.Y + vp.Height - _surface.MeasureStringInPixels(
                new StringBuilder("W"), FONT, FS_SUB).Y - MARGIN - 4f);

            frame.Dispose();
        }

        private void Section(MySpriteDrawFrame frame, RectangleF vp,
            ref float curY, float charH, string title)
        {
            TXT(frame, vp.X + MARGIN, curY, title.ToUpper(), ColSection, FS, TextAlignment.LEFT);
            curY += charH + 1f;
        }

        private void Row(MySpriteDrawFrame frame, RectangleF vp,
            ref float curY, float charH,
            string label, string value, Color labelCol, Color valueCol)
        {
            Rect(frame,
                vp.X + vp.Width * 0.5f,
                curY + charH * 0.5f,
                vp.Width - MARGIN * 2f, charH + 1f, ColRowBg);

            TXT(frame, vp.X + MARGIN + 6f,       curY, label, labelCol, FS_SUB, TextAlignment.LEFT);
            TXT(frame, vp.X + vp.Width - MARGIN, curY, value, valueCol, FS_SUB, TextAlignment.RIGHT);

            curY += charH + 1f;
            HLine(frame, vp, curY, ColSep);
            curY += 3f;
        }

        private void DrawFooter(MySpriteDrawFrame frame, RectangleF vp, float y)
        {
            HLine(frame, vp, y, ColHeader); y += 4f;
            TXT(frame, vp.X + MARGIN, y, "Terran Titans Naval Advanced Systems", ColFooter, FS_SUB, TextAlignment.LEFT);
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
