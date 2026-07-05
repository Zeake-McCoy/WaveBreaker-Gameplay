using System;
using System.Collections.Generic;

namespace TTNAS
{
    public static class CableConfigs
    {
        private static readonly Dictionary<string, CableBlockConfig> _configs =
            new Dictionary<string, CableBlockConfig>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "TTCarrier_Elizabeth_Elevator_11x8x6",
                    new CableBlockConfig
                    {
                        SubtypeId         = "TTCarrier_Elizabeth_Elevator_11x8x6",
                        MovingSubpartName = "lift",
                        CableCount        = 12,
                        StartPrefix       = "cablestart_",
                        EndPrefix         = "cableend_",
                        Material          = "TTSteelCable",
                        Width             = 0.02f,
                        SegmentLen        = 0.25,
                        SagFactor         = 0.08,
                        Mode              = CableRenderMode.Line,
                        ColorR = 0.6f, ColorG = 0.6f, ColorB = 0.6f, ColorA = 1f
                    }
                }
            };

        public static bool TryGet(string subtypeId, out CableBlockConfig cfg)
        {
            cfg = null;
            if (string.IsNullOrEmpty(subtypeId)) return false;
            return _configs.TryGetValue(subtypeId, out cfg);
        }

        public static void AddOrReplace(CableBlockConfig cfg)
        {
            if (cfg == null || string.IsNullOrWhiteSpace(cfg.SubtypeId)) return;
            _configs[cfg.SubtypeId] = cfg;
        }
    }
}
