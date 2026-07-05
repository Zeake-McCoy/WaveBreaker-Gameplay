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
    // MissileStatusLcd — Missile Fire Control status screen.
    //
    // Sections:
    //   FC STATUS     — fire control block state (READY / ENGAGING / OFFLINE),
    //                   current filter, salvo size, queued shots, last summary
    //   WEAPON BAYS   — one row per static launcher: name, grid size tag, ready/offline
    //   OUTBOUND      — count of our smart projectiles in flight
    //   INBOUND WARN  — prominent warning banner when missiles are locked on us
    //   THREATS       — tracked contacts: range, speed, inbound missile indicator,
    //                   selected targets highlighted
    //
    // Script ID: TT_MissileStatus  |  "TT CIC — Missile Status"
    // Update:    Update10 (fast enough to catch engagement state changes)
    // ───────────────────────────────────────────────────────────────────────────
    [MyTextSurfaceScript("TT_MissileStatus", "TT CIC \u2014 Missile Status")]
    public class MissileStatusLcd : MyTSSCommon
    {
        private readonly IMyTextSurface     _surface;
        private readonly IMyCubeBlock       _block;
        private readonly List<IMySlimBlock> _slims = new List<IMySlimBlock>();

        private const float MARGIN   = 8f;
        private const float LINE_PAD = 1f;

        public MissileStatusLcd(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
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
            _surface.ScriptBackgroundColor = ShipMapHelper.ColBg;

            var vp = new RectangleF(
                (_surface.TextureSize - _surface.SurfaceSize) / 2f,
                _surface.SurfaceSize);

            float charH = _surface.MeasureStringInPixels(new StringBuilder("W"),
                              ShipMapHelper.FONT, ShipMapHelper.FS_MAIN).Y + LINE_PAD;
            float subH  = _surface.MeasureStringInPixels(new StringBuilder("W"),
                              ShipMapHelper.FONT, ShipMapHelper.FS_SUB).Y  + LINE_PAD;
            float xsH   = _surface.MeasureStringInPixels(new StringBuilder("W"),
                              ShipMapHelper.FONT, ShipMapHelper.FS_XS).Y   + LINE_PAD;

            var   frame = _surface.DrawFrame();
            float x0    = vp.X + MARGIN;
            float w     = vp.Width - MARGIN * 2f;

            ShipMapHelper.Rect(frame,
                vp.X + vp.Width * 0.5f, vp.Y + vp.Height * 0.5f,
                vp.Width, vp.Height, ShipMapHelper.ColBg);

            float curY = vp.Y + MARGIN;

            // ── Header ────────────────────────────────────────────────────────
            ShipMapHelper.TXT(frame, x0, curY,
                "TT CIC \u2014 MISSILE STATUS", ShipMapHelper.ColHeader,
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

            // ── Gather data ───────────────────────────────────────────────────
            var cic = CICProcessor.GetForGrid(_block.CubeGrid);
            var fc  = FindMissileFC();

            bool   cicOnline    = cic != null && cic.IsOnline;
            bool   fcOnline     = fc  != null;
            bool   engaging     = fc  != null && fc.IsEngaging;
            int    queued       = fc  != null ? fc.FireQueueCount    : 0;
            int    lastFired    = fc  != null ? fc.LastShotsFired    : 0;
            int    salvo        = fc  != null ? fc.SalvoCount        : 1;
            int    filterMode   = fc  != null ? fc.GridSizeFilter    : 0;
            float  maxRangeKm   = fc  != null ? fc.MaxRangeKm        : 0f;
            bool   autoEngage   = fc  != null && fc.AutoEngage;
            string lastSummary  = fc  != null ? fc.LastEngageSummary : "";
            int    outbound     = cic != null ? cic.OutboundPositions.Count : 0;
            bool   inbound      = cic != null && cic.MissilesInbound;
            int    inboundCount = cic != null ? cic.MissileInboundCount : 0;

            // ── FC STATUS ─────────────────────────────────────────────────────
            DrawSectionHeader(frame, x0, w, ref curY, charH, "FC STATUS", "", ShipMapHelper.ColHeader);

            // Status row: state + filter + salvo side-by-side
            string stateStr; Color stateCol;
            if (!cicOnline)     { stateStr = "CIC OFFLINE";  stateCol = ShipMapHelper.ColDim;   }
            else if (!fcOnline) { stateStr = "NO FC BLOCK";  stateCol = ShipMapHelper.ColDim;   }
            else if (engaging)  { stateStr = "ENGAGING";     stateCol = ShipMapHelper.ColError; }
            else                { stateStr = "READY";        stateCol = ShipMapHelper.ColOk;    }

            string filterStr = filterMode == 1 ? "LG" : filterMode == 2 ? "SG" : "ALL";

            ShipMapHelper.Rect(frame, x0 + w * 0.5f, curY + subH * 0.5f,
                w, subH + 2f, ShipMapHelper.ColRowBg);
            ShipMapHelper.TXT(frame, x0 + 4f, curY,
                stateStr, stateCol, ShipMapHelper.FS_SUB, TextAlignment.LEFT);
            string rangeStr = maxRangeKm <= 0f ? "UNL" : maxRangeKm.ToString("F0") + "km";
            string midStr   = "FILTER:" + filterStr + "  RNG:" + rangeStr + "  SLV:" + salvo;
            Color  midCol   = autoEngage ? ShipMapHelper.ColWarn : ShipMapHelper.ColDim;
            ShipMapHelper.TXT(frame, x0 + w * 0.5f, curY,
                midStr + (autoEngage ? "  AUTO" : ""),
                midCol, ShipMapHelper.FS_SUB, TextAlignment.CENTER);
            if (queued > 0)
                ShipMapHelper.TXT(frame, x0 + w, curY,
                    "QUEUED: " + queued, ShipMapHelper.ColWarn,
                    ShipMapHelper.FS_SUB, TextAlignment.RIGHT);
            else if (lastFired > 0)
                ShipMapHelper.TXT(frame, x0 + w, curY,
                    "LAST: " + lastFired + " fired", ShipMapHelper.ColDim,
                    ShipMapHelper.FS_SUB, TextAlignment.RIGHT);
            curY += subH + 2f;

            if (!string.IsNullOrEmpty(lastSummary))
            {
                ShipMapHelper.TXT(frame, x0 + 4f, curY,
                    "Last engage: " + lastSummary,
                    ShipMapHelper.ColDim, ShipMapHelper.FS_XS, TextAlignment.LEFT);
                curY += xsH + 1f;
            }

            curY += 2f;
            ShipMapHelper.HLine(frame, x0 + w * 0.5f, curY, w, ShipMapHelper.ColSep);
            curY += 5f;

            // ── INBOUND WARNING ───────────────────────────────────────────────
            // Drawn before weapon bays so it's always visible near the top.
            if (inbound)
            {
                float warnH = subH + 4f;
                ShipMapHelper.Rect(frame, x0 + w * 0.5f, curY + warnH * 0.5f,
                    w, warnH, new Color(60, 0, 0));
                ShipMapHelper.Rect(frame, x0 + w * 0.5f, curY,              w, 1.5f, ShipMapHelper.ColError);
                ShipMapHelper.Rect(frame, x0 + w * 0.5f, curY + warnH - 1f, w, 1.5f, ShipMapHelper.ColError);

                ShipMapHelper.TXT(frame, x0 + w * 0.5f, curY + (warnH - subH) * 0.5f,
                    "\u26A0 MISSILES INBOUND: " + inboundCount,
                    ShipMapHelper.ColError, ShipMapHelper.FS_SUB, TextAlignment.CENTER);
                curY += warnH + 5f;
            }

            // ── WEAPON BAYS ───────────────────────────────────────────────────
            var launchers = cic?.Statics;
            int launcherCount = launchers != null ? launchers.Count : 0;

            string bayHeader = "WEAPON BAYS  (" + launcherCount + ")";
            string bayRight  = launcherCount > 0
                ? (fc != null ? fc.SelectedWeaponCount + " SELECTED" : "")
                : "NO LAUNCHERS";
            DrawSectionHeader(frame, x0, w, ref curY, charH, bayHeader, bayRight,
                launcherCount > 0 ? ShipMapHelper.ColHeader : ShipMapHelper.ColDim);

            if (launchers != null && launcherCount > 0)
            {
                // Show up to 10 launchers; clip the rest
                int show = Math.Min(launcherCount, 10);
                for (int i = 0; i < show; i++)
                {
                    var entry = launchers[i];
                    var tb    = entry.Block;
                    if (tb == null || tb.MarkedForClose) continue;

                    bool  isWorking = (tb as IMyFunctionalBlock)?.IsWorking ?? false;
                    bool  isLG      = tb.CubeGrid?.GridSizeEnum == VRage.Game.MyCubeSize.Large;
                    bool  selected  = fc != null && fc.IsWeaponSelected(entry);
                    string sizeTag  = isLG ? "[LG]" : "[SG]";
                    string name     = ShipMapHelper.Trunc(entry.DisplayName, 20);

                    Color rowBg  = selected ? new Color(0, 35, 20) : ShipMapHelper.ColRowBg;
                    Color namCol = selected ? ShipMapHelper.ColOk
                                 : isWorking ? ShipMapHelper.ColValue : ShipMapHelper.ColDim;
                    Color stCol  = isWorking ? ShipMapHelper.ColOk : ShipMapHelper.ColError;

                    ShipMapHelper.Rect(frame, x0 + w * 0.5f, curY + subH * 0.5f,
                        w, subH, rowBg);

                    ShipMapHelper.TXT(frame, x0 + 4f, curY,
                        name, namCol, ShipMapHelper.FS_SUB, TextAlignment.LEFT);

                    ShipMapHelper.TXT(frame, x0 + w * 0.5f, curY,
                        sizeTag, ShipMapHelper.ColDim, ShipMapHelper.FS_XS, TextAlignment.CENTER);

                    ShipMapHelper.TXT(frame, x0 + w, curY,
                        isWorking ? "READY" : "OFFLINE",
                        stCol, ShipMapHelper.FS_SUB, TextAlignment.RIGHT);

                    curY += subH;
                    ShipMapHelper.Rect(frame, x0 + w * 0.5f, curY, w, 1f, ShipMapHelper.ColSep);
                    curY += 1f;
                }
                if (launcherCount > 10)
                {
                    ShipMapHelper.TXT(frame, x0 + 4f, curY,
                        "  ... +" + (launcherCount - 10) + " more",
                        ShipMapHelper.ColDim, ShipMapHelper.FS_XS, TextAlignment.LEFT);
                    curY += xsH + 1f;
                }
            }
            else
            {
                ShipMapHelper.TXT(frame, x0 + 4f, curY,
                    "No static launchers detected",
                    ShipMapHelper.ColDim, ShipMapHelper.FS_SUB, TextAlignment.LEFT);
                curY += subH;
            }

            curY += 3f;
            ShipMapHelper.HLine(frame, x0 + w * 0.5f, curY, w, ShipMapHelper.ColSep);
            curY += 5f;

            // ── OUTBOUND ──────────────────────────────────────────────────────
            DrawSectionHeader(frame, x0, w, ref curY, charH,
                "OUTBOUND",
                outbound > 0 ? outbound + " IN FLIGHT" : "NONE",
                outbound > 0 ? ShipMapHelper.ColOk : ShipMapHelper.ColDim);

            if (outbound > 0)
            {
                // Mini pip bar: one square per outbound missile, up to 16
                int   pips    = Math.Min(outbound, 16);
                float pipSize = Math.Min(subH - 2f, (w - (pips - 1) * 2f) / pips);
                for (int i = 0; i < pips; i++)
                {
                    float px = x0 + i * (pipSize + 2f) + pipSize * 0.5f;
                    ShipMapHelper.Rect(frame, px, curY + pipSize * 0.5f,
                        pipSize, pipSize, ShipMapHelper.ColOk);
                }
                if (outbound > 16)
                    ShipMapHelper.TXT(frame, x0 + w, curY + (pipSize - xsH) * 0.5f,
                        "+" + (outbound - 16), ShipMapHelper.ColDim,
                        ShipMapHelper.FS_XS, TextAlignment.RIGHT);
                curY += pipSize + 3f;
            }

            ShipMapHelper.HLine(frame, x0 + w * 0.5f, curY, w, ShipMapHelper.ColSep);
            curY += 5f;

            // ── THREATS ───────────────────────────────────────────────────────
            var threats      = cic?.Threats;
            var selectedTgts = fc  != null ? fc.SelectedTargets : null;
            int threatCount  = threats != null ? threats.Count : 0;

            DrawSectionHeader(frame, x0, w, ref curY, charH,
                "THREATS  (" + threatCount + ")",
                inbound ? inboundCount + " INBOUND" : "",
                inbound ? ShipMapHelper.ColError : ShipMapHelper.ColHeader);

            if (threats != null && threatCount > 0)
            {
                // Sort: selected first, then by range ascending (already sorted by WC score,
                // but we re-order so the operator sees their selected targets at top).
                var sorted = new List<ThreatEntry>(threats);
                var selSet = new HashSet<ThreatEntry>(selectedTgts ?? (IReadOnlyList<ThreatEntry>)new ThreatEntry[0]);
                if (selSet.Count > 0)
                {
                    sorted.Sort((a, b) =>
                    {
                        bool aSel = selSet.Contains(a);
                        bool bSel = selSet.Contains(b);
                        if (aSel != bSel) return aSel ? -1 : 1;
                        return a.RangeMetres.CompareTo(b.RangeMetres);
                    });
                }

                // Estimate rows remaining
                float rowsLeft = (vp.Y + vp.Height - MARGIN - subH - curY) / (subH + 1f);
                int   maxRows  = Math.Max(1, (int)rowsLeft - 1); // leave room for footer
                int   showT    = Math.Min(threatCount, maxRows);

                for (int i = 0; i < showT; i++)
                {
                    var    t       = sorted[i];
                    bool   sel     = selSet.Contains(t);
                    bool   hasInc  = t.IncomingCount > 0;
                    Color  rowBg   = sel     ? new Color(35, 0, 0)
                                 : hasInc   ? new Color(25, 0, 0)
                                 :             ShipMapHelper.ColRowBg;
                    Color  nameCol = sel ? ShipMapHelper.ColError
                                 : hasInc ? ShipMapHelper.ColWarn
                                 :           ShipMapHelper.ColValue;

                    ShipMapHelper.Rect(frame, x0 + w * 0.5f, curY + subH * 0.5f,
                        w, subH, rowBg);

                    // Name (truncated), faction tag
                    string nameStr = ShipMapHelper.Trunc(t.DisplayName, 18);
                    if (!string.IsNullOrEmpty(t.FactionTag))
                        nameStr = "[" + t.FactionTag + "] " + nameStr;
                    ShipMapHelper.TXT(frame, x0 + 4f, curY,
                        nameStr, nameCol, ShipMapHelper.FS_SUB, TextAlignment.LEFT);

                    // Range + speed (right side)
                    string distStr = FormatDist(t.RangeMetres);
                    string statStr = distStr + "  " + t.SpeedMs.ToString("F0") + "m/s";
                    if (hasInc) statStr += " !" + t.IncomingCount;
                    ShipMapHelper.TXT(frame, x0 + w, curY,
                        statStr,
                        hasInc ? ShipMapHelper.ColError : ShipMapHelper.ColDim,
                        ShipMapHelper.FS_XS, TextAlignment.RIGHT);

                    curY += subH;
                    ShipMapHelper.Rect(frame, x0 + w * 0.5f, curY, w, 1f, ShipMapHelper.ColSep);
                    curY += 1f;
                }
                if (threatCount > showT)
                {
                    ShipMapHelper.TXT(frame, x0 + 4f, curY,
                        "  ... +" + (threatCount - showT) + " more",
                        ShipMapHelper.ColDim, ShipMapHelper.FS_XS, TextAlignment.LEFT);
                }
            }
            else
            {
                ShipMapHelper.TXT(frame, x0 + 4f, curY,
                    "No contacts", ShipMapHelper.ColDim, ShipMapHelper.FS_SUB, TextAlignment.LEFT);
            }

            // ── Footer ────────────────────────────────────────────────────────
            DrawFooter(frame, vp, w, subH);
            frame.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private MissileFireControl FindMissileFC()
        {
            _slims.Clear();
            try { _block.CubeGrid.GetBlocks(_slims); }
            catch { return null; }

            foreach (var slim in _slims)
            {
                var fat = slim?.FatBlock;
                if (fat == null) continue;
                var logic = fat.GameLogic?.GetAs<MissileFireControl>();
                if (logic != null) return logic;
            }
            return null;
        }

        private void DrawSectionHeader(MySpriteDrawFrame frame, float x, float w,
            ref float y, float charH, string label, string value, Color valueCol)
        {
            ShipMapHelper.TXT(frame, x,     y, label, ShipMapHelper.ColHeader,
                ShipMapHelper.FS_MAIN, TextAlignment.LEFT);
            if (!string.IsNullOrEmpty(value))
                ShipMapHelper.TXT(frame, x + w, y, value, valueCol,
                    ShipMapHelper.FS_MAIN, TextAlignment.RIGHT);
            y += charH;
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

        private static string FormatDist(double m)
            => m >= 1000.0 ? (m / 1000.0).ToString("F1") + " km" : m.ToString("F0") + " m";
    }
}
