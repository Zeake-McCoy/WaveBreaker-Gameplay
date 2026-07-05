using System;
using ProtoBuf;

namespace TTNAS
{
    public enum CableRenderMode
    {
        Tube = 0,
        Line = 1
    }

    [Serializable]
    [ProtoContract]
    public class CableBlockConfig
    {
        [ProtoMember(1)]  public string SubtypeId;

        /// <summary>Optional. If null/empty, end dummies are on the base model.</summary>
        [ProtoMember(2)]  public string MovingSubpartName;

        [ProtoMember(3)]  public int    CableCount   = 1;
        [ProtoMember(4)]  public string StartPrefix  = "cablestart_";
        [ProtoMember(5)]  public string EndPrefix    = "cableend_";

        [ProtoMember(6)]  public string Material     = "GizmoDrawLine";
        [ProtoMember(7)]  public float  Width        = 0.025f;
        [ProtoMember(8)]  public double SegmentLen   = 0.25;
        [ProtoMember(9)]  public double SagFactor    = 0.08;

        [ProtoMember(10)] public CableRenderMode Mode = CableRenderMode.Tube;

        // 0..1 RGBA
        [ProtoMember(11)] public float ColorR = 0.6f;
        [ProtoMember(12)] public float ColorG = 0.6f;
        [ProtoMember(13)] public float ColorB = 0.6f;
        [ProtoMember(14)] public float ColorA = 1.0f;
    }
}
