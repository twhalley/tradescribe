// What you are carrying and where to sell it, a selling plan for a big stock, notes when you enter a town,
// and your own caravans. Uses only your inventory, your own trades, the ledger and the rumour log.
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace TradeScribe
{
    public class Held { public string ItemId, Good, Type; public int Units; }
    public class PlanStep { public Quote Town; public int Units, First, Last, Total; public float Distance = -1f; }

    public static class Carry
    {
        /// Goods in your inventory worth trading: trade goods, food, livestock, horses, and anything you bought.
        public static List<Held> Holding()
        {
            var res = new Dictionary<string, Held>();
            ItemRoster roster = MobileParty.MainParty?.ItemRoster;
            if (roster == null) return new List<Held>();
            for (int i = 0; i < roster.Count; i++)
            {
                ItemRosterElement el = roster.GetElementCopyAtIndex(i);
                EquipmentElement eq = el.EquipmentElement;
                if (eq.Item == null || el.Amount <= 0) continue;
                string id = eq.ItemModifier == null ? eq.Item.StringId : eq.Item.StringId + "+" + eq.ItemModifier.StringId;
                string type = Runs.TypeOf(eq.Item);
                if (!Runs.TradeTypes.Contains(type) && !(Ledger.CostOf(id, out int u, out _) && u > 0)) continue;
                if (!res.TryGetValue(id, out Held h)) res[id] = h = new Held { ItemId = id, Good = eq.GetModifiedItemName().ToString(), Type = type };
                h.Units += el.Amount;
            }
            return res.Values.ToList();
        }

        public static int PaidEach(string itemId)
        {
            return Ledger.CostOf(itemId, out int u, out int total) && u > 0 ? (int)Math.Round((double)total / u) : 0;
        }

        private static float Median(List<float> v) { v.Sort(); return v[v.Count / 2]; }

        /// How much the price drops with each unit you sell, as a share of the first unit's price.
        /// Measured from your own sales: the first unit sold for p and the average was a, so the slide is 2(p - a) over the lot.
        public static float Slide(string itemId, string type, out bool measured)
        {
            bool walks = Runs.Walks(type);
            var types = new Dictionary<string, string>();
            foreach (PricePoint p in Ledger.Points.Values) types[p.ItemId] = p.Type;
            var mine = new List<float>(); var kin = new List<float>();
            foreach (Trade t in Ledger.Trades)
            {
                if (t.Action != "Sold" || t.Units < 3) continue;
                if (!Ledger.History.TryGetValue(t.TownId + "|" + t.ItemId, out List<HistPoint> h)) continue;
                HistPoint p0 = h.LastOrDefault(x => x.Sell > 0 && x.Hour <= t.Hour + 0.0005);
                if (p0 == null) continue;
                double avg = (double)t.Total / t.Units;
                float f = (float)Math.Max(0, Math.Min(0.05, 2 * (p0.Sell - avg) / ((t.Units - 1) * (double)p0.Sell)));
                if (t.ItemId == itemId) mine.Add(f);
                else if (types.TryGetValue(t.ItemId, out string ty) && Runs.Walks(ty) == walks) kin.Add(f);
            }
            // Small lots barely move the price, so a measured slide can be zero. Nobody can sell a hundred of anything
            // in one town at the same price, so keep a floor.
            float floor = walks ? 0.003f : 0.005f;
            measured = mine.Count > 0;
            if (mine.Count > 0) return Math.Max(floor, Median(mine));
            if (kin.Count > 0) return Math.Max(floor, Median(kin));
            return walks ? 0.004f : 0.01f;
        }

        /// Spreads a stock over the places that pay most, one unit at a time, allowing for the price falling as you sell.
        public static List<PlanStep> Plan(string itemId, string type, int units, out float slide, out bool measured)
        {
            slide = Slide(itemId, type, out measured);
            var steps = new List<PlanStep>();
            foreach (Quote q in Runs.SellQuotes(itemId).Where(q => q.Price > 0))
            {
                float d = Runs.DistanceFromParty(q.TownId);
                if (Settings.MaxDistance > 0 && d > Settings.MaxDistance) continue;
                steps.Add(new PlanStep { Town = q, Distance = d });
            }
            if (steps.Count == 0) return steps;
            for (int u = 0; u < units; u++)
            {
                PlanStep best = null; int bestPrice = 0;
                foreach (PlanStep s in steps)
                {
                    int p = (int)Math.Round(s.Town.Price * Math.Max(0.2, 1 - slide * s.Units));
                    if (p > bestPrice) { bestPrice = p; best = s; }
                }
                if (best == null || bestPrice < 1) break;
                if (best.Units == 0) best.First = bestPrice;
                best.Units++; best.Last = bestPrice; best.Total += bestPrice;
            }
            return steps.Where(s => s.Units > 0).OrderByDescending(s => s.Total).ToList();
        }

        public static string HorseWarning(string type)
        {
            return Runs.Walks(type)
                ? "Horses and livestock: traders pay only about " + Math.Round(Runs.SellRatio(type) * 100) + "% of their own price. Check the sell price before buying."
                : null;
        }

        public static List<Entry> HoldingEntries()
        {
            var res = new List<KeyValuePair<int, Entry>>();
            foreach (Held h in Holding())
            {
                int paid = PaidEach(h.ItemId);
                string text = h.Good + ": " + Fmt.N(h.Units) + " held" + (paid > 0 ? ", paid about " + Fmt.N(paid) + " each" : ", cost not known");
                List<Quote> best = Runs.SellQuotes(h.ItemId).OrderByDescending(q => q.Price).Take(3).ToList();
                if (best.Count == 0) text += "\n    No place known to buy it yet";
                else text += "\n    Best known: " + string.Join(", ", best.Select(q => Runs.Label(q.TownId, q.Town) + " " + Fmt.N(q.Price) + " (" + q.Basis + ")"));
                int value = 0;
                if (best.Count > 0)
                {
                    List<PlanStep> plan = Plan(h.ItemId, h.Type, h.Units, out _, out _);
                    value = plan.Sum(s => s.Total);
                    if (h.Units >= 5 && plan.Count > 0)
                        text += "\n    Plan: " + string.Join(", ", plan.Take(3).Select(s => s.Units + " in " + s.Town.Town)) + (plan.Count > 3 ? ", ..." : "")
                            + ", about " + Fmt.N(value) + (paid > 0 ? " (" + Fmt.Signed(value - paid * plan.Sum(s => s.Units)) + " against cost)" : "");
                    else if (paid > 0)
                        text += "\n    " + Fmt.Signed(best[0].Price - paid) + " each against cost at " + best[0].Town;
                }
                string warn = HorseWarning(h.Type); if (warn != null) text += "\n    " + warn;
                res.Add(new KeyValuePair<int, Entry>(value, new Entry { Text = text, ItemId = h.ItemId }));
            }
            return res.OrderByDescending(x => x.Key).Select(x => x.Value).ToList();
        }

        public static List<Entry> PlanEntries(Held h, out string intro)
        {
            List<PlanStep> plan = Plan(h.ItemId, h.Type, h.Units, out float slide, out bool measured);
            int paid = PaidEach(h.ItemId), sold = plan.Sum(s => s.Units), total = plan.Sum(s => s.Total);
            intro = Fmt.N(h.Units) + " " + h.Good + " held" + (paid > 0 ? ", paid about " + Fmt.N(paid) + " each" : "") + ".\n"
                + "Each unit you sell lowers the price by about " + (slide * 100).ToString("0.0") + "% ("
                + (measured ? "measured from your own sales of it" : "a guess from similar goods you have sold") + ").";
            if (plan.Count > 0)
                intro += "\nWhole plan: " + Fmt.N(sold) + " sold for about " + Fmt.N(total) + (paid > 0 ? ", " + Fmt.Signed(total - paid * sold) + " against cost" : "") + ".";
            string warn = HorseWarning(h.Type); if (warn != null) intro += "\n" + warn;
            return plan.Select(s => new Entry
            {
                ItemId = h.ItemId,
                Text = Runs.Label(s.Town.TownId, s.Town.Town) + ": sell " + s.Units + ", from " + Fmt.N(s.First) + " down to " + Fmt.N(s.Last) + ", about " + Fmt.N(s.Total)
                    + "\n    " + s.Town.Basis + (s.Town.Basis == "estimate" ? "" : ", " + Fmt.Age(Ledger.Today - s.Town.Day))
                    + (s.Distance >= 0f ? ", distance " + s.Distance.ToString("0") + " from you" : ""),
                Pin = "Selling plan for " + h.Good + ": " + s.Units + " in " + s.Town.Town + " from " + Fmt.N(s.First) + " down to " + Fmt.N(s.Last)
            }).ToList();
        }

        /// A short note when you arrive somewhere that is your best known market for what you carry,
        /// or the cheapest known place for a good you pinned.
        public static void OnEnter(Settlement s)
        {
            if (s == null || !(s.IsTown || s.IsVillage || s.IsCastle)) return;
            Ledger.EnsureLoaded();
            var sell = new List<string>();
            foreach (Held h in Holding())
            {
                Quote best = Runs.SellQuotes(h.ItemId).OrderByDescending(q => q.Price).FirstOrDefault();
                if (best == null || best.TownId != s.StringId) continue;
                int paid = PaidEach(h.ItemId);
                sell.Add(h.Good + " (" + h.Units + " held, about " + Fmt.N(best.Price) + " here" + (paid > 0 ? ", " + Fmt.Signed(best.Price - paid) + " each" : "") + ")");
            }
            if (sell.Count > 0) Say("Best known place to sell " + string.Join(", ", sell.Take(4)) + (sell.Count > 4 ? " and " + (sell.Count - 4) + " more" : "") + ".");
            var cheap = new List<string>();
            foreach (string id in Ledger.Pins.Select(p => p.ItemId).Where(x => !string.IsNullOrEmpty(x)).Distinct())
            {
                Quote q = Runs.BuyQuotes(id).OrderBy(x => x.Price).FirstOrDefault();
                if (q != null && q.TownId == s.StringId)
                    cheap.Add(Ledger.Points.Values.First(p => p.ItemId == id).Good + " at " + Fmt.N(q.Price));
            }
            if (cheap.Count > 0) Say("Pinned goods cheapest here (last time you looked): " + string.Join(", ", cheap) + ".");
        }

        private static void Say(string t) => InformationManager.DisplayMessage(new InformationMessage("TradeScribe: " + t, Color.FromUint(0xFFC9A227)));

        // ---- Caravans ----------------------------------------------------------------------------------------

        private static int GoodsValue(MobileParty m)
        {
            int v = 0; ItemRoster r = m.ItemRoster; if (r == null) return 0;
            for (int i = 0; i < r.Count; i++) { ItemRosterElement el = r.GetElementCopyAtIndex(i); if (el.EquipmentElement.Item != null) v += el.EquipmentElement.Item.Value * el.Amount; }
            return v;
        }

        private static IEnumerable<MobileParty> Caravans()
        {
            var list = Hero.MainHero?.OwnedCaravans;
            if (list == null) yield break;
            foreach (CaravanPartyComponent c in list) if (c?.MobileParty != null && c.MobileParty.IsActive) yield return c.MobileParty;
        }

        public static int CaravanCount => Caravans().Count();

        /// Once a day, write down what each of your caravans is worth: its trade gold plus its goods at base value.
        public static void RecordCaravans()
        {
            foreach (MobileParty m in Caravans())
                Ledger.AddCaravan(m.StringId, m.Name.ToString(), m.PartyTradeGold + GoodsValue(m));
        }

        public static List<Entry> CaravanEntries()
        {
            RecordCaravans();
            var res = new List<Entry>(); int today = Ledger.Today;
            foreach (MobileParty m in Caravans())
            {
                int gold = m.PartyTradeGold, goods = GoodsValue(m), worth = gold + goods;
                string where = m.CurrentSettlement != null ? "In " + m.CurrentSettlement.Name : m.TargetSettlement != null ? "Heading to " + m.TargetSettlement.Name : "On the road";
                string text = m.Name + (m.LeaderHero != null ? ", led by " + m.LeaderHero.Name : "") + "\n    " + where
                    + "\n    Worth about " + Fmt.N(worth) + ": " + Fmt.N(gold) + " gold and goods worth about " + Fmt.N(goods);
                List<CaravanPoint> hist = Ledger.CaravanLog.Where(c => c.Id == m.StringId).OrderBy(c => c.Day).ToList();
                CaravanPoint week = hist.LastOrDefault(c => c.Day <= today - 7), first = hist.FirstOrDefault();
                if (week != null) text += "\n    Last 7 days: " + Fmt.Signed(worth - week.Worth);
                if (first != null && first.Day < today) text += "\n    Since " + Fmt.Age(today - first.Day).Replace(" ago", " ago (first logged)") + ": " + Fmt.Signed(worth - first.Worth);
                res.Add(new Entry { Text = text, Pin = m.Name + " on " + CampaignTime.Now + ": worth about " + Fmt.N(worth) });
            }
            return res;
        }
    }
}
