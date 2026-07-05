using TTNAS.NetworkLib;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;

namespace TTNAS
{
    /// <summary>
    /// Single session-component network handler for all TTNAS block types.
    /// Replaces TTNavalBlocks' NavalNetworkHandler with a unified TTNAS channel.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class NAS_NetworkHandler : MySessionComponentBase
    {
        public static NAS_NetworkHandler Instance { get; private set; }

        private Network           _net;
        private NavalStatePacket  _statePacket;
        private NavalActionPacket _actionPacket;

        private const ushort CHANNEL_ID = 47293;   // distinct from TTNavalBlocks (47292)

        public override void LoadData()
        {
            Instance = this;
        }

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            try
            {
                _net          = new Network(CHANNEL_ID, "TTNavalAdvancedSystems");
                _statePacket  = new NavalStatePacket();
                _actionPacket = new NavalActionPacket();
            }
            catch (Exception e)
            {
                NASLog.Error("Net", "NAS_NetworkHandler.Init error: " + e);
            }
        }

        protected override void UnloadData()
        {
            _net?.Dispose();
            _net          = null;
            _statePacket  = null;
            _actionPacket = null;
            Instance      = null;
        }

        // ── Outbound ──────────────────────────────────────────────────────────

        public void SendState(long entityId, string blob)
        {
            if (!MyAPIGateway.Multiplayer.IsServer || _net == null || _statePacket == null) return;
            _statePacket.EntityId = entityId;
            _statePacket.Blob     = blob;
            _net.SendToEveryone(_statePacket);
        }

        public void SendAction(long entityId, byte actionType)
        {
            if (_net == null || _actionPacket == null) return;
            _actionPacket.EntityId   = entityId;
            _actionPacket.ActionType = actionType;
            _net.SendToServer(_actionPacket);
        }

        // ── Permission check ──────────────────────────────────────────────────

        public static bool CanPlayerControl(IMyTerminalBlock block, ulong steamId)
        {
            if (block == null) return false;
            if (block.OwnerId == 0) return true;

            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players, p => p.SteamUserId == steamId);
            if (players.Count == 0) return false;

            var player = players[0];
            if (player.IdentityId == block.OwnerId) return true;

            var pf = MyAPIGateway.Session.Factions.TryGetPlayerFaction(player.IdentityId);
            var bf = MyAPIGateway.Session.Factions.TryGetPlayerFaction(block.OwnerId);

            if (pf != null && bf != null)
            {
                var rel = MyAPIGateway.Session.Factions.GetRelationBetweenFactions(pf.FactionId, bf.FactionId);
                return rel == VRage.Game.MyRelationsBetweenFactions.Friends
                    || rel == VRage.Game.MyRelationsBetweenFactions.Neutral
                    || pf.FactionId == bf.FactionId;
            }

            return false;
        }
    }
}
