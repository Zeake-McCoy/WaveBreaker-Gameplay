using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Definitions;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace TTNAS
{
    // ───────────────────────────────────────────────────────────────────────────
    // EngPowerLcd — Power display; layout adapts to grid size.
    //
    // LARGE GRID:  Full management view.
    //   GENERATION  — per-source breakdown (reactor / H2 / solar / wind / other)
    //   LOAD        — current draw vs capacity bar
    //   BATTERIES   — count, charge mode, stored/max MWh
    //   H2 TANKS    — count, stockpile mode, stored/max volume
    //
    // SMALL GRID:  Aircraft instrument panel (CGR-30P inspired).
    //   ARC GAUGES  — 3 radial dials side-by-side:
    //                   LOAD (grid power draw %)
    //                   H2 ENG (hydrogen engine output %)
    //                   H2 FUEL (hydrogen tank fill %)
    //   GENERATION  — compact per-source bar rows
    //   H2 FUEL BAR — segmented level bar with volume readout
    //   AFTERBURNER — active/total + per-thruster indicators (if any registered)
    //
    // Script ID: TTNB_EngPower  |  "TT Power — Management"
    // Update:    Update100 (~1.67 s)
    // ───────────────────────────────────────────────────────────────────────────
    [MyTextSurfaceScript("TTNB_EngPower", "TT ENG \u2014 Power")]
    public class EngPowerLcd : MyTSSCommon
    {
        private readonly IMyTextSurface      _surface;
        private readonly IMyCubeBlock        _block;
        private readonly List<IMySlimBlock>  _slims  = new List<IMySlimBlock>();
        private readonly List<KeyValuePair<string, bool>> _abList = new List<KeyValuePair<string, bool>>();

        private const float MARGIN   = 8f;
        private const float LINE_PAD = 1f;

        // Arc gauge geometry (small-grid view).
        // 300° clockwise from 7 o'clock to 5 o'clock in screen coords (Y-down).
        // 7 o'clock ≈ 120° from +X, 5 o'clock ≈ 60°; clockwise = increasing angle.
        private const float ARC_START_DEG = 120f;
        private const float ARC_SPAN_DEG  = 300f;
        private const int   ARC_TICKS     = 24;

        // ── Reactor fuel-type registry (large-grid view) ──────────────────────
        private static Dictionary<string, string> _fuelTypeCache;

        private static string GetReactorCategory(IMyReactor reactor, HashSet<string> navalOverrides)
        {
            string sub = reactor.BlockDefinition.SubtypeId;
            if (navalOverrides != null && navalOverrides.Contains(sub)) return "naval";

            if (_fuelTypeCache == null) BuildFuelTypeCache();
            string cat;
            if (_fuelTypeCache.TryGetValue(sub, out cat)) return cat;
            return "nuclear";
        }

        private static void BuildFuelTypeCache()
        {
            _fuelTypeCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var def in MyDefinitionManager.Static.GetAllDefinitions())
                {
                    var rDef = def as MyReactorDefinition;
                    if (rDef == null) continue;
                    string category = "nuclear";
                    if (rDef.FuelInfos != null)
                    {
                        foreach (var fuel in rDef.FuelInfos)
                        {
                            string fuelSub = fuel.FuelId.SubtypeId.String.ToLower();
                            if (fuelSub.Contains("fuelblock") || fuelSub.Contains("fuel_block")
                             || fuelSub.Contains("navalengine") || fuelSub.Contains("naval_engine"))
                            { category = "naval"; break; }
                        }
                    }
                    _fuelTypeCache[rDef.Id.SubtypeId.String] = category;
                }
            }
            catch { }
        }

        private HashSet<string> ParseNavalOverrides()
        {
            var tb = _block as IMyTerminalBlock;
            if (tb == null) return null;
            int idx = tb.CustomData.IndexOf("[TT_NAVAL=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            int eq  = tb.CustomData.IndexOf('=', idx);
            int end = tb.CustomData.IndexOf(']', idx);
            if (eq < 0 || end <= eq) return null;
            var parts = tb.CustomData.Substring(eq + 1, end - eq - 1).Split(',');
            var set   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in parts) { string s = p.Trim(); if (s.Length > 0) set.Add(s); }
            return set.Count > 0 ? set : null;
        }

        public EngPowerLcd(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _surface = surface;
            _block   = block;
        }

        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update100;

        public override void Run()
        {
            try
            {
                if (_block.CubeGrid.GridSizeEnum == MyCubeSize.Small)
                    DrawSmallGrid();
                else
                    DrawLargeGrid();
            }
            catch { }
        }

        // ════════════════════════════════════════════════════════════════════════
        // SMALL GRID — aircraft instrument panel
        // ════════════════════════════════════════════════════════════════════════

        private void DrawSmallGrid()
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
                "TT POWER \u2014 STATUS", ShipMapHelper.ColHeader,
                ShipMapHelper.FS_MAIN, TextAlignment.LEFT);
            ShipMapHelper.TXT(frame, x0 + w, curY,
                DateTime.Now.ToString("HH:mm"), ShipMapHelper.ColHeader,
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

            // ── Block scan ────────────────────────────────────────────────────
            float h2EngCurMW = 0f, h2EngMaxMW = 0f; int h2EngCount = 0;
            float navalCurMW = 0f, navalMaxMW = 0f; int navalCount  = 0;
            float solarCurMW = 0f, solarMaxMW = 0f; int solarCount  = 0;
            float windCurMW  = 0f, windMaxMW  = 0f; int windCount   = 0;
            float otherCurMW = 0f, otherMaxMW = 0f; int otherCount  = 0;

            int   h2TankCount = 0;
            float h2Stored    = 0f;
            float h2Max       = 0f;
            int   h2Stockpile = 0;

            _abList.Clear();
            _slims.Clear();
            try { _block.CubeGrid.GetBlocks(_slims); }
            catch (InvalidOperationException) { frame.Dispose(); return; }

            foreach (var slim in _slims)
            {
                var fat = slim.FatBlock;
                if (fat == null) continue;
                var  fb      = fat as IMyFunctionalBlock;
                bool working = fb != null && fb.IsWorking;

                var reactor = fat as IMyReactor;
                if (reactor != null)
                {
                    float cur = working ? reactor.CurrentOutput : 0f;
                    float max = working ? reactor.MaxOutput     : 0f;
                    string sub = fat.BlockDefinition.SubtypeId.ToLower();
                    if (sub.Contains("naval") || sub.Contains("engine") || sub.Contains("diesel"))
                    { navalCount++; navalCurMW += cur; navalMaxMW += max; }
                    else
                    { otherCount++; otherCurMW += cur; otherMaxMW += max; }
                    continue;
                }

                if (fat is IMyBatteryBlock) continue;

                var power = fat as IMyPowerProducer;
                if (power != null)
                {
                    float cur     = working ? power.CurrentOutput : 0f;
                    float max     = working ? power.MaxOutput     : 0f;
                    string typeId = fat.BlockDefinition.TypeId.ToString().ToLower();
                    if      (typeId.Contains("hydrogenengine")) { h2EngCount++; h2EngCurMW += cur; h2EngMaxMW += max; }
                    else if (typeId.Contains("solarpanel"))     { solarCount++; solarCurMW += cur; solarMaxMW += max; }
                    else if (typeId.Contains("windturbine"))    { windCount++;  windCurMW  += cur; windMaxMW  += max; }
                    else                                        { otherCount++; otherCurMW += cur; otherMaxMW += max; }
                    continue;
                }

                var gasTank = fat as IMyGasTank;
                if (gasTank != null)
                {
                    string subStr = fat.BlockDefinition.SubtypeId.ToLower();
                    bool isO2 = subStr.Contains("oxygen") || subStr.Contains("_oxy")
                             || subStr.StartsWith("oxy");
                    if (!isO2)
                    {
                        h2TankCount++;
                        h2Stored += (float)(gasTank.FilledRatio * gasTank.Capacity);
                        h2Max    += gasTank.Capacity;
                        if (gasTank.Stockpile) h2Stockpile++;
                    }
                    continue;
                }

                if (AfterburnerConfigs.Contains(fat.BlockDefinition.SubtypeId))
                {
                    var    ctrl = AfterburnerController.GetByEntityId(fat.EntityId);
                    bool   burn = ctrl != null && ctrl.GetState();
                    var    tb   = fat as IMyTerminalBlock;
                    string nm   = ShipMapHelper.Trunc(
                        tb != null ? tb.CustomName : fat.BlockDefinition.SubtypeId, 14);
                    _abList.Add(new KeyValuePair<string, bool>(nm, burn));
                }
            }

            // Totals
            float genCurMW  = h2EngCurMW + navalCurMW + solarCurMW + windCurMW + otherCurMW;
            float genMaxMW  = h2EngMaxMW + navalMaxMW + solarMaxMW + windMaxMW + otherMaxMW;
            var   eng       = EngineeringProcessor.GetForGrid(_block.CubeGrid);
            float loadCurMW = eng != null ? eng.PowerDrawMW   : genCurMW;
            float loadMaxMW = eng != null ? eng.PowerOutputMW : genMaxMW;
            float loadFrac  = loadMaxMW > 0f ? Math.Min(1f, loadCurMW / loadMaxMW) : 0f;
            float engFrac   = h2EngMaxMW > 0f ? Math.Min(1f, h2EngCurMW / h2EngMaxMW) : 0f;
            float fuelFrac  = h2Max > 0f ? Math.Min(1f, h2Stored / h2Max) : 0f;

            Color loadCol = loadFrac > 0.9f ? ShipMapHelper.ColError
                          : loadFrac > 0.7f ? ShipMapHelper.ColWarn
                          : ShipMapHelper.ColOk;
            Color engCol  = h2EngCount == 0 ? ShipMapHelper.ColDim
                          : engFrac > 0.9f  ? ShipMapHelper.ColError
                          : engFrac > 0.7f  ? ShipMapHelper.ColWarn
                          : ShipMapHelper.ColOk;
            Color fuelCol = h2TankCount == 0 ? ShipMapHelper.ColDim
                          : fuelFrac > 0.3f ? ShipMapHelper.ColOk
                          : fuelFrac > 0.1f ? ShipMapHelper.ColWarn
                          : ShipMapHelper.ColError;

            // ── 3-arc instrument row ──────────────────────────────────────────
            // Radius fits 3 circles side-by-side with gap, plus headroom for text inside.
            float R    = Math.Min(w * 0.13f, vp.Height * 0.14f);
            float bgR  = R + R * 0.22f * 0.65f;   // background circle radius (matches DrawArcGauge)
            float arcY = curY + bgR;               // arc centre Y: circle top sits at curY

            float arcX1 = x0 + w * (1f / 6f);
            float arcX2 = x0 + w * (3f / 6f);
            float arcX3 = x0 + w * (5f / 6f);

            DrawArcGauge(frame, arcX1, arcY, R, loadFrac,  loadCol);
            DrawArcGauge(frame, arcX2, arcY, R, engFrac,   engCol);
            DrawArcGauge(frame, arcX3, arcY, R, fuelFrac,  fuelCol);

            // Labels + readouts inside each gauge face
            DrawGaugeText(frame, arcX1, arcY, R, xsH,
                "LOAD",
                (loadFrac * 100f).ToString("F1") + "%",
                loadCol,
                FormatMW(loadCurMW));

            DrawGaugeText(frame, arcX2, arcY, R, xsH,
                "H2 ENG",
                h2EngCount > 0 ? (engFrac * 100f).ToString("F1") + "%" : "---",
                engCol,
                h2EngCount > 0 ? FormatMW(h2EngCurMW) : "no engines");

            DrawGaugeText(frame, arcX3, arcY, R, xsH,
                "H2 FUEL",
                h2TankCount > 0 ? (fuelFrac * 100f).ToString("F1") + "%" : "---",
                fuelCol,
                h2TankCount > 0 ? FormatVolume(h2Stored) : "no tanks");

            curY = arcY + bgR + 8f;
            ShipMapHelper.HLine(frame, x0 + w * 0.5f, curY, w, ShipMapHelper.ColSep);
            curY += 5f;

            // ── GENERATION ────────────────────────────────────────────────────
            bool anyGen = h2EngCount > 0 || navalCount > 0 || solarCount > 0
                       || windCount  > 0 || otherCount > 0;
            if (anyGen)
            {
                ShipMapHelper.TXT(frame, x0, curY,
                    "GENERATION", ShipMapHelper.ColHeader,
                    ShipMapHelper.FS_MAIN, TextAlignment.LEFT);
                ShipMapHelper.TXT(frame, x0 + w, curY,
                    FormatMW(genCurMW) + " / " + FormatMW(genMaxMW),
                    loadCol, ShipMapHelper.FS_MAIN, TextAlignment.RIGHT);
                curY += charH + 2f;

                if (h2EngCount > 0) SG_DrawGenRow(frame, x0, w, ref curY, xsH,
                    "H2 Engines (" + h2EngCount + ")", h2EngCurMW, h2EngMaxMW);
                if (navalCount > 0) SG_DrawGenRow(frame, x0, w, ref curY, xsH,
                    "Naval Eng (" + navalCount + ")", navalCurMW, navalMaxMW);
                if (solarCount > 0) SG_DrawGenRow(frame, x0, w, ref curY, xsH,
                    "Solar (" + solarCount + ")", solarCurMW, solarMaxMW);
                if (windCount  > 0) SG_DrawGenRow(frame, x0, w, ref curY, xsH,
                    "Wind (" + windCount + ")", windCurMW, windMaxMW);
                if (otherCount > 0) SG_DrawGenRow(frame, x0, w, ref curY, xsH,
                    "Other (" + otherCount + ")", otherCurMW, otherMaxMW);

                ShipMapHelper.HLine(frame, x0 + w * 0.5f, curY, w, ShipMapHelper.ColSep);
                curY += 5f;
            }

            // ── H2 FUEL BAR ───────────────────────────────────────────────────
            if (h2TankCount > 0)
            {
                string stockStr = h2Stockpile == h2TankCount ? "STOCKPILE"
                                : h2Stockpile > 0            ? "MIXED"
                                : "AUTO";
                Color stockCol  = h2Stockpile > 0 ? ShipMapHelper.ColWarn : ShipMapHelper.ColDim;

                ShipMapHelper.TXT(frame, x0, curY,
                    "H2 FUEL  (" + h2TankCount + ")", ShipMapHelper.ColHeader,
                    ShipMapHelper.FS_MAIN, TextAlignment.LEFT);
                ShipMapHelper.TXT(frame, x0 + w, curY,
                    (fuelFrac * 100f).ToString("F1") + "%  " + stockStr,
                    fuelFrac < 0.15f ? ShipMapHelper.ColError : stockCol,
                    ShipMapHelper.FS_MAIN, TextAlignment.RIGHT);
                curY += charH + 2f;

                SG_DrawFuelBar(frame, x0, curY, w, subH, fuelFrac, fuelCol);
                curY += subH + 2f;

                ShipMapHelper.TXT(frame, x0 + w * 0.5f, curY,
                    FormatVolume(h2Stored) + " / " + FormatVolume(h2Max),
                    ShipMapHelper.ColDim, ShipMapHelper.FS_XS, TextAlignment.CENTER);
                curY += xsH + 3f;

                ShipMapHelper.HLine(frame, x0 + w * 0.5f, curY, w, ShipMapHelper.ColSep);
                curY += 5f;
            }

            // ── AFTERBURNER ───────────────────────────────────────────────────
            if (_abList.Count > 0)
            {
                int activeAB = 0;
                foreach (var kv in _abList) if (kv.Value) activeAB++;

                ShipMapHelper.TXT(frame, x0, curY,
                    "AFTERBURNER", ShipMapHelper.ColHeader,
                    ShipMapHelper.FS_MAIN, TextAlignment.LEFT);
                ShipMapHelper.TXT(frame, x0 + w, curY,
                    "ACTIVE  " + activeAB + " / " + _abList.Count,
                    activeAB > 0 ? ShipMapHelper.ColOk : ShipMapHelper.ColDim,
                    ShipMapHelper.FS_MAIN, TextAlignment.RIGHT);
                curY += charH + 2f;

                int   show  = Math.Min(_abList.Count, 8);
                float cellH = subH + 2f;
                float cellW = (w - (show - 1) * 2f) / show;

                for (int i = 0; i < show; i++)
                {
                    var   kv  = _abList[i];
                    bool  on  = kv.Value;
                    float ccx = x0 + i * (cellW + 2f) + cellW * 0.5f;
                    ShipMapHelper.Rect(frame, ccx, curY + cellH * 0.5f, cellW, cellH,
                        on ? ShipMapHelper.ColOk : ShipMapHelper.ColMapBg);
                    ShipMapHelper.TXT(frame, ccx, curY + (cellH - xsH) * 0.5f,
                        on ? "BURN" : "OFF",
                        on ? ShipMapHelper.ColBg : ShipMapHelper.ColDim,
                        ShipMapHelper.FS_XS, TextAlignment.CENTER);
                }
                if (_abList.Count > 8)
                {
                    ShipMapHelper.TXT(frame, x0 + w, curY + (cellH - xsH) * 0.5f,
                        "+" + (_abList.Count - 8) + " more",
                        ShipMapHelper.ColDim, ShipMapHelper.FS_XS, TextAlignment.RIGHT);
                }
                curY += cellH + 3f;
            }

            DrawFooter(frame, vp, w, subH);
            frame.Dispose();
        }

        // ── Arc gauge (shared) ─────────────────────────────────────────────────
        // Draws ARC_TICKS tick marks around a radial 300° arc.
        // t ≤ frac ticks are coloured green→orange→red; the rest are ColDim.
        // Every 6th tick and the final tick are rendered as major marks.

        private void DrawArcGauge(MySpriteDrawFrame frame,
            float cx, float cy, float R, float frac, Color fillBase)
        {
            float tickLen = R * 0.22f;
            float tickW   = Math.Max(1.5f, R * 0.032f);
            float tickR   = R - tickLen * 0.5f;   // tick centres at R from origin edge

            float startRad = ARC_START_DEG * (float)Math.PI / 180f;
            float spanRad  = ARC_SPAN_DEG  * (float)Math.PI / 180f;

            // Dark circular face
            float bgR = R + tickLen * 0.65f;
            frame.Add(new MySprite(SpriteType.TEXTURE, "Circle",
                new Vector2(cx, cy), new Vector2(bgR * 2f, bgR * 2f),
                ShipMapHelper.ColMapBg));

            for (int i = 0; i < ARC_TICKS; i++)
            {
                float t        = (float)i / (ARC_TICKS - 1);
                float angleRad = startRad + t * spanRad;
                float px       = cx + tickR * (float)Math.Cos(angleRad);
                float py       = cy + tickR * (float)Math.Sin(angleRad);

                Color col = t <= frac
                    ? (t > 0.9f ? ShipMapHelper.ColError
                     : t > 0.7f ? ShipMapHelper.ColWarn
                     : ShipMapHelper.ColOk)
                    : ShipMapHelper.ColDim;

                bool  major = (i % 6 == 0) || (i == ARC_TICKS - 1);
                float tLen  = major ? tickLen * 1.35f : tickLen;
                float tW    = major ? tickW   * 1.55f : tickW;

                // SquareSimple rotation: width-axis aligns with +X at 0 rad.
                // Passing angleRad makes the width axis point radially outward.
                frame.Add(new MySprite(
                    SpriteType.TEXTURE, "SquareSimple",
                    new Vector2(px, py), new Vector2(tLen, tW),
                    col, null, TextAlignment.CENTER, angleRad));
            }

            ShipMapHelper.Rect(frame, cx, cy, R * 0.07f, R * 0.07f, ShipMapHelper.ColDim);
        }

        // ── Gauge text (label, %, value drawn inside the arc face) ───────────

        private void DrawGaugeText(MySpriteDrawFrame frame, float cx, float cy, float R,
            float xsH, string label, string pctStr, Color pctCol, string valueStr)
        {
            float pctH = _surface.MeasureStringInPixels(new StringBuilder("88.8%"),
                             ShipMapHelper.FONT, ShipMapHelper.FS_MAIN).Y;

            ShipMapHelper.TXT(frame, cx, cy - pctH * 0.5f - xsH - 1f,
                label, ShipMapHelper.ColDim, ShipMapHelper.FS_XS, TextAlignment.CENTER);

            ShipMapHelper.TXT(frame, cx, cy - pctH * 0.5f,
                pctStr, pctCol, ShipMapHelper.FS_MAIN, TextAlignment.CENTER);

            ShipMapHelper.TXT(frame, cx, cy + pctH * 0.5f + 2f,
                valueStr, ShipMapHelper.ColValue, ShipMapHelper.FS_XS, TextAlignment.CENTER);
        }

        // ── SG compact generation row ─────────────────────────────────────────

        private void SG_DrawGenRow(MySpriteDrawFrame frame, float x, float w,
            ref float y, float rowH, string label, float curMW, float maxMW)
        {
            float labelW = w * 0.42f;
            float barW   = w * 0.28f;
            float barX   = x + labelW + w * 0.02f;

            ShipMapHelper.TXT(frame, x + 2f, y,
                label, ShipMapHelper.ColLabel, ShipMapHelper.FS_XS, TextAlignment.LEFT);

            float frac   = maxMW > 0f ? curMW / maxMW : 0f;
            Color barCol = curMW > 0f
                ? (frac > 0.85f ? ShipMapHelper.ColOk : ShipMapHelper.ColWarn)
                : ShipMapHelper.ColDim;

            ShipMapHelper.Rect(frame, barX + barW * 0.5f, y + rowH * 0.5f,
                barW, rowH - 1f, ShipMapHelper.ColMapBg);
            if (frac > 0f)
            {
                float fw = Math.Max(1f, barW * Math.Min(1f, frac));
                ShipMapHelper.Rect(frame, barX + fw * 0.5f, y + rowH * 0.5f,
                    fw, rowH - 1f, barCol);
            }

            ShipMapHelper.TXT(frame, x + w, y,
                FormatMW(curMW), ShipMapHelper.ColValue, ShipMapHelper.FS_XS, TextAlignment.RIGHT);

            y += rowH + 1f;
        }

        // ── SG fuel bar — segmented with tick dividers every 10% ─────────────

        private void SG_DrawFuelBar(MySpriteDrawFrame frame, float x, float y,
            float w, float h, float frac, Color fillCol)
        {
            float cx = x + w * 0.5f;
            float cy = y + h * 0.5f;
            ShipMapHelper.Rect(frame, cx, cy, w, h, ShipMapHelper.ColMapBg);
            if (frac > 0f)
            {
                float fw = Math.Max(1f, w * Math.Min(1f, frac));
                ShipMapHelper.Rect(frame, x + fw * 0.5f, cy, fw, h, fillCol);
            }
            for (int i = 1; i < 10; i++)
                ShipMapHelper.Rect(frame, x + w * (i * 0.1f), cy, 1.5f, h, ShipMapHelper.ColBg);
            ShipMapHelper.TXT(frame, cx, y,
                (frac * 100f).ToString("F1") + "%",
                ShipMapHelper.ColValue, ShipMapHelper.FS_XS, TextAlignment.CENTER);
        }

        // ════════════════════════════════════════════════════════════════════════
        // LARGE GRID — full power management view
        // ════════════════════════════════════════════════════════════════════════

        private struct PowerGroup
        {
            public string Label;
            public int    Count;
            public float  CurrentMW;
            public float  MaxMW;
            public float  FuelAmt;
            public string FuelSuffix;
        }

        private struct BattData
        {
            public int   Count, OffCount, AutoCount, ChargeCount, DischargeCount;
            public float StoredMWh, MaxMWh;
        }

        private struct TankData
        {
            public int   Count, StockpileCount;
            public float StoredL, MaxL;
            public string Label;
        }

        private void DrawLargeGrid()
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

            ShipMapHelper.TXT(frame, x0, curY,
                "TT POWER \u2014 MANAGEMENT", ShipMapHelper.ColHeader,
                ShipMapHelper.FS_MAIN, TextAlignment.LEFT);
            ShipMapHelper.TXT(frame, x0 + w, curY,
                DateTime.Now.ToString("HH:mm:ss"), ShipMapHelper.ColHeader,
                ShipMapHelper.FS_MAIN, TextAlignment.RIGHT);
            curY += charH;
            ShipMapHelper.HLine(frame, x0 + w * 0.5f, curY, w, ShipMapHelper.ColHeader);
            curY += 4f;

            ShipMapHelper.Rect(frame, x0 + w * 0.5f, curY + subH * 0.5f,
                w, subH + 2f, ShipMapHelper.ColRowBg);
            ShipMapHelper.TXT(frame, x0 + 4f, curY,
                _block.CubeGrid.DisplayName ?? "Unknown",
                ShipMapHelper.ColValue, ShipMapHelper.FS_SUB, TextAlignment.LEFT);
            curY += subH + 2f;
            ShipMapHelper.HLine(frame, x0 + w * 0.5f, curY, w, ShipMapHelper.ColSep);
            curY += 5f;

            PowerGroup nuclear = new PowerGroup { Label = "Nuclear Reactors", FuelSuffix = "" };
            PowerGroup naval   = new PowerGroup { Label = "Naval Engines",    FuelSuffix = "" };
            PowerGroup h2eng   = new PowerGroup { Label = "Hydrogen Engines", FuelSuffix = "" };
            PowerGroup solar   = new PowerGroup { Label = "Solar Panels",     FuelSuffix = "" };
            PowerGroup wind    = new PowerGroup { Label = "Wind Turbines",    FuelSuffix = "" };
            PowerGroup other   = new PowerGroup { Label = "Other Power",      FuelSuffix = "" };
            BattData   batt    = new BattData();
            TankData   h2tank  = new TankData { Label = "H2 TANKS"  };
            TankData   o2tank  = new TankData { Label = "O2 TANKS"  };
            TankData   gastank = new TankData { Label = "FUEL TANKS" };

            var navalOverrides = ParseNavalOverrides();

            _slims.Clear();
            try { _block.CubeGrid.GetBlocks(_slims); }
            catch (InvalidOperationException) { frame.Dispose(); return; }

            foreach (var slim in _slims)
            {
                var fat = slim.FatBlock;
                if (fat == null) continue;
                var  fb      = fat as IMyFunctionalBlock;
                bool working = fb != null && fb.IsWorking;

                var reactor = fat as IMyReactor;
                if (reactor != null)
                {
                    float cur = working ? reactor.CurrentOutput : 0f;
                    float max = working ? reactor.MaxOutput     : 0f;
                    if (GetReactorCategory(reactor, navalOverrides) == "naval")
                    { naval.Count++;   naval.CurrentMW   += cur; naval.MaxMW   += max; }
                    else
                    { nuclear.Count++; nuclear.CurrentMW += cur; nuclear.MaxMW += max; }
                    continue;
                }

                var battery = fat as IMyBatteryBlock;
                if (battery != null)
                {
                    batt.Count++;
                    if (working)
                    {
                        batt.StoredMWh += battery.CurrentStoredPower;
                        batt.MaxMWh    += battery.MaxStoredPower;
                        switch (battery.ChargeMode)
                        {
                            case Sandbox.ModAPI.Ingame.ChargeMode.Auto:
                                batt.AutoCount++;      break;
                            case Sandbox.ModAPI.Ingame.ChargeMode.Recharge:
                                batt.ChargeCount++;    break;
                            case Sandbox.ModAPI.Ingame.ChargeMode.Discharge:
                                batt.DischargeCount++; break;
                        }
                    }
                    else batt.OffCount++;
                    continue;
                }

                var power = fat as IMyPowerProducer;
                if (power != null)
                {
                    float cur     = working ? power.CurrentOutput : 0f;
                    float max     = working ? power.MaxOutput     : 0f;
                    string typeId = fat.BlockDefinition.TypeId.ToString().ToLower();
                    if      (typeId.Contains("hydrogenengine")) { h2eng.Count++; h2eng.CurrentMW += cur; h2eng.MaxMW += max; }
                    else if (typeId.Contains("solarpanel"))     { solar.Count++; solar.CurrentMW += cur; solar.MaxMW += max; }
                    else if (typeId.Contains("windturbine"))    { wind.Count++;  wind.CurrentMW  += cur; wind.MaxMW  += max; }
                    else                                        { other.Count++; other.CurrentMW += cur; other.MaxMW += max; }
                    continue;
                }

                var gasTank = fat as IMyGasTank;
                if (gasTank != null)
                {
                    string typeStr = fat.BlockDefinition.TypeId.ToString().ToLower();
                    string subStr  = fat.BlockDefinition.SubtypeId.ToLower();
                    float stored   = (float)(gasTank.FilledRatio * gasTank.Capacity);
                    float cap      = gasTank.Capacity;
                    bool  stock    = gasTank.Stockpile;

                    bool isO2 = subStr.Contains("oxygen") || subStr.Contains("_oxy")
                             || subStr.StartsWith("oxy");
                    bool isH2 = subStr.Contains("hydrogen") || subStr.Contains("h2")
                             || subStr.Contains("hyd")      || subStr.Contains("hydro");

                    if (isO2)
                    { o2tank.Count++; o2tank.StoredL += stored; o2tank.MaxL += cap; if (stock) o2tank.StockpileCount++; }
                    else if (isH2 || typeStr.Contains("oxygentank"))
                    { h2tank.Count++; h2tank.StoredL += stored; h2tank.MaxL += cap; if (stock) h2tank.StockpileCount++; }
                    else
                    { gastank.Count++; gastank.StoredL += stored; gastank.MaxL += cap; if (stock) gastank.StockpileCount++; }
                }
            }

            float totalCurMW = nuclear.CurrentMW + naval.CurrentMW + h2eng.CurrentMW
                             + solar.CurrentMW   + wind.CurrentMW  + other.CurrentMW;
            float totalMaxMW = nuclear.MaxMW + naval.MaxMW + h2eng.MaxMW
                             + solar.MaxMW   + wind.MaxMW  + other.MaxMW;

            var   engLG      = EngineeringProcessor.GetForGrid(_block.CubeGrid);
            float loadCurMW  = engLG != null ? engLG.PowerDrawMW   : totalCurMW;
            float loadMaxMW  = engLG != null ? engLG.PowerOutputMW : totalMaxMW;

            // GENERATION
            LG_DrawSectionHeader(frame, x0, w, ref curY, charH,
                "GENERATION",
                FormatMW(totalCurMW) + " / " + FormatMW(totalMaxMW),
                ShipMapHelper.ColHeader);
            curY += 2f;

            bool anySource = false;
            if (nuclear.Count > 0) { LG_DrawSourceRow(frame, x0, w, ref curY, subH, nuclear); anySource = true; }
            if (naval.Count   > 0) { LG_DrawSourceRow(frame, x0, w, ref curY, subH, naval);   anySource = true; }
            if (h2eng.Count   > 0) { LG_DrawSourceRow(frame, x0, w, ref curY, subH, h2eng);   anySource = true; }
            if (solar.Count   > 0) { LG_DrawSourceRow(frame, x0, w, ref curY, subH, solar);   anySource = true; }
            if (wind.Count    > 0) { LG_DrawSourceRow(frame, x0, w, ref curY, subH, wind);    anySource = true; }
            if (other.Count   > 0) { LG_DrawSourceRow(frame, x0, w, ref curY, subH, other);   anySource = true; }
            if (!anySource)
            {
                ShipMapHelper.TXT(frame, x0 + 4f, curY, "No power sources found",
                    ShipMapHelper.ColDim, ShipMapHelper.FS_SUB, TextAlignment.LEFT);
                curY += subH;
            }

            curY += 2f;
            ShipMapHelper.HLine(frame, x0 + w * 0.5f, curY, w, ShipMapHelper.ColSep);
            curY += 5f;

            // LOAD
            float loadFrac = loadMaxMW > 0f ? loadCurMW / loadMaxMW : 0f;
            Color loadCol  = loadFrac > 0.9f ? ShipMapHelper.ColError
                           : loadFrac > 0.7f ? ShipMapHelper.ColWarn
                           : ShipMapHelper.ColOk;
            LG_DrawSectionHeader(frame, x0, w, ref curY, charH,
                "LOAD",
                FormatMW(loadCurMW) + " / " + FormatMW(loadMaxMW),
                loadCol);
            curY += 2f;
            LG_DrawInlineBar(frame, x0, curY, w, subH, loadFrac, loadCol,
                (loadFrac * 100f).ToString("F1") + "%");
            curY += subH + 5f;
            ShipMapHelper.HLine(frame, x0 + w * 0.5f, curY, w, ShipMapHelper.ColSep);
            curY += 5f;

            // BATTERIES
            if (batt.Count > 0)
            {
                float battFrac = batt.MaxMWh > 0f ? batt.StoredMWh / batt.MaxMWh : 0f;
                Color battCol  = battFrac > 0.3f ? ShipMapHelper.ColOk
                               : battFrac > 0.1f ? ShipMapHelper.ColWarn
                               : ShipMapHelper.ColError;
                int   activeCount = batt.Count - batt.OffCount;
                string modeStr; Color modeCol;
                if      (batt.OffCount       == batt.Count)  { modeStr = "ALL OFF";   modeCol = ShipMapHelper.ColDim;   }
                else if (batt.ChargeCount    == activeCount)  { modeStr = "CHARGE";    modeCol = ShipMapHelper.ColWarn;  }
                else if (batt.DischargeCount == activeCount)  { modeStr = "DISCHARGE"; modeCol = ShipMapHelper.ColError; }
                else if (batt.AutoCount      == activeCount)  { modeStr = "AUTO";      modeCol = ShipMapHelper.ColDim;   }
                else                                          { modeStr = "MIXED";     modeCol = ShipMapHelper.ColWarn;  }

                string battCountStr = batt.OffCount > 0
                    ? (batt.Count - batt.OffCount) + "/" + batt.Count + " ON"
                    : batt.Count.ToString();
                string battHeader = "BATTERIES  (" + battCountStr + ")  ";
                LG_DrawSectionHeader(frame, x0, w, ref curY, charH,
                    battHeader,
                    batt.OffCount == batt.Count ? "OFFLINE"
                        : FormatMWh(batt.StoredMWh) + " / " + FormatMWh(batt.MaxMWh),
                    batt.OffCount == batt.Count ? ShipMapHelper.ColDim : battCol);
                float modeX = x0 + _surface.MeasureStringInPixels(
                    new StringBuilder(battHeader), ShipMapHelper.FONT, ShipMapHelper.FS_MAIN).X;
                ShipMapHelper.TXT(frame, modeX, curY - charH, modeStr, modeCol,
                    ShipMapHelper.FS_MAIN, TextAlignment.LEFT);

                curY += 2f;
                LG_DrawInlineBar(frame, x0, curY, w, subH, battFrac, battCol,
                    (battFrac * 100f).ToString("F1") + "%");
                curY += subH + 5f;
                ShipMapHelper.HLine(frame, x0 + w * 0.5f, curY, w, ShipMapHelper.ColSep);
                curY += 5f;
            }

            // GAS TANKS
            LG_DrawTankSection(frame, x0, w, ref curY, charH, subH, h2tank);
            LG_DrawTankSection(frame, x0, w, ref curY, charH, subH, o2tank);
            LG_DrawTankSection(frame, x0, w, ref curY, charH, subH, gastank);

            DrawFooter(frame, vp, w, subH);
            frame.Dispose();
        }

        // ── Large-grid helpers ────────────────────────────────────────────────

        private void LG_DrawSectionHeader(MySpriteDrawFrame frame, float x, float w,
            ref float y, float charH, string label, string value, Color valueCol)
        {
            ShipMapHelper.TXT(frame, x,     y, label, ShipMapHelper.ColHeader,
                ShipMapHelper.FS_MAIN, TextAlignment.LEFT);
            ShipMapHelper.TXT(frame, x + w, y, value, valueCol,
                ShipMapHelper.FS_MAIN, TextAlignment.RIGHT);
            y += charH;
        }

        private void LG_DrawSourceRow(MySpriteDrawFrame frame, float x, float w,
            ref float y, float rowH, PowerGroup g)
        {
            float labelW = w * 0.40f;
            float barW   = w * 0.26f;
            float mwW    = w * 0.20f;
            float barX   = x + labelW + w * 0.02f;
            float mwRX   = barX + barW + w * 0.02f + mwW;
            float fuelRX = x + w;

            ShipMapHelper.Rect(frame, x + w * 0.5f, y + rowH * 0.5f,
                w, rowH, ShipMapHelper.ColRowBg);
            ShipMapHelper.TXT(frame, x + 4f, y,
                g.Label + "  (" + g.Count + ")",
                ShipMapHelper.ColLabel, ShipMapHelper.FS_SUB, TextAlignment.LEFT);

            float frac   = g.MaxMW > 0f ? g.CurrentMW / g.MaxMW : 0f;
            Color barCol = g.CurrentMW > 0f
                ? (frac > 0.85f ? ShipMapHelper.ColOk : ShipMapHelper.ColWarn)
                : ShipMapHelper.ColDim;
            LG_DrawBar(frame, barX, y + 1f, barW, rowH - 2f, frac, barCol, ShipMapHelper.ColMapBg);

            ShipMapHelper.TXT(frame, mwRX, y,
                FormatMW(g.CurrentMW), ShipMapHelper.ColValue,
                ShipMapHelper.FS_SUB, TextAlignment.RIGHT);

            if (g.FuelSuffix.Length > 0 && g.FuelAmt > 0f)
                ShipMapHelper.TXT(frame, fuelRX, y,
                    FormatFuel(g.FuelAmt) + " " + g.FuelSuffix,
                    ShipMapHelper.ColDim, ShipMapHelper.FS_XS, TextAlignment.RIGHT);
            else if (g.FuelSuffix.Length > 0 && g.FuelAmt <= 0f && g.Count > 0)
                ShipMapHelper.TXT(frame, fuelRX, y, "no fuel",
                    ShipMapHelper.ColError, ShipMapHelper.FS_XS, TextAlignment.RIGHT);

            y += rowH;
            ShipMapHelper.Rect(frame, x + w * 0.5f, y, w, 1f, ShipMapHelper.ColSep);
            y += 2f;
        }

        private void LG_DrawInlineBar(MySpriteDrawFrame frame, float x, float y,
            float w, float h, float frac, Color fillCol, string pctLabel)
        {
            float cx = x + w * 0.5f;
            float cy = y + h * 0.5f;
            ShipMapHelper.Rect(frame, cx, cy, w, h, ShipMapHelper.ColMapBg);
            if (frac > 0f)
            {
                float fw  = Math.Max(1f, w * Math.Min(1f, frac));
                ShipMapHelper.Rect(frame, x + fw * 0.5f, cy, fw, h, fillCol);
            }
            ShipMapHelper.TXT(frame, x + w * 0.5f, y, pctLabel,
                ShipMapHelper.ColValue, ShipMapHelper.FS_XS, TextAlignment.CENTER);
        }

        private static void LG_DrawBar(MySpriteDrawFrame frame, float x, float y,
            float w, float h, float frac, Color fillCol, Color bgCol)
        {
            float cx = x + w * 0.5f;
            float cy = y + h * 0.5f;
            ShipMapHelper.Rect(frame, cx, cy, w, h, bgCol);
            if (frac > 0f)
            {
                float fw = Math.Max(1f, w * Math.Min(1f, frac));
                ShipMapHelper.Rect(frame, x + fw * 0.5f, cy, fw, h, fillCol);
            }
        }

        private void LG_DrawTankSection(MySpriteDrawFrame frame, float x0, float w,
            ref float curY, float charH, float subH, TankData tank)
        {
            if (tank.Count == 0) return;

            float frac = tank.MaxL > 0f ? tank.StoredL / tank.MaxL : 0f;
            Color col  = frac > 0.3f ? ShipMapHelper.ColOk
                       : frac > 0.1f ? ShipMapHelper.ColWarn
                       : ShipMapHelper.ColError;
            string stockStr; Color stockCol;
            if      (tank.StockpileCount == tank.Count) { stockStr = "STOCKPILE"; stockCol = ShipMapHelper.ColWarn; }
            else if (tank.StockpileCount == 0)          { stockStr = "AUTO";      stockCol = ShipMapHelper.ColDim;  }
            else                                        { stockStr = "MIXED";     stockCol = ShipMapHelper.ColWarn; }

            string header = tank.Label + "  (" + tank.Count + ")  ";
            LG_DrawSectionHeader(frame, x0, w, ref curY, charH,
                header,
                FormatVolume(tank.StoredL) + " / " + FormatVolume(tank.MaxL) + "  "
                    + (frac * 100f).ToString("F0") + "%",
                col);
            float stockX = x0 + _surface.MeasureStringInPixels(
                new StringBuilder(header), ShipMapHelper.FONT, ShipMapHelper.FS_MAIN).X;
            ShipMapHelper.TXT(frame, stockX, curY - charH, stockStr, stockCol,
                ShipMapHelper.FS_MAIN, TextAlignment.LEFT);

            curY += 2f;
            LG_DrawInlineBar(frame, x0, curY, w, subH, frac, col,
                (frac * 100f).ToString("F1") + "%");
            curY += subH + 5f;
            ShipMapHelper.HLine(frame, x0 + w * 0.5f, curY, w, ShipMapHelper.ColSep);
            curY += 5f;
        }

        // ── Shared footer ─────────────────────────────────────────────────────

        private void DrawFooter(MySpriteDrawFrame frame, RectangleF vp, float w, float subH)
        {
            float y = vp.Y + vp.Height - subH - MARGIN;
            ShipMapHelper.HLine(frame, vp.X + MARGIN + w * 0.5f, y, w, ShipMapHelper.ColHeader);
            y += 4f;
            ShipMapHelper.TXT(frame, vp.X + MARGIN, y,
                "Terran Titans Naval Advanced Systems", ShipMapHelper.ColFooter,
                ShipMapHelper.FS_SUB, TextAlignment.LEFT);
        }

        // ── Format helpers ────────────────────────────────────────────────────

        private static string FormatMW(float mw)
        {
            if (mw >= 1000f)  return (mw / 1000f).ToString("F2") + " GW";
            if (mw >= 1f)     return mw.ToString("F2")            + " MW";
            if (mw >= 0.001f) return (mw * 1000f).ToString("F1") + " kW";
            return "0 kW";
        }

        private static string FormatMWh(float mwh)
        {
            if (mwh >= 1000f)  return (mwh / 1000f).ToString("F2") + " GWh";
            if (mwh >= 1f)     return mwh.ToString("F2")            + " MWh";
            if (mwh >= 0.001f) return (mwh * 1000f).ToString("F1") + " kWh";
            return "0 kWh";
        }

        private static string FormatVolume(float liters)
        {
            if (liters >= 1000000f) return (liters / 1000000f).ToString("F1") + " ML";
            if (liters >= 1000f)    return (liters / 1000f).ToString("F1")    + " kL";
            return liters.ToString("F0") + " L";
        }

        private static string FormatFuel(float amount)
        {
            if (amount >= 1000000f) return (amount / 1000000f).ToString("F1") + "M";
            if (amount >= 1000f)    return (amount / 1000f).ToString("F1")    + "k";
            return amount.ToString("F1");
        }
    }
}
