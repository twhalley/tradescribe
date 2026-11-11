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
                new InquiryElement("good", "Prices for one good", null),
                new InquiryElement("rumours", "Rumour log", null),
                new InquiryElement("trades", "My trades and profit", null),
                new InquiryElement("pins", "Pinned notes (" + Ledger.Pins.Count + ")", null),
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
                true, true, more ? "Next page" : "Menu", entries.Count == 0 ? "Close" : "Options",
                () => { if (more) go(page + 1); else _pending = ShowMain; },
                () => { if (entries.Count > 0) _pending = () => ShowOptions(shown, page, pinned, go); }), true);
        }

        private static void ShowOptions(List<Entry> shown, int page, bool pinned, Action<int> go)
        {
            var items = new List<InquiryElement>
            {
                new InquiryElement("pin", pinned ? "Unpin notes on this page" : "Pin something on this page", null),
                new InquiryElement("prev", "Previous page", null, page > 0, "This is the first page"),
                new InquiryElement("first", "First page", null, page > 0, "This is the first page"),
                new InquiryElement("menu", "Back to the ledger menu", null),
            };
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData("Options",
                pinned ? "Unpinned notes are gone for good." : "A pinned note keeps today's wording and prices. It is never refreshed and never ages out.",
                items, true, 1, 1, "Choose", "Back",
                picked =>
                {
                    string id = picked != null && picked.Count > 0 ? picked[0].Identifier as string : null;
                    if (id == "pin") _pending = () => PickPins(shown, page, pinned, go);
                    else if (id == "prev") go(page - 1);
                    else if (id == "first") go(0);
                    else if (id == "menu") _pending = ShowMain;
                    else go(page);
                }, _ => go(page), "", false), true);
        }

        private static void PickPins(List<Entry> shown, int page, bool pinned, Action<int> go)
        {
            List<InquiryElement> items = shown.Select((e, i) => new InquiryElement(i, Brief(e), null)).ToList();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(pinned ? "Unpin notes" : "Pin to your notes",
                "Tick one or more.", items, true, 1, items.Count, pinned ? "Unpin" : "Pin", "Back",
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
                }, _ => go(page), "", false), true);
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
            return "Trip profit is rough: prices move as you buy and sell. Estimates assume traders pay about "
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

        private static void ShowGoods()
        {
            List<PricePoint> goods = Ledger.Points.Values.GroupBy(p => p.ItemId).Select(g => g.First())
                .Where(p => p.Type == "Food" || p.Type == "Trade good" || p.Type == "Animal" || p.Type == "Horse").OrderBy(p => p.Good).ToList();
            if (goods.Count == 0) { ShowPages("Prices for one good", null, new List<Entry>(), "No goods logged yet. Open a trade screen.", null, 10); return; }
            List<InquiryElement> items = goods.Select(p => new InquiryElement(p.ItemId, p.Good, null)).ToList();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData("Prices for one good", "Pick a good.", items, true, 1, 1, "Show", "Back",
                picked =>
                {
                    if (picked == null || picked.Count == 0) { _pending = ShowMain; return; }
                    string id = picked[0].Identifier as string; string title = picked[0].Title;
                    _pending = () => ShowPages(title, null, GoodEntries(id, title), "No price known for it in the last " + Runs.MaxAgeDays + " days.", null, 10);
                }, _ => { _pending = ShowMain; }, "", true), true);
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
                fromId == null ? "Pick where you will buy." : "Buying in " + fromName + ". Pick where you will sell.", items, true, 1, 1, "Next", "Back",
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
                }, _ => { _pending = ShowMain; }, "", true), true);
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
