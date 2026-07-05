using ProtoBuf;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using System.Collections.Generic;
using VRage.Game.Components;

namespace TTNAS
{
    public static class AudioTerminalPatch
    {
        private static bool _done = false;

        public static void HideSorterControls()
        {
            if (_done) return;
            _done = true;

            List<IMyTerminalControl> controls;
            MyAPIGateway.TerminalControls.GetControls<IMyConveyorSorter>(out controls);
            foreach (var ctrl in controls)
            {
                var capture = ctrl.Visible;
                ctrl.Visible = b =>
                {
                    var gl = b.GameLogic;
                    if (gl?.GetAs<AudioControllerBlock>() != null) return false;
                    if (gl?.GetAs<AudioSpeakerBlock>()    != null) return false;
                    return capture == null || capture(b);
                };
            }
        }
    }

    [ProtoContract]
    public class SpeakerEventSync
    {
        [ProtoMember(1)] public long EntityId;
        [ProtoMember(2)] public int  EventIndex;
        [ProtoMember(3)] public bool Enabled;
    }

    [ProtoContract]
    public class AudioEventTrigger
    {
        [ProtoMember(1)] public long   GridEntityId;
        [ProtoMember(2)] public int    EventIndex;
        [ProtoMember(3)] public string SoundId;
        [ProtoMember(4)] public float  Volume;
    }

    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class AudioSession : MySessionComponentBase
    {
        public const ushort CHANNEL       = 49210; // speaker cfg sync
        public const ushort CHANNEL_EVENT = 49211; // audio event trigger

        public override void LoadData()
        {
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(CHANNEL,       HandleSpeakerCfg);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(CHANNEL_EVENT, HandleAudioEvent);
        }

        protected override void UnloadData()
        {
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(CHANNEL,       HandleSpeakerCfg);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(CHANNEL_EVENT, HandleAudioEvent);
        }

        // ── Speaker config (enable/disable per event) ─────────────────────────

        private void HandleSpeakerCfg(ushort channel, byte[] data, ulong sender, bool fromServer)
        {
            var msg = MyAPIGateway.Utilities.SerializeFromBinary<SpeakerEventSync>(data);
            if (msg == null) return;

            var ent   = MyAPIGateway.Entities.GetEntityById(msg.EntityId) as IMyTerminalBlock;
            var logic = ent?.GameLogic?.GetAs<AudioSpeakerBlock>();
            if (logic == null) return;

            if (MyAPIGateway.Multiplayer.IsServer)
            {
                logic.SetEventEnabledInternal(msg.EventIndex, msg.Enabled);
                MyAPIGateway.Multiplayer.SendMessageToOthers(CHANNEL, data);
            }
            else
            {
                logic.SetEventEnabledInternal(msg.EventIndex, msg.Enabled);
            }
        }

        // ── Audio event trigger (play sound on all clients) ───────────────────

        private void HandleAudioEvent(ushort channel, byte[] data, ulong sender, bool fromServer)
        {
            var msg = MyAPIGateway.Utilities.SerializeFromBinary<AudioEventTrigger>(data);
            if (msg == null) return;

            if (MyAPIGateway.Multiplayer.IsServer)
            {
                // Client triggered a test — play locally on server and relay to everyone
                AudioControllerBlock.PlayOnSpeakers(msg.GridEntityId, (AudioEvent)msg.EventIndex, msg.SoundId, msg.Volume);
                MyAPIGateway.Multiplayer.SendMessageToOthers(CHANNEL_EVENT, data);
            }
            else
            {
                // Server fired an event — play locally on this client
                AudioControllerBlock.PlayOnSpeakers(msg.GridEntityId, (AudioEvent)msg.EventIndex, msg.SoundId, msg.Volume);
            }
        }
    }
}
