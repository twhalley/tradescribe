// The ledger: everything the character has seen on trade screens, plus the game's own trade rumours.
// Kept per campaign in plain CSV files; nothing is written into the save.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace TradeScribe
{
    public class Obs
    {
        public long Snap; public int Day; public string TownId = "", Town = "", ItemId = "", Good = "", Type = "", Date = "";
        public int Buy = -1, Sell = -1, Stock, Held; public float Weight;
    }

    public class PricePoint
    {
        public string TownId, Town, ItemId, Good, Type; public float Weight;
        public int Buy = -1, BuyDay = -1, Stock, Sell = -1, SellDay = -1;
    }

    public class Rumour
    {
        public int DaySeen, EndDay, Buy, Sell; public string TownId = "", Town = "", ItemId = "", Good = "";
        public string Key => TownId + "|" + ItemId + "|" + EndDay + "|" + Buy + "|" + Sell;
    }

    public class Trade
    {
        public int Day, Units, Total, Profit, Matched; public string Date = "", TownId = "", Town = "", ItemId = "", Good = "", Action = "";
    }

    public static class Ledger
    {
        private const string TradeHeader = "Day,Date,Town,Action,Units,Good,Total,Each,Profit,Units with known cost,TownId,ItemId";
        public static readonly List<Trade> Trades = new List<Trade>();
        // What the goods you bought and still hold cost you: item -> (units, total paid).
        private static readonly Dictionary<string, KeyValuePair<int, int>> Basis = new Dictionary<string, KeyValuePair<int, int>>();
        private static string TradePath => Path.Combine(Dir, "trades.csv");

        public static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord", "TradeScribe");
        private const string ObsHeader = "Snap,Day,Town,Good,Buy price,Sell price,Stock,Type,Date,Held,TownId,ItemId,Weight";
        private const string RumHeader = "Day seen,Town,Good,Buy price,Sell price,Expires day,TownId,ItemId";

        private static string _gameId;
        public static readonly Dictionary<string, PricePoint> Points = new Dictionary<string, PricePoint>();
        public static readonly List<Rumour> Rumours = new List<Rumour>();
        private static readonly HashSet<string> RumourKeys = new HashSet<string>();
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static int Today => (int)CampaignTime.Now.ToDays;
        private static string Dir => Path.Combine(Root, Safe(_gameId));
        private static string ObsPath => Path.Combine(Dir, "observations.csv");
        private static string RumPath => Path.Combine(Dir, "rumours.csv");

        private static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "campaign";
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }

        /// Loads this campaign's files the first time it is needed (or when another save is loaded).
        public static void EnsureLoaded()
        {
            string id = Campaign.Current.UniqueGameId ?? "campaign";
            if (id == _gameId) return;
            _gameId = id; Points.Clear(); Rumours.Clear(); RumourKeys.Clear(); Trades.Clear(); Basis.Clear();
            if (File.Exists(ObsPath))
            {
                var group = new List<Obs>(); long snap = long.MinValue;
                foreach (string line in File.ReadAllLines(ObsPath).Skip(1))
                {
                    List<string> f = Csv.Parse(line); if (f.Count < 13) continue;
                    var o = new Obs { Snap = L(f[0]), Day = I(f[1], 0), Town = f[2], Good = f[3], Buy = I(f[4], -1), Sell = I(f[5], -1), Stock = I(f[6], 0),
                        Type = f[7], Date = f[8], Held = I(f[9], 0), TownId = f[10], ItemId = f[11], Weight = F(f[12]) };
                    if (o.Snap != snap && group.Count > 0) { Apply(group); group = new List<Obs>(); }
                    snap = o.Snap; group.Add(o);
                }
                if (group.Count > 0) Apply(group);
            }
            LoadTrades();
            if (File.Exists(RumPath))
                foreach (string line in File.ReadAllLines(RumPath).Skip(1))
                {
                    List<string> f = Csv.Parse(line); if (f.Count < 8) continue;
                    var r = new Rumour { DaySeen = I(f[0], 0), Town = f[1], Good = f[2], Buy = I(f[3], 0), Sell = I(f[4], 0), EndDay = I(f[5], 0), TownId = f[6], ItemId = f[7] };
                    if (RumourKeys.Add(r.Key)) Rumours.Add(r);
                }
        }

        private static void LoadTrades()
        {
            if (!File.Exists(TradePath)) return;
            foreach (string line in File.ReadAllLines(TradePath).Skip(1))
            {
                List<string> f = Csv.Parse(line); if (f.Count < 12) continue;
                var t = new Trade { Day = I(f[0], 0), Date = f[1], Town = f[2], Action = f[3], Units = I(f[4], 0), Good = f[5], Total = I(f[6], 0), TownId = f[10], ItemId = f[11] };
                Book(t); Trades.Add(t);
            }
        }

        /// Works out the profit on a sale against what the same goods cost, and keeps the running cost of what is still held.
        private static void Book(Trade t)
        {
            Basis.TryGetValue(t.ItemId, out KeyValuePair<int, int> b);
            if (t.Action == "Bought") { Basis[t.ItemId] = new KeyValuePair<int, int>(b.Key + t.Units, b.Value + t.Total); return; }
            t.Matched = Math.Min(t.Units, b.Key);
            if (t.Matched <= 0 || t.Units <= 0) { t.Matched = 0; t.Profit = 0; return; }
            int cost = (int)Math.Round((double)b.Value * t.Matched / b.Key);
            t.Profit = (int)Math.Round((double)t.Total * t.Matched / t.Units) - cost;
            Basis[t.ItemId] = new KeyValuePair<int, int>(b.Key - t.Matched, b.Value - cost);
        }

        /// Records the trades made in one visit, once the trade screen has been confirmed.
        public static void AddTrades(List<Trade> trades)
        {
            if (trades.Count == 0) return;
            EnsureLoaded();
            Directory.CreateDirectory(Dir);
            var sb = new StringBuilder();
            if (!File.Exists(TradePath)) sb.AppendLine(TradeHeader);
            foreach (Trade t in trades)
            {
                Book(t); Trades.Add(t);
                sb.AppendLine(string.Join(",", t.Day.ToString(Inv), Csv.Q(t.Date), Csv.Q(t.Town), t.Action, t.Units.ToString(Inv), Csv.Q(t.Good), t.Total.ToString(Inv),
                    (t.Units > 0 ? (double)t.Total / t.Units : 0).ToString("0.#", Inv), t.Action == "Sold" && t.Matched > 0 ? t.Profit.ToString(Inv) : "",
                    t.Action == "Sold" ? t.Matched.ToString(Inv) : "", Csv.Q(t.TownId), Csv.Q(t.ItemId)));
            }
            File.AppendAllText(TradePath, sb.ToString(), new UTF8Encoding(false));
        }

        /// One trade screen: everything the trader stocks and everything the player carries.
        public static void AddSnapshot(List<Obs> rows)
        {
            if (rows.Count == 0) return;
            EnsureLoaded();
            Directory.CreateDirectory(Dir);
            var sb = new StringBuilder();
            if (!File.Exists(ObsPath)) sb.AppendLine(ObsHeader);
            foreach (Obs o in rows)
                sb.AppendLine(string.Join(",", o.Snap.ToString(Inv), o.Day.ToString(Inv), Csv.Q(o.Town), Csv.Q(o.Good), N(o.Buy), N(o.Sell),
                    o.Stock > 0 ? o.Stock.ToString(Inv) : "", Csv.Q(o.Type), Csv.Q(o.Date), o.Held > 0 ? o.Held.ToString(Inv) : "",
                    Csv.Q(o.TownId), Csv.Q(o.ItemId), o.Weight.ToString("0.###", Inv)));
            File.AppendAllText(ObsPath, sb.ToString(), new UTF8Encoding(false));
            Apply(rows);
        }

        private static void Apply(List<Obs> rows)
        {
            // The screen showed the trader's whole stock, so anything not listed is no longer for sale there.
            string town = rows[0].TownId;
            foreach (PricePoint p in Points.Values) if (p.TownId == town) { p.Buy = -1; p.Stock = 0; }
            foreach (Obs o in rows)
            {
                string key = o.TownId + "|" + o.ItemId;
                if (!Points.TryGetValue(key, out PricePoint p))
                    Points[key] = p = new PricePoint { TownId = o.TownId, ItemId = o.ItemId };
                p.Town = o.Town; p.Good = o.Good; p.Type = o.Type; p.Weight = o.Weight;
                if (o.Buy > 0) { p.Buy = o.Buy; p.BuyDay = o.Day; p.Stock = o.Stock; }
                if (o.Sell > 0) { p.Sell = o.Sell; p.SellDay = o.Day; }
            }
        }

        /// Copies any rumours the game has given the player that are not in the log yet. Returns how many were new.
        public static int SyncRumours()
        {
            EnsureLoaded();
            var beh = Campaign.Current.GetCampaignBehavior<ITradeRumorCampaignBehavior>();
            if (beh == null || beh.TradeRumors == null) return 0;
            var sb = new StringBuilder(); int added = 0;
            foreach (TradeRumor tr in beh.TradeRumors)
            {
                if (tr == null || tr.Settlement == null || tr.ItemCategory == null) continue;
                var r = new Rumour { DaySeen = Today, EndDay = (int)tr.RumorEndTime.ToDays, Buy = tr.BuyPrice, Sell = tr.SellPrice,
                    TownId = tr.Settlement.StringId, Town = tr.Settlement.Name.ToString(), ItemId = tr.ItemCategory.StringId, Good = tr.ItemCategory.Name.ToString() };
                if (!RumourKeys.Add(r.Key)) continue;
                Rumours.Add(r); added++;
                sb.AppendLine(string.Join(",", r.DaySeen.ToString(Inv), Csv.Q(r.Town), Csv.Q(r.Good), r.Buy.ToString(Inv), r.Sell.ToString(Inv),
                    r.EndDay.ToString(Inv), Csv.Q(r.TownId), Csv.Q(r.ItemId)));
            }
            if (added > 0)
            {
                Directory.CreateDirectory(Dir);
                if (!File.Exists(RumPath)) sb.Insert(0, RumHeader + Environment.NewLine);
                File.AppendAllText(RumPath, sb.ToString(), new UTF8Encoding(false));
            }
            return added;
        }

        public static void LogError(Exception e)
        {
            try { Directory.CreateDirectory(Root); File.AppendAllText(Path.Combine(Root, "errors.txt"), DateTime.Now + " " + e + Environment.NewLine); } catch { }
        }

        private static string N(int v) => v >= 0 ? v.ToString(Inv) : "";
        private static int I(string s, int d) => int.TryParse(s, NumberStyles.Integer, Inv, out int v) ? v : d;
        private static long L(string s) => long.TryParse(s, NumberStyles.Integer, Inv, out long v) ? v : 0;
        private static float F(string s) => float.TryParse(s, NumberStyles.Float, Inv, out float v) ? v : 0f;
    }

    public static class Csv
    {
        public static string Q(string s)
        {
            if (s == null) return "";
            return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        public static List<string> Parse(string line)
        {
            var res = new List<string>(); var sb = new StringBuilder(); bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q)
                {
                    if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; }
                    else sb.Append(c);
                }
                else if (c == '"') q = true;
                else if (c == ',') { res.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            res.Add(sb.ToString());
            return res;
        }
    }
}
