using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using System;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace TTNAS
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ConveyorSorter), false,
        "TT_AudioSpeaker", "TT_AudioSpeaker_SG")]
    public class AudioSpeakerBlock : MyGameLogicComponent
    {
        private static bool _controlsRegistered = false;

        private IMyTerminalBlock       _block;
        private MyEntity3DSoundEmitter _emitter;
        private string                 _lastSound = "";

        private string _pendingSound  = null;
        private float  _pendingVolume = 1f;
        private bool   _stateLoaded   = false;

        private readonly bool[] _enabledEvents = new bool[AudioControllerBlock.EVENT_COUNT]
            { true, true, true, true };

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            _block = Entity as IMyTerminalBlock;
            if (_block == null) return;

            _emitter = new MyEntity3DSoundEmitter(Entity as MyEntity);
            _emitter.CanPlayLoopSounds = true;

            if (!_controlsRegistered)
            {
                _controlsRegistered = true;
                RegisterControls();
            }

            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            _block.AppendingCustomInfo += AppendInfo;
        }

        public override void Close()
        {
            try { _emitter?.StopSound(true); _emitter?.Cleanup(); } catch { }
            if (_block != null)
                _block.AppendingCustomInfo -= AppendInfo;
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void PlayAlert(AudioEvent ev, string soundId, float volume)
        {
            if (_emitter == null || string.IsNullOrEmpty(soundId)) return;
            if (!_enabledEvents[(int)ev]) return;

            _pendingSound  = soundId;
            _pendingVolume = volume;
            NeedsUpdate   |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            // Load state once storage is ready
            if (!_stateLoaded)
            {
                _stateLoaded = true;
                StorageInit();
                LoadState();
            }

            if (_pendingSound == null || _emitter == null) return;
            if (MyAPIGateway.Utilities.IsDedicated) { _pendingSound = null; return; }

            try
            {
                var pair = new MySoundPair(_pendingSound);
                _emitter.VolumeMultiplier  = 1f;
                _emitter.CustomMaxDistance = 1000f;
                _emitter.PlaySingleSound(pair, stopPrevious: true);
                _lastSound    = _pendingSound;
                _pendingSound = null;
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowNotification("[TT Audio] Error: " + e.Message, 3000, "Red");
                _pendingSound = null;
            }
        }

        public void StopSound()
        {
            try { _emitter?.StopSound(false); } catch { }
        }

        public override bool IsSerialized()
        {
            SaveState();
            return base.IsSerialized();
        }

        public void SetEventEnabledInternal(int index, bool value)
        {
            if (index < 0 || index >= _enabledEvents.Length) return;
            _enabledEvents[index] = value;
            SaveState();
        }

        // ── Persistence ───────────────────────────────────────────────────────

        private static readonly Guid StorageGuid = new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234560002");

        private void StorageInit()
        {
            if (Entity.Storage == null)
                Entity.Storage = new MyModStorageComponent { [StorageGuid] = "" };
        }

        private void SaveState()
        {
            if (_block == null || Entity.Storage == null) return;
            var sb = new StringBuilder();
            for (int i = 0; i < AudioControllerBlock.EVENT_COUNT; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(_enabledEvents[i] ? "1" : "0");
            }
            Entity.Storage[StorageGuid] = sb.ToString();
        }

        private void LoadState()
        {
            if (Entity.Storage == null) return;
            string data;
            if (!Entity.Storage.TryGetValue(StorageGuid, out data) || string.IsNullOrEmpty(data)) return;
            var parts = data.Split(',');
            for (int i = 0; i < parts.Length && i < AudioControllerBlock.EVENT_COUNT; i++)
                _enabledEvents[i] = parts[i] != "0";
        }

        // ── Info panel ────────────────────────────────────────────────────────

        private void AppendInfo(IMyTerminalBlock block, StringBuilder sb)
        {
            sb.AppendLine("=== TT AUDIO SPEAKER ===");
            var ctrl = AudioControllerBlock.GetForGrid(_block?.CubeGrid);
            sb.AppendLine("Controller: " + (ctrl != null ? "LINKED" : "NOT FOUND"));
            sb.AppendLine("Last sound: " + (_lastSound.Length > 0 ? _lastSound : "—"));
            for (int i = 0; i < AudioControllerBlock.EVENT_COUNT; i++)
                sb.AppendLine("  " + AudioControllerBlock.EventNames[i] + ": " +
                    (_enabledEvents[i] ? "ON" : "OFF"));
        }

        // ── Terminal controls ─────────────────────────────────────────────────

        private static void RegisterControls()
        {
            AudioTerminalPatch.HideSorterControls();

            var sep = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlSeparator, IMyConveyorSorter>("TT_Speaker_Sep");
            MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(sep);

            var lbl = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlLabel, IMyConveyorSorter>("TT_Speaker_EventsLbl");
            lbl.Label   = MyStringId.GetOrCompute("Active Events");
            lbl.Visible = b => b.GameLogic?.GetAs<AudioSpeakerBlock>() != null;
            MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(lbl);

            for (int evIdx = 0; evIdx < AudioControllerBlock.EVENT_COUNT; evIdx++)
            {
                int capturedIdx = evIdx;

                var chk = MyAPIGateway.TerminalControls.CreateControl<
                    IMyTerminalControlCheckbox, IMyConveyorSorter>("TT_Speaker_Event_" + evIdx);
                chk.Title   = MyStringId.GetOrCompute(AudioControllerBlock.EventNames[evIdx]);
                chk.Visible = b => b.GameLogic?.GetAs<AudioSpeakerBlock>() != null;
                chk.Getter  = b =>
                {
                    var s = b.GameLogic?.GetAs<AudioSpeakerBlock>();
                    return s == null || s._enabledEvents[capturedIdx];
                };
                chk.Setter  = (b, v) =>
                {
                    var s = b.GameLogic?.GetAs<AudioSpeakerBlock>();
                    if (s == null) return;

                    var msg = new SpeakerEventSync
                    {
                        EntityId   = b.EntityId,
                        EventIndex = capturedIdx,
                        Enabled    = v,
                    };
                    var bytes = MyAPIGateway.Utilities.SerializeToBinary(msg);

                    if (MyAPIGateway.Multiplayer.IsServer)
                    {
                        s.SetEventEnabledInternal(capturedIdx, v);
                        MyAPIGateway.Multiplayer.SendMessageToOthers(AudioSession.CHANNEL, bytes);
                    }
                    else
                    {
                        MyAPIGateway.Multiplayer.SendMessageToServer(AudioSession.CHANNEL, bytes);
                    }
                };
                MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(chk);
            }
        }
    }
}
