// The player's choices for run suggestions. Kept in settings.txt beside the ledger folders.
using System;
using System.Globalization;
using System.IO;

namespace TradeScribe
{
    public static class Settings
    {
        public static int MaxDistance;          // 0 = no limit
        public static int RumourDays = 3;       // rumours older than this are not used for run suggestions
        public static bool PerDistance;         // rank runs by profit per distance instead of trip profit
        private static bool _loaded;
        private static string FilePath => Path.Combine(Ledger.Root, "settings.txt");

        public static void EnsureLoaded()
        {
            if (_loaded) return; _loaded = true;
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] kv = line.Split('='); if (kv.Length != 2) continue;
                    int.TryParse(kv[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v);
                    if (kv[0].Trim() == "MaxDistance") MaxDistance = Math.Max(0, v);
                    else if (kv[0].Trim() == "RumourDays") RumourDays = Math.Max(0, v);
                    else if (kv[0].Trim() == "PerDistance") PerDistance = v != 0;
                }
            }
            catch (Exception e) { Ledger.LogError(e); }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Ledger.Root);
                File.WriteAllText(FilePath, "MaxDistance=" + MaxDistance + "\nRumourDays=" + RumourDays + "\nPerDistance=" + (PerDistance ? 1 : 0) + "\n");
            }
            catch (Exception e) { Ledger.LogError(e); }
        }
    }
}
