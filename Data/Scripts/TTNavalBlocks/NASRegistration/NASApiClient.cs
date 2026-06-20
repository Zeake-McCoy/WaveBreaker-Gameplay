// NASApiClient — stripped copy for TTNavalBlocks (Catapult + Cable only).
// Full template: TTNavalAdvancedSystems/Data/Scripts/TTNAS/API/NASApiClient_Template.cs

using ProtoBuf;
using Sandbox.ModAPI;

namespace TTNavalBlocks.NASRegistration
{
    public static class NASApiClient
    {
        private const long NAS_CHANNEL = 47299;

        [ProtoContract]
        public class CatapultConfigData
        {
            [ProtoMember(20)] public string SubtypeId;
            [ProtoMember(1)]  public double ShuttleOffsetM        = 7.5;
            [ProtoMember(2)]  public double CatapultLengthM       = 30.0;
            [ProtoMember(3)]  public double DetectHalfLengthM     = -1;
            [ProtoMember(4)]  public double DetectHalfWidthM      = 4.0;
            [ProtoMember(5)]  public double DetectHalfHeightM     = 3.0;
            [ProtoMember(6)]  public double DetectUpOffsetM       = 2.5;
            [ProtoMember(7)]  public double MaxReadySpeedMps      = 6.0;
            [ProtoMember(8)]  public double MinAlignDotAbs        = 0.50;
            [ProtoMember(9)]  public int    DetectEveryNUpdates10 = 3;
            [ProtoMember(10)] public float  LaunchAddSpeedMps     = 120f;
            [ProtoMember(11)] public float  LaunchDurationS       = -1f;
            [ProtoMember(12)] public int    CooldownTicks         = 180;
            [ProtoMember(13)] public int    ReturnAfterLaunchTicks= 120;
            [ProtoMember(14)] public double MechGroupScanRadiusM  = 10.0;
            [ProtoMember(15)] public bool   HoldEnabled           = true;
            [ProtoMember(16)] public double HoldPosCorrectionKp   = 10.0;
            [ProtoMember(17)] public double HoldMaxPosCorrection  = 5.0;
            [ProtoMember(18)] public double HoldLiftOffsetM       = 0.10;
            [ProtoMember(19)] public double HoldAlignKp           = 3.0;
            [ProtoMember(21)] public double HoldAlignMaxOmega     = 1.0;
            [ProtoMember(22)] public double HoldAlignGateRad      = 0.52;
        }

        [ProtoContract]
        public class CableBlockConfigData
        {
            [ProtoMember(1)]  public string SubtypeId;
            [ProtoMember(2)]  public string MovingSubpartName;
            [ProtoMember(3)]  public int    CableCount   = 1;
            [ProtoMember(4)]  public string StartPrefix  = "cablestart_";
            [ProtoMember(5)]  public string EndPrefix    = "cableend_";
            [ProtoMember(6)]  public string Material     = "GizmoDrawLine";
            [ProtoMember(7)]  public float  Width        = 0.025f;
            [ProtoMember(8)]  public double SegmentLen   = 0.25;
            [ProtoMember(9)]  public double SagFactor    = 0.08;
            [ProtoMember(10)] public int    Mode         = 0;  // 0=Tube, 1=Line
            [ProtoMember(11)] public float  ColorR       = 0.6f;
            [ProtoMember(12)] public float  ColorG       = 0.6f;
            [ProtoMember(13)] public float  ColorB       = 0.6f;
            [ProtoMember(14)] public float  ColorA       = 1.0f;
        }

        public static void RegisterCatapult(CatapultConfigData config)
        {
            if (config == null) return;
            byte[] data = MyAPIGateway.Utilities.SerializeToBinary(config);
            MyAPIGateway.Utilities.SendModMessage(NAS_CHANNEL,
                VRage.MyTuple.Create("CatapultConfig", data));
        }

        public static void RegisterCable(CableBlockConfigData config)
        {
            if (config == null) return;
            byte[] data = MyAPIGateway.Utilities.SerializeToBinary(config);
            MyAPIGateway.Utilities.SendModMessage(NAS_CHANNEL,
                VRage.MyTuple.Create("CableBlockConfig", data));
        }
    }
}
