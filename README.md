# TradeScribe

An in-game trade ledger for Mount & Blade II: Bannerlord. It writes down the prices your character
sees and the trade rumours the game gives you, then suggests trade runs from that alone.

Built and tested against game v1.4.7 with Empires of Europe 1100. It uses no Harmony patches and
stores nothing in your save, so it can be removed at any time.

## What it does

- **Logs every trade screen you open:** the trader's stock and buy prices, and the sell prices of
  what you are carrying.
- **Logs trade rumours:** every rumour the game gives you is kept, even after it expires in game.
- **Suggests trade runs:** cheapest known place to buy each good and the best known place to sell
  it, with profit each, a rough trip profit, and the map distance. Units are capped by your cash,
  the trader's stock and your free carry weight.
- **Records your trades:** what you bought and sold, and the profit on goods you bought earlier.

## Fair play

TradeScribe only uses what your character could know: trade screens you opened, your own rumour
list, map distance, and your own cash and carry capacity. It never reads another town's market and
never changes anything in the game.

Rumours are the game's own: Bannerlord gives you a town's prices as rumours when you enter it, plus
one rumour a day from a random town, and shows them in item tooltips. TradeScribe copies that list
and keeps it; it does not create rumours.

## Using it

- Press **F7** on the campaign map, or choose **Open the trade ledger** in a town menu.
- Menu entries: Best trade runs, What to buy here, Prices for one good, Rumour log, My trades and profit.
- Each sell price is marked **seen** (you saw it), **rumour** (from a live rumour) or **estimate**
  (worked out from that town's buy price).
- Prices older than 20 days are ignored.

## Install

1. Copy the `TradeScribe` folder into the game's `Modules` folder so that you have
   `Modules/TradeScribe/SubModule.xml` and `Modules/TradeScribe/bin/Win64_Shipping_Client/TradeScribe.dll`.
2. Tick **TradeScribe** in the launcher's Mods list and start the game.

## Where the data goes

`Documents/Mount and Blade II Bannerlord/TradeScribe/<campaign id>/`

| File | Contents |
|---|---|
| `observations.csv` | Every item on every trade screen you opened |
| `rumours.csv` | Every trade rumour, with the day you heard it |
| `trades.csv` | Every confirmed purchase and sale |

Errors, if any, go to `errors.txt` in the `TradeScribe` folder.

## Build

Needs the .NET SDK and a copy of the game.

```sh
BANNERLORD_DIR="/path/to/Mount & Blade II Bannerlord" dotnet build -c Release
```

Then copy `bin/Release/netstandard2.0/TradeScribe.dll` into
`Modules/TradeScribe/bin/Win64_Shipping_Client/` alongside `SubModule.xml`.

## Limits

- Trip profit is rough: prices move as you buy and sell in bulk.
- Rumour prices come from the game as given and may be more generous than what a trader offers you.
- The menu uses the game's standard pop-ups, so lists are short text, not sortable tables.
