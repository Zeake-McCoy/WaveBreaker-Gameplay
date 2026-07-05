using TTNAS.NetworkLib;
using ProtoBuf;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using System;
using VRage.ModAPI;

namespace TTNAS
{
    [ProtoContract]
    public class NavalStatePacket : PacketBase
    {
        [ProtoMember(1)] public long   EntityId;
        [ProtoMember(2)] public string Blob;

        public NavalStatePacket() { }

        public override void Received(ref PacketInfo packetInfo, ulong senderSteamId)
        {
            if (MyAPIGateway.Multiplayer.IsServer) return;
            try
            {
                var entity = MyAPIGateway.Entities.GetEntityById(EntityId) as IMyTerminalBlock;
                if (entity == null) return;

                var naval = entity.GameLogic?.GetAs<NavalBlockBase>();
                if (naval != null) { naval.ApplyNetworkBlob(Blob); return; }

                var ab = entity.GameLogic?.GetAs<AfterburnerController>();
                if (ab != null) { ab.ApplyNetworkBlob(Blob); return; }

                entity.GameLogic?.GetAs<ThrustReverseController>()?.ApplyNetworkBlob(Blob);
            }
            catch (Exception e)
            {
                NASLog.Error("Net", "NavalStatePacket.Received error: " + e);
            }
        }
    }

    [ProtoContract]
    public class NavalActionPacket : PacketBase
    {
        [ProtoMember(1)] public long EntityId;
        [ProtoMember(2)] public byte ActionType;

        public NavalActionPacket() { }

        public override void Received(ref PacketInfo packetInfo, ulong senderSteamId)
        {
            if (!MyAPIGateway.Multiplayer.IsServer) return;
            try
            {
                var entity = MyAPIGateway.Entities.GetEntityById(EntityId) as IMyTerminalBlock;
                if (entity == null) return;

                if (!NAS_NetworkHandler.CanPlayerControl(entity, senderSteamId))
                {
                    NASLog.Warn("Net", $"NavalActionPacket: player {senderSteamId} denied for block {EntityId}");
                    return;
                }

                // Dispatch chain: NavalBlockBase subclasses → CatapultBlock → Afterburner → ThrustReverser
                var logic = entity.GameLogic?.GetAs<NavalBlockBase>();
                if (logic != null)
                {
                    logic.HandleAction(ActionType, senderSteamId);
                    return;
                }

                var door     = MyAPIGateway.Entities.GetEntityById(EntityId) as IMyDoor;
                var catapult = door?.GameLogic?.GetAs<CatapultBlock>();
                if (catapult != null)
                {
                    catapult.HandleCatapultAction(ActionType);
                    return;
                }

                var thrust = entity as IMyThrust;
                var ab = thrust?.GameLogic?.GetAs<AfterburnerController>();
                if (ab != null) { ab.HandleAction(ActionType); return; }

                thrust?.GameLogic?.GetAs<ThrustReverseController>()?.HandleAction(ActionType);
            }
            catch (Exception e)
            {
                NASLog.Error("Net", "NavalActionPacket.Received error: " + e);
            }
        }
    }
}

// Register packet subtypes with TTNAS NetworkLib ProtoBuf hierarchy.
namespace TTNAS.NetworkLib
{
    [ProtoInclude(10, typeof(TTNAS.NavalStatePacket))]
    [ProtoInclude(11, typeof(TTNAS.NavalActionPacket))]
    public abstract partial class PacketBase { }
}
