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
            var items = new List<InquiryElement>
            {
                new InquiryElement("runs", "Best trade runs", null),
                new InquiryElement("here", "What to buy here", null, Settlement.CurrentSettlement != null, "Only available inside a settlement"),
                new InquiryElement("good", "Prices for one good", null),
                new InquiryElement("rumours", "Rumour log", null),
                new InquiryElement("trades", "My trades and profit", null),
            };
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData("Trade ledger", desc, items, true, 1, 1, "Open", "Close",
                picked =>
                {
                    string id = picked != null && picked.Count > 0 ? picked[0].Identifier as string : null;
                    if (id == "runs") _pending = () => ShowText("Best trade runs", RunsText(Runs.Best(), "No profitable run known yet. Open the trade screen in a second town."));
                    else if (id == "here") _pending = ShowHere;
                    else if (id == "good") _pending = ShowGoods;
                    else if (id == "trades") _pending = () => ShowText("My trades and profit", TradesText());
                    else if (id == "rumours") _pending = () => ShowText("Rumour log", RumourText());
                }, null), true);
        }

        private static void ShowHere()
        {
            Settlement s = Settlement.CurrentSettlement;
            if (s == null) { ShowText("What to buy here", "You are not in a settlement."); return; }
            ShowText("What to buy in " + s.Name, RunsText(Runs.Best(s.StringId),
                "Nothing here is known to sell for more elsewhere. Open this town's trade screen first if you have not."));
        }

        private static void ShowGoods()
        {
            List<PricePoint> goods = Ledger.Points.Values.GroupBy(p => p.ItemId).Select(g => g.First())
                .Where(p => p.Type == "Food" || p.Type == "Trade good" || p.Type == "Animal" || p.Type == "Horse").OrderBy(p => p.Good).ToList();
            if (goods.Count == 0) { ShowText("Prices for one good", "No goods logged yet. Open a trade screen."); return; }
            List<InquiryElement> items = goods.Select(p => new InquiryElement(p.ItemId, p.Good, null)).ToList();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData("Prices for one good", "Pick a good.", items, true, 1, 1, "Show", "Back",
                picked =>
                {
                    if (picked == null || picked.Count == 0) { _pending = ShowMain; return; }
                    string id = picked[0].Identifier as string; string title = picked[0].Title;
                    _pending = () => ShowText(title, GoodText(id));
                }, _ => { _pending = ShowMain; }, "", true), true);
        }

        private static void ShowText(string title, string text)
        {
            InformationManager.ShowInquiry(new InquiryData(title, text, true, true, "Back", "Close", () => { _pending = ShowMain; }, null), true);
        }

        private static string RunsText(List<Run> runs, string empty)
        {
            if (runs.Count == 0) return empty;
            var sb = new StringBuilder(); int n = 0;
            foreach (Run r in runs.Take(10)) sb.Append(++n).Append(". ").AppendLine(r.Line());
            sb.AppendLine().Append("Trip profit is rough: prices move as you buy and sell. Estimates assume traders pay about "
                + Math.Round(Runs.SellRatio("Trade good") * 100) + "% of their own price for goods and "
                + Math.Round(Runs.SellRatio("Horse") * 100) + "% for horses and livestock.");
            return sb.ToString();
        }

        private static string GoodText(string itemId)
        {
            var sb = new StringBuilder(); int today = Ledger.Today;
            List<Quote> buys = Runs.BuyQuotes(itemId).OrderBy(q => q.Price).ToList();
            List<Quote> sells = Runs.SellQuotes(itemId).OrderByDescending(q => q.Price).ToList();
            sb.AppendLine("Buy at:");
            if (buys.Count == 0) sb.AppendLine("  nowhere known in the last " + Runs.MaxAgeDays + " days");
            foreach (Quote q in buys.Take(8)) sb.AppendLine("  " + q.Town + ": " + q.Price + " (" + q.Stock + " in stock, " + Age(today - q.Day) + ")");
            sb.AppendLine("Sell at:");
            if (sells.Count == 0) sb.AppendLine("  nowhere known");
            foreach (Quote q in sells.Take(8)) sb.AppendLine("  " + q.Town + ": " + q.Price + " (" + q.Basis + ", " + Age(today - q.Day) + ")");
            return sb.ToString();
        }

        private static string RumourText()
        {
            if (Ledger.Rumours.Count == 0) return "No rumours logged yet. The game gives you rumours as you visit towns and raise your Trade skill.";
            var sb = new StringBuilder(); int today = Ledger.Today;
            foreach (Rumour r in Ledger.Rumours.OrderByDescending(x => x.DaySeen).ThenBy(x => x.Good).Take(20))
                sb.AppendLine(r.Good + " in " + r.Town + ": buy " + r.Buy + ", sell " + r.Sell + " (heard " + Age(today - r.DaySeen)
                    + (r.EndDay < today ? ", expired" : "") + ")");
            if (Ledger.Rumours.Count > 20) sb.AppendLine().Append("Showing the latest 20 of " + Ledger.Rumours.Count + ". The full log is in rumours.csv.");
            return sb.ToString();
        }

        private static string TradesText()
        {
            if (Ledger.Trades.Count == 0) return "No trades recorded yet. Buy or sell something and press Done.";
            int spent = 0, earned = 0, profit = 0, other = 0;
            foreach (Trade t in Ledger.Trades)
            {
                if (t.Action == "Bought") { spent += t.Total; continue; }
                earned += t.Total; profit += t.Profit;
                if (t.Units > t.Matched) other += (int)Math.Round((double)t.Total * (t.Units - t.Matched) / t.Units);
            }
            var sb = new StringBuilder();
            sb.AppendLine("Spent " + spent + ", earned " + earned + ".");
            sb.AppendLine("Trading profit (sold for more than you paid): " + (profit >= 0 ? "+" : "") + profit + ".");
            if (other > 0) sb.AppendLine("Sales of loot and goods bought before TradeScribe: " + other + ".");
            sb.AppendLine().AppendLine("Latest trades:");
            foreach (Trade t in Enumerable.Reverse(Ledger.Trades).Take(12))
                sb.AppendLine(t.Date + ", " + t.Town + ": " + t.Action.ToLowerInvariant() + " " + t.Units + " " + t.Good + " for " + t.Total
                    + (t.Action == "Sold" && t.Matched > 0 ? " (profit " + (t.Profit >= 0 ? "+" : "") + t.Profit + ")" : ""));
            return sb.ToString();
        }

        private static string Age(int days) => days <= 0 ? "today" : days == 1 ? "1 day ago" : days + " days ago";
    }
}
