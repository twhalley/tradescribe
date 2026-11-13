// TradeScribe: an in-game trade ledger.
// It writes down what each trade screen shows (the trader's stock and buy prices, your goods and sell
// prices) and the trade rumours the game gives you, then suggests trade runs from that alone.
// It reads no other town's market and changes nothing in the game.
using System;
using System.Collections.Generic;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace TradeScribe
{
    public class TradeScribeSubModule : MBSubModuleBase
    {
        private InventoryLogic _lastLogic;
        private float _timer;
        private bool _tracking;
        private int _goldAtOpen;
        private static string _townId, _town;
        private List<ValueTuple<ItemRosterElement, int>> _bought, _sold;
        private static List<Obs> _openRows;    // the trade screen as it was when opened

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            try
            {
                if (gameStarterObject is CampaignGameStarter starter)
                    starter.AddGameMenuOption("town", "tradescribe_ledger", "Open the trade ledger",
                        args => { args.optionLeaveType = GameMenuOption.LeaveType.Trade; return true; },
                        args => LedgerMenu.Open(), false, 8);
            }
            catch (Exception e) { Ledger.LogError(e); }
        }

        protected override void OnApplicationTick(float dt)
        {
            try
            {
                if (Campaign.Current == null || Game.Current == null) { _lastLogic = null; return; }
                LedgerMenu.RunPending();
                if (Game.Current.GameStateManager.ActiveState is MapState && Input.IsKeyPressed(InputKey.F7)) LedgerMenu.Open();

                _timer += dt;
                if (_timer < 0.1f) return;
                _timer = 0f;
                InventoryLogic logic = InventoryScreenHelper.GetActiveInventoryState()?.InventoryLogic;
                if (logic == null)
                {
                    if (_lastLogic != null && _tracking) CloseTrade();
                    _lastLogic = null; _tracking = false; return;
                }
                if (!ReferenceEquals(logic, _lastLogic))
                {
                    _lastLogic = logic; _tracking = logic.IsTrading; _bought = null; _sold = null; _openRows = null;
                    if (!_tracking) return;
                    _goldAtOpen = Hero.MainHero.Gold;
                    Snapshot(logic);
                }
                if (_tracking) { _bought = logic.GetBoughtItems(); _sold = logic.GetSoldItems(); }
            }
            catch (Exception e) { Ledger.LogError(e); }
        }

        private static string Id(EquipmentElement eq) => eq.ItemModifier == null ? eq.Item.StringId : eq.Item.StringId + "+" + eq.ItemModifier.StringId;

        /// Everything on the trade screen right now.
        private static List<Obs> BuildRows(InventoryLogic logic)
        {
            Settlement place = logic.CurrentSettlementComponent?.Settlement ?? Settlement.CurrentSettlement;
            string townId = place != null ? place.StringId : (logic.OtherParty != null ? "party:" + logic.OtherParty.Name : "unknown");
            string town = place != null ? place.Name.ToString() : (logic.OtherParty != null ? logic.OtherParty.Name.ToString() : "Unknown trader");
            _townId = townId; _town = town;
            Ledger.EnsureLoaded();
            long snap = DateTime.UtcNow.Ticks; int day = Ledger.Today; string date = CampaignTime.Now.ToString();

            var rows = new Dictionary<string, Obs>(); var order = new List<Obs>();
            foreach (ItemRosterElement el in logic.GetElementsInRoster(InventoryLogic.InventorySide.OtherInventory))
            {
                if (el.EquipmentElement.Item == null || el.Amount <= 0) continue;
                Obs o = Row(rows, order, el, snap, day, date, townId, town);
                o.Stock += el.Amount; o.Buy = logic.GetItemPrice(el.EquipmentElement, true);
            }
            foreach (ItemRosterElement el in logic.GetElementsInRoster(InventoryLogic.InventorySide.PlayerInventory))
            {
                if (el.EquipmentElement.Item == null || el.Amount <= 0) continue;
                Obs o = Row(rows, order, el, snap, day, date, townId, town);
                o.Held += el.Amount; o.Sell = logic.GetItemPrice(el.EquipmentElement, false);
            }
            return order;
        }

        private static void Snapshot(InventoryLogic logic)
        {
            List<Obs> order = BuildRows(logic); _openRows = order;
            string townId = _townId, town = _town;
            Ledger.AddSnapshot(order);
            int rumours = Ledger.SyncRumours();

            string msg = "TradeScribe: logged " + order.Count + " goods in " + town + (rumours > 0 ? " and " + rumours + " new rumours" : "") + ".";
            List<Run> here = Runs.Best(townId);
            if (here.Count > 0) msg += " Best buy here: " + here[0].Good + " for " + here[0].Sell.Town + " (+" + here[0].Each + " each). F7 for more.";
            InformationManager.DisplayMessage(new InformationMessage(msg, Color.FromUint(0xFFC9A227)));
        }

        /// The trade screen has closed. If the deal was confirmed, write down what was bought and sold.
        private void CloseTrade()
        {
            int nb = _bought?.Count ?? 0, ns = _sold?.Count ?? 0;
            if (nb + ns == 0) return;
            int gold = Hero.MainHero.Gold - _goldAtOpen;
            // The game reports a price per line; work out whether it is a total or a unit price from the gold that actually moved.
            int asTotals = Sum(_sold, false) - Sum(_bought, false), asUnits = Sum(_sold, true) - Sum(_bought, true);
            if (gold == 0 && asTotals != 0) return;                       // cancelled
            bool unitPrices = gold != asTotals && gold == asUnits;
            var trades = new List<Trade>();
            Collect(trades, _bought, "Bought", unitPrices); Collect(trades, _sold, "Sold", unitPrices);
            Ledger.AddTrades(trades);
            if (_openRows != null && _openRows.Count > 0) Ledger.AddSnapshot(AfterTrades(_openRows, trades));
            int profit = 0, known = 0;
            foreach (Trade t in trades) if (t.Action == "Sold" && t.Matched > 0) { profit += t.Profit; known++; }
            string msg = "TradeScribe: recorded " + trades.Count + " trade" + (trades.Count == 1 ? "" : "s") + " in " + _town + ".";
            if (known > 0) msg += " Profit on goods you bought earlier: " + (profit >= 0 ? "+" : "") + profit + ".";
            InformationManager.DisplayMessage(new InformationMessage(msg, Color.FromUint(0xFFC9A227)));
        }

        /// The opening prices, moved by your own deal. The screen cannot show this directly: goods you just sold
        /// are offered back at the price you sold them for. So the next price is worked out from what you were paid:
        /// if the first unit sold for p and the average was a, prices slid about 2(p - a) over the lot.
        private static List<Obs> AfterTrades(List<Obs> open, List<Trade> trades)
        {
            long snap = DateTime.UtcNow.Ticks; var res = new List<Obs>();
            foreach (Obs o in open)
            {
                var n = new Obs { Snap = snap, Hour = o.Hour + 0.001, Day = o.Day, Date = o.Date, TownId = o.TownId, Town = o.Town, ItemId = o.ItemId,
                    Good = o.Good, Type = o.Type, Weight = o.Weight, Buy = o.Buy, Sell = o.Sell, Stock = o.Stock, Held = o.Held };
                foreach (Trade t in trades)
                {
                    if (t.ItemId != o.ItemId || t.Units <= 0) continue;
                    double avg = (double)t.Total / t.Units;
                    if (t.Action == "Sold" && n.Sell > 0)
                    {
                        int next = Math.Max(1, (int)Math.Round(Math.Min(avg, 2 * avg - n.Sell)));
                        if (n.Buy > 0) n.Buy = Math.Max(1, (int)Math.Round((double)n.Buy * next / n.Sell));
                        n.Sell = next; n.Stock += t.Units; n.Held = Math.Max(0, n.Held - t.Units);
                    }
                    else if (t.Action == "Bought" && n.Buy > 0)
                    {
                        int next = (int)Math.Round(Math.Max(avg, 2 * avg - n.Buy));
                        if (n.Sell > 0) n.Sell = Math.Max(1, (int)Math.Round((double)n.Sell * next / n.Buy));
                        n.Buy = next; n.Stock = Math.Max(0, n.Stock - t.Units); n.Held += t.Units;
                    }
                }
                res.Add(n);
            }
            return res;
        }

        private static int Sum(List<ValueTuple<ItemRosterElement, int>> list, bool unit)
        {
            int s = 0; if (list == null) return 0;
            foreach (ValueTuple<ItemRosterElement, int> x in list) s += unit ? x.Item2 * x.Item1.Amount : x.Item2;
            return s;
        }

        private static void Collect(List<Trade> trades, List<ValueTuple<ItemRosterElement, int>> list, string action, bool unitPrices)
        {
            if (list == null) return;
            foreach (ValueTuple<ItemRosterElement, int> x in list)
            {
                EquipmentElement eq = x.Item1.EquipmentElement; if (eq.Item == null || x.Item1.Amount <= 0) continue;
                trades.Add(new Trade { Hour = Ledger.NowHours, Day = Ledger.Today, Date = CampaignTime.Now.ToString(), TownId = _townId, Town = _town, Action = action,
                    ItemId = eq.ItemModifier == null ? eq.Item.StringId : eq.Item.StringId + "+" + eq.ItemModifier.StringId,
                    Good = eq.GetModifiedItemName().ToString(), Units = x.Item1.Amount, Total = unitPrices ? x.Item2 * x.Item1.Amount : x.Item2 });
            }
        }

        private static Obs Row(Dictionary<string, Obs> rows, List<Obs> order, ItemRosterElement el, long snap, int day, string date, string townId, string town)
        {
            ItemObject it = el.EquipmentElement.Item;
            // Modified items (e.g. "Bent") are different goods with different prices, so key on the shown name too.
            string name = el.EquipmentElement.GetModifiedItemName().ToString();
            string id = el.EquipmentElement.ItemModifier == null ? it.StringId : it.StringId + "+" + el.EquipmentElement.ItemModifier.StringId;
            if (!rows.TryGetValue(id, out Obs o))
            {
                string type = it.IsTradeGood ? (it.IsFood ? "Food" : "Trade good") : (it.IsAnimal ? "Animal" : it.ItemType.ToString());
                o = new Obs { Snap = snap, Hour = Ledger.NowHours, Day = day, Date = date, TownId = townId, Town = town, ItemId = id, Good = name, Type = type, Weight = it.Weight };
                rows[id] = o; order.Add(o);
            }
            return o;
        }
    }
}
