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
        private List<Obs> _latest;      // the screen as it stood after the latest buy or sell
        private int _moved = -1;

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
                    _lastLogic = logic; _tracking = logic.IsTrading; _bought = null; _sold = null; _latest = null; _moved = -1;
                    if (!_tracking) return;
                    _goldAtOpen = Hero.MainHero.Gold;
                    Snapshot(logic);
                }
                if (_tracking)
                {
                    _bought = logic.GetBoughtItems(); _sold = logic.GetSoldItems();
                    // Your own buying and selling moves the prices on the screen. Keep the latest view so the
                    // ledger can be brought up to date when the deal is confirmed.
                    int moved = Sum(_bought, false) * 31 + Sum(_sold, false) + Amount(_bought) * 7919 + Amount(_sold) * 104729;
                    if (moved != _moved) { _moved = moved; _latest = (_bought.Count + _sold.Count) > 0 ? BuildRows(logic, _sold) : null; }
                }
            }
            catch (Exception e) { Ledger.LogError(e); }
        }

        private static int Amount(List<ValueTuple<ItemRosterElement, int>> list)
        {
            int n = 0; if (list == null) return 0;
            foreach (ValueTuple<ItemRosterElement, int> x in list) n += x.Item1.Amount;
            return n;
        }

        private static string Id(EquipmentElement eq) => eq.ItemModifier == null ? eq.Item.StringId : eq.Item.StringId + "+" + eq.ItemModifier.StringId;

        /// Everything on the trade screen right now. For goods just sold, also what the trader would pay for one more.
        private static List<Obs> BuildRows(InventoryLogic logic, List<ValueTuple<ItemRosterElement, int>> sold)
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
            if (sold != null)
            {
                var soldIds = new HashSet<string>();
                foreach (ValueTuple<ItemRosterElement, int> x in sold) if (x.Item1.EquipmentElement.Item != null) soldIds.Add(Id(x.Item1.EquipmentElement));
                foreach (ItemRosterElement el in logic.GetElementsInRoster(InventoryLogic.InventorySide.OtherInventory))
                {
                    if (el.EquipmentElement.Item == null || el.Amount <= 0 || !soldIds.Contains(Id(el.EquipmentElement))) continue;
                    if (rows.TryGetValue(Id(el.EquipmentElement), out Obs o) && o.Sell <= 0) o.Sell = logic.GetItemPrice(el.EquipmentElement, false);
                }
            }
            return order;
        }

        private static void Snapshot(InventoryLogic logic)
        {
            List<Obs> order = BuildRows(logic, null);
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
            if (_latest != null) Ledger.AddSnapshot(_latest);    // prices as they stood after your own trades
            int profit = 0, known = 0;
            foreach (Trade t in trades) if (t.Action == "Sold" && t.Matched > 0) { profit += t.Profit; known++; }
            string msg = "TradeScribe: recorded " + trades.Count + " trade" + (trades.Count == 1 ? "" : "s") + " in " + _town + ".";
            if (known > 0) msg += " Profit on goods you bought earlier: " + (profit >= 0 ? "+" : "") + profit + ".";
            InformationManager.DisplayMessage(new InformationMessage(msg, Color.FromUint(0xFFC9A227)));
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
