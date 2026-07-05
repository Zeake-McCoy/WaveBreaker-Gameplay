using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using IMyTextSurface    = Sandbox.ModAPI.Ingame.IMyTextSurface;
using MyInventoryItem   = VRage.Game.ModAPI.Ingame.MyInventoryItem;

namespace TTNAS
{
    // ───────────────────────────────────────────────────────────────────────────
    // EngAmmoCargoLcd — Ammunition inventory and cargo fill display.
    //
    // Sections:
    //   CARGO  — total fill %, fill bar, per-container rows (up to 8)
    //   AMMO   — ammo magazines grouped by subtype, sorted by count descending
    //
    // Container grouping (custom data on each cargo container):
    //   [TT_GROUP=GroupName]
    //   Containers sharing the same group name are merged into one row showing
    //   combined volume and container count, e.g. "Ammo Storage (4)".
    //   Containers without this tag are shown individually.
    //   Grouped rows appear first (sorted alphabetically), then individual rows.
    //
    // LCD script ID: TTNB_EngAmmoCargo  |  "TT Ammo & Cargo"
    // Update:        Update100 (~1.67 s)
    // ───────────────────────────────────────────────────────────────────────────
    [MyTextSurfaceScript("TTNB_EngAmmoCargo", "TT ENG \u2014 Ammo \u0026 Cargo")]
    public class EngAmmoCargoLcd : MyTSSCommon
    {
        private readonly IMyTextSurface     _surface;
        private readonly IMyCubeBlock       _block;
        private readonly List<IMySlimBlock> _slims = new List<IMySlimBlock>();

        private const float MARGIN   = 8f;
        private const float LINE_PAD = 1f;
        private const int   MAX_CARGO_ROWS = 8;
        private const int   MAX_AMMO_ROWS  = 12;

        private const string AMMO_TYPEID = "MyObjectBuilder_AmmoMagazine";

        private struct CargoEntry
        {
            public string Name;
            public float  Current;
            public float  Max;
            public int    Count;    // >1 means this is a merged group row
        }

