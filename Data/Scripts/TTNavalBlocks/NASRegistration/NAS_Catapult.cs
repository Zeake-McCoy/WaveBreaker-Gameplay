using VRage.Game.Components;

namespace TTNavalBlocks.NASRegistration
{
    // ───────────────────────────────────────────────────────────────────────────
    // NAS_Catapult — registers all TT catapult subtypes with TTNavalAdvancedSystems.
    //
    // Requires TTNAS in the modlist. If absent, messages are silently dropped.
    // ───────────────────────────────────────────────────────────────────────────
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class NAS_Catapult_Session : MySessionComponentBase
    {
        public override void BeforeStart()
        {
            // ── Standard carrier catapult (30m track, 1×9 large blocks) ───────
            NASApiClient.RegisterCatapult(new NASApiClient.CatapultConfigData
            {
                SubtypeId            = "TTCarrier_Catapult",
                ShuttleOffsetM       = 7.5,
                CatapultLengthM      = 30.0,
                LaunchAddSpeedMps    = 120f,
                MechGroupScanRadiusM = 10.0,
            });

            // ── Long-run catapult (200m track, 1×40 large blocks) ────────────
            NASApiClient.RegisterCatapult(new NASApiClient.CatapultConfigData
            {
                SubtypeId            = "TTCarrier_Catapult_100m",
                ShuttleOffsetM       = 0,
                CatapultLengthM      = 200.0,
                LaunchAddSpeedMps    = 600f,
                LaunchDurationS      = 10f,
                MechGroupScanRadiusM = 40.0,
            });

            // ── Test catapult (5×10 large blocks, latch subpart) ─────────────
            NASApiClient.RegisterCatapult(new NASApiClient.CatapultConfigData
            {
                SubtypeId            = "TTRNDCatapult",
                ShuttleOffsetM       = 7.5,
                CatapultLengthM      = 25.0,
                LaunchAddSpeedMps    = 120f,
                MechGroupScanRadiusM = 15.0,
            });
        }
    }
}
