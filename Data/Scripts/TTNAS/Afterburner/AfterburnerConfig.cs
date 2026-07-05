using System.Collections.Generic;
using ProtoBuf;

namespace TTNAS
{
    // ───────────────────────────────────────────────────────────────────────────
    // AfterburnerConfig — per-subtype afterburner parameters.
    //
    // External mods can register their thruster subtypes two ways:
    //   1. Direct call (if loaded alongside TTNAS in same mod):
    //        AfterburnerConfigs.Register(new AfterburnerConfig { ... });
    //
    //   2. Cross-mod API (separate workshop mod):
    //        MyAPIGateway.Utilities.SendModMessage(NASApiSession.Channel,
    //            MyTuple.Create("AfterburnerConfig",
    //                MyAPIGateway.Utilities.SerializeToBinary(myConfig)));
    //      where myConfig is a local copy of AfterburnerConfig with matching
    //      [ProtoMember] attribute numbers.
    // ───────────────────────────────────────────────────────────────────────────
    [ProtoContract]
    public class AfterburnerConfig
    {
        [ProtoMember(1)] public string SubtypeId;
        [ProtoMember(2)] public string ParticleEffect;
        [ProtoMember(3)] public string SoundEffect;
        [ProtoMember(4)] public float  PowerMultiplier  = 2.0f;
        [ProtoMember(5)] public float  ThrustMultiplier = 1.5f;
        [ProtoMember(6)] public float  ParticleScale    = 1.0f;
    }

    public static class AfterburnerConfigs
    {
        private static readonly Dictionary<string, AfterburnerConfig> _configs =
            new Dictionary<string, AfterburnerConfig>();

        /// <summary>Register a thruster subtype for afterburner support.</summary>
        public static void Register(AfterburnerConfig config)
        {
            if (config == null || string.IsNullOrEmpty(config.SubtypeId)) return;
            _configs[config.SubtypeId] = config;
        }

        public static bool TryGet(string subtypeId, out AfterburnerConfig config)
        {
            if (subtypeId == null) { config = null; return false; }
            return _configs.TryGetValue(subtypeId, out config);
        }

        public static bool Contains(string subtypeId)
            => subtypeId != null && _configs.ContainsKey(subtypeId);
    }
}
