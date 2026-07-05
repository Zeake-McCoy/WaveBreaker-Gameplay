using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using VRage.Utils;

namespace TTNAS
{
    public static class NavalPersistence
    {
        private const string SAVE_FILE = "TTNASBlockStates.dat";

        public static string Read(long entityId, Type callerType)
        {
            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(SAVE_FILE, callerType))
                    return null;

                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(SAVE_FILE, callerType))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        long eid;
                        if (long.TryParse(line.Substring(0, eq), out eid) && eid == entityId)
                            return line.Substring(eq + 1);
                    }
                }
            }
            catch (Exception e) { NASLog.Error("Persistence", "Read error: " + e); }
            return null;
        }

        public static void Write(long entityId, string blob, Type callerType)
        {
            try
            {
                var lines = new List<string>();
                bool found = false;

                if (MyAPIGateway.Utilities.FileExistsInWorldStorage(SAVE_FILE, callerType))
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(SAVE_FILE, callerType))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            int eq = line.IndexOf('=');
                            if (eq > 0)
                            {
                                long eid;
                                if (long.TryParse(line.Substring(0, eq), out eid) && eid == entityId)
                                {
                                    lines.Add(entityId + "=" + blob);
                                    found = true;
                                    continue;
                                }
                            }
                            lines.Add(line);
                        }
                    }
                }

                if (!found) lines.Add(entityId + "=" + blob);

                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(SAVE_FILE, callerType))
                    foreach (var line in lines)
                        writer.WriteLine(line);
            }
            catch (Exception e) { NASLog.Error("Persistence", "Write error: " + e); }
        }

        // ── Blob encoding ─────────────────────────────────────────────────────

        public static void Append(StringBuilder sb, string key, string value)
            => sb.Append(key).Append('=').Append(value).Append(';');

        public static void AppendBool(StringBuilder sb, string key, bool value)
            => Append(sb, key, value ? "1" : "0");

        public static void AppendFloat(StringBuilder sb, string key, float value)
            => Append(sb, key, value.ToString("0.###", CultureInfo.InvariantCulture));

        public static void AppendDouble(StringBuilder sb, string key, double value)
            => Append(sb, key, value.ToString("0.######", CultureInfo.InvariantCulture));

        // ── Blob decoding ─────────────────────────────────────────────────────

        public static string ReadStr(string blob, string key, string def = null)
        {
            if (string.IsNullOrEmpty(blob)) return def;
            var parts = blob.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                int eq = p.IndexOf('=');
                if (eq > 0 && eq == key.Length &&
                    string.Compare(p, 0, key, 0, eq, StringComparison.OrdinalIgnoreCase) == 0)
                    return p.Substring(eq + 1);
            }
            return def;
        }

        public static bool ReadBool(string blob, string key, bool def = false)
        {
            var s = ReadStr(blob, key);
            if (s == null) return def;
            if (s == "1" || string.Equals(s, "true",  StringComparison.OrdinalIgnoreCase)) return true;
            if (s == "0" || string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return def;
        }

        public static float ReadFloat(string blob, string key, float def = 0f)
        {
            var s = ReadStr(blob, key);
            float f;
            return (!string.IsNullOrEmpty(s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) ? f : def;
        }

        public static double ReadDouble(string blob, string key, double def = 0.0)
        {
            var s = ReadStr(blob, key);
            double d;
            return (!string.IsNullOrEmpty(s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) ? d : def;
        }
    }
}
