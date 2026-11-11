// Turns the ledger into trade runs: where to buy each good cheapest and where to sell it for most.
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace TradeScribe
{
    public class Quote { public string TownId, Town, Basis; public int Price, Day, Stock; }

    public class Run
    {
        public string ItemId, Good; public Quote Buy, Sell; public float Weight, Distance = -1f;
        public int Each, Units, Trip;
        public string Line()
        {
            string s = "Buy " + (Units > 0 ? Units + " " : "") + Good + " in " + Buy.Town + " at " + Buy.Price
                + ", sell in " + Sell.Town + " for " + Sell.Price + " (" + Sell.Basis + (Sell.Basis == "rumour" ? ", " + Math.Max(0, Ledger.Today - Sell.Day) + " days old" : "") + "): +" + Each + " each";
            if (Units > 0) s += ", about +" + Trip + " for the trip";
            if (Distance >= 0f) s += ", distance " + Distance.ToString("0");
            return s;
        }
    }

    public static class Runs
    {
        public const int MaxAgeDays = 20;
        private static readonly HashSet<string> TradeTypes = new HashSet<string> { "Food", "Trade good", "Animal", "Horse" };

        private static bool Walks(string type) => type == "Horse" || type == "Animal";

        /// Share of the buy price a trader pays you, taken from towns where both prices were seen on the same day.
        /// Horses and livestock are measured separately from goods: traders pay far less of the asking price for them.
        public static float SellRatio(string type)
        {
            bool walks = Walks(type);
            List<float> r = Ledger.Points.Values.Where(p => p.Buy > 0 && p.Sell > 0 && p.BuyDay == p.SellDay && Walks(p.Type) == walks)
                .Select(p => (float)p.Sell / p.Buy).OrderBy(x => x).ToList();
            return r.Count == 0 ? 0.5f : r[r.Count / 2];
        }

        public static bool Fresh(int day) => day >= 0 && Ledger.Today - day <= MaxAgeDays;

        public static List<Quote> BuyQuotes(string itemId)
        {
            var res = new List<Quote>();
            foreach (PricePoint p in Ledger.Points.Values)
                if (p.ItemId == itemId && p.Buy > 0 && p.Stock > 0 && Fresh(p.BuyDay))
                    res.Add(new Quote { TownId = p.TownId, Town = p.Town, Price = p.Buy, Day = p.BuyDay, Stock = p.Stock, Basis = "seen" });
            return res;
        }

        /// Best known sell price per town: seen first, then a live rumour, then an estimate from the buy price.
        public static List<Quote> SellQuotes(string itemId)
        {
            var byTown = new Dictionary<string, Quote>();
            foreach (PricePoint p in Ledger.Points.Values)
                if (p.ItemId == itemId && p.Sell > 0 && Fresh(p.SellDay))
                    byTown[p.TownId] = new Quote { TownId = p.TownId, Town = p.Town, Price = p.Sell, Day = p.SellDay, Basis = "seen" };
            foreach (Rumour r in Ledger.Rumours)
                if (r.ItemId == itemId && r.Sell > 0 && r.EndDay >= Ledger.Today && !byTown.ContainsKey(r.TownId))
                    byTown[r.TownId] = new Quote { TownId = r.TownId, Town = r.Town, Price = r.Sell, Day = r.DaySeen, Basis = "rumour" };
            foreach (PricePoint p in Ledger.Points.Values)
                if (p.ItemId == itemId && p.Buy > 0 && Fresh(p.BuyDay) && !byTown.ContainsKey(p.TownId))
                    byTown[p.TownId] = new Quote { TownId = p.TownId, Town = p.Town, Price = (int)Math.Round(p.Buy * SellRatio(p.Type)), Day = p.BuyDay, Basis = "estimate" };
            return byTown.Values.ToList();
        }

        /// Best run for each good. With buyTownId set, only runs that start in that town.
        public static List<Run> Best(string buyTownId = null)
        {
            int cash = Hero.MainHero != null ? Hero.MainHero.Gold : 0;
            float free = FreeWeight();
            var runs = new List<Run>();
            foreach (IGrouping<string, PricePoint> g in Ledger.Points.Values.Where(p => TradeTypes.Contains(p.Type)).GroupBy(p => p.ItemId))
            {
                List<Quote> buys = BuyQuotes(g.Key); if (buyTownId != null) buys = buys.Where(b => b.TownId == buyTownId).ToList();
                List<Quote> sells = SellQuotes(g.Key);
                Run best = null;
                foreach (Quote b in buys)
                    foreach (Quote s in sells)
                    {
                        if (s.TownId == b.TownId || s.Price <= b.Price) continue;
                        if (best == null || s.Price - b.Price > best.Each)
                            best = new Run { ItemId = g.Key, Good = g.First().Good, Weight = g.First().Weight, Buy = b, Sell = s, Each = s.Price - b.Price };
                    }
                if (best == null) continue;
                Size(best, g.First().Type, cash, free);
                runs.Add(best);
            }
            return runs.OrderByDescending(r => r.Trip).ThenByDescending(r => r.Each).ToList();
        }

        private static void Size(Run run, string type, int cash, float free)
        {
            int units = Math.Min(run.Buy.Stock, cash / Math.Max(1, run.Buy.Price));
            // Horses and livestock walk; they do not use carry capacity.
            if (!Walks(type) && run.Weight > 0.01f && free >= 0f) units = Math.Min(units, (int)(free / run.Weight));
            run.Units = Math.Max(0, units); run.Trip = run.Units * run.Each;
            run.Distance = Distance(run.Buy.TownId, run.Sell.TownId);
        }

        /// Every good worth carrying from one chosen place to another, best first.
        public static List<Run> Between(string fromId, string toId)
        {
            int cash = Hero.MainHero != null ? Hero.MainHero.Gold : 0;
            float free = FreeWeight();
            var runs = new List<Run>();
            foreach (IGrouping<string, PricePoint> g in Ledger.Points.Values.Where(p => TradeTypes.Contains(p.Type)).GroupBy(p => p.ItemId))
            {
                Quote b = BuyQuotes(g.Key).FirstOrDefault(q => q.TownId == fromId);
                Quote s = SellQuotes(g.Key).FirstOrDefault(q => q.TownId == toId);
                if (b == null || s == null || s.Price <= b.Price) continue;
                var run = new Run { ItemId = g.Key, Good = g.First().Good, Weight = g.First().Weight, Buy = b, Sell = s, Each = s.Price - b.Price };
                Size(run, g.First().Type, cash, free);
                runs.Add(run);
            }
            return runs.OrderByDescending(r => r.Trip).ThenByDescending(r => r.Each).ToList();
        }

        /// Places with any known price: seen on a trade screen or named in a rumour.
        public static List<KeyValuePair<string, string>> KnownPlaces()
        {
            var d = new Dictionary<string, string>();
            foreach (PricePoint p in Ledger.Points.Values) d[p.TownId] = p.Town;
            foreach (Rumour r in Ledger.Rumours) if (r.EndDay >= Ledger.Today && !d.ContainsKey(r.TownId)) d[r.TownId] = r.Town;
            return d.OrderBy(k => k.Value).ToList();
        }

        private static float FreeWeight()
        {
            try { MobileParty p = MobileParty.MainParty; return Math.Max(0f, p.InventoryCapacity - p.TotalWeightCarried); }
            catch { return -1f; }
        }

        private static float Distance(string fromId, string toId)
        {
            try
            {
                Settlement a = Settlement.Find(fromId), b = Settlement.Find(toId);
                if (a == null || b == null) return -1f;
                return Campaign.Current.Models.MapDistanceModel.GetDistance(a, b, false, false, MobileParty.NavigationType.Default);
            }
            catch { return -1f; }
        }
    }
}
