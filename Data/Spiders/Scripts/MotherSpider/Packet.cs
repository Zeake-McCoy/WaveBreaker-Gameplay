using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage;
using VRage.Utils;
using VRageMath;

namespace SpiderBoss
{
  [ProtoContract]
  public class Packet
  {
    [ProtoMember(1)]
    public readonly ulong SenderId;

    [ProtoMember(2)]
    public SerializableVector3D SpawnPosition;

    [ProtoMember(3)]
    public int NumberToSpawn;

    [ProtoMember(4)]
    public bool SpawnBoss;

    public Packet()
    {
      SenderId = MyAPIGateway.Multiplayer.MyId;
    }

    public Packet(Vector3D spawnPosition, int numToSpawn, bool isBoss)
    {
      SenderId = MyAPIGateway.Multiplayer.MyId;
      SpawnPosition = spawnPosition;
      NumberToSpawn = numToSpawn;
      SpawnBoss = isBoss;
    }
  }
}