        public EngAmmoCargoLcd(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
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
                "TT AMMO \u0026 CARGO", ShipMapHelper.ColHeader,
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

            // ── Scan all blocks ────────────────────────────────────────────────
            _slims.Clear();
            try { _block.CubeGrid.GetBlocks(_slims); }
            catch (InvalidOperationException) { frame.Dispose(); return; }

            // grouped: group name → accumulated entry
            var groupedCargo   = new Dictionary<string, CargoEntry>(StringComparer.OrdinalIgnoreCase);
            // individual containers that have no [TT_GROUP=] tag
            var individualCargo = new List<CargoEntry>();
            float totalCargoVol = 0f;
            float totalCargoMax = 0f;

            var ammoTotals = new Dictionary<string, long>(StringComparer.Ordinal);
            var invItems   = new List<MyInventoryItem>();

            foreach (var slim in _slims)
            {
                var fat = slim.FatBlock;
                if (fat == null || !fat.HasInventory) continue;

                bool isCargo = fat is IMyCargoContainer;

                for (int i = 0; i < fat.InventoryCount; i++)
                {
                    var inv = fat.GetInventory(i);
                    if (inv == null) continue;

                    float cur = (float)(MyFixedPoint)inv.CurrentVolume;
                    float max = (float)(MyFixedPoint)inv.MaxVolume;

                    if (isCargo && i == 0)
                    {
                        totalCargoVol += cur;
                        totalCargoMax += max;

                        var tb = fat as IMyTerminalBlock;
                        string groupName = tb != null ? ParseGroupTag(tb.CustomData) : null;

                        if (groupName != null)
                        {
                            CargoEntry grp;
                            groupedCargo.TryGetValue(groupName, out grp);
                            grp.Name     = groupName;
                            grp.Current += cur;
                            grp.Max     += max;
                            grp.Count++;
                            groupedCargo[groupName] = grp;
                        }
                        else
                        {
                            individualCargo.Add(new CargoEntry
                            {
                                Name    = tb != null ? TruncateName(tb.CustomName, 22) : "Container",
                                Current = cur,
                                Max     = max,
                                Count   = 1,
                            });
                        }
                    }

                    // Collect ammo magazines from every inventory
                    invItems.Clear();
                    inv.GetItems(invItems);
                    foreach (var item in invItems)
                    {
                        if (item.Type.TypeId != AMMO_TYPEID) continue;
                        string sub = item.Type.SubtypeId;
                        long  existing;
                        ammoTotals.TryGetValue(sub, out existing);
                        ammoTotals[sub] = existing + (long)(float)(MyFixedPoint)item.Amount;
                    }
                }
            }

            // Build final display list: groups first (alpha), then individuals
            var cargoEntries = new List<CargoEntry>();
            var sortedGroups = new List<KeyValuePair<string, CargoEntry>>(groupedCargo);
            sortedGroups.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
            foreach (var kv in sortedGroups)
                cargoEntries.Add(kv.Value);
            cargoEntries.AddRange(individualCargo);

            // ── CARGO section ─────────────────────────────────────────────────
            float totalFrac = totalCargoMax > 0f ? totalCargoVol / totalCargoMax : 0f;
            Color totalCol  = totalFrac > 0.9f ? ShipMapHelper.ColError
                            : totalFrac > 0.7f ? ShipMapHelper.ColWarn
                            : ShipMapHelper.ColOk;

            ShipMapHelper.TXT(frame, x0, curY, "CARGO", ShipMapHelper.ColHeader,
                ShipMapHelper.FS_MAIN, TextAlignment.LEFT);
            ShipMapHelper.TXT(frame, x0 + w, curY,
                FormatVol(totalCargoVol) + " / " + FormatVol(totalCargoMax),
                totalCol, ShipMapHelper.FS_MAIN, TextAlignment.RIGHT);
            curY += charH;

            // Total fill bar
            DrawBar(frame, x0, curY, w, subH, totalFrac, totalCol,
                (totalFrac * 100f).ToString("F1") + "%");
            curY += subH + 3f;

            int shownCargo = 0;
            foreach (var entry in cargoEntries)
            {
                if (shownCargo >= MAX_CARGO_ROWS) break;

                float frac = entry.Max > 0f ? entry.Current / entry.Max : 0f;
                Color col  = frac > 0.9f ? ShipMapHelper.ColError
                           : frac > 0.7f ? ShipMapHelper.ColWarn
                           : ShipMapHelper.ColOk;

                ShipMapHelper.Rect(frame, x0 + w * 0.5f, curY + subH * 0.5f,
                    w, subH, ShipMapHelper.ColRowBg);

                // Name column (left 55%) — grouped rows get "(N)" suffix
                string displayName = entry.Count > 1
                    ? TruncateName(entry.Name, 18) + " (" + entry.Count + ")"
                    : entry.Name;
                float nameW = w * 0.55f;
                ShipMapHelper.TXT(frame, x0 + 4f, curY, displayName,
                    entry.Count > 1 ? ShipMapHelper.ColValue : ShipMapHelper.ColLabel,
                    ShipMapHelper.FS_SUB, TextAlignment.LEFT);

                // Mini bar (next 25%)
                float barX = x0 + nameW + 4f;
                float barW = w * 0.25f;
                DrawBarRaw(frame, barX, curY + 1f, barW, subH - 2f, frac, col);

                // Pct (right)
                ShipMapHelper.TXT(frame, x0 + w, curY,
                    (frac * 100f).ToString("F0") + "%",
                    col, ShipMapHelper.FS_SUB, TextAlignment.RIGHT);

                curY += subH;
                ShipMapHelper.Rect(frame, x0 + w * 0.5f, curY, w, 1f, ShipMapHelper.ColSep);
                curY += 2f;
                shownCargo++;
            }

            if (cargoEntries.Count > MAX_CARGO_ROWS)
            {
                ShipMapHelper.TXT(frame, x0 + 4f, curY,
                    "  +" + (cargoEntries.Count - MAX_CARGO_ROWS) + " more...",
                    ShipMapHelper.ColDim, ShipMapHelper.FS_SUB, TextAlignment.LEFT);
                curY += subH;
            }

            if (cargoEntries.Count == 0)
            {
                ShipMapHelper.TXT(frame, x0 + 4f, curY, "No cargo containers found",
                    ShipMapHelper.ColDim, ShipMapHelper.FS_SUB, TextAlignment.LEFT);
                curY += subH;
            }

            curY += 3f;
            ShipMapHelper.HLine(frame, x0 + w * 0.5f, curY, w, ShipMapHelper.ColSep);
            curY += 5f;

            // ── AMMO section ──────────────────────────────────────────────────
            ShipMapHelper.TXT(frame, x0, curY, "AMMO", ShipMapHelper.ColHeader,
                ShipMapHelper.FS_MAIN, TextAlignment.LEFT);
            ShipMapHelper.TXT(frame, x0 + w, curY,
                ammoTotals.Count + " type(s)",
                ShipMapHelper.ColLabel, ShipMapHelper.FS_MAIN, TextAlignment.RIGHT);
            curY += charH;

            if (ammoTotals.Count == 0)
            {
                ShipMapHelper.TXT(frame, x0 + 4f, curY, "No ammunition found",
                    ShipMapHelper.ColDim, ShipMapHelper.FS_SUB, TextAlignment.LEFT);
                curY += subH;
            }
            else
            {
                // Sort descending by count
                var sortedAmmo = new List<KeyValuePair<string, long>>(ammoTotals);
                sortedAmmo.Sort((a, b) => b.Value.CompareTo(a.Value));

                int shown = 0;
                foreach (var kv in sortedAmmo)
                {
                    if (shown >= MAX_AMMO_ROWS) break;

                    ShipMapHelper.Rect(frame, x0 + w * 0.5f, curY + subH * 0.5f,
                        w, subH, ShipMapHelper.ColRowBg);

                    ShipMapHelper.TXT(frame, x0 + 4f, curY,
                        TruncateName(kv.Key, 28),
                        ShipMapHelper.ColLabel, ShipMapHelper.FS_SUB, TextAlignment.LEFT);
                    ShipMapHelper.TXT(frame, x0 + w, curY,
                        FormatCount(kv.Value),
                        ShipMapHelper.ColValue, ShipMapHelper.FS_SUB, TextAlignment.RIGHT);

                    curY += subH;
                    ShipMapHelper.Rect(frame, x0 + w * 0.5f, curY, w, 1f, ShipMapHelper.ColSep);
                    curY += 2f;
                    shown++;
                }

                if (sortedAmmo.Count > MAX_AMMO_ROWS)
                {
                    ShipMapHelper.TXT(frame, x0 + 4f, curY,
                        "  +" + (sortedAmmo.Count - MAX_AMMO_ROWS) + " more type(s)...",
                        ShipMapHelper.ColDim, ShipMapHelper.FS_SUB, TextAlignment.LEFT);
                    curY += subH;
                }
            }

            // ── Footer ────────────────────────────────────────────────────────
            DrawFooter(frame, vp, w, subH);

            frame.Dispose();
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

        private static void DrawBarRaw(MySpriteDrawFrame frame,
            float x, float y, float w, float h, float frac, Color fillCol)
        {
            float cx = x + w * 0.5f;
            float cy = y + h * 0.5f;
            ShipMapHelper.Rect(frame, cx, cy, w, h, ShipMapHelper.ColMapBg);
            if (frac > 0f)
            {
                float fw = Math.Max(1f, w * Math.Min(1f, frac));
                ShipMapHelper.Rect(frame, x + fw * 0.5f, cy, fw, h, fillCol);
            }
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

        private static string FormatVol(float liters)
        {
            if (liters >= 1000000f) return (liters / 1000000f).ToString("F1") + " ML";
            if (liters >= 1000f)    return (liters / 1000f).ToString("F1")    + " kL";
            return liters.ToString("F0") + " L";
        }

        private static string FormatCount(long count)
        {
            if (count >= 1000000L) return (count / 1000000f).ToString("F1") + "M";
            if (count >= 1000L)    return (count / 1000f).ToString("F1")    + "k";
            return count.ToString();
        }

        // Reads [TT_GROUP=GroupName] from a block's existing custom data.
        // Returns null if the tag is absent. Never writes to custom data.
        private static string ParseGroupTag(string customData)
        {
            if (string.IsNullOrEmpty(customData)) return null;
            int idx = customData.IndexOf("[TT_GROUP=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            int eq  = customData.IndexOf('=', idx);
            int end = customData.IndexOf(']', idx);
            if (eq < 0 || end <= eq + 1) return null;
            string name = customData.Substring(eq + 1, end - eq - 1).Trim();
            return name.Length > 0 ? name : null;
        }

        private static string TruncateName(string name, int max)
            => name.Length <= max ? name : name.Substring(0, max - 1) + "~";
    }
}
