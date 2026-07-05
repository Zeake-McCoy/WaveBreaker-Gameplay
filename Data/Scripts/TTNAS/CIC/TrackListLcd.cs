using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace TTNAS
{
    [MyTextSurfaceScript("TT_TrackList", "TT CIC \u2014 Track List")]
    public class TrackListLcd : MyTSSCommon
    {
        private const float  TITLE_H     = 38f;
        private const float  ROW_H       = 52f;
        private const float  MARGIN      = 6f;
        private const string FONT        = "Debug";
        private const float  FONT_NAME   = 0.52f;
        private const float  FONT_DETAIL = 0.40f;
        private const float  FONT_TITLE  = 0.52f;
        private const float  FONT_LABEL  = 0.35f;
        private const float  ICON_SIZE   = 16f;

        private static readonly Color ColBg          = new Color(  8,  10,  14, 255);
        private static readonly Color ColTitleBg     = new Color( 10,  20,  35, 255);
        private static readonly Color ColTitleText   = new Color(  0, 180, 255, 220);
        private static readonly Color ColRowBg       = new Color( 14,  18,  26, 255);
        private static readonly Color ColRowBgAlt    = new Color( 10,  14,  20, 255);
        private static readonly Color ColRowBorder   = new Color( 30,  50,  80,  80);
        private static readonly Color ColHostile     = new Color(255,  60,  60, 255);
        private static readonly Color ColHostileDim  = new Color(120,  20,  20, 180);
        private static readonly Color ColFriendly    = new Color( 60, 180, 255, 255);
        private static readonly Color ColOutbound    = new Color( 80, 255, 120, 255);
        private static readonly Color ColOutboundDim = new Color( 20,  80,  40, 180);
        private static readonly Color ColFriendlyDim = new Color( 20,  60, 120, 180);
        private static readonly Color ColLarge       = new Color(255, 200,  30, 255);
        private static readonly Color ColSmall       = new Color( 60, 220, 220, 255);
        private static readonly Color ColLabel       = new Color(100, 130, 160, 200);
        private static readonly Color ColValue       = new Color(200, 220, 240, 220);
        private static readonly Color ColName        = new Color(230, 235, 245, 240);
        private static readonly Color ColClosing     = new Color(255,  80,  80, 220);
        private static readonly Color ColOpening     = new Color( 80, 200,  80, 200);
        private static readonly Color ColSeparator   = new Color( 30,  50,  80, 120);
        private static readonly Color ColOffline     = new Color(180,  60,  60, 200);
        private static readonly Color ColNoTracks    = new Color( 80, 100, 120, 180);

        private const string INI_SECTION        = "TT Track List";
        private const string INI_FRIENDLIES     = "ShowFriendlies";
        private const string INI_MISSILES       = "ShowMissiles";
        private const string INI_SHOW_OUTBOUND  = "ShowOutbound";
        private const string INI_SHOW_ALL_HOSTILE = "ShowAllHostileMissiles";
        private const string INI_MAX_TRACKS     = "MaxTracks";

        private bool _showFriendlies = true;
        private bool _showMissiles   = true;
        private bool _showOutbound   = true;
        private bool _showAllHostile = true;
        private int  _maxTracks      = 10;

        private readonly MyIni           _ini       = new MyIni();
        private readonly IMyCubeBlock    _block;
        private readonly IMyTerminalBlock _termBlock;

        private struct TrackRow
        {
            public string Name;
            public string FactionTag;
            public float  CoastAlpha;
            public string SizeTag;
            public float  RangeKm;
            public int    Bearing;
            public int    COG;
            public float  SOG;
            public bool   Closing;
            public bool   IsFriendly;
            public bool   IsOutbound;
            public bool   IsLarge;
            public bool   IsMissile;
        }

        private readonly List<TrackRow> _rows = new List<TrackRow>();

        private const float TL_COAST_SECONDS = 2.5f;
        private struct TLCoastEntry { public TrackRow Row; public float SeenAt; }
        private readonly Dictionary<string, TLCoastEntry> _tlCoastMap
            = new Dictionary<string, TLCoastEntry>();
        private float _tlTime = 0f;

        public TrackListLcd(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _block     = block;
            _termBlock = block as IMyTerminalBlock;
        }

        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update10;

        public override void Run()
        {
            try { ReadSettings(); BuildRows(); Draw(); }
            catch { }
        }

        private void ReadSettings()
        {
            string cd = _termBlock?.CustomData ?? "";
            MyIniParseResult result;
            if (!_ini.TryParse(cd, out result)) return;
            if (!_ini.ContainsSection(INI_SECTION)) { WriteDefaultSettings(); return; }

            _showFriendlies = _ini.Get(INI_SECTION, INI_FRIENDLIES).ToBoolean(true);
            _showMissiles   = _ini.Get(INI_SECTION, INI_MISSILES).ToBoolean(true);
            _showOutbound   = _ini.Get(INI_SECTION, INI_SHOW_OUTBOUND).ToBoolean(true);
            _showAllHostile = _ini.Get(INI_SECTION, INI_SHOW_ALL_HOSTILE).ToBoolean(true);
            _maxTracks      = Math.Max(1, Math.Min(
                _ini.Get(INI_SECTION, INI_MAX_TRACKS).ToInt32(10), 20));
        }

        private void WriteDefaultSettings()
        {
            _ini.Set(INI_SECTION, INI_FRIENDLIES, true);
            _ini.SetComment(INI_SECTION, INI_FRIENDLIES, " Show friendly contacts (true/false)");
            _ini.Set(INI_SECTION, INI_MISSILES, true);
            _ini.SetComment(INI_SECTION, INI_MISSILES, " Show inbound missiles (true/false)");
            _ini.Set(INI_SECTION, INI_SHOW_OUTBOUND, true);
            _ini.SetComment(INI_SECTION, INI_SHOW_OUTBOUND, " Show outbound (friendly) missiles");
            _ini.Set(INI_SECTION, INI_SHOW_ALL_HOSTILE, true);
            _ini.SetComment(INI_SECTION, INI_SHOW_ALL_HOSTILE, " Show all hostile missiles (not just inbound)");
            _ini.Set(INI_SECTION, INI_MAX_TRACKS, 10);
            _ini.SetComment(INI_SECTION, INI_MAX_TRACKS, " Maximum track rows shown (1-20)");
            if (_termBlock != null) _termBlock.CustomData = _ini.ToString();
        }

        private void BuildRows()
        {
            _rows.Clear();
            _tlTime += 1f / 6f;
            var cic = CICProcessor.GetForGrid(_block.CubeGrid);
            if (cic == null || !cic.IsOnline) return;

            var wm     = _block.WorldMatrix;
            var origin = _block.GetPosition();

            var filteredThreats = new List<ThreatEntry>();
            foreach (var t in cic.Threats)
                if (t.Entity == null || t.Entity.EntityId >= 0) filteredThreats.Add(t);
            AddEntries(filteredThreats, wm, origin, friendly: false);

            if (_showFriendlies)
            {
                var filteredFriendlies = new List<ThreatEntry>();
                foreach (var f in cic.Friendlies)
                    if (f.Entity == null || f.Entity.EntityId >= 0) filteredFriendlies.Add(f);
                AddEntries(filteredFriendlies, wm, origin, friendly: true);
            }

            const double TL_DEDUP_SQ = 2000.0 * 2000.0;
            var shownMslPos = new List<Vector3D>();

            if (_showOutbound)
            {
                AddMissileEntriesDedup(cic.OutboundPositions, cic.OutboundVelocities,
                    cic.OutboundFactionIds, shownMslPos, TL_DEDUP_SQ, true, true);
                AddMissileEntriesDedup(cic.FriendlyMissilePositions, cic.FriendlyMissileVelocities,
                    cic.FriendlyFactionIds, shownMslPos, TL_DEDUP_SQ, true, false);
            }
            if (_showMissiles)
                AddMissileEntriesDedup(cic.MissilePositions, cic.MissileVelocities,
                    cic.InboundFactionIds, shownMslPos, TL_DEDUP_SQ, false, false);
            if (_showAllHostile)
                AddMissileEntriesDedup(cic.HostileOtherPositions, cic.HostileOtherVelocities,
                    cic.HostileFactionIds, shownMslPos, TL_DEDUP_SQ, false, false);

            _rows.Sort((a, b) => a.RangeKm.CompareTo(b.RangeKm));
            if (_rows.Count > _maxTracks)
                _rows.RemoveRange(_maxTracks, _rows.Count - _maxTracks);
        }

        private void AddEntries(IReadOnlyList<ThreatEntry> list,
            MatrixD wm, Vector3D origin, bool friendly)
        {
            foreach (var entry in list)
            {
                if (entry.Entity == null || entry.Entity.MarkedForClose) continue;

                var entPos  = entry.Entity.PositionComp.GetPosition();
                var delta   = entPos - origin;
                float right   = (float)delta.Dot(wm.Right);
                float forward = (float)delta.Dot(wm.Forward);
                float dist    = (float)Math.Sqrt(right * right + forward * forward);

                int bearing = (int)(Math.Atan2(right, forward) * 180.0 / Math.PI);
                if (bearing < 0) bearing += 360;

                int   cog     = 0;
                bool  closing = false;
                var   phys    = entry.Entity.Physics;
                if (phys != null)
                {
                    var vel = phys.LinearVelocity;
                    float vr = (float)vel.Dot(wm.Right);
                    float vf = (float)vel.Dot(wm.Forward);
                    if (vr * vr + vf * vf > 0.25f)
                    {
                        cog = (int)(Math.Atan2(vr, vf) * 180.0 / Math.PI);
                        if (cog < 0) cog += 360;
                    }
                    var relVel = vel - (_block.Physics?.LinearVelocity ?? vel);
                    closing = (float)relVel.Dot(Vector3D.Normalize(entPos - origin)) < -0.5f;
                }

                string sizeTag;
                bool   isLarge   = false;
                bool   isMissile = false;
                var    tn        = entry.Entity.GetType().Name;
                if (entry.Entity.EntityId < 0 ||
                    tn.IndexOf("Missile",    StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tn.IndexOf("Projectile", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sizeTag  = "MSL"; isMissile = true;
                }
                else
                {
                    var grid = entry.Entity as VRage.Game.ModAPI.IMyCubeGrid;
                    isLarge  = grid != null && grid.GridSizeEnum == MyCubeSize.Large;
                    sizeTag  = isLarge ? "LRG" : "SML";
                }

                string ftag = !string.IsNullOrEmpty(entry.FactionTag)
                    ? "[" + entry.FactionTag + "] " : "";

                _rows.Add(new TrackRow
                {
                    CoastAlpha = 1.0f,
                    Name       = TruncateName(entry.DisplayName, 16),
                    FactionTag = ftag,
                    SizeTag    = sizeTag,
                    RangeKm    = dist / 1000f,
                    Bearing    = bearing,
                    COG        = cog,
                    SOG        = entry.SpeedMs,
                    Closing    = closing,
                    IsFriendly = friendly,
                    IsLarge    = isLarge,
                    IsMissile  = isMissile,
                });
            }
        }

        private void AddMissileEntriesDedup(
            List<Vector3D> positions, List<Vector3D> velocities,
            List<long> factionIds, List<Vector3D> shown, double dedupSq,
            bool isFriendly, bool isOutbound)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                var pos = positions[i];
                bool dup = false;
                foreach (var s in shown)
                    if (Vector3D.DistanceSquared(pos, s) < dedupSq) { dup = true; break; }
                if (dup) continue;
                shown.Add(pos);

                var vel     = (i < velocities.Count) ? velocities[i] : Vector3D.Zero;
                var delta   = pos - _block.GetPosition();
                var wm      = _block.WorldMatrix;
                float right   = (float)delta.Dot(wm.Right);
                float forward = (float)delta.Dot(wm.Forward);
                float dist    = (float)Math.Sqrt(right * right + forward * forward);
                int bearing   = (int)(Math.Atan2(right, forward) * 180.0 / Math.PI);
                if (bearing < 0) bearing += 360;

                int   cog   = bearing;
                float speed = (float)vel.Length();
                if (speed > 1f)
                {
                    float vr = (float)vel.Dot(wm.Right);
                    float vf = (float)vel.Dot(wm.Forward);
                    cog = (int)(Math.Atan2(vr, vf) * 180.0 / Math.PI);
                    if (cog < 0) cog += 360;
                }

                bool closing = speed > 1f &&
                    vel.Dot(Vector3D.Normalize(_block.GetPosition() - pos)) < 0;

                long   facId  = (i < factionIds.Count) ? factionIds[i] : 0;
                string facTag = "";
                if (facId != 0)
                {
                    var fac = MyAPIGateway.Session.Factions.TryGetFactionById(facId);
                    if (fac != null) facTag = "[" + fac.Tag + "] ";
                }
                string mslLabel = isOutbound ? "OUTBOUND MSL"
                                : isFriendly  ? "FRIENDLY MSL"
                                : "HOSTILE MSL";

                _rows.Add(new TrackRow
                {
                    CoastAlpha = 1.0f,
                    Name       = mslLabel,
                    FactionTag = facTag,
                    SizeTag    = "MSL",
                    RangeKm    = dist / 1000f,
                    Bearing    = bearing,
                    COG        = cog,
                    SOG        = speed,
                    Closing    = closing,
                    IsFriendly = isFriendly,
                    IsOutbound = isOutbound,
                    IsLarge    = false,
                    IsMissile  = true,
                });
            }
        }

        private static Color ScaleA(Color c, float t)
        {
            byte a = (byte)(c.A * Math.Max(0f, Math.Min(1f, t)));
            return new Color(c.R, c.G, c.B, a);
        }

        private void Draw()
        {
            m_surface.ScriptBackgroundColor = ColBg;

            var vp = new RectangleF(
                (m_surface.TextureSize - m_surface.SurfaceSize) / 2f,
                m_surface.SurfaceSize);

            float scale  = Math.Min(vp.Width, vp.Height) / 512f;
            float titleH = TITLE_H * scale;
            float rowH   = ROW_H   * scale;

            var frame = m_surface.DrawFrame();

            DrawTitleBar(frame, vp, titleH, scale);

            var cic = CICProcessor.GetForGrid(_block.CubeGrid);
            if (cic == null || !cic.IsOnline)
            {
                DrawCentredText(frame, vp, "CIC OFFLINE", ColOffline, 0.65f * scale);
                frame.Dispose();
                return;
            }

            if (_rows.Count == 0)
            {
                DrawCentredText(frame, vp, "NO CONTACTS", ColNoTracks, 0.55f * scale);
                frame.Dispose();
                return;
            }

            float contentTop = vp.Y + titleH;
            float availH     = vp.Height - titleH;
            int   maxVisible = (int)(availH / rowH);
            int   count      = Math.Min(_rows.Count, maxVisible);

            for (int i = 0; i < count; i++)
                DrawRow(frame, vp, _rows[i], i, contentTop + i * rowH, rowH, scale);

            frame.Dispose();
        }

        private void DrawTitleBar(MySpriteDrawFrame frame, RectangleF vp,
            float titleH, float scale)
        {
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                new Vector2(vp.X + vp.Width * 0.5f, vp.Y + titleH * 0.5f),
                new Vector2(vp.Width, titleH), ColTitleBg));

            var cic      = CICProcessor.GetForGrid(_block.CubeGrid);
            int hostile  = cic?.Threats.Count ?? 0;
            int friendly = _showFriendlies ? (cic?.Friendlies.Count ?? 0) : 0;
            int missiles = _showMissiles ? (cic?.MissileInboundCount ?? 0) : 0;
            string title = missiles > 0
                ? "TRACK LIST   H:" + hostile + "  MSL:" + missiles + "  F:" + friendly
                : "TRACK LIST   H:" + hostile + "  F:" + friendly;

            frame.Add(new MySprite(SpriteType.TEXT, title,
                new Vector2(vp.X + vp.Width * 0.5f, vp.Y + titleH * 0.15f),
                null, ColTitleText, FONT, TextAlignment.CENTER, FONT_TITLE * scale));
        }

        private void DrawRow(MySpriteDrawFrame frame, RectangleF vp,
            TrackRow row, int index, float rowTop, float rowH, float scale)
        {
            float rowCentreY = rowTop + rowH * 0.5f;
            float left       = vp.X + MARGIN * scale;
            float right      = vp.X + vp.Width - MARGIN * scale;
            float width      = right - left;

            var rowBg = index % 2 == 0 ? ColRowBg : ColRowBgAlt;
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                new Vector2(vp.X + vp.Width * 0.5f, rowCentreY),
                new Vector2(vp.Width, rowH - 1f * scale), rowBg));
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                new Vector2(vp.X + vp.Width * 0.5f, rowTop + rowH - 0.5f * scale),
                new Vector2(vp.Width, 1f * scale), ColSeparator));

            float cm = row.CoastAlpha > 0f ? row.CoastAlpha : 1.0f;
            Color mainCol = ScaleA(row.IsOutbound ? ColOutbound
                          : row.IsFriendly ? ColFriendly : ColHostile, cm);
            Color dimCol  = ScaleA(row.IsOutbound ? ColOutboundDim
                          : row.IsFriendly ? ColFriendlyDim : ColHostileDim, cm);
            Color sizeCol = ScaleA(row.IsMissile
                          ? (row.IsOutbound ? ColOutbound : ColHostile)
                          : (row.IsLarge ? ColLarge : ColSmall), cm);

            float lineTop = rowTop + rowH * 0.12f;
            float lineBot = rowTop + rowH * 0.58f;
            float iconX   = left   + ICON_SIZE * scale * 0.6f;

            DrawContactIcon(frame, row, new Vector2(iconX, rowCentreY - rowH * 0.06f), scale, mainCol, dimCol);

            float textLeft = left + ICON_SIZE * scale * 1.6f;

            frame.Add(new MySprite(SpriteType.TEXT, row.SizeTag,
                new Vector2(textLeft, lineTop),
                null, sizeCol, FONT, TextAlignment.LEFT, FONT_LABEL * scale));

            float nameX = textLeft + 28f * scale;
            string displayName = string.IsNullOrEmpty(row.FactionTag)
                ? row.Name
                : row.FactionTag + row.Name;
            frame.Add(new MySprite(SpriteType.TEXT, displayName,
                new Vector2(nameX, lineTop),
                null, ColName, FONT, TextAlignment.LEFT, FONT_NAME * scale));

            string rngStr = row.RangeKm >= 10f
                ? row.RangeKm.ToString("F1") + "km"
                : row.RangeKm.ToString("F2") + "km";
            string brgStr = row.Bearing.ToString("D3") + "°T";

            frame.Add(new MySprite(SpriteType.TEXT, rngStr,
                new Vector2(right - 52f * scale, lineTop),
                null, ColValue, FONT, TextAlignment.LEFT, FONT_DETAIL * scale));
            frame.Add(new MySprite(SpriteType.TEXT, brgStr,
                new Vector2(right - 52f * scale, lineTop + 14f * scale),
                null, ColLabel, FONT, TextAlignment.LEFT, FONT_DETAIL * scale));

            string cogStr = "COG " + row.COG.ToString("D3") + "°";
            string sogStr = row.SOG >= 1f
                ? "SOG " + row.SOG.ToString("F0") + "m/s"
                : "SOG 0m/s";

            frame.Add(new MySprite(SpriteType.TEXT, cogStr,
                new Vector2(textLeft, lineBot),
                null, ColLabel, FONT, TextAlignment.LEFT, FONT_DETAIL * scale));
            frame.Add(new MySprite(SpriteType.TEXT, sogStr,
                new Vector2(textLeft + 60f * scale, lineBot),
                null, ColLabel, FONT, TextAlignment.LEFT, FONT_DETAIL * scale));

            DrawClosingArrow(frame, row.Closing, row.SOG,
                new Vector2(right - 12f * scale, rowCentreY), scale);
            DrawCOGArrow(frame, row.COG, row.Bearing,
                new Vector2(right - 30f * scale, lineBot + 6f * scale), scale);
        }

        private static void DrawContactIcon(MySpriteDrawFrame frame, TrackRow row,
            Vector2 pos, float scale, Color col, Color dim)
        {
            float s = scale * 0.9f;
            if (row.IsMissile)
            {
                frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle",
                    pos + new Vector2(0, -3f * s), new Vector2(9f * s, 9f * s), dim, rotation: 0f));
                frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle",
                    pos + new Vector2(0,  3f * s), new Vector2(9f * s, 9f * s), dim, rotation: MathHelper.Pi));
                frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle",
                    pos + new Vector2(0, -2.5f * s), new Vector2(6f * s, 6f * s), col, rotation: 0f));
                frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle",
                    pos + new Vector2(0,  2.5f * s), new Vector2(6f * s, 6f * s), col, rotation: MathHelper.Pi));
            }
            else if (row.IsLarge)
            {
                frame.Add(new MySprite(SpriteType.TEXTURE, "Circle",
                    pos, new Vector2(13f * s, 13f * s), dim));
                frame.Add(new MySprite(SpriteType.TEXTURE, "Circle",
                    pos, new Vector2( 8f * s,  8f * s), col));
                DrawLine(frame, pos - new Vector2(0, 6f * s), pos - new Vector2(0, 11f * s), 2f * s, col);
            }
            else if (row.IsFriendly)
            {
                frame.Add(new MySprite(SpriteType.TEXTURE, "Circle",
                    pos, new Vector2(12f * s, 12f * s), dim));
                frame.Add(new MySprite(SpriteType.TEXTURE, "Circle",
                    pos, new Vector2( 8f * s,  8f * s), new Color(8, 10, 14, 255)));
                frame.Add(new MySprite(SpriteType.TEXTURE, "Circle",
                    pos, new Vector2( 6f * s,  6f * s), col));
            }
            else
            {
                frame.Add(new MySprite(SpriteType.TEXTURE, "Circle",
                    pos, new Vector2(10f * s, 10f * s), dim));
                frame.Add(new MySprite(SpriteType.TEXTURE, "Circle",
                    pos, new Vector2( 6f * s,  6f * s), col));
            }
        }

        private static void DrawClosingArrow(MySpriteDrawFrame frame, bool closing,
            float speed, Vector2 pos, float scale)
        {
            if (speed < 0.5f) return;
            Color col = closing ? ColClosing : ColOpening;
            float rot = closing ? MathHelper.Pi : 0f;
            frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle",
                pos, new Vector2(10f * scale, 10f * scale), col, rotation: rot));
        }

        private static void DrawCOGArrow(MySpriteDrawFrame frame, int cog, int bearing,
            Vector2 pos, float scale)
        {
            float relAngle = MathHelper.ToRadians(cog - bearing + 180f);
            float len      = 10f * scale;
            float cos      = (float)Math.Cos(relAngle - MathHelper.PiOver2);
            float sin      = (float)Math.Sin(relAngle - MathHelper.PiOver2);

            var tip  = pos + new Vector2(cos * len, sin * len);
            var tail = pos - new Vector2(cos * len * 0.5f, sin * len * 0.5f);
            DrawLine(frame, tail, tip, 1.5f * scale, ColLabel);
            frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle",
                tip, new Vector2(6f * scale, 6f * scale), ColLabel,
                rotation: relAngle - MathHelper.PiOver2));
        }

        private void DrawCentredText(MySpriteDrawFrame frame, RectangleF vp,
            string text, Color col, float fontSize)
        {
            frame.Add(new MySprite(SpriteType.TEXT, text,
                new Vector2(vp.X + vp.Width * 0.5f, vp.Y + vp.Height * 0.5f - 10f),
                null, col, FONT, TextAlignment.CENTER, fontSize));
        }

        private static void DrawLine(MySpriteDrawFrame frame,
            Vector2 a, Vector2 b, float w, Color col)
        {
            var d = b - a;
            float len = d.Length();
            if (len < 0.5f) return;
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                (a + b) * 0.5f, new Vector2(len, w), col,
                rotation: (float)Math.Atan2(d.Y, d.X)));
        }

        private static string TruncateName(string s, int max)
            => s == null ? "?" : (s.Length <= max ? s : s.Substring(0, max - 1) + "~");
    }
}
