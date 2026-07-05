using System.Collections.Generic;
using ProtoBuf;

namespace TTNAS
{
    // ───────────────────────────────────────────────────────────────────────────
    // ThrustReverserConfig — per-subtype thrust reverser parameters.
    //
    // Registration follows the same two-path pattern as AfterburnerConfig.
    // See AfterburnerConfig.cs for cross-mod API usage details.
    // ───────────────────────────────────────────────────────────────────────────
    [ProtoContract]
    public class ThrustReverserConfig
    {
        [ProtoMember(1)] public string SubtypeId;
        /// <summary>Name of the reverser subpart (without the "Subpart_" prefix).</summary>
        [ProtoMember(2)] public string SubpartName      = "reverser";
        /// <summary>Number of simulation frames the deploy/retract animation takes.</summary>
        [ProtoMember(3)] public int    AnimationLength  = 120;
        /// <summary>Metres the subpart translates along local Z when fully deployed.</summary>
        [ProtoMember(4)] public float  MovementDistance = 1.5f;
        /// <summary>Multiplier applied to the thruster's base force when reversing.</summary>
        [ProtoMember(5)] public float  ForceMultiplier  = 1.70f;
    }

    public static class ThrustReverserConfigs
    {
        private static readonly Dictionary<string, ThrustReverserConfig> _configs =
            new Dictionary<string, ThrustReverserConfig>();

        /// <summary>Register a thruster subtype for thrust-reverser support.</summary>
        public static void Register(ThrustReverserConfig config)
        {
            if (config == null || string.IsNullOrEmpty(config.SubtypeId)) return;
            _configs[config.SubtypeId] = config;
        }

        public static bool TryGet(string subtypeId, out ThrustReverserConfig config)
        {
            if (subtypeId == null) { config = null; return false; }
            return _configs.TryGetValue(subtypeId, out config);
        }

        public static bool Contains(string subtypeId)
            => subtypeId != null && _configs.ContainsKey(subtypeId);
    }
}
