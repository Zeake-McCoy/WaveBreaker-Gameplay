using System.Collections.Generic;
using ProtoBuf;

namespace TTNAS
{
    /// <summary>
    /// Per-block visual and physics configuration for the anchor framework.
    /// External mods register new configs via AnchorConfigs.Register().
    /// </summary>
    [ProtoContract]
    public class AnchorBlockConfig
    {
        /// <summary>SBC SubtypeId that this config applies to.</summary>
        [ProtoMember(1)] public string SubtypeId;

        /// <summary>Name of the anchor subpart in the block's .mwm model.</summary>
        [ProtoMember(2)] public string SubpartName    = "anchor";

        /// <summary>Material string ID used to draw the chain/cable line segments.</summary>
        [ProtoMember(3)] public string ChainTextureId = "anchorchain";

        /// <summary>Width of each chain line segment (metres).</summary>
        [ProtoMember(4)] public float  ChainWidth     = 0.7f;

        /// <summary>Particle effect spawned when the anchor hits the seabed.</summary>
        [ProtoMember(5)] public string LandParticle   = "Anchor_Land";
    }

    /// <summary>
    /// Registry of all anchor block subtypes handled by the anchor framework.
    /// TTNavalBlocks' own blocks are pre-registered below.
    /// External mods call Register() from their session component's LoadData().
    /// </summary>
    public static class AnchorConfigs
    {
        public static readonly Dictionary<string, AnchorBlockConfig> Configs =
            new Dictionary<string, AnchorBlockConfig>();

        /// <summary>
        /// Register a custom anchor block config.
        /// Call from your session component's LoadData() — before any blocks are initialized.
        /// </summary>
        public static void Register(AnchorBlockConfig config)
        {
            if (config?.SubtypeId != null)
                Configs[config.SubtypeId] = config;
        }

        public static bool TryGet(string subtypeId, out AnchorBlockConfig config)
            => Configs.TryGetValue(subtypeId, out config);
    }
}
