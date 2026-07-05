using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace TTNAS
{
    [MyTextSurfaceScript("TT_GeneralOverview", "TT CIC \u2014 General Overview")]
    public class GeneralOverviewLcd : MyTSSCommon
    {
        private readonly IMyTextSurface _surface;
        private readonly IMyCubeBlock   _block;

        private const string FONT       = "Debug";
        private const float  FONT_SCALE = 0.6f;
        private const float  LINE_PAD   = 2f;
        private const float  MARGIN     = 10f;

        private static readonly Color ColHeader  = new Color(  0, 180, 255);
        private static readonly Color ColOnline  = new Color(  0, 255, 128);
        private static readonly Color ColOffline = new Color(255,  60,  60);
        private static readonly Color ColLabel   = new Color(160, 160, 160);
        private static readonly Color ColValue   = Color.White;
        private static readonly Color ColDivider = new Color( 80,  80,  80);

        public GeneralOverviewLcd(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _surface = surface;
            _block   = block;
        }

        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update10;

        public override void Run()
        {
            try   { Draw(); }
            catch { }
        }

        private void Draw()
        {
            var viewport = new RectangleF(
                (_surface.TextureSize - _surface.SurfaceSize) / 2f,
                _surface.SurfaceSize);

            var frame = _surface.DrawFrame();

            var charH = _surface.MeasureStringInPixels(
                new System.Text.StringBuilder("W"), FONT, FONT_SCALE).Y + LINE_PAD;

            var cursor = new Vector2(viewport.X + MARGIN, viewport.Y + MARGIN);

            var grid     = _block.CubeGrid;
            var fcBlocks = new List<IMyTerminalBlock>();
            bool wcReady = FCGridCache.I != null && FCGridCache.I.WcApiReady;

            MyAPIGateway.TerminalActionsHelper?.GetTerminalSystemForGrid(grid)
                ?.GetBlocksOfType<IMyUpgradeModule>(fcBlocks,
                    b => b.CubeGrid.EntityId == grid.EntityId
                      && (b.BlockDefinition.SubtypeId == "TT_CIC_Processor"
                       || b.BlockDefinition.SubtypeId == "TT_CIC_Processor_SG"));

            if (fcBlocks.Count == 0)
            {
                var mechGrids = new List<VRage.Game.ModAPI.IMyCubeGrid>();
                MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Mechanical, mechGrids);
                foreach (var mg in mechGrids)
                {
                    if (mg.EntityId == grid.EntityId) continue;
                    MyAPIGateway.TerminalActionsHelper?.GetTerminalSystemForGrid(mg)
                        ?.GetBlocksOfType<IMyUpgradeModule>(fcBlocks,
                            b => b.BlockDefinition.SubtypeId == "TT_CIC_Processor"
                              || b.BlockDefinition.SubtypeId == "TT_CIC_Processor_SG");
                    if (fcBlocks.Count > 0) break;
                }
            }

            bool hasCIC  = fcBlocks.Count > 0;
            var  cache   = wcReady ? FCGridCache.I.PeekCache(grid) : null;
            int  wTotal  = cache?.AllWeapons.Count      ?? 0;
            int  wStatic = cache?.StaticLaunchers.Count ?? 0;
            int  wTurret = cache?.Turrets.Count         ?? 0;

            // ── Header ───────────────────────────────────────────────────────
            AddLine(frame, ref cursor, charH, "TT GENERAL OVERVIEW", ColHeader);
            AddDivider(frame, ref cursor, charH, viewport, ColDivider);

            if (!hasCIC)
            {
                AddLine(frame, ref cursor, charH, "", ColValue);
                AddLine(frame, ref cursor, charH, "  TT CIC OFFLINE", ColOffline);
                frame.Dispose();
                return;
            }

            // ── Status ───────────────────────────────────────────────────────
            AddLine(frame, ref cursor, charH, "", ColValue);
            AddLineLR(frame, ref cursor, charH, viewport,
                "STATUS", wcReady ? "ONLINE" : "WC NOT READY",
                ColLabel, wcReady ? ColOnline : ColOffline);

            // ── Fire Control blocks ───────────────────────────────────────────
            AddLine(frame, ref cursor, charH, "", ColValue);
            AddLine(frame, ref cursor, charH, "FIRE CONTROL", ColHeader);
            AddDivider(frame, ref cursor, charH, viewport, ColDivider);

            foreach (var b in fcBlocks)
            {
                var status = (b as IMyFunctionalBlock)?.IsWorking == true ? "ONLINE" : "OFFLINE";
                var col    = status == "ONLINE" ? ColOnline : ColOffline;
                AddLineLR(frame, ref cursor, charH, viewport,
                    "  " + TruncateName(b.CustomName, 20), status, ColValue, col);
            }

            // ── Weapons ───────────────────────────────────────────────────────
            AddLine(frame, ref cursor, charH, "", ColValue);
            AddLine(frame, ref cursor, charH, "WEAPONS", ColHeader);
            AddDivider(frame, ref cursor, charH, viewport, ColDivider);

            AddLineLR(frame, ref cursor, charH, viewport, "  Total",   wTotal.ToString(),  ColLabel, ColValue);
            AddLineLR(frame, ref cursor, charH, viewport, "  Static",  wStatic.ToString(), ColLabel, ColValue);
            AddLineLR(frame, ref cursor, charH, viewport, "  Turrets", wTurret.ToString(), ColLabel, ColValue);

            AddLine(frame, ref cursor, charH, "", ColValue);
            AddDivider(frame, ref cursor, charH, viewport, ColDivider);
            AddLine(frame, ref cursor, charH, DateTime.Now.ToString("HH:mm:ss"), ColLabel);

            frame.Dispose();
        }

        private void AddLine(MySpriteDrawFrame frame, ref Vector2 cursor,
            float lineH, string text, Color color)
        {
            frame.Add(new MySprite(SpriteType.TEXT, text,
                cursor, Vector2.Zero, color, FONT, TextAlignment.LEFT, FONT_SCALE));
            cursor.Y += lineH;
        }

        private void AddLineLR(MySpriteDrawFrame frame, ref Vector2 cursor,
            float lineH, RectangleF viewport,
            string left, string right, Color leftCol, Color rightCol)
        {
            frame.Add(new MySprite(SpriteType.TEXT, left,
                cursor, Vector2.Zero, leftCol, FONT, TextAlignment.LEFT, FONT_SCALE));
            frame.Add(new MySprite(SpriteType.TEXT, right,
                new Vector2(viewport.X + viewport.Width - MARGIN, cursor.Y),
                Vector2.Zero, rightCol, FONT, TextAlignment.RIGHT, FONT_SCALE));
            cursor.Y += lineH;
        }

        private void AddDivider(MySpriteDrawFrame frame, ref Vector2 cursor,
            float lineH, RectangleF viewport, Color color)
        {
            frame.Add(new MySprite(SpriteType.TEXT, new string('-', 40),
                cursor, Vector2.Zero, color, FONT, TextAlignment.LEFT, FONT_SCALE));
            cursor.Y += lineH;
        }

        private static string TruncateName(string name, int max)
            => name.Length <= max ? name : name.Substring(0, max - 1) + "~";
    }
}
