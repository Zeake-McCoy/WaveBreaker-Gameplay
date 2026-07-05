using Sandbox.ModAPI;
using System;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;

namespace TTNAS
{
    /// <summary>
    /// Cross-mod message API for TTNavalAdvancedSystems.
    ///
    /// External mods send MyTuple&lt;string, long, string&gt; to MODAPI_CHANNEL_IN.
    /// Responses arrive on MODAPI_CHANNEL_OUT.
    ///
    /// Commands:
    ///   "GetState" — returns MyTuple&lt;long, string&gt;(entityId, stateBlob) on out channel
    ///   "Action"   — fires HandleAction on server; payload = byte value as string (e.g. "0")
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class NavalModAPI : MySessionComponentBase
    {
        public const long MODAPI_CHANNEL_IN  = 7419182749190L;   // unique channel for TTNAS
        public const long MODAPI_CHANNEL_OUT = 7419182749191L;

        public override void LoadData()
        {
            MyAPIGateway.Utilities.RegisterMessageHandler(MODAPI_CHANNEL_IN, HandleMessage);
        }

        protected override void UnloadData()
        {
            MyAPIGateway.Utilities.UnregisterMessageHandler(MODAPI_CHANNEL_IN, HandleMessage);
        }

        private static bool IsServer =>
            MyAPIGateway.Multiplayer == null || MyAPIGateway.Multiplayer.IsServer;

        private void HandleMessage(object msg)
        {
            try
            {
                if (!(msg is MyTuple<string, long, string>)) return;
                var tuple    = (MyTuple<string, long, string>)msg;
                var cmd      = tuple.Item1;
                var entityId = tuple.Item2;
                var payload  = tuple.Item3;

                var entity = MyAPIGateway.Entities.GetEntityById(entityId) as IMyTerminalBlock;
                if (entity == null) return;

                var logic = entity.GameLogic?.GetAs<NavalBlockBase>();
                if (logic == null) return;

                switch (cmd)
                {
                    case "GetState":
                        MyAPIGateway.Utilities.SendModMessage(
                            MODAPI_CHANNEL_OUT,
                            new MyTuple<long, string>(entityId, logic.GetPublicState()));
                        break;

                    case "Action":
                        if (!IsServer) return;
                        byte actionType;
                        if (byte.TryParse(payload, out actionType))
                            logic.HandleAction(actionType, 0UL);
                        break;
                }
            }
            catch (Exception e)
            {
                NASLog.Error("ModAPI", "Error: " + e);
            }
        }
    }
}
