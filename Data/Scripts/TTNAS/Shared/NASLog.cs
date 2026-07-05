using Sandbox.ModAPI;
using VRage.Utils;

namespace TTNAS
{
    // ───────────────────────────────────────────────────────────────────────────
    // NASLog — unified logging for all TTNAS components.
    //
    // Every log line is prefixed with [TTNAS] so the mod's output is trivially
    // grep-able in the Space Engineers log file.
    //
    // Info  → MyLog only (verbose, never console-spams the player)
    // Warn  → MyLog only
    // Error → MyLog + game console (visible in-session)
    // ───────────────────────────────────────────────────────────────────────────
    public static class NASLog
    {
        private const string PREFIX = "[TTNAS] ";

        public static void Info(string msg)
            => MyLog.Default.WriteLine(PREFIX + msg);

        public static void Info(string context, string msg)
            => MyLog.Default.WriteLine(PREFIX + "[" + context + "] " + msg);

        public static void Warn(string msg)
            => MyLog.Default.WriteLine(PREFIX + "WARN: " + msg);

        public static void Warn(string context, string msg)
            => MyLog.Default.WriteLine(PREFIX + "[" + context + "] WARN: " + msg);

        public static void Error(string msg)
            => MyLog.Default.WriteLineAndConsole(PREFIX + "ERROR: " + msg);

        public static void Error(string context, string msg)
            => MyLog.Default.WriteLineAndConsole(PREFIX + "[" + context + "] ERROR: " + msg);
    }
}
