using VRage.Game.Components;

namespace TTNavalBlocks.NASRegistration
{
    // ───────────────────────────────────────────────────────────────────────────
    // NAS_Cable — registers carrier elevator cable configs with TTNAS.
    //
    // MovingSubpartName : the subpart that slides (the elevator platform).
    // CableCount        : number of cable pairs in the model.
    // StartPrefix/EndPrefix : bone name prefixes for cable attach points.
    //   Verify against the .mwm model — defaults assume cablestart_1/cableend_1.
    // ───────────────────────────────────────────────────────────────────────────
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class NAS_Cable_Session : MySessionComponentBase
    {
        public override void BeforeStart()
        {
            // ── Carrier Central Elevator 9×7×5 ───────────────────────────────
            NASApiClient.RegisterCable(new NASApiClient.CableBlockConfigData
            {
                SubtypeId         = "TTCarrier_CenterLift_9x7x5",
                MovingSubpartName = "platform",
                CableCount        = 2,
                StartPrefix       = "cablestart_",
                EndPrefix         = "cableend_",
                Material          = "TTSteelCable",
                Width             = 0.06f,
                SegmentLen        = 0.4,
                SagFactor         = 0.0,   // vertical — no sag
                Mode              = 0,
                ColorR            = 0.55f,
                ColorG            = 0.55f,
                ColorB            = 0.55f,
                ColorA            = 1.0f,
            });

            // ── Elizabeth-class Elevator 11×8×6 (WIP) ────────────────────────
            NASApiClient.RegisterCable(new NASApiClient.CableBlockConfigData
            {
                SubtypeId         = "TTCarrier_Elizabeth_Elevator_11x8x6",
                MovingSubpartName = "lift",
                CableCount        = 2,
                StartPrefix       = "cablestart_",
                EndPrefix         = "cableend_",
                Material          = "TTSteelCable",
                Width             = 0.06f,
                SegmentLen        = 0.4,
                SagFactor         = 0.0,
                Mode              = 0,
                ColorR            = 0.55f,
                ColorG            = 0.55f,
                ColorB            = 0.55f,
                ColorA            = 1.0f,
            });
        }
    }
}
