namespace TTNAS
{
    // ───────────────────────────────────────────────────────────────────────────
    // CatapultActions — action type bytes for catapult RPC via NAS_NetworkHandler.
    //
    // Sent as the ActionType byte in NavalActionPacket (client → server).
    // NavalActionPacket.Received dispatches to CatapultBlock.HandleCatapultAction.
    // ───────────────────────────────────────────────────────────────────────────
    public static class CatapultActions
    {
        public const byte Launch = 0;
        public const byte ArmOn  = 1;
        public const byte ArmOff = 2;
    }
}
