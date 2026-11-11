// Advice for the workshops the player owns: where to get their inputs and where to sell what they make.
// Uses only the player's own workshops, the ledger and the rumour log.
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace TradeScribe
{
    public static class Workshops
    {
        public const string Footer = "A workshop buys its inputs and sells what it makes in its own town. It earns more when the input is cheap "
            + "there and the product is dear. Selling inputs in that town pushes their price down; buying the product there and selling it "
            + "elsewhere pushes its price up.";

        private static readonly Dictionary<string, string> Cat = new Dictionary<string, string>();

        private static string Category(string itemId)
        {
            if (Cat.TryGetValue(itemId, out string c)) return c;
            try
            {
                int plus = itemId.IndexOf('+');
                ItemObject item = MBObjectManager.Instance.GetObject<ItemObject>(plus > 0 ? itemId.Substring(0, plus) : itemId);
                c = item?.ItemCategory?.StringId ?? "";
            }
            catch { c = ""; }
            return Cat[itemId] = c;
        }

        private static string Pretty(string id)
        {
            string s = (id ?? "").Replace('_', ' ').Trim();
            return s.Length == 0 ? "goods" : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        public static int Count => Hero.MainHero?.OwnedWorkshops?.Count ?? 0;

        public static List<Entry> Entries()
        {
            var res = new List<Entry>();
            // Every good the ledger knows, by item id.
            var known = new Dictionary<string, string>();
            foreach (PricePoint p in Ledger.Points.Values) known[p.ItemId] = p.Good;
            foreach (Rumour r in Ledger.Rumours) if (!known.ContainsKey(r.ItemId)) known[r.ItemId] = r.Good;

            foreach (Workshop w in Hero.MainHero.OwnedWorkshops)
            {
                if (w == null || w.WorkshopType == null || w.Settlement == null) continue;
                string townId = w.Settlement.StringId, town = w.Settlement.Name.ToString();
                string head = w.WorkshopType.Name + " in " + town;
                res.Add(new Entry { Header = head, Text = "Latest profit " + Fmt.Signed(w.ProfitMade) + ", capital " + Fmt.N(w.Capital),
                    Pin = head + ": latest profit " + Fmt.Signed(w.ProfitMade) + ", capital " + Fmt.N(w.Capital) });

                var inputs = new List<string>(); var outputs = new List<string>();
                foreach (WorkshopType.Production pr in w.WorkshopType.Productions)
                {
                    foreach (var i in pr.Inputs) if (i.Item1 != null && !inputs.Contains(i.Item1.StringId)) inputs.Add(i.Item1.StringId);
                    foreach (var o in pr.Outputs) if (o.Item1 != null && !outputs.Contains(o.Item1.StringId)) outputs.Add(o.Item1.StringId);
                }

                foreach (string cat in inputs)
                {
                    List<string> items = known.Keys.Where(k => Category(k) == cat).ToList();
                    if (items.Count == 0) { res.Add(Unknown(head, "Uses", cat)); continue; }
                    foreach (string id in items.Take(3))
                    {
                        string text = "Uses " + known[id] + "\n    Price in " + town + ": " + Here(townId, town, id);
                        Quote src = Runs.BuyQuotes(id).Where(q => q.TownId != townId).OrderBy(q => q.Price).FirstOrDefault();
                        if (src == null) text += "\n    No other place known to sell it yet";
                        else
                        {
                            text += "\n    Cheapest elsewhere: " + src.Town + " at " + Fmt.N(src.Price) + " (" + src.Stock + " in stock, seen " + Fmt.Age(Ledger.Today - src.Day) + ")";
                            Run run = Runs.Make(id, src, Runs.SellQuotes(id).FirstOrDefault(q => q.TownId == townId));
                            text += run != null
                                ? "\n    Carry it to " + town + ": sells for " + Fmt.N(run.Sell.Price) + " (" + run.SellBasis() + "), +" + Fmt.N(run.Each) + " each"
                                    + (run.Units > 0 ? ", about +" + Fmt.N(run.Trip) + " for " + run.Units : "")
                                : "\n    Carrying it here would not pay on its own, but it lowers what the workshop pays";
                        }
                        res.Add(new Entry { Header = head, Text = text, Pin = head + ": " + text });
                    }
                }

                foreach (string cat in outputs)
                {
                    var found = new List<KeyValuePair<Run, string>>();
                    List<string> items = known.Keys.Where(k => Category(k) == cat).ToList();
                    if (items.Count == 0) { res.Add(Unknown(head, "Makes", cat)); continue; }
                    foreach (string id in items)
                    {
                        string text = "Makes " + known[id] + "\n    Price in " + town + ": " + Here(townId, town, id);
                        Quote sell = Runs.SellQuotes(id).Where(q => q.TownId != townId).OrderByDescending(q => q.Price).FirstOrDefault();
                        Run run = Runs.Make(id, Runs.BuyQuotes(id).FirstOrDefault(q => q.TownId == townId), sell);
                        if (run != null)
                            text += "\n    Buy it here and sell in " + run.Sell.Town + " for " + Fmt.N(run.Sell.Price) + " (" + run.SellBasis() + "): +" + Fmt.N(run.Each)
                                + " each" + (run.Units > 0 ? ", about +" + Fmt.N(run.Trip) + " for " + run.Units : "");
                        else if (sell != null)
                            text += "\n    Best known sale elsewhere: " + sell.Town + " for " + Fmt.N(sell.Price) + " (" + sell.Basis + ")";
                        else text += "\n    No other place known to buy it yet";
                        found.Add(new KeyValuePair<Run, string>(run, text));
                    }
                    // A product type can cover many items (every kind of bow, say): show the three that pay best.
                    foreach (var f in found.OrderByDescending(x => x.Key != null ? x.Key.Each : -1).Take(3))
                        res.Add(new Entry { Header = head, Text = f.Value, Pin = head + ": " + f.Value });
                }
            }
            return res;
        }

        private static Entry Unknown(string head, string verb, string cat)
        {
            string t = verb + " " + Pretty(cat) + "\n    No price seen for it yet. Open trade screens to find it.";
            return new Entry { Header = head, Text = t, Pin = head + ": " + t };
        }

        /// What the good costs in the workshop's own town, from the trade screen or failing that a rumour.
        private static string Here(string townId, string town, string itemId)
        {
            if (Ledger.Points.TryGetValue(townId + "|" + itemId, out PricePoint p) && p.Buy > 0)
                return Fmt.N(p.Buy) + " (" + p.Stock + " in stock, seen " + Fmt.Age(Ledger.Today - p.BuyDay) + ")";
            Rumour r = Ledger.Rumours.Where(x => x.TownId == townId && x.ItemId == itemId).OrderByDescending(x => x.Hour).FirstOrDefault();
            if (r != null) return "about " + Fmt.N(r.Buy) + " (rumour, heard " + Fmt.Age(Ledger.Today - r.DaySeen) + ")";
            return "not seen; open the trade screen in " + town;
        }
    }
}
