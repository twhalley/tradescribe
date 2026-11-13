// The in-game menu (F7 on the map, or "Open the trade ledger" in a town). Built from the game's own pop-ups.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace TradeScribe
{
    /// One numbered item on a page. Pin is the text kept if the player pins it (defaults to Text).
    public class Entry { public string Header, Text, Pin; public object Tag; }

    public static class LedgerMenu
    {
        private static Action _pending;

        /// Pop-ups are opened on the next tick, so one can be opened from another's button.
        public static void Open() { _pending = ShowMain; }

        public static void RunPending()
        {
            Action a = _pending; if (a == null) return;
            _pending = null;
            try { a(); } catch (Exception e) { Ledger.LogError(e); }
        }

        private static void ShowMain()
        {
            Ledger.EnsureLoaded(); Ledger.SyncRumours();
            int towns = Ledger.Points.Values.Select(p => p.TownId).Distinct().Count();
            string desc = CampaignTime.Now + ". Prices seen in " + towns + " place" + (towns == 1 ? "" : "s") + ", " + Ledger.Rumours.Count
                + " rumours logged. Only what you have seen on trade screens and heard as rumours.";
            int shops = Workshops.Count;
            var items = new List<InquiryElement>
            {
                new InquiryElement("runs", "Best trade runs", null),
                new InquiryElement("here", "What to buy here", null, Settlement.CurrentSettlement != null, "Only available inside a settlement"),
                new InquiryElement("route", "Plan a run between two places", null),
                new InquiryElement("shops", "My workshops (" + shops + ")", null),
                new InquiryElement("suit", "Which workshop suits a town", null),
                new InquiryElement("good", "Prices for one good", null),
                new InquiryElement("rumours", "Rumour log", null),
                new InquiryElement("trades", "My trades and profit", null),
                new InquiryElement("summary", "Profit by good and by town", null),
                new InquiryElement("pins", "Pinned notes (" + Ledger.Pins.Count + ")", null),
                new InquiryElement("settings", "Settings", null),
            };
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData("Trade ledger", desc, items, true, 1, 1, "Open", "Close",
                picked =>
                {
                    string id = picked != null && picked.Count > 0 ? picked[0].Identifier as string : null;
                    if (id == "runs") _pending = () => ShowPages("Best trade runs", null, RunEntries(Runs.Best()),
                        "No profitable run known yet. Open the trade screen in a second town.", RunFooter(), 4);
                    else if (id == "here") _pending = ShowHere;
                    else if (id == "route") _pending = () => PickPlace(null);
                    else if (id == "shops") _pending = ShowWorkshops;
                    else if (id == "good") _pending = ShowGoods;
                    else if (id == "trades") _pending = ShowTrades;
                    else if (id == "rumours") _pending = ShowRumours;
                    else if (id == "pins") _pending = () => ShowPinned(0);
                    else if (id == "suit") _pending = PickWorkshopTown;
                    else if (id == "summary") _pending = ShowSummary;
                    else if (id == "settings") _pending = ShowSettings;
                }, null), true);
        }

        // ---- Pages -------------------------------------------------------------------------------------------

        /// Shows a list a few entries at a time. "Options" leads to pinning, the previous page and the menu.
        private static void ShowPages(string title, string intro, List<Entry> entries, string empty, string footer, int per, int page = 0, bool pinned = false)
        {
            int pages = Math.Max(1, (entries.Count + per - 1) / per);
            page = Math.Max(0, Math.Min(page, pages - 1));
            List<Entry> shown = entries.Skip(page * per).Take(per).ToList();
            bool spaced = entries.Any(e => e.Text.Contains("\n"));
            var sb = new StringBuilder();
            if (page == 0 && !string.IsNullOrEmpty(intro)) sb.AppendLine(intro).AppendLine();
            if (entries.Count == 0) sb.AppendLine(empty);
            string header = null; int n = page * per;
            foreach (Entry e in shown)
            {
                if (e.Header != null && e.Header != header)
                {
                    if (header != null && !spaced) sb.AppendLine();
                    sb.AppendLine("-- " + e.Header + " --"); header = e.Header;
                }
                sb.Append(++n).Append(". ").AppendLine(e.Text);
                if (spaced) sb.AppendLine();
            }
            if (page == pages - 1 && entries.Count > 0 && !string.IsNullOrEmpty(footer)) { if (!spaced) sb.AppendLine(); sb.Append(footer); }

            bool more = page < pages - 1;
            Action<int> go = p => _pending = () => ShowPages(title, intro, entries, empty, footer, per, p, pinned);
            InformationManager.ShowInquiry(new InquiryData(pages > 1 ? title + " (page " + (page + 1) + " of " + pages + ")" : title, sb.ToString(),
                true, true, entries.Count == 0 ? "Menu" : more ? "Next page / options" : "Options", "Close",
                () => { if (entries.Count == 0) _pending = ShowMain; else _pending = () => ShowOptions(shown, page, pinned, go, more); },
                null), true);
        }

        /// The tick on a page leads here. The X on any pop-up closes the ledger.
        private static void ShowOptions(List<Entry> shown, int page, bool pinned, Action<int> go, bool more)
        {
            var items = new List<InquiryElement>
            {
                new InquiryElement("next", "Next page", null, more, "This is the last page"),
                new InquiryElement("pin", pinned ? "Unpin notes on this page" : "Pin something on this page", null),
                new InquiryElement("prev", "Previous page", null, page > 0, "This is the first page"),
                new InquiryElement("first", "First page", null, page > 0, "This is the first page"),
                new InquiryElement("menu", "Back to the ledger menu", null),
                new InquiryElement("stay", "Back to this page", null),
            };
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData("Options",
                pinned ? "Unpinned notes are gone for good." : "A pinned note keeps today's wording and prices. It is never refreshed and never ages out.",
                items, true, 1, 1, "Choose", "Close",
                picked =>
                {
                    string id = picked != null && picked.Count > 0 ? picked[0].Identifier as string : null;
                    if (id == "next") go(page + 1);
                    else if (id == "pin") _pending = () => PickPins(shown, page, pinned, go);
                    else if (id == "prev") go(page - 1);
                    else if (id == "first") go(0);
                    else if (id == "menu") _pending = ShowMain;
                    else go(page);
                }, null, "", false), true);
        }

        private static void PickPins(List<Entry> shown, int page, bool pinned, Action<int> go)
        {
            List<InquiryElement> items = shown.Select((e, i) => new InquiryElement(i, Brief(e), null)).ToList();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(pinned ? "Unpin notes" : "Pin to your notes",
                "Tick one or more.", items, true, 1, items.Count, pinned ? "Unpin" : "Pin", "Close",
                picked =>
                {
                    List<Entry> chosen = (picked ?? new List<InquiryElement>()).Select(p => shown[(int)p.Identifier]).ToList();
                    if (pinned)
                    {
                        Ledger.RemovePins(chosen.Select(e => e.Tag as Pin).Where(p => p != null).ToList());
                        Say("Unpinned " + chosen.Count + ".");
                        _pending = () => ShowPinned(page);
                    }
                    else
                    {
                        int added = Ledger.AddPins(chosen.Select(e => e.Pin ?? e.Text));
                        Say(added == 0 ? "Already pinned." : "Pinned " + added + ". Find them under Pinned notes.");
                        go(page);
                    }
                }, null, "", false), true);
        }

        private static string Brief(Entry e)
        {
            string s = (e.Pin ?? e.Text).Replace("\n    ", "; ").Replace("\n", "; ");
            return s.Length > 90 ? s.Substring(0, 87) + "..." : s;
        }

        private static void Say(string text)
        {
            InformationManager.DisplayMessage(new InformationMessage("TradeScribe: " + text, Color.FromUint(0xFFC9A227)));
        }

        private static void ShowPinned(int page)
        {
            List<Entry> entries = Enumerable.Reverse(Ledger.Pins)
                .Select(p => new Entry { Text = p.Text + "\n    Pinned " + p.Date, Tag = p }).ToList();
            ShowPages("Pinned notes", null, entries,
                "Nothing pinned yet. On any page, choose Options, then Pin something on this page.",
                "Pinned notes keep the prices from the day you pinned them. They may be out of date.", 4, page, true);
        }

        // ---- The pages ---------------------------------------------------------------------------------------

        private static List<Entry> RunEntries(IEnumerable<Run> runs, string header = null)
        {
            return runs.Select(r => new Entry { Header = header, Text = r.Block() }).ToList();
        }

        private static string RunFooter()
        {
            return (Settings.MaxDistance > 0 ? "Runs longer than distance " + Settings.MaxDistance + " are hidden. " : "")
                + (Settings.PerDistance ? "Ranked by profit per distance. " : "")
                + "Rumours older than " + Settings.RumourDays + " days are not used. Change these under Settings.\n" + "Trip profit is rough: prices move as you buy and sell. Estimates assume traders pay about "
                + Math.Round(Runs.SellRatio("Trade good") * 100) + "% of their own price for goods and "
                + Math.Round(Runs.SellRatio("Horse") * 100) + "% for horses and livestock.";
        }

        private static void ShowHere()
        {
            Settlement s = Settlement.CurrentSettlement;
            if (s == null) { _pending = ShowMain; return; }
            ShowPages("What to buy in " + s.Name, null, RunEntries(Runs.Best(s.StringId)),
                "Nothing here is known to sell for more elsewhere. Open this town's trade screen first if you have not.", RunFooter(), 4);
        }

        private static void ShowWorkshops()
        {
            ShowPages("My workshops", null, Workshops.Entries(),
                "You own no workshops. When you buy one, this page shows where to find what it uses and where to sell what it makes.",
                Workshops.Footer, 3);
        }

        private static void PickWorkshopTown()
        {
            string here = Settlement.CurrentSettlement?.StringId;
            List<InquiryElement> items = Runs.KnownPlaces().Where(p => { Settlement s = Settlement.Find(p.Key); return s != null && s.IsTown; })
                .OrderByDescending(p => p.Key == here)
                .Select(p => new InquiryElement(p.Key, p.Value + (p.Key == here ? " (you are here)" : ""), null)).ToList();
            if (items.Count == 0)
            {
                ShowPages("Which workshop suits a town", null, new List<Entry>(), "No town prices seen yet. Open a town's trade screen first.", null, 4);
                return;
            }
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData("Which workshop suits a town", "Pick a town whose prices you have seen.",
                items, true, 1, 1, "Show", "Close",
                picked =>
                {
                    if (picked == null || picked.Count == 0) { _pending = ShowMain; return; }
                    string id = picked[0].Identifier as string; string name = picked[0].Title.Replace(" (you are here)", "");
                    _pending = () => ShowPages("Workshops for " + name, null, Workshops.Suits(id, name),
                        "No workshop types found.", Workshops.SuitFooter, 4);
                }, null, "", true), true);
        }

        private static void ShowSummary()
        {
            var entries = new List<Entry>();
            List<Trade> sales = Ledger.Trades.Where(t => t.Action == "Sold" && t.Matched > 0).ToList();
            foreach (var g in sales.GroupBy(t => t.Good).Select(g => new { g.Key, Profit = g.Sum(t => t.Profit), Units = g.Sum(t => t.Matched) }).OrderByDescending(x => x.Profit))
            {
                string t = g.Key + ": " + Fmt.Signed(g.Profit) + " on " + Fmt.N(g.Units) + " sold (" + Fmt.Signed((int)Math.Round((double)g.Profit / Math.Max(1, g.Units))) + " each)";
                entries.Add(new Entry { Header = "By good, best first", Text = t, Pin = "Profit so far, " + t });
            }
            foreach (var g in sales.GroupBy(t => t.Town).Select(g => new { g.Key, Profit = g.Sum(t => t.Profit), Count = g.Count() }).OrderByDescending(x => x.Profit))
            {
                string t = g.Key + ": " + Fmt.Signed(g.Profit) + " from " + g.Count + " sale" + (g.Count == 1 ? "" : "s");
                entries.Add(new Entry { Header = "By town you sold in, best first", Text = t, Pin = "Profit so far, sold in " + t });
            }
            foreach (var g in Ledger.Trades.Where(t => t.Action == "Bought").GroupBy(t => t.Town)
                .Select(g => new { g.Key, Spent = g.Sum(t => t.Total) }).OrderByDescending(x => x.Spent))
            {
                string t = g.Key + ": spent " + Fmt.N(g.Spent);
                entries.Add(new Entry { Header = "By town you bought in", Text = t, Pin = "Spent so far in " + t });
            }
            ShowPages("Profit by good and by town", "Total trading profit: " + Fmt.Signed(sales.Sum(t => t.Profit)), entries,
                "No sales of goods you bought earlier yet.", "Profit counts only goods TradeScribe saw you buy. Loot and older stock are left out.", 12);
        }

        private static void ShowSettings()
        {
            var items = new List<InquiryElement>
            {
                new InquiryElement("dist", "Longest run: " + (Settings.MaxDistance > 0 ? "distance " + Settings.MaxDistance : "no limit"), null),
                new InquiryElement("rank", "Rank runs by: " + (Settings.PerDistance ? "profit per distance" : "trip profit") + " (choose to switch)", null),
                new InquiryElement("rum", "Use rumours up to: " + (Settings.RumourDays == 0 ? "never (seen prices and estimates only)" : Settings.RumourDays + " days old"), null),
                new InquiryElement("menu", "Back to the ledger menu", null),
            };
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData("Settings", "These shape the run suggestions. The rumour log itself keeps everything.",
                items, true, 1, 1, "Change", "Close",
                picked =>
                {
                    string id = picked != null && picked.Count > 0 ? picked[0].Identifier as string : null;
                    if (id == "rank") { Settings.PerDistance = !Settings.PerDistance; Settings.Save(); _pending = ShowSettings; }
                    else if (id == "dist") _pending = PickDistance;
                    else if (id == "rum") _pending = () => PickNumber("Use rumours up to", "Old rumours can be well off. Seen prices are not affected.",
                        new[] { 0, 1, 2, 3, 5, 10 }, v => v == 0 ? "Never use rumours for runs" : v + (v == 1 ? " day old" : " days old"), v => Settings.RumourDays = v);
                    else _pending = ShowMain;
                }, null, "", false), true);
        }

        private static void PickDistance()
        {
            // Show how many of the known runs fit under each limit, so the numbers mean something.
            int keep = Settings.MaxDistance; Settings.MaxDistance = 0;
            List<float> d; try { d = Runs.Best().Where(r => r.Distance >= 0f).Select(r => r.Distance).ToList(); } finally { Settings.MaxDistance = keep; }
            PickNumber("Longest run", "Runs further than this are hidden." + (d.Count > 0 ? " Your known runs range from distance " + d.Min().ToString("0") + " to " + d.Max().ToString("0") + "." : ""),
                new[] { 0, 50, 100, 150, 200, 300, 400, 600, 800 },
                v => v == 0 ? "No limit" : "Up to " + v + " (" + d.Count(x => x <= v) + " of " + d.Count + " known runs)", v => Settings.MaxDistance = v);
        }

        private static void PickNumber(string title, string desc, int[] options, Func<int, string> label, Action<int> set)
        {
            List<InquiryElement> items = options.Select(v => new InquiryElement(v, label(v), null)).ToList();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(title, desc, items, true, 1, 1, "Set", "Close",
                picked =>
                {
                    if (picked != null && picked.Count > 0) { set((int)picked[0].Identifier); Settings.Save(); }
                    _pending = ShowSettings;
                }, null, "", false), true);
        }

        private static void ShowGoods()
        {
            List<PricePoint> goods = Ledger.Points.Values.GroupBy(p => p.ItemId).Select(g => g.First())
                .Where(p => p.Type == "Food" || p.Type == "Trade good" || p.Type == "Animal" || p.Type == "Horse").OrderBy(p => p.Good).ToList();
            if (goods.Count == 0) { ShowPages("Prices for one good", null, new List<Entry>(), "No goods logged yet. Open a trade screen.", null, 10); return; }
            List<InquiryElement> items = goods.Select(p => new InquiryElement(p.ItemId, p.Good, null)).ToList();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData("Prices for one good", "Pick a good.", items, true, 1, 1, "Show", "Close",
                picked =>
                {
                    if (picked == null || picked.Count == 0) { _pending = ShowMain; return; }
                    string id = picked[0].Identifier as string; string title = picked[0].Title;
                    _pending = () => ShowPages(title, null, GoodEntries(id, title), "No price known for it in the last " + Runs.MaxAgeDays + " days.", null, 10);
                }, null, "", true), true);
        }

        private static List<Entry> GoodEntries(string itemId, string good)
        {
            int today = Ledger.Today; var res = new List<Entry>();
            foreach (Quote q in Runs.BuyQuotes(itemId).OrderBy(q => q.Price))
            {
                string t = q.Town + ": " + Fmt.N(q.Price) + " (" + q.Stock + " in stock, seen " + Fmt.Age(today - q.Day) + ")";
                res.Add(new Entry { Header = "Buy at, cheapest first", Text = t, Pin = good + ", buy in " + t });
            }
            foreach (Quote q in Runs.SellQuotes(itemId).OrderByDescending(q => q.Price))
            {
                string t = q.Town + ": " + Fmt.N(q.Price) + " (" + q.Basis + (q.Basis == "estimate" ? "" : ", " + Fmt.Age(today - q.Day)) + ")";
                res.Add(new Entry { Header = "Sell at, best first", Text = t, Pin = good + ", sell in " + t });
            }
            return res;
        }

        /// Pick the start, then the destination, then show what to carry each way.
        private static void PickPlace(string fromId)
        {
            List<KeyValuePair<string, string>> places = Runs.KnownPlaces();
            string here = Settlement.CurrentSettlement?.StringId;
            if (fromId == null && places.Count < 2)
            {
                ShowPages("Plan a run", null, new List<Entry>(), "You need prices for at least two places. Open a trade screen in a second town.", null, 4);
                return;
            }
            // Where you are standing goes first in the list.
            List<InquiryElement> items = places.Where(p => p.Key != fromId).OrderByDescending(p => p.Key == here)
                .Select(p => new InquiryElement(p.Key, p.Value + (p.Key == here ? " (you are here)" : ""), null)).ToList();
            string fromName = fromId == null ? null : places.Where(p => p.Key == fromId).Select(p => p.Value).FirstOrDefault();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData("Plan a run",
                fromId == null ? "Pick where you will buy." : "Buying in " + fromName + ". Pick where you will sell.", items, true, 1, 1, "Next", "Close",
                picked =>
                {
                    if (picked == null || picked.Count == 0) { _pending = ShowMain; return; }
                    string id = picked[0].Identifier as string;
                    if (fromId == null) { _pending = () => PickPlace(id); return; }
                    string toName = places.Where(p => p.Key == id).Select(p => p.Value).FirstOrDefault();
                    _pending = () =>
                    {
                        List<Entry> entries = RunEntries(Runs.Between(fromId, id).Take(8), "Out: " + fromName + " to " + toName);
                        entries.AddRange(RunEntries(Runs.Between(id, fromId).Take(8), "Back: " + toName + " to " + fromName));
                        ShowPages(fromName + " and " + toName, null, entries, "Nothing known to be worth carrying either way.",
                            "Buy prices are ones you saw on a trade screen there. Sell prices marked estimate are the least reliable; old rumours can be well off.", 4);
                    };
                }, null, "", true), true);
        }

        private static void ShowRumours()
        {
            int today = Ledger.Today;
            List<Entry> entries = Ledger.Rumours.OrderByDescending(x => x.DaySeen).ThenBy(x => x.Town).ThenBy(x => x.Good)
                .Select(r => new Entry
                {
                    Header = "Heard " + Fmt.Age(today - r.DaySeen),
                    Text = r.Good + " in " + r.Town + ": buy " + Fmt.N(r.Buy) + ", sell " + Fmt.N(r.Sell) + (r.EndDay < today ? " (expired in game)" : ""),
                    Pin = "Rumour: " + r.Good + " in " + r.Town + ", buy " + Fmt.N(r.Buy) + ", sell " + Fmt.N(r.Sell)
                }).ToList();
            ShowPages("Rumour log", null, entries, "No rumours logged yet. The game gives you rumours as you visit towns and raise your Trade skill.",
                "Newest first. The full log is also in rumours.csv.", 12);
        }

        private static void ShowTrades()
        {
            int spent = 0, earned = 0, profit = 0, other = 0;
            foreach (Trade t in Ledger.Trades)
            {
                if (t.Action == "Bought") { spent += t.Total; continue; }
                earned += t.Total; profit += t.Profit;
                if (t.Units > t.Matched) other += (int)Math.Round((double)t.Total * (t.Units - t.Matched) / t.Units);
            }
            string intro = "Trading profit: " + Fmt.Signed(profit) + " (sold for more than you paid)\nSpent " + Fmt.N(spent) + ", earned " + Fmt.N(earned)
                + (other > 0 ? "\nSales of loot and goods bought before TradeScribe: " + Fmt.N(other) : "");
            List<Entry> entries = Enumerable.Reverse(Ledger.Trades).Select(t =>
            {
                string line = t.Action + " " + t.Units + " " + t.Good + " for " + Fmt.N(t.Total)
                    + (t.Units > 1 ? " (" + Fmt.N((int)Math.Round((double)t.Total / t.Units)) + " each)" : "")
                    + (t.Action == "Sold" && t.Matched > 0 ? ", profit " + Fmt.Signed(t.Profit) : "");
                return new Entry { Header = t.Date + ", " + t.Town, Text = line, Pin = t.Date + ", " + t.Town + ": " + line };
            }).ToList();
            ShowPages("My trades and profit", intro, entries, "No trades recorded yet. Buy or sell something and press Done.", "Newest first.", 10);
        }
    }
}
