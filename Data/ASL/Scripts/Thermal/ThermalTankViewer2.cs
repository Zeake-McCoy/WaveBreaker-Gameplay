using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace ASL_TV2_Klime.Thermal
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class Thermal : MySessionComponentBase
    {
        string thermal_subtype = "ASL_TankViewer2_Camera";
        float thermal_radius = 3000f;
        BoundingSphereD reuse_sphere = new BoundingSphereD();
        BoundingBoxD reuse_box = new BoundingBoxD();
        IMyCameraBlock reuse_camera_block;
        IMyCubeGrid reuse_grid;
        IMyCharacter reuse_character;
        List<MyEntity> old_ents = new List<MyEntity>();
        List<MyEntity> new_ents = new List<MyEntity>();
        List<MyEntity> scan_ents = new List<MyEntity>();
        long localID = 0;
        Color grid_col = new Color(Color.White, 0.4f);
        Color player_col = Color.White;
        MyStringId covTexture;
        Color covCol = new Color(Color.Black, 0.99f);

        MyIni config = new MyIni();
        List<string> reuse_parse = new List<string>();
        bool enabled_thermal = true;
        bool thermal_visuals = false;
        int timer = 0;

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            thermal_radius = MyAPIGateway.Session.SessionSettings.SyncDistance;
        }

        public override void LoadData()
        {
            covTexture = MyStringId.GetOrCompute("Square");
        }

        private void ComputeThermal()
        {
            foreach (var ent in new_ents)
            {
                if (ent is IMyCharacter)
                {
                    MyVisualScriptLogicProvider.SetHighlightLocal(ent.Name, 4, 0, player_col, localID);
                }
                if (ent is IMyCubeGrid)
                {
                    MyVisualScriptLogicProvider.SetHighlightLocal(ent.Name, 10, 0, grid_col, localID);
                }
            }
        }

        private void DisableThermal()
        {
            foreach (var ent in old_ents)
            {
                if (ent is IMyCharacter)
                {
                    MyVisualScriptLogicProvider.SetHighlightLocal(ent.Name, 0, 0, player_col, localID);
                }
                if (ent is IMyCubeGrid)
                {
                    MyVisualScriptLogicProvider.SetHighlightLocal(ent.Name, 0, 0, grid_col, localID);
                }
            }
            foreach (var ent in new_ents)
            {
                if (ent is IMyCharacter)
                {
                    MyVisualScriptLogicProvider.SetHighlightLocal(ent.Name, 0, 0, player_col, localID);
                }
                if (ent is IMyCubeGrid)
                {
                    MyVisualScriptLogicProvider.SetHighlightLocal(ent.Name, 0, 0, grid_col, localID);
                }
            }
            old_ents.Clear();
            new_ents.Clear();
            SwitchFog(false);
        }


        private void ParseColors()
        {
            if (string.IsNullOrWhiteSpace(reuse_camera_block.CustomData))
            {
                config.Set("Grid Color", "rgba", grid_col.R + ":" + grid_col.G + ":" + grid_col.B + ":" + grid_col.A);
                config.Set("Player Color", "rgba", player_col.R + ":" + player_col.G + ":" + player_col.B + ":" + player_col.A);

                reuse_camera_block.CustomData = config.ToString();
            }

            if (config.TryParse(reuse_camera_block.CustomData))
            {
                try
                {
                    reuse_parse.Clear();

                    string grid_col_string = config.Get("Grid Color", "rgba").ToString();
                    reuse_parse = grid_col_string.Split(':').ToList();
                    if (reuse_parse != null && reuse_parse.Count == 4)
                    {
                        grid_col = new Color(int.Parse(reuse_parse[0]), int.Parse(reuse_parse[1]), int.Parse(reuse_parse[2]), int.Parse(reuse_parse[3]));
                    }

                    reuse_parse.Clear();

                    string player_col_string = config.Get("Player Color", "rgba").ToString();
                    reuse_parse = player_col_string.Split(':').ToList();
                    if (reuse_parse != null && reuse_parse.Count == 4)
                    {
                        player_col = new Color(int.Parse(reuse_parse[0]), int.Parse(reuse_parse[1]), int.Parse(reuse_parse[2]), int.Parse(reuse_parse[3]));
                    }
                }
                catch (Exception e)
                {
                    grid_col = new Color(Color.White, 0.4f);
                    player_col = Color.White;
                    MyLog.Default.WriteLine("KLIME: THERMAL FAILED TO PARSE CUSTOM DATA " + e);
                }
            }
        }

        private void DoThermal()
        {
            if (MyAPIGateway.Session.Camera != null)
            {
                if (thermal_visuals)
                {
                    reuse_sphere.Center = MyAPIGateway.Session.Camera.WorldMatrix.Translation;
                    reuse_sphere.Radius = thermal_radius;
                    old_ents.AddRange(new_ents);
                    new_ents.Clear();
                    scan_ents.Clear();
                    MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref reuse_sphere, scan_ents);


                    foreach (var ent in scan_ents)
                    {
                        if (!old_ents.Contains(ent))
                        {
                            bool valid = false;
                            reuse_grid = ent as IMyCubeGrid;
                            reuse_character = ent as IMyCharacter;

                            if (reuse_grid != null && reuse_grid.Physics != null)
                            {
                                reuse_box = reuse_grid.WorldAABB;
                                valid = true;
                            }

                            if (reuse_character != null)
                            {
                                reuse_box = reuse_character.WorldAABB;
                                valid = true;
                            }

                            if (valid && MyAPIGateway.Session.Camera.IsInFrustum(ref reuse_box))
                            {
                                new_ents.Add(ent);
                            }
                        }
                    }
                    ComputeThermal();
                }
            }
        }

        private void GlobalClearThermal()
        {
            HashSet<IMyEntity> global_ents = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(global_ents);
            foreach (var ent in global_ents)
            {
                if (ent is IMyCubeGrid)
                {
                    MyVisualScriptLogicProvider.SetHighlightLocal(ent.Name, 0, 0, grid_col, localID);
                }
                if (ent is IMyCharacter)
                {
                    MyVisualScriptLogicProvider.SetHighlightLocal(ent.Name, 0, 0, player_col, localID);
                }
            }
            SwitchFog(false);
        }

        public override void UpdateAfterSimulation()
        {
            try
            {
                if (MyAPIGateway.Utilities.IsDedicated)
                {
                    return;
                }

                if (localID == 0 && MyAPIGateway.Session.Player != null)
                {
                    localID = MyAPIGateway.Session.Player.IdentityId;
                }

                if (timer == 10)
                {
                    GlobalClearThermal();
                }

                if (timer % 100 == 0)
                {
                    DoThermal();
                }

                if (MyAPIGateway.Session?.CameraController?.Entity != null)
                {
                    reuse_camera_block = null;
                    reuse_camera_block = MyAPIGateway.Session.CameraController as IMyCameraBlock;
                    if (reuse_camera_block != null)
                    {
                        if (!reuse_camera_block.MarkedForClose && reuse_camera_block.BlockDefinition.SubtypeName.StartsWith(thermal_subtype))
                        {
                            if (ValidInput() && MyAPIGateway.Input.IsNewKeyPressed(MyKeys.N))
                            {
                                enabled_thermal = !enabled_thermal;
                                if (!enabled_thermal)
                                {
                                    DisableThermal();
                                    thermal_visuals = false;
                                }
                            }


                            if (!thermal_visuals && enabled_thermal)
                            {
                                //Main Activator
                                thermal_visuals = true;
                                SwitchFog(true);
                                ParseColors();
                                DoThermal();
                            }
                        }
                        else
                        {
                            if (thermal_visuals)
                            {
                                DisableThermal();
                                thermal_visuals = false;
                            }
                        }

                    }
                    else
                    {
                        if (thermal_visuals)
                        {
                            DisableThermal();
                            thermal_visuals = false;
                        }
                    }
                }
                else
                {
                    if (thermal_visuals)
                    {
                        DisableThermal();
                        thermal_visuals = false;
                    }
                }
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine("KLIME: THERMAL " + e);
            }

            timer += 1;
        }

        public override void Draw()
        {
            //if (thermal_visuals && enabled_thermal)
            //{
            //    IMyCamera cam = MyAPIGateway.Session.Camera;

            //    if (cam == null || covTexture == null)
            //    {
            //        return;
            //    }

            //    Vector3D origin = cam.WorldMatrix.Translation + cam.WorldMatrix.Forward * 0.01;
            //    MyTransparentGeometry.AddBillboardOriented(covTexture, covCol, origin, cam.WorldMatrix.Left, cam.WorldMatrix.Up,
            //        0.1f, 0.1f, Vector2.Zero, VRageRender.MyBillboard.BlendTypeEnum.PostPP);
            //}
        }

        public void SwitchFog(bool enabled)
        {
            if (enabled)
            {
                MyAPIGateway.Session.WeatherEffects.FogMultiplierOverride = 1;
                MyAPIGateway.Session.WeatherEffects.FogDensityOverride = 1;
                MyAPIGateway.Session.WeatherEffects.FogAtmoOverride = 1;
                MyAPIGateway.Session.WeatherEffects.FogColorOverride = Color.DimGray;
            }
            else
            {
                MyAPIGateway.Session.WeatherEffects.FogMultiplierOverride = null;
                MyAPIGateway.Session.WeatherEffects.FogDensityOverride = null;
                MyAPIGateway.Session.WeatherEffects.FogAtmoOverride = null;
                MyAPIGateway.Session.WeatherEffects.FogColorOverride = null;
            }
        }

        private bool ValidInput()
        {
            if (MyAPIGateway.Session.CameraController != null && !MyAPIGateway.Gui.ChatEntryVisible && !MyAPIGateway.Gui.IsCursorVisible
                && MyAPIGateway.Gui.GetCurrentScreen == MyTerminalPageEnum.None)
            {
                return true;
            }
            return false;
        }

        protected override void UnloadData()
        {
            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                GlobalClearThermal();
                new_ents.Clear();
                old_ents.Clear();
                new_ents = null;
                old_ents = null;
            }
        }
    }
}