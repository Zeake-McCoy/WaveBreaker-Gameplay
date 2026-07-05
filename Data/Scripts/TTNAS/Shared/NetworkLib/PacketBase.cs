using ProtoBuf;
using Sandbox.ModAPI;

namespace TTNAS.NetworkLib
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public abstract partial class PacketBase
    {
        [ProtoMember(1)]
        public ulong OriginalSenderSteamId;

        public PacketBase()
        {
            if (MyAPIGateway.Multiplayer == null)
                Network.CrashAfterLoad($"Cannot instantiate packets in fields ({GetType().Name}), too early!");
            else
                OriginalSenderSteamId = MyAPIGateway.Multiplayer.MyId;
        }

        public abstract void Received(ref PacketInfo packetInfo, ulong senderSteamId);
    }
}
