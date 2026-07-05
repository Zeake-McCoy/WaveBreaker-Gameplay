using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace TTNAS
{
    // ───────────────────────────────────────────────────────────────────────────
    // TT Radar — Flat top-down 2D naval radar TSS
    //
    // Select "TT Radar" from the Script dropdown on any LCD surface.
    // Reads threat data from CICProcessor (WC GetSortedThreats).
    //
    // Icons (real naval radar convention):
    //   Inbound missile   — diamond (two triangles tip-to-tip)   RED
    //   Outbound missile  — upward arrow                         GREEN
    //   Friendly missile  — upward arrow                         BLUE
    //   Hostile other     — diamond                              ORANGE
    //   Large Grid        — filled circle with tick              YELLOW
    //   Small Grid        — small filled circle                  CYAN
    //   Friendly grid     — hollow ring                          BLUE
    //   Unknown           — cross / plus                         GREY
    //
    // Rotating sweep line advances each Update10 tick.
    // Out-of-range contacts appear as inward-pointing chevrons on the ring edge.
    //
    // CustomData settings (auto-written on first run):
    //   [TT Radar]
    //   RangeKm = 10          ; 0.5 – 500
    //   Filter  = All         ; All | Hostile | Friendly | LargeOnly | SmallOnly | MissileOnly
    //   ShowOutbound = true
    //   ShowAllHostileMissiles = true
    // ───────────────────────────────────────────────────────────────────────────

    public enum RadarFilter
    {
        All,
        Hostile,
        Friendly,
        LargeOnly,
        SmallOnly,
        MissileOnly,
    }

    [MyTextSurfaceScript("TT_Radar", "TT CIC \u2014 Radar")]
    public class RadarLcd : MyTSSCommon
    {
        // ── Layout ────────────────────────────────────────────────────────────
        private const float SWEEP_SPEED_DEG    = 6f;
        private const float VAMPIRE_H          = 36f;
        private const float LEGEND_H           = 28f;
        private const float MARGIN             = 8f;
        private const float LABEL_FONT_SCALE   = 0.40f;
        private const float RANGE_FONT_SCALE   = 0.35f;
        private const string FONT              = "Debug";

        // ── Colours — CRT green-on-dark palette ───────────────────────────────
        private static readonly Color ColBg                 = new Color(  2,   8,   4, 255);
        private static readonly Color ColRadarFill          = new Color(  0,  12,   6, 255);
        private static readonly Color ColRing               = new Color(  0,  80,  30,  90);
        private static readonly Color ColRingMinor          = new Color(  0,  70,  28,  80);
        private static readonly Color ColRingOuter          = new Color(  0, 100,  40, 130);
        private static readonly Color ColCross              = new Color(  0,  70,  25,  70);
        private static readonly Color ColSweep              = new Color(  0, 200,  80,  50);
        private static readonly Color ColSweepTrail         = new Color(  0, 140,  50,  18);
        private static readonly Color ColSelf               = new Color(  0, 255, 120, 255);
        private static readonly Color ColTitleBg            = new Color(  0,  20,   8, 255);
        private static readonly Color ColRangeLabel         = new Color(  0, 120,  50, 160);
        private static readonly Color ColBearing            = new Color(  0, 100,  40, 120);
        private static readonly Color ColChevron            = new Color(  0, 180,  70, 180);
        private static readonly Color ColVampireBg          = new Color(160,  10,  10, 255);
        private static readonly Color ColVampireText        = new Color(255, 220, 220, 255);
        private static readonly Color ColMissile            = new Color(255,  50,  50, 255);
        private static readonly Color ColMissileDim         = new Color(120,  20,  20, 200);
        private static readonly Color ColLargeGrid          = new Color(255, 210,  30, 255);
        private static readonly Color ColLargeGridDim       = new Color(110,  80,   0, 200);
        private static readonly Color ColSmallGrid          = new Color( 60, 230, 230, 255);
        private static readonly Color ColSmallGridDim       = new Color(  0,  80,  90, 200);
        private static readonly Color ColUnknown            = new Color(160, 160, 160, 220);
        private static readonly Color ColUnknownDim         = new Color( 60,  60,  60, 180);
        private static readonly Color ColFriendly           = new Color( 80, 160, 255, 255);
        private static readonly Color ColFriendlyDim        = new Color( 20,  50, 110, 200);
        private static readonly Color ColMissileOutbound    = new Color( 80, 255, 120, 255);
        private static readonly Color ColMissileOutboundDim = new Color( 20,  80,  40, 200);
        private static readonly Color ColMissileFriendly    = new Color( 60, 180, 255, 255);
        private static readonly Color ColMissileFriendlyDim = new Color( 20,  60, 120, 200);
        private static readonly Color ColMissileOther       = new Color(255, 140,  30, 255);
        private static readonly Color ColMissileOtherDim    = new Color(100,  50,   0, 200);
        private static readonly Color ColLabelText          = new Color(180, 220, 180, 210);

        // ── State ─────────────────────────────────────────────────────────────
        private float _sweepAngle = 0f;

        // ── Per-surface settings ───────────────────────────────────────────────
        private const string INI_SECTION          = "TT Radar";
        private const string INI_RANGE            = "RangeKm";
        private const string INI_FILTER           = "Filter";
        private const string INI_SHOW_OUTBOUND    = "ShowOutbound";
        private const string INI_SHOW_ALL_HOSTILE = "ShowAllHostileMissiles";

        private const float  DEFAULT_RANGE_M = 10000f;
        private const string DEFAULT_FILTER  = "All";

        private float       _rangeM         = DEFAULT_RANGE_M;
        private RadarFilter _filter         = RadarFilter.All;
        private bool        _showOutbound   = true;
        private bool        _showAllHostile = true;

        private readonly MyIni _ini = new MyIni();

        // ── Contact types ──────────────────────────────────────────────────────
        private enum ContactType
        {
            Missile, LargeGrid, SmallGrid, Unknown,
            Friendly, MissileOutbound, MissileFriendly, MissileHostileOther
        }

        private struct Contact
        {
            public Vector2     Pos2D;
            public string      Name;
            public ContactType Type;
            public bool        InRange;
            public float       EdgeAngle;
            public string      PaintKey;
            public float       CoastAlpha;
        }

        private readonly List<Contact> _contacts = new List<Contact>();

        // ── Contact coasting ───────────────────────────────────────────────────
        private const float COAST_SECONDS = 2.5f;
        private struct CoastEntry { public Contact C; public float SeenAt; }
        private readonly Dictionary<string, CoastEntry> _coastMap  = new Dictionary<string, CoastEntry>();
        private readonly Dictionary<string, float>      _paintAngles = new Dictionary<string, float>();
        private float _timeSecs = 0f;

        // ── Block refs ─────────────────────────────────────────────────────────
        private readonly IMyCubeBlock    _block;
        private readonly IMyTerminalBlock _termBlock;

        public RadarLcd(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _block     = block;
            _termBlock = block as IMyTerminalBlock;
        }

        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update10;

        // ── Settings I/O ───────────────────────────────────────────────────────

        private void ReadSettings()
        {
            string cd = _termBlock?.CustomData ?? "";
            MyIniParseResult result;
            if (!_ini.TryParse(cd, out result)) return;

            if (!_ini.ContainsSection(INI_SECTION)) { WriteDefaultSettings(); return; }

            float km = _ini.Get(INI_SECTION, INI_RANGE).ToSingle(DEFAULT_RANGE_M / 1000f);
            _rangeM = Math.Max(500f, Math.Min(km * 1000f, 500000f));

            string filterStr = _ini.Get(INI_SECTION, INI_FILTER).ToString(DEFAULT_FILTER).Trim();
            RadarFilter parsed;
            _filter         = TryParseFilter(filterStr, out parsed) ? parsed : RadarFilter.All;
            _showOutbound   = _ini.Get(INI_SECTION, INI_SHOW_OUTBOUND).ToBoolean(true);
            _showAllHostile = _ini.Get(INI_SECTION, INI_SHOW_ALL_HOSTILE).ToBoolean(true);
        }

        private void WriteDefaultSettings()
        {
            _ini.Set(INI_SECTION, INI_RANGE, DEFAULT_RANGE_M / 1000f);
            _ini.SetComment(INI_SECTION, INI_RANGE, " Display range in kilometres (0.5 - 500)");
            _ini.Set(INI_SECTION, INI_FILTER, DEFAULT_FILTER);
            _ini.SetComment(INI_SECTION, INI_FILTER, " Contact filter: All | Hostile | Friendly | LargeOnly | SmallOnly | MissileOnly");
            _ini.Set(INI_SECTION, INI_SHOW_OUTBOUND, true);
            _ini.SetComment(INI_SECTION, INI_SHOW_OUTBOUND, " Show outbound (friendly) missiles");
            _ini.Set(INI_SECTION, INI_SHOW_ALL_HOSTILE, true);
            _ini.SetComment(INI_SECTION, INI_SHOW_ALL_HOSTILE, " Show all hostile missiles (not just inbound)");

            // Preserve any non-TT-Radar content already in CustomData
            string existing = _termBlock?.CustomData ?? "";
            MyIni existingIni = new MyIni();
            MyIniParseResult r;
            if (existingIni.TryParse(existing, out r))
            {
                var sections = new List<string>();
                existingIni.GetSections(sections);
                foreach (var section in sections)
                {
                    if (section == INI_SECTION) continue;
                    var keys = new List<MyIniKey>();
                    existingIni.GetKeys(section, keys);
                    foreach (var key in keys)
                        _ini.Set(key, existingIni.Get(key).ToString());
                }
            }
            if (_termBlock != null) _termBlock.CustomData = _ini.ToString();
        }

        private static bool TryParseFilter(string s, out RadarFilter result)
        {
            switch (s.ToLowerInvariant())
            {
                case "all":         result = RadarFilter.All;         return true;
                case "hostile":     result = RadarFilter.Hostile;     return true;
                case "friendly":    result = RadarFilter.Friendly;    return true;
                case "largeonly":   result = RadarFilter.LargeOnly;   return true;
                case "smallonly":   result = RadarFilter.SmallOnly;   return true;
                case "missileonly": result = RadarFilter.MissileOnly; return true;
                default:            result = RadarFilter.All;         return false;
            }
        }

        // ── Run / Draw ─────────────────────────────────────────────────────────

        public override void Run()
        {
            try
            {
                ReadSettings();
                _sweepAngle = (_sweepAngle + SWEEP_SPEED_DEG) % 360f;
                Draw();
            }
            catch { }
        }

        private void Draw()
        {
            m_surface.ScriptBackgroundColor = ColBg;

            var vp = new RectangleF(
                (m_surface.TextureSize - m_surface.SurfaceSize) / 2f,
                m_surface.SurfaceSize);

            float scale    = Math.Min(vp.Width, vp.Height) / 512f;
            float legendH  = LEGEND_H  * scale;
            float vampireH = VAMPIRE_H * scale;

            _timeSecs += 1f / 6f;
            GatherContacts();
            MergeCoastingContacts();

            var cic          = CICProcessor.GetForGrid(_block.CubeGrid);
            int missileCount = cic?.MissileInboundCount ?? 0;
            bool showVampire = cic != null && cic.MissilesInbound;
            float topOffset  = showVampire ? vampireH : 0f;

            float availW = vp.Width  - MARGIN * 2f * scale;
            float availH = vp.Height - topOffset - legendH - MARGIN * 2f * scale;
            float r      = Math.Min(availW, availH) * 0.5f;

            var centre = new Vector2(
                vp.X + vp.Width * 0.5f,
                vp.Y + topOffset + MARGIN * scale + r);

            var frame = m_surface.DrawFrame();

            DrawBackground(frame, centre, r, scale);
            DrawSweep(frame, centre, r, scale);
            DrawRings(frame, centre, r, scale, _rangeM);
            DrawCrosshairs(frame, centre, r, scale);
            DrawBearingTicks(frame, centre, r, scale);

            // Update paint angles
            float sweepRad = MathHelper.ToRadians(_sweepAngle);
            foreach (var c in _contacts)
            {
                if (!c.InRange || c.PaintKey == null) continue;
                float contactScreenDeg = MathHelper.ToDegrees((float)Math.Atan2(c.Pos2D.X, -c.Pos2D.Y));
                if (contactScreenDeg < 0) contactScreenDeg += 360f;
                float diff = (_sweepAngle - contactScreenDeg + 360f) % 360f;
                if (diff < 20f) _paintAngles[c.PaintKey] = _sweepAngle;
            }

            // Prune stale paint keys
            var activeKeys = new HashSet<string>();
            foreach (var c in _contacts) if (c.PaintKey != null) activeKeys.Add(c.PaintKey);
            var toRemove = new List<string>();
            foreach (var k in _paintAngles.Keys) if (!activeKeys.Contains(k)) toRemove.Add(k);
            foreach (var k in toRemove) _paintAngles.Remove(k);

            foreach (var c in _contacts)
                if (c.InRange)
                    DrawContact(frame, c, centre, r, scale, _sweepAngle, _paintAngles);

            DrawSelf(frame, centre, scale);

            foreach (var c in _contacts)
                if (!c.InRange)
                    DrawEdgeChevron(frame, c, centre, r, scale);

            DrawLegend(frame, vp, legendH, scale);
            if (showVampire) DrawVampireBar(frame, vp, vampireH, missileCount, scale);

            frame.Dispose();
        }

        // ── Contact coasting ───────────────────────────────────────────────────

        private void MergeCoastingContacts()
        {
            foreach (var c in _contacts)
            {
                if (c.PaintKey == null) continue;
                _coastMap[c.PaintKey] = new CoastEntry { C = c, SeenAt = _timeSecs };
            }

            var toRemove = new List<string>();
            foreach (var kv in _coastMap)
            {
                float age = _timeSecs - kv.Value.SeenAt;
                if (age > COAST_SECONDS) { toRemove.Add(kv.Key); continue; }

                bool live = false;
                foreach (var c in _contacts) if (c.PaintKey == kv.Key) { live = true; break; }
                if (live) continue;

                float alpha = 1.0f - (age / COAST_SECONDS);
                var coasting = kv.Value.C;
                coasting.CoastAlpha = alpha * 0.6f;
                _contacts.Add(coasting);
            }
            foreach (var k in toRemove) _coastMap.Remove(k);
        }

        // ── Contact gathering ──────────────────────────────────────────────────

        private void GatherContacts()
        {
            _contacts.Clear();

            var cic = CICProcessor.GetForGrid(_block.CubeGrid);
            if (cic == null || !cic.IsOnline) return;

            var wm     = _block.WorldMatrix;
            var origin = _block.GetPosition();

            foreach (var threat in cic.Threats)
            {
                if (threat.Entity != null && threat.Entity.EntityId < 0) continue;
                AddContact(threat, ref wm, ref origin, friendly: false);
            }
            foreach (var friendly in cic.Friendlies)
            {
                if (friendly.Entity != null && friendly.Entity.EntityId < 0) continue;
                AddContact(friendly, ref wm, ref origin, friendly: true);
            }

            const double DEDUP_SQ = 2000.0 * 2000.0;
            var shownPositions = new List<Vector3D>();

            if (_showOutbound)
            {
                var oPos = cic.OutboundPositions;
                var oVel = cic.OutboundVelocities;
                for (int mi = 0; mi < oPos.Count; mi++)
                {
                    if (MslDup(oPos[mi], shownPositions, DEDUP_SQ)) continue;
                    shownPositions.Add(oPos[mi]);
                    AddMissileContact(oPos[mi], mi < oVel.Count ? oVel[mi] : Vector3D.Zero,
                        ContactType.MissileOutbound, ref wm, ref origin);
                }

                var fPos = cic.FriendlyMissilePositions;
                var fVel = cic.FriendlyMissileVelocities;
                for (int mi = 0; mi < fPos.Count; mi++)
                {
                    if (MslDup(fPos[mi], shownPositions, DEDUP_SQ)) continue;
                    shownPositions.Add(fPos[mi]);
                    AddMissileContact(fPos[mi], mi < fVel.Count ? fVel[mi] : Vector3D.Zero,
                        ContactType.MissileFriendly, ref wm, ref origin);
                }
            }

            {
                var mPos = cic.MissilePositions;
                var mVel = cic.MissileVelocities;
                for (int mi = 0; mi < mPos.Count; mi++)
                {
                    if (MslDup(mPos[mi], shownPositions, DEDUP_SQ)) continue;
                    shownPositions.Add(mPos[mi]);
                    AddMissileContact(mPos[mi], mi < mVel.Count ? mVel[mi] : Vector3D.Zero,
                        ContactType.Missile, ref wm, ref origin);
                }
            }

            if (_showAllHostile)
            {
                var hPos = cic.HostileOtherPositions;
                var hVel = cic.HostileOtherVelocities;
                for (int mi = 0; mi < hPos.Count; mi++)
                {
                    if (MslDup(hPos[mi], shownPositions, DEDUP_SQ)) continue;
                    shownPositions.Add(hPos[mi]);
                    AddMissileContact(hPos[mi], mi < hVel.Count ? hVel[mi] : Vector3D.Zero,
                        ContactType.MissileHostileOther, ref wm, ref origin);
                }
            }
        }

        private void AddMissileContact(Vector3D missilePos, Vector3D velocity, ContactType type,
            ref MatrixD wm, ref Vector3D origin)
        {
            if (!PassesFilter(type, _filter)) return;

            var delta   = missilePos - origin;
            float right   = (float)delta.Dot(wm.Right);
            float forward = (float)delta.Dot(wm.Forward);
            float dist    = (float)Math.Sqrt(right * right + forward * forward);
            float speed   = (float)velocity.Length();
            string label  = speed > 1f ? $"MSL {speed:F0}m/s" : "MSL";
            string mkey   = $"msl_{(int)(right / 500f)},{(int)(forward / 500f)}";

            _contacts.Add(new Contact
            {
                Pos2D      = new Vector2(right / _rangeM, forward / _rangeM),
                Name       = label,
                Type       = type,              // fixed: was always ContactType.Missile
                InRange    = dist <= _rangeM,
                EdgeAngle  = (float)Math.Atan2(right, forward),
                PaintKey   = mkey,
                CoastAlpha = 1.0f,
            });
        }

        private void AddContact(ThreatEntry entry, ref MatrixD wm, ref Vector3D origin, bool friendly)
        {
            if (entry.Entity == null || entry.Entity.MarkedForClose) return;

            var worldDelta = entry.Entity.PositionComp.GetPosition() - origin;
            float right    = (float)worldDelta.Dot(wm.Right);
            float forward  = (float)worldDelta.Dot(wm.Forward);
            float dist     = (float)Math.Sqrt(right * right + forward * forward);
            string pkey    = entry.Entity.EntityId.ToString();

            _contacts.Add(new Contact
            {
                Pos2D      = new Vector2(right / _rangeM, forward / _rangeM),
                Name       = Truncate(entry.DisplayName, 12),
                Type       = friendly ? ContactType.Friendly : InferType(entry),
                InRange    = dist <= _rangeM,
                EdgeAngle  = (float)Math.Atan2(right, forward),
                PaintKey   = pkey,
                CoastAlpha = 1.0f,
            });
        }

        private static ContactType InferType(ThreatEntry threat)
        {
            if (threat.Entity == null) return ContactType.Unknown;
            if (threat.Entity.EntityId < 0) return ContactType.Missile;

            var tn = threat.Entity.GetType().Name;
            if (tn.IndexOf("Missile",    StringComparison.OrdinalIgnoreCase) >= 0 ||
                tn.IndexOf("Projectile", StringComparison.OrdinalIgnoreCase) >= 0)
                return ContactType.Missile;

            var grid = threat.Entity as VRage.Game.ModAPI.IMyCubeGrid;
            if (grid != null)
                return grid.GridSizeEnum == MyCubeSize.Large ? ContactType.LargeGrid : ContactType.SmallGrid;
            return ContactType.Unknown;
        }

        private static bool PassesFilter(ContactType type, RadarFilter filter)
        {
            switch (filter)
            {
                case RadarFilter.Hostile:     return type != ContactType.Friendly && type != ContactType.MissileOutbound && type != ContactType.MissileFriendly;
                case RadarFilter.Friendly:    return type == ContactType.Friendly || type == ContactType.MissileOutbound || type == ContactType.MissileFriendly;
                case RadarFilter.LargeOnly:   return type == ContactType.LargeGrid;
                case RadarFilter.SmallOnly:   return type == ContactType.SmallGrid;
                case RadarFilter.MissileOnly: return type == ContactType.Missile || type == ContactType.MissileOutbound || type == ContactType.MissileFriendly || type == ContactType.MissileHostileOther;
                default:                      return true;
            }
        }

        // ── Draw helpers ───────────────────────────────────────────────────────

        private static void DrawBackground(MySpriteDrawFrame frame, Vector2 centre, float r, float scale)
        {
            frame.Add(new MySprite(SpriteType.TEXTURE, "Circle",
                centre, new Vector2(r * 2f, r * 2f), ColRadarFill));
        }

        private void DrawSweep(MySpriteDrawFrame frame, Vector2 centre, float r, float scale)
        {
            for (int i = 6; i >= 1; i--)
            {
                float tRad = MathHelper.ToRadians(_sweepAngle - 90f - i * 5f);
                var   tEnd = centre + new Vector2((float)Math.Cos(tRad) * r, (float)Math.Sin(tRad) * r);
                byte  a    = (byte)(ColSweepTrail.A / (i + 1));
                DrawLine(frame, centre, tEnd, 2f * scale,
                    new Color(ColSweepTrail.R, ColSweepTrail.G, ColSweepTrail.B, a));
            }
            float sRad    = MathHelper.ToRadians(_sweepAngle - 90f);
            var   sweepEnd = centre + new Vector2((float)Math.Cos(sRad) * r, (float)Math.Sin(sRad) * r);
            DrawLine(frame, centre, sweepEnd, 2.5f * scale, ColSweep);
        }

        private static void DrawRings(MySpriteDrawFrame frame, Vector2 centre, float r, float scale, float rangeM)
        {
            float rangeKm = rangeM / 1000f;
            float majorStepKm;
            if      (rangeKm <= 15f)  majorStepKm = 5f;
            else if (rangeKm <= 50f)  majorStepKm = 10f;
            else if (rangeKm <= 150f) majorStepKm = 25f;
            else if (rangeKm <= 300f) majorStepKm = 50f;
            else                      majorStepKm = 100f;

            float minorStepKm = Math.Max(5f, majorStepKm / 2f);
            int   totalSteps  = (int)(rangeKm / minorStepKm);

            for (int i = 1; i <= totalSteps; i++)
            {
                float ringKm = i * minorStepKm;
                if (ringKm > rangeKm + 0.01f) break;

                float fr = ringKm / rangeKm;
                float px = r * fr;
                if (px < 4f) continue;

                bool isMajor = (ringKm % majorStepKm) < 0.01f;
                bool isOuter = Math.Abs(ringKm - rangeKm) < 0.01f;

                if (isOuter)
                {
                    DrawCircleOutline(frame, centre, px, 2.5f * scale, ColRingOuter);
                    string lbl = rangeKm >= 1f ? $"{rangeKm:F0}km" : $"{rangeM:F0}m";
                    frame.Add(new MySprite(SpriteType.TEXT, lbl,
                        centre + new Vector2(px + 4f * scale, -7f * scale),
                        null, ColRangeLabel, FONT, TextAlignment.LEFT, RANGE_FONT_SCALE * scale));
                }
                else if (isMajor)
                {
                    DrawCircleOutline(frame, centre, px, 2f * scale, ColRing);
                    string lbl = ringKm >= 1f ? $"{ringKm:F0}km" : $"{ringKm * 1000f:F0}m";
                    frame.Add(new MySprite(SpriteType.TEXT, lbl,
                        centre + new Vector2(px + 4f * scale, -7f * scale),
                        null, ColRangeLabel, FONT, TextAlignment.LEFT, RANGE_FONT_SCALE * scale));
                }
                else
                {
                    DrawCircleOutline(frame, centre, px, 1.5f * scale, ColRingMinor);
                }
            }
        }

        private static void DrawCircleOutline(MySpriteDrawFrame frame, Vector2 centre, float radiusPx, float width, Color col)
        {
            int   segs = Math.Max(24, (int)(radiusPx * 0.25f));
            float step = MathHelper.TwoPi / segs;
            for (int s = 0; s < segs; s++)
            {
                float a0 = s * step, a1 = (s + 1) * step;
                DrawLine(frame,
                    centre + new Vector2((float)Math.Cos(a0) * radiusPx, (float)Math.Sin(a0) * radiusPx),
                    centre + new Vector2((float)Math.Cos(a1) * radiusPx, (float)Math.Sin(a1) * radiusPx),
                    width, col);
            }
        }

        private static void DrawCrosshairs(MySpriteDrawFrame frame, Vector2 centre, float r, float scale)
        {
            DrawLine(frame, centre - new Vector2(r, 0), centre + new Vector2(r, 0), 1f * scale, ColCross);
            DrawLine(frame, centre - new Vector2(0, r), centre + new Vector2(0, r), 1f * scale, ColCross);
        }

        private static void DrawBearingTicks(MySpriteDrawFrame frame, Vector2 centre, float r, float scale)
        {
            for (int deg = 0; deg < 360; deg += 10)
            {
                float rad  = MathHelper.ToRadians(deg - 90f);
                float cos  = (float)Math.Cos(rad);
                float sin  = (float)Math.Sin(rad);
                bool  maj  = deg % 30 == 0;
                float tLen = (maj ? 10f : 5f) * scale;
                DrawLine(frame,
                    centre + new Vector2(cos * (r - tLen), sin * (r - tLen)),
                    centre + new Vector2(cos *  r,         sin *  r),
                    (maj ? 2f : 1f) * scale, ColBearing);
                if (maj)
                    frame.Add(new MySprite(SpriteType.TEXT, $"{deg:000}",
                        centre + new Vector2(cos * (r + 12f * scale), sin * (r + 12f * scale)),
                        null, ColBearing, FONT, TextAlignment.CENTER, 0.32f * scale));
            }
        }

        private static void DrawSelf(MySpriteDrawFrame frame, Vector2 centre, float scale)
        {
            float s = 9f * scale;
            DrawLine(frame, centre - new Vector2(s, 0), centre + new Vector2(s, 0), 2f * scale, ColSelf);
            DrawLine(frame, centre - new Vector2(0, s), centre + new Vector2(0, s), 2f * scale, ColSelf);
            frame.Add(new MySprite(SpriteType.TEXTURE, "Circle",
                centre, new Vector2(5f * scale, 5f * scale), ColSelf));
        }

        private static void DrawContact(
            MySpriteDrawFrame frame, Contact c,
            Vector2 centre, float r, float scale,
            float sweepAngle, Dictionary<string, float> paintAngles)
        {
            var p = c.Pos2D;
            if (p.LengthSquared() > 1f) p = Vector2.Normalize(p) * 0.985f;
            var pos = centre + new Vector2(p.X * r, -p.Y * r);

            float coastMult = c.CoastAlpha > 0f ? c.CoastAlpha : 1.0f;
            float bright    = 0.35f;
            if (c.PaintKey != null && paintAngles.ContainsKey(c.PaintKey))
            {
                float age = (sweepAngle - paintAngles[c.PaintKey] + 360f) % 360f;
                bright = age < 20f ? 1.0f : Math.Max(0.35f, 1.0f - (age - 20f) / 280f);
            }

            Color col, dim;
            GetColors(c.Type, out col, out dim);
            col = ScaleAlpha(col, bright * coastMult);
            dim = ScaleAlpha(dim, bright * 0.6f * coastMult);
            DrawIcon(frame, c.Type, pos, scale, col, dim);

            frame.Add(new MySprite(SpriteType.TEXT, c.Name,
                pos + new Vector2(9f * scale, -12f * scale),
                null, ScaleAlpha(ColLabelText, bright), FONT, TextAlignment.LEFT, LABEL_FONT_SCALE * scale));
        }

        private static void DrawIcon(MySpriteDrawFrame frame, ContactType type, Vector2 pos, float scale, Color col, Color dim)
        {
            float s = scale;
            switch (type)
            {
                case ContactType.Missile:
                    frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle", pos + new Vector2(0, -3.5f * s), new Vector2(10f * s, 10f * s), dim, rotation: 0f));
                    frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle", pos + new Vector2(0,  3.5f * s), new Vector2(10f * s, 10f * s), dim, rotation: MathHelper.Pi));
                    frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle", pos + new Vector2(0, -3f   * s), new Vector2( 7f * s,  7f * s), col, rotation: 0f));
                    frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle", pos + new Vector2(0,  3f   * s), new Vector2( 7f * s,  7f * s), col, rotation: MathHelper.Pi));
                    break;
                case ContactType.LargeGrid:
                    frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", pos, new Vector2(14f * s, 14f * s), dim));
                    frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", pos, new Vector2( 9f * s,  9f * s), col));
                    DrawLine(frame, pos - new Vector2(0,  7f * s), pos - new Vector2(0, 13f * s), 2.5f * s, col);
                    break;
                case ContactType.SmallGrid:
                    frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", pos, new Vector2(11f * s, 11f * s), dim));
                    frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", pos, new Vector2( 7f * s,  7f * s), col));
                    break;
                case ContactType.Friendly:
                    frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", pos, new Vector2(13f * s, 13f * s), dim));
                    frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", pos, new Vector2( 9f * s,  9f * s), ColRadarFill));
                    frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", pos, new Vector2( 7f * s,  7f * s), col));
                    break;
                case ContactType.MissileOutbound:
                case ContactType.MissileFriendly:
                    frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle", pos, new Vector2(13f * s, 13f * s), dim, rotation: 0f));
                    frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle", pos, new Vector2( 9f * s,  9f * s), col, rotation: 0f));
                    DrawLine(frame, pos + new Vector2(0, 5f * s), pos + new Vector2(0, 12f * s), 1.5f * s, col);
                    break;
                case ContactType.MissileHostileOther:
                    frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle", pos + new Vector2(0, -3.5f * s), new Vector2(10f * s, 10f * s), dim, rotation: 0f));
                    frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle", pos + new Vector2(0,  3.5f * s), new Vector2(10f * s, 10f * s), dim, rotation: MathHelper.Pi));
                    frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle", pos + new Vector2(0, -3f   * s), new Vector2( 7f * s,  7f * s), col, rotation: 0f));
                    frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle", pos + new Vector2(0,  3f   * s), new Vector2( 7f * s,  7f * s), col, rotation: MathHelper.Pi));
                    break;
                default:
                    DrawLine(frame, pos - new Vector2(7f * s, 0), pos + new Vector2(7f * s, 0), 2f * s, col);
                    DrawLine(frame, pos - new Vector2(0, 7f * s), pos + new Vector2(0, 7f * s), 2f * s, col);
                    break;
            }
        }

        private static void DrawEdgeChevron(MySpriteDrawFrame frame, Contact c, Vector2 centre, float r, float scale)
        {
            float ea     = c.EdgeAngle;
            var edgePos  = centre + new Vector2((float)Math.Sin(ea) * r, (float)Math.Cos(ea) * -r);
            float rotBase = (float)Math.Atan2(-(float)Math.Cos(ea), (float)Math.Sin(ea));
            float armLen = 10f * scale;
            float armW   =  2f * scale;
            float spread = 0.55f;

            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                edgePos + ArmOffset(rotBase - spread, armLen),
                new Vector2(armW, armLen), ColChevron, rotation: rotBase - spread));
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                edgePos + ArmOffset(rotBase + spread, armLen),
                new Vector2(armW, armLen), ColChevron, rotation: rotBase + spread));

            Color col, dim;
            GetColors(c.Type, out col, out dim);
            var labelPos = centre + new Vector2(
                (float)Math.Sin(ea) * (r - 22f * scale),
                (float)Math.Cos(ea) * -(r - 22f * scale));
            frame.Add(new MySprite(SpriteType.TEXT, c.Name,
                labelPos, null, col, FONT, TextAlignment.CENTER, 0.34f * scale));
        }

        private static Vector2 ArmOffset(float angle, float len)
            => new Vector2((float)Math.Sin(angle) * len * 0.5f, -(float)Math.Cos(angle) * len * 0.5f);

        private static void DrawLegend(MySpriteDrawFrame frame, RectangleF vp, float legendH, float scale)
        {
            float y = vp.Y + vp.Height - legendH;
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                new Vector2(vp.X + vp.Width * 0.5f, y + legendH * 0.5f),
                new Vector2(vp.Width, legendH), ColTitleBg));

            string[]      labels = { "INBD", "OUT", "ALLY", "HST", "LRG", "SML" };
            ContactType[] types  = { ContactType.Missile, ContactType.MissileOutbound,
                                     ContactType.MissileFriendly, ContactType.MissileHostileOther,
                                     ContactType.LargeGrid, ContactType.SmallGrid };
            float cellW = vp.Width / 6f;

            for (int i = 0; i < 6; i++)
            {
                float cx = vp.X + cellW * i + cellW * 0.5f;
                Color col, dim;
                GetColors(types[i], out col, out dim);
                DrawIcon(frame, types[i],
                    new Vector2(cx - cellW * 0.15f, y + legendH * 0.4f),
                    scale * 0.6f, col, dim);
                frame.Add(new MySprite(SpriteType.TEXT, labels[i],
                    new Vector2(cx + cellW * 0.1f, y + legendH * 0.1f),
                    null, col, FONT, TextAlignment.CENTER, 0.35f * scale));
            }
        }

        private static void DrawVampireBar(MySpriteDrawFrame frame, RectangleF vp, float barH, int count, float scale)
        {
            var barCentre = new Vector2(vp.X + vp.Width * 0.5f, vp.Y + barH * 0.5f);
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                barCentre, new Vector2(vp.Width, barH), ColVampireBg));
            string msg = $"VAMPIRE VAMPIRE  {count} MISSILE{(count == 1 ? "" : "S")}";
            frame.Add(new MySprite(SpriteType.TEXT, msg,
                barCentre - new Vector2(0, 8f * scale),
                null, ColVampireText, FONT, TextAlignment.CENTER, 0.58f * scale));
        }

        // ── Utility ────────────────────────────────────────────────────────────

        private static void DrawLine(MySpriteDrawFrame frame, Vector2 a, Vector2 b, float w, Color col)
        {
            var d = b - a; float len = d.Length();
            if (len < 0.5f) return;
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                (a + b) * 0.5f, new Vector2(len, w), col,
                rotation: (float)Math.Atan2(d.Y, d.X)));
        }

        private static Color ScaleAlpha(Color c, float t)
        {
            byte a = (byte)(c.A * Math.Max(0f, Math.Min(1f, t)));
            return new Color(c.R, c.G, c.B, a);
        }

        private static bool MslDup(Vector3D pos, List<Vector3D> shown, double dedupSq)
        {
            foreach (var s in shown) if (Vector3D.DistanceSquared(pos, s) < dedupSq) return true;
            return false;
        }

        private static void GetColors(ContactType type, out Color col, out Color dim)
        {
            switch (type)
            {
                case ContactType.Missile:             col = ColMissile;         dim = ColMissileDim;         break;
                case ContactType.LargeGrid:           col = ColLargeGrid;       dim = ColLargeGridDim;       break;
                case ContactType.SmallGrid:           col = ColSmallGrid;       dim = ColSmallGridDim;       break;
                case ContactType.Friendly:            col = ColFriendly;        dim = ColFriendlyDim;        break;
                case ContactType.MissileOutbound:     col = ColMissileOutbound; dim = ColMissileOutboundDim; break;
                case ContactType.MissileFriendly:     col = ColMissileFriendly; dim = ColMissileFriendlyDim; break; // fixed: was missing
                case ContactType.MissileHostileOther: col = ColMissileOther;    dim = ColMissileOtherDim;    break;
                default:                              col = ColUnknown;         dim = ColUnknownDim;         break;
            }
        }

        private static string Truncate(string s, int max)
            => s == null ? "?" : s.Length <= max ? s : s.Substring(0, max - 1) + "~";
    }
}
