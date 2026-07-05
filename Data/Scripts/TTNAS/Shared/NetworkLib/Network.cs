using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace TTNAS.NetworkLib
{
    public class Network : IDisposable
    {
        public readonly ushort ChannelId;

        public Action<Exception>              ExceptionHandler;
        public Action<ulong, IMyPlayer, byte[]> ReceiveExceptionHandler;
        public Action<string>                 ErrorHandler;
        public bool SerializeTest = false;

        readonly string         ModName;
        readonly List<IMyPlayer> TempPlayers;

        static bool AlreadyInstanced = false;

        public Network(ushort channelId, string modName, bool registerListener = true)
        {
            ChannelId = channelId;
            ModName   = modName;

            if (MyAPIGateway.Session == null)
            {
                CrashAfterLoad($"{ModName}: Network constructor called too early.");
                return;
            }
            if (AlreadyInstanced)
            {
                CrashAfterLoad($"{ModName}: Network instanced more than once.");
                return;
            }

            AlreadyInstanced = true;

            if (registerListener)
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(ChannelId, ReceivedPacket);

            TempPlayers = new List<IMyPlayer>(MyAPIGateway.Session.SessionSettings.MaxPlayers);
        }

        public void Dispose()
        {
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(ChannelId, ReceivedPacket);
            TempPlayers.Clear();
            AlreadyInstanced = false;
        }

        public void SendToServer(PacketBase packet, byte[] serialized = null)
        {
            if (!SerializeTest && MyAPIGateway.Multiplayer.IsServer)
            {
                HandlePacket(packet, MyAPIGateway.Multiplayer.MyId, serialized);
                return;
            }
            if (serialized == null) serialized = MyAPIGateway.Utilities.SerializeToBinary(packet);
            MyAPIGateway.Multiplayer.SendMessageToServer(ChannelId, serialized);
        }

        public void SendToPlayer(PacketBase packet, ulong steamId, byte[] serialized = null)
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
                throw new Exception($"{ModName}: Clients can't send directly to clients.");
            if (serialized == null) serialized = MyAPIGateway.Utilities.SerializeToBinary(packet);
            MyAPIGateway.Multiplayer.SendMessageTo(ChannelId, serialized, steamId);
        }

        public void SendToEveryone(PacketBase packet, byte[] serialized = null)
        {
            RelayToClients(packet, 0, serialized);
        }

        void RelayToClients(PacketBase packet, ulong senderSteamId = 0, byte[] serialized = null)
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
                throw new Exception($"{ModName}: Clients can't relay packets.");

            TempPlayers.Clear();
            MyAPIGateway.Players.GetPlayers(TempPlayers);

            foreach (IMyPlayer p in TempPlayers)
            {
                if (p.SteamUserId == MyAPIGateway.Multiplayer.ServerId || p.SteamUserId == senderSteamId)
                    continue;
                if (serialized == null)
                    serialized = MyAPIGateway.Utilities.SerializeToBinary(packet);
                MyAPIGateway.Multiplayer.SendMessageTo(ChannelId, serialized, p.SteamUserId);
            }
            TempPlayers.Clear();
        }

        void ReceivedPacket(ushort channelId, byte[] serialized, ulong senderSteamId, bool isSenderServer)
        {
            try
            {
                PacketBase packet = MyAPIGateway.Utilities.SerializeFromBinary<PacketBase>(serialized);
                HandlePacket(packet, senderSteamId, serialized);
            }
            catch (Exception e)
            {
                if (ExceptionHandler != null) ExceptionHandler.Invoke(e);
                else DefaultExceptionHandler(e);

                TempPlayers.Clear();
                MyAPIGateway.Players.GetPlayers(TempPlayers, (p) => p.SteamUserId == senderSteamId);
                IMyPlayer sender = TempPlayers.FirstOrDefault();
                TempPlayers.Clear();

                if (ReceiveExceptionHandler != null) ReceiveExceptionHandler.Invoke(senderSteamId, sender, serialized);
                else MyLog.Default.WriteLineAndConsole($"{ModName} ReceivedPacket extra: sender={sender?.DisplayName ?? "<unknown>"} ({senderSteamId}); bytes={string.Join(",", serialized)}");
            }
        }

        void HandlePacket(PacketBase packet, ulong senderSteamId, byte[] serialized = null)
        {
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                if (senderSteamId != packet.OriginalSenderSteamId)
                {
                    string text = $"WARNING: packet {packet.GetType().Name} from {senderSteamId} has altered OriginalSenderSteamId to {packet.OriginalSenderSteamId}. Replaced.";
                    if (ErrorHandler != null) ErrorHandler.Invoke(text);
                    else DefaultErrorHandler(text);
                    packet.OriginalSenderSteamId = senderSteamId;
                    serialized = null;
                }
            }

            PacketInfo info = new PacketInfo { Relay = RelayMode.None, Reserialize = false };
            packet.Received(ref info, senderSteamId);

            if (MyAPIGateway.Multiplayer.IsServer)
            {
                if (info.Reserialize) serialized = null;
                switch (info.Relay)
                {
                    case RelayMode.None:      break;
                    case RelayMode.ToOthers:  RelayToClients(packet, senderSteamId, serialized); break;
                    case RelayMode.ToEveryone:RelayToClients(packet, 0, serialized); break;
                    default: throw new Exception($"{ModName}: Unknown relay mode: {info.Relay}");
                }
            }
        }

        void DefaultExceptionHandler(Exception e)
        {
            MyLog.Default.WriteLineAndConsole($"{ModName} ERROR: {e}");
            if (MyAPIGateway.Session?.Player != null)
                MyAPIGateway.Utilities.ShowNotification($"[ERROR: {ModName}: {e.Message}]", 10000, MyFontEnum.Red);
        }

        void DefaultErrorHandler(string error)
        {
            MyLog.Default.WriteLineAndConsole($"{ModName} ERROR: {error}");
            if (MyAPIGateway.Session?.Player != null)
                MyAPIGateway.Utilities.ShowNotification($"[ERROR: {ModName}: {error}]", 10000, MyFontEnum.Red);
        }

        public static void CrashAfterLoad(string text)
        {
            MyAPIGateway.Utilities = MyAPIUtilities.Static;
            MyAPIGateway.Utilities.InvokeOnGameThread(() => { throw new Exception(text); });
        }
    }

    public delegate void ReceiveDelegate<T>(T packet, ref PacketInfo packetInfo, ulong senderSteamId);

    public struct PacketInfo
    {
        public RelayMode Relay;
        public bool      Reserialize;
    }

    public enum RelayMode
    {
        None      = 0,
        ToOthers,
        ToEveryone,
    }
}
