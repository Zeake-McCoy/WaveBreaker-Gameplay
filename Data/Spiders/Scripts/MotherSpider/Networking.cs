using System;
using System.Text;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace SpiderBoss
{
  public class Networking
  {
    public readonly ushort NetworkId;
    Spawner _mod;

    public Networking(ushort networkId, Spawner mod)
    {
      NetworkId = networkId;
      _mod = mod;
    }

    public void Register()
    {
      MyAPIGateway.Multiplayer.RegisterMessageHandler(NetworkId, ReceivedPacket);
    }

    public void Unregister()
    {
      MyAPIGateway.Multiplayer.UnregisterMessageHandler(NetworkId, ReceivedPacket);
    }

    private void ReceivedPacket(byte[] rawData)
    {
      try
      {
        if (!MyAPIGateway.Multiplayer.IsServer)
          return;

        var packet = MyAPIGateway.Utilities.SerializeFromBinary<Packet>(rawData);
        if (packet == null)
          return;

        if (packet.SpawnBoss)
          _mod.SpawnSpiderBoss(packet.SpawnPosition, packet.NumberToSpawn);
        else
          _mod.SpawnSpiders(packet.SpawnPosition, packet.NumberToSpawn);
      }
      catch (Exception e)
      {
        MyLog.Default.WriteLineAndConsole($"Error in SpiderBoss.Networking.ReceivedPacket:\n{e.Message}\n{e.StackTrace}");

        if (MyAPIGateway.Session?.Player != null)
          MyAPIGateway.Utilities.ShowNotification($"[SpiderBoss] ERROR: {GetType().FullName} -- {e.Message} | Send SpaceEngineers.log to mod author ]", 10000, MyFontEnum.Red);
      }
    }

    public void SendToServer(Packet packet)
    {
      if (MyAPIGateway.Multiplayer.IsServer)
        return;

      _mod.ShowMessage("Packet being sent now!");

      var data = MyAPIGateway.Utilities.SerializeToBinary(packet);
      MyAPIGateway.Multiplayer.SendMessageToServer(NetworkId, data);
    }
  }
}
