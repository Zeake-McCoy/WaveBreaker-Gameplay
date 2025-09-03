using System.Linq;
using System.Collections.Generic;
using System.Text;
using SpaceEngineers.Game.ModAPI;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRageMath;
using Sandbox.Game;
using VRage;
using VRage.Input;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using Sandbox;
using Sandbox.Definitions;
using Sandbox.Definitions.GUI;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Gui;
using Sandbox.Game.GUI;
using Sandbox.Game.World;
using VRage.Game.ModAPI.Ingame.Utilities;
using System.Collections.Concurrent;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using Sandbox.Common.ObjectBuilders;
using VRage.ObjectBuilders;
using System;
using VRage.Game.ObjectBuilders.AI.Bot;
using VRage.Render.Scene;
using VRage.Audio;
using SpaceEngineers.Game.Entities.Blocks;
using VRage.Game.GUI.TextPanel;
using System.Net.Mime;

namespace SpiderBoss
{
  [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
  public class Spawner : MySessionComponentBase
  {
    List<IMyCharacter> _bots = new List<IMyCharacter>(1);
    List<IMyPlayer> _players = new List<IMyPlayer>(16);
    IMyHudNotification _hudMsg;
    Networking _network;
    bool _isServer, _isClient;
    Random _rand = new Random();

    StringBuilder _debugSB = new StringBuilder();

    HashSet<string> _botSubtypes = new HashSet<string>()
    {
      "SpaceMotherSpider",
    };

    List<string> _spaceSpiderSubtypes = new List<string>(4)
    {
      "SpaceSpider",
      "SpaceSpiderBrown",
      "SpaceSpiderBlack",
      "SpaceSpiderGreen"
    };

    protected override void UnloadData()
    {
      _network?.Unregister();
      _players?.Clear();
      _bots?.Clear();
      _botSubtypes?.Clear();
      _spaceSpiderSubtypes?.Clear();
      _debugSB?.Clear();
      _hudMsg?.Hide();

      _network = null;
      _players = null;
      _bots = null;
      _botSubtypes = null;
      _spaceSpiderSubtypes = null;
      _debugSB = null;
      _rand = null;
      _hudMsg = null;

      //MyEntities.OnEntityAdd -= MyEntities_OnEntityAdd;
      //MyEntities.OnEntityRemove -= MyEntities_OnEntityRemove;
      MyAPIGateway.Utilities.MessageEntered -= MessageHandler;
      base.UnloadData();
    }

    public override void BeforeStart()
    {
      try
      {
        _network = new Networking(21473, this);
        _network.Register();

        _isServer = MyAPIGateway.Multiplayer.IsServer;
        _isClient = !_isServer;

        MyAPIGateway.Utilities.MessageEntered += MessageHandler;

        if (_isClient)
          return;

        //var hash = new HashSet<IMyEntity>();
        //MyAPIGateway.Entities.GetEntities(hash);

        //foreach (var entity in hash)
        //  MyEntities_OnEntityAdd((MyEntity)entity);

        //hash.Clear();

        //MyEntities.OnEntityAdd += MyEntities_OnEntityAdd;
        //MyEntities.OnEntityRemove += MyEntities_OnEntityRemove;
      }
      catch (Exception e)
      {
        MyLog.Default.WriteLineAndConsole($"Exception occurred in MotherSpider.BeforeStart:\n{e.Message}\n{e.StackTrace}");
        UnloadData();
      }

      base.BeforeStart();
    }

    private void MessageHandler(string messageText, ref bool sendToOthers)
    {
      if (!messageText.StartsWith("/spawn spider", StringComparison.OrdinalIgnoreCase))
        return;

      sendToOthers = false;

      var player = MyAPIGateway.Session?.Player;
      if (player == null || (int)player.PromoteLevel < 4)
        return;

      var position = GetRandomPosition(player?.Character);
      if (!position.HasValue)
        return;

      var split = messageText.Split(' ');
      int numToSpawn = 1;
      if (split.Length > 2 && int.TryParse(split[2], out numToSpawn))
        numToSpawn = MathHelper.Clamp(numToSpawn, 1, 10);

      bool isBoss = split[1].Equals("spiderboss", StringComparison.OrdinalIgnoreCase);

      if (MyAPIGateway.Multiplayer.IsServer)
      {
        if (isBoss)
          SpawnSpiderBoss(position.Value, numToSpawn);
        else
          SpawnSpiders(position.Value, numToSpawn);
      }
      else
      {
        var packet = new Packet(position.Value, numToSpawn, isBoss);
        _network.SendToServer(packet);
      }
    }

    public override void UpdateBeforeSimulation()
    {
      try
      {
        if (_bots == null)
          return;

        for (int i = _bots.Count - 1; i >= 0; i--)
        {
          if (_bots[i]?.IsDead != false)
            _bots.RemoveAtFast(i);
        }
      }
      catch (Exception e)
      {
        MyLog.Default.WriteLineAndConsole($"Exception occurred in MotherSpider.UpdateBeforeSim:\n{e.Message}\n{e.StackTrace}");
        UnloadData();
      }
    }

    public void SpawnSpiderBoss(Vector3D position, int numToSpawn)
    {
      //MyLog.Default.WriteLineAndConsole($"Spawning {numToSpawn} bosses at position {position.ToString()}");
      for (int i = 0; i < numToSpawn; i++)
      {
        MyVisualScriptLogicProvider.SpawnBot("SpaceMotherSpider", position);
      }
    }

    public void SpawnSpiders(Vector3D position, int numToSpawn)
    {
      //MyLog.Default.WriteLineAndConsole($"Spawning {numToSpawn} regulars at position {position.ToString()}");
      for (int i = 0; i < numToSpawn; i++)
      {
        var next = _rand.Next(0, 4);
        var subtype = _spaceSpiderSubtypes[next];
        MyVisualScriptLogicProvider.SpawnBot(subtype, position);
      }
    }

    Vector3D? GetRandomPosition(IMyCharacter character)
    {
      if (character == null)
        return null;

      float num;
      var charPosition = character.WorldAABB.Center;
      Vector3D gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(charPosition, out num);

      MatrixD matrix;
      if (gravity.LengthSquared() > 0)
      {
        gravity.Normalize();
        matrix = MatrixD.CreateWorld(charPosition, Vector3D.CalculatePerpendicularVector(gravity), -gravity);
      }
      else
        matrix = character.WorldMatrix;

      var random = MyUtils.GetRandomInt(1, 9);

      Vector3D direction;
      switch (random)
      {
        case 1:
          direction = matrix.Forward;
          break;
        case 2:
          direction = matrix.Forward + matrix.Right;
          break;
        case 3:
          direction = matrix.Right;
          break;
        case 4:
          direction = matrix.Right + matrix.Backward;
          break;
        case 5:
          direction = matrix.Backward;
          break;
        case 6:
          direction = matrix.Backward + matrix.Left;
          break;
        case 7:
          direction = matrix.Left;
          break;
        case 8:
          direction = matrix.Left + matrix.Forward;
          break;
        default:
          return null;
      }

      var position = charPosition + direction * 200;
      var planet = MyGamePruningStructure.GetClosestPlanet(position);
      if (planet == null)
        return position;

      var surfacePoint = planet.GetClosestSurfacePointGlobal(ref position);
      return surfacePoint - (gravity * 5);
    }

    //private void MyEntities_OnEntityRemove(MyEntity obj)
    //{
    //  var character = obj as IMyCharacter;
    //  if (character == null || !_botSubtypes.Contains(character.Definition.Id.SubtypeName))
    //    return;

    //  for (int i = _bots.Count - 1; i >= 0; i--)
    //  {
    //    if (_bots[i]?.EntityId == obj.EntityId)
    //    {
    //      _bots.RemoveAtFast(i);
    //      break;
    //    }
    //  }
    //}

    //private void MyEntities_OnEntityAdd(MyEntity obj)
    //{
    //  try
    //  {
    //    var character = obj as IMyCharacter;
    //    if (character == null)
    //      return;

    //    var subtype = character.Definition.Id.SubtypeName;
    //    ShowMessage($"Subtype of spawn = {subtype}");
    //    if (!_botSubtypes.Contains(subtype) && !_spaceSpiderSubtypes.Contains(subtype))
    //      return;

    //    if (_bots.Count > 9)
    //    {
    //      character.Close();
    //      ShowMessage("You must slay the current enemies before summoning another!");
    //    }
    //    else
    //    {
    //      _bots.Add(character);
    //      ShowMessage($"A spider boss has spawned!");
    //    }
    //  }
    //  catch (Exception e)
    //  {
    //    MyLog.Default.WriteLineAndConsole($"Exception occurred in MotherSpider.MyEntities_OnEntityAdd:\n{e.Message}\n{e.StackTrace}");
    //  }
    //}

    public void ShowMessage(string text, string font = MyFontEnum.Red, int timeToLive = 2000)
    {
      if (_hudMsg == null)
        _hudMsg = MyAPIGateway.Utilities.CreateNotification(string.Empty);

      _hudMsg.Hide();
      _hudMsg.Font = font;
      _hudMsg.AliveTime = timeToLive;
      _hudMsg.Text = text;
      _hudMsg.Show();
    }
  }
}
