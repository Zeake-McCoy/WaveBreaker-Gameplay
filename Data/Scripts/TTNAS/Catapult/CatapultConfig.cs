using System;
using System.Collections.Generic;
using ProtoBuf;

namespace TTNAS
{
    // ───────────────────────────────────────────────────────────────────────────
    // CatapultConfig — per-subtype tunables.
    //
    // All values have defaults matching the original TTCarrier_Catapult constants.
    // To add a new catapult variant, call CatapultConfigs.Register() with a new
    // instance and override only the values that differ.
    // ───────────────────────────────────────────────────────────────────────────
    [ProtoContract]
    public class CatapultConfig
    {
        // ── Identity (required for cross-mod API registration) ────────────────
        [ProtoMember(20)] public string SubtypeId;

        // ── Track geometry ────────────────────────────────────────────────────
        [ProtoMember(1)]  public double ShuttleOffsetM        = 7.5;
        [ProtoMember(2)]  public double CatapultLengthM       = 30.0;

        // ── Detection box ─────────────────────────────────────────────────────
        [ProtoMember(3)]  public double DetectHalfLengthM     = -1;   // -1 = auto (CatapultLengthM / 4)
        [ProtoMember(4)]  public double DetectHalfWidthM      = 4.0;
        [ProtoMember(5)]  public double DetectHalfHeightM     = 3.0;
        [ProtoMember(6)]  public double DetectUpOffsetM       = 2.5;

        public double EffectiveDetectHalfLengthM
        {
            get { return DetectHalfLengthM >= 0 ? DetectHalfLengthM : CatapultLengthM / 4.0; }
        }

        // ── Ready thresholds ──────────────────────────────────────────────────
        [ProtoMember(7)]  public double MaxReadySpeedMps      = 6.0;
        [ProtoMember(8)]  public double MinAlignDotAbs        = 0.50;

        // ── Detection cadence ─────────────────────────────────────────────────
        [ProtoMember(9)]  public int    DetectEveryNUpdates10 = 3;

        // ── Launch ────────────────────────────────────────────────────────────
        [ProtoMember(10)] public float  LaunchAddSpeedMps     = 120f;
        [ProtoMember(11)] public float  LaunchDurationS       = -1f;  // -1 = auto from track length

        public float EffectiveLaunchDurationS
        {
            get
            {
                if (LaunchDurationS >= 0f) return LaunchDurationS;
                float v = Math.Max(1f, LaunchAddSpeedMps);
                return (float)(2.0 * CatapultLengthM / v);
            }
        }

        // ── Timing ────────────────────────────────────────────────────────────
        [ProtoMember(12)] public int    CooldownTicks          = 180;
        [ProtoMember(13)] public int    ReturnAfterLaunchTicks = 120;

        // ── Mechanical-link BFS scan radius ───────────────────────────────────
        [ProtoMember(14)] public double MechGroupScanRadiusM   = 10.0;

        // ── Hold ──────────────────────────────────────────────────────────────
        [ProtoMember(15)] public bool   HoldEnabled            = true;
        [ProtoMember(16)] public double HoldPosCorrectionKp    = 10.0;
        [ProtoMember(17)] public double HoldMaxPosCorrection   = 5.0;

        // ── Hold lift ─────────────────────────────────────────────────────────
        [ProtoMember(18)] public double HoldLiftOffsetM        = 0.10;

        // ── Hold alignment ────────────────────────────────────────────────────
        [ProtoMember(19)] public double HoldAlignKp            = 3.0;
        [ProtoMember(21)] public double HoldAlignMaxOmega      = 1.0;
        [ProtoMember(22)] public double HoldAlignGateRad       = 0.52;
    }

    // ───────────────────────────────────────────────────────────────────────────
    // CatapultConfigs — static registry keyed by block subtype ID.
    // ───────────────────────────────────────────────────────────────────────────
    public static class CatapultConfigs
    {
        private static readonly Dictionary<string, CatapultConfig> _registry
            = new Dictionary<string, CatapultConfig>();

        static CatapultConfigs()
        {
            Register("TTCarrier_Catapult", new CatapultConfig());
            Register("TTCarrier_Catapult_100m", new CatapultConfig {
                CatapultLengthM      = 200.0,
                ShuttleOffsetM       = 0,
                MechGroupScanRadiusM = 40.0,
                LaunchAddSpeedMps    = 600f,
                LaunchDurationS      = 10f,
            });
        }

        public static void Register(string subtypeId, CatapultConfig config)
        {
            _registry[subtypeId] = config;
        }

        public static bool TryGet(string subtypeId, out CatapultConfig config)
        {
            return _registry.TryGetValue(subtypeId, out config);
        }

        public static bool IsKnown(string subtypeId)
        {
            return _registry.ContainsKey(subtypeId);
        }
    }
}
