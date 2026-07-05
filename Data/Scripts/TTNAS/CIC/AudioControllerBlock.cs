using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace TTNAS
{
    public enum AudioEvent
    {
        IncomingMissile = 0,
        UnderAttack     = 1,
        NewContact      = 2,
        AllClear        = 3,
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ConveyorSorter), false,
        "TT_AudioController", "TT_AudioController_SG")]
    public class AudioControllerBlock : MyGameLogicComponent
    {
        // ── Constants ─────────────────────────────────────────────────────────

        public const int EVENT_COUNT = 4;

        public static readonly string[] EventNames = new string[]
        {
            "Incoming Missile",
            "Under Attack",
            "New Contact",
            "All Clear",
        };

        public struct SoundEntry
        {
            public long   Key;
            public string SoundId;
            public string DisplayName;
            public SoundEntry(long k, string s, string d) { Key = k; SoundId = s; DisplayName = d; }
        }

        public static readonly List<SoundEntry> SoundLibrary = new List<SoundEntry>
        {
            new SoundEntry(0, "",                               "— None —"),
            new SoundEntry(1, "TTNAS_action_royalnavy",            "RN Action Stations"),
            new SoundEntry(2, "TTNAS_action_usnavyold",            "USN Action Stations old(BROKEN)"),
            new SoundEntry(3, "TTNAS_action_usnavynew",            "USN Action Stations new"),
            new SoundEntry(4, "TTNAS_action_submarine",     "USN Action Stations submarine"),

        };

        // ── Registry ──────────────────────────────────────────────────────────

        private static readonly Dictionary<long, AudioControllerBlock> _registry
            = new Dictionary<long, AudioControllerBlock>();

        public static AudioControllerBlock GetForGrid(IMyCubeGrid grid)
        {
            if (grid == null) return null;
            var top = grid.GetTopMostParent() as IMyCubeGrid ?? grid;
            AudioControllerBlock ctrl;
            return _registry.TryGetValue(top.EntityId, out ctrl) ? ctrl : null;
        }

        // ── Block state ───────────────────────────────────────────────────────

        private IMyTerminalBlock   _block;
        private IMyFunctionalBlock _fblock;

        // Per-event sound selection
        private readonly long[] _soundKeys = new long[EVENT_COUNT];

        // Shared global settings
        private float _globalVolume    = 1f;
        private float _globalCooldownS = 15f;

        // Per-event cooldown countdown (uses _globalCooldownS as duration)
        private readonly float[] _cooldowns = new float[EVENT_COUNT];

        // Previous-frame CIC state
        private bool _prevMissilesInbound = false;
        private int  _prevThreatCount     = 0;
        private bool _prevUnderAttack     = false;

        private static bool _controlsRegistered = false;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            _block  = Entity as IMyTerminalBlock;
            _fblock = Entity as IMyFunctionalBlock;
            if (_block == null) return;

            NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME | MyEntityUpdateEnum.BEFORE_NEXT_FRAME;

            if (!_controlsRegistered)
            {
                _controlsRegistered = true;
                RegisterControls();
            }

            var top = _block.CubeGrid.GetTopMostParent() as IMyCubeGrid ?? _block.CubeGrid;
            if (!_registry.ContainsKey(top.EntityId))
                _registry[top.EntityId] = this;

            _block.AppendingCustomInfo += AppendInfo;
        }

        public override void Close()
        {
            if (_block != null)
            {
                _block.AppendingCustomInfo -= AppendInfo;
                var top = _block.CubeGrid?.GetTopMostParent() as IMyCubeGrid ?? _block.CubeGrid;
                if (top != null)
                {
                    AudioControllerBlock existing;
                    if (_registry.TryGetValue(top.EntityId, out existing) && existing == this)
                        _registry.Remove(top.EntityId);
                }
            }
        }

        // ── Update ────────────────────────────────────────────────────────────

        public override void UpdateOnceBeforeFrame()
        {
            StorageInit();
            LoadState();
        }

        public override bool IsSerialized()
        {
            SaveState();
            return base.IsSerialized();
        }

        public override void UpdateAfterSimulation10()
        {
            if (_block == null || _fblock == null || !_fblock.IsWorking) return;
            if (!MyAPIGateway.Multiplayer.IsServer) return;

            float dt = 10f / 60f;
            for (int i = 0; i < EVENT_COUNT; i++)
                if (_cooldowns[i] > 0f) _cooldowns[i] -= dt;

            var cic = CICProcessor.GetForGrid(_block.CubeGrid);
            if (cic == null) return;

            bool missilesNow = cic.MissilesInbound;
            int  threatsNow  = cic.Threats.Count;
            bool underAttack = threatsNow > 0;

            if (missilesNow && !_prevMissilesInbound)
                TryFire(AudioEvent.IncomingMissile);

            if (underAttack && !_prevUnderAttack)
                TryFire(AudioEvent.UnderAttack);

            if (threatsNow > _prevThreatCount && !underAttack)
                TryFire(AudioEvent.NewContact);

            if (!underAttack && _prevUnderAttack)
                TryFire(AudioEvent.AllClear);

            _prevMissilesInbound = missilesNow;
            _prevThreatCount     = threatsNow;
            _prevUnderAttack     = underAttack;
        }

        // ── Event firing ─────────────────────────────────────────────────────

        private void TryFire(AudioEvent ev)
        {
            int idx = (int)ev;
            if (_soundKeys[idx] == 0) return;
            if (_cooldowns[idx] > 0f) return;

            string soundId = GetSoundId(_soundKeys[idx]);
            if (string.IsNullOrEmpty(soundId)) return;

            _cooldowns[idx] = _globalCooldownS;
            BroadcastToSpeakers(ev, soundId, _globalVolume);
        }

        public void FireEvent(AudioEvent ev) => TryFire(ev);

        private void BroadcastToSpeakers(AudioEvent ev, string soundId, float volume)
        {
            var top = _block.CubeGrid.GetTopMostParent() as IMyCubeGrid ?? _block.CubeGrid;
            PlayOnSpeakers(top.EntityId, ev, soundId, volume);

            if (!MyAPIGateway.Multiplayer.IsServer) return;
            var msg = new AudioEventTrigger
            {
                GridEntityId = top.EntityId,
                EventIndex   = (int)ev,
                SoundId      = soundId,
                Volume       = volume,
            };
            MyAPIGateway.Multiplayer.SendMessageToOthers(
                AudioSession.CHANNEL_EVENT,
                MyAPIGateway.Utilities.SerializeToBinary(msg));
        }

        public static void PlayOnSpeakers(long gridEntityId, AudioEvent ev, string soundId, float volume)
        {
            var grid = MyAPIGateway.Entities.GetEntityById(gridEntityId) as IMyCubeGrid;
            if (grid == null) return;
            var top = grid.GetTopMostParent() as IMyCubeGrid ?? grid;
            var blocks = new List<IMySlimBlock>();
            top.GetBlocks(blocks, slim =>
                slim.FatBlock != null &&
                (slim.FatBlock.BlockDefinition.SubtypeId == "TT_AudioSpeaker" ||
                 slim.FatBlock.BlockDefinition.SubtypeId == "TT_AudioSpeaker_SG"));
            foreach (var slim in blocks)
            {
                var spk = slim.FatBlock?.GameLogic?.GetAs<AudioSpeakerBlock>();
                spk?.PlayAlert(ev, soundId, volume);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        public static string GetSoundId(long key)
        {
            foreach (var e in SoundLibrary)
                if (e.Key == key) return e.SoundId;
            return null;
        }

        public static string GetDisplayName(long key)
        {
            foreach (var e in SoundLibrary)
                if (e.Key == key) return e.DisplayName;
            return null;
        }

        // ── Persistence ───────────────────────────────────────────────────────

        private static readonly Guid StorageGuid = new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234560001");

        private void StorageInit()
        {
            if (Entity.Storage == null)
                Entity.Storage = new MyModStorageComponent { [StorageGuid] = "" };
        }

        private void SaveState()
        {
            if (_block == null || Entity.Storage == null) return;
            var sb = new StringBuilder();
            for (int i = 0; i < EVENT_COUNT; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(_soundKeys[i]);
            }
            sb.Append('|');
            sb.Append(_globalVolume.ToString(CultureInfo.InvariantCulture));
            sb.Append('|');
            sb.Append(_globalCooldownS.ToString(CultureInfo.InvariantCulture));
            Entity.Storage[StorageGuid] = sb.ToString();
        }

        private void LoadState()
        {
            if (Entity.Storage == null) return;
            string data;
            if (!Entity.Storage.TryGetValue(StorageGuid, out data) || string.IsNullOrEmpty(data)) return;
            var sections = data.Split('|');
            if (sections.Length >= 1)
            {
                var keys = sections[0].Split(',');
                for (int i = 0; i < keys.Length && i < EVENT_COUNT; i++)
                    long.TryParse(keys[i], out _soundKeys[i]);
            }
            if (sections.Length >= 2)
                float.TryParse(sections[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _globalVolume);
            if (sections.Length >= 3)
                float.TryParse(sections[2], NumberStyles.Float, CultureInfo.InvariantCulture, out _globalCooldownS);
        }

        // ── Info panel ────────────────────────────────────────────────────────

        private void AppendInfo(IMyTerminalBlock block, StringBuilder sb)
        {
            sb.AppendLine("=== TT AUDIO CONTROLLER ===");
            var cic = CICProcessor.GetForGrid(_block?.CubeGrid);
            sb.AppendLine("CIC      : " + (cic != null ? "LINKED" : "NOT FOUND"));
            sb.AppendLine("Volume   : " + (_globalVolume * 100f).ToString("0") + "%");
            sb.AppendLine("Cooldown : " + _globalCooldownS.ToString("0") + "s");
            sb.AppendLine();
            for (int i = 0; i < EVENT_COUNT; i++)
            {
                string sound = _soundKeys[i] > 0 ? (GetDisplayName(_soundKeys[i]) ?? "?") : "—";
                sb.AppendLine(EventNames[i] + ": " + sound);
            }
        }

        // ── Terminal controls ─────────────────────────────────────────────────

        private static void RegisterControls()
        {
            AudioTerminalPatch.HideSorterControls();
            AddSep("TT_Audio_Sep0");

            // ── One sound dropdown per event ──────────────────────────────────
            for (int evIdx = 0; evIdx < EVENT_COUNT; evIdx++)
            {
                int capturedIdx = evIdx; // capture for lambda

                var lbl = MyAPIGateway.TerminalControls.CreateControl<
                    IMyTerminalControlLabel, IMyConveyorSorter>("TT_Audio_Lbl_" + evIdx);
                lbl.Label   = MyStringId.GetOrCompute(EventNames[evIdx]);
                lbl.Visible = b => b.GameLogic?.GetAs<AudioControllerBlock>() != null;
                MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(lbl);

                var combo = MyAPIGateway.TerminalControls.CreateControl<
                    IMyTerminalControlCombobox, IMyConveyorSorter>("TT_Audio_Sound_" + evIdx);
                combo.Visible = b => b.GameLogic?.GetAs<AudioControllerBlock>() != null;
                combo.ComboBoxContent = list =>
                {
                    foreach (var s in SoundLibrary)
                        list.Add(new MyTerminalControlComboBoxItem
                        {
                            Key   = s.Key,
                            Value = MyStringId.GetOrCompute(s.DisplayName),
                        });
                };
                combo.Getter = b =>
                {
                    var l = b.GameLogic?.GetAs<AudioControllerBlock>();
                    return l == null ? 0L : l._soundKeys[capturedIdx];
                };
                combo.Setter = (b, v) =>
                {
                    var l = b.GameLogic?.GetAs<AudioControllerBlock>();
                    if (l != null) { l._soundKeys[capturedIdx] = v; l.SaveState(); }
                };
                MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(combo);

                var testBtn = MyAPIGateway.TerminalControls.CreateControl<
                    IMyTerminalControlButton, IMyConveyorSorter>("TT_Audio_Test_" + evIdx);
                testBtn.Title   = MyStringId.GetOrCompute("Test: " + EventNames[evIdx]);
                testBtn.Visible = b => b.GameLogic?.GetAs<AudioControllerBlock>() != null;
                testBtn.Action  = b =>
                {
                    var l = b.GameLogic?.GetAs<AudioControllerBlock>();
                    if (l == null) return;
                    string soundId = GetSoundId(l._soundKeys[capturedIdx]);
                    if (string.IsNullOrEmpty(soundId)) return;

                    var top = b.CubeGrid.GetTopMostParent() as IMyCubeGrid ?? b.CubeGrid;
                    var msg = new AudioEventTrigger
                    {
                        GridEntityId = top.EntityId,
                        EventIndex   = capturedIdx,
                        SoundId      = soundId,
                        Volume       = l._globalVolume,
                    };
                    var bytes = MyAPIGateway.Utilities.SerializeToBinary(msg);

                    if (MyAPIGateway.Multiplayer.IsServer)
                    {
                        l.BroadcastToSpeakers((AudioEvent)capturedIdx, soundId, l._globalVolume);
                    }
                    else
                    {
                        MyAPIGateway.Multiplayer.SendMessageToServer(AudioSession.CHANNEL_EVENT, bytes);
                    }
                };
                MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(testBtn);
            }

            AddSep("TT_Audio_Sep1");

            // ── Volume slider (shared) ────────────────────────────────────────
            var volSlider = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlSlider, IMyConveyorSorter>("TT_Audio_Volume");
            volSlider.Title   = MyStringId.GetOrCompute("Volume");
            volSlider.Visible = b => b.GameLogic?.GetAs<AudioControllerBlock>() != null;
            volSlider.SetLimits(0.1f, 1f);
            volSlider.Getter  = b => b.GameLogic?.GetAs<AudioControllerBlock>()?._globalVolume ?? 1f;
            volSlider.Setter  = (b, v) =>
            {
                var l = b.GameLogic?.GetAs<AudioControllerBlock>();
                if (l != null) { l._globalVolume = v; l.SaveState(); }
            };
            volSlider.Writer = (b, sb) =>
            {
                var l = b.GameLogic?.GetAs<AudioControllerBlock>();
                if (l != null) sb.Append((l._globalVolume * 100f).ToString("0") + "%");
            };
            MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(volSlider);

            // ── Cooldown slider (shared) ──────────────────────────────────────
            var cdSlider = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlSlider, IMyConveyorSorter>("TT_Audio_Cooldown");
            cdSlider.Title   = MyStringId.GetOrCompute("Cooldown (s)");
            cdSlider.Visible = b => b.GameLogic?.GetAs<AudioControllerBlock>() != null;
            cdSlider.SetLimits(5f, 120f);
            cdSlider.Getter  = b => b.GameLogic?.GetAs<AudioControllerBlock>()?._globalCooldownS ?? 15f;
            cdSlider.Setter  = (b, v) =>
            {
                var l = b.GameLogic?.GetAs<AudioControllerBlock>();
                if (l != null) { l._globalCooldownS = v; l.SaveState(); }
            };
            cdSlider.Writer = (b, sb) =>
            {
                var l = b.GameLogic?.GetAs<AudioControllerBlock>();
                if (l != null) sb.Append(l._globalCooldownS.ToString("0") + "s");
            };
            MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(cdSlider);

        }

        private static void AddSep(string id)
        {
            var sep = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlSeparator, IMyConveyorSorter>(id);
            MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(sep);
        }
    }
}
