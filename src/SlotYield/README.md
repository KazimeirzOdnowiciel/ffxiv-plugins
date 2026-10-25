# Slot Yield

A Dalamud plugin for Final Fantasy XIV that treats your retainer market slots like what they are: a
small portfolio with a fixed number of slots and a limited pile of gil.

It scans the market board through Universalis, models how much of each item's daily sales you can
actually capture given the competition at your price, and then solves the assignment problem: which
N items, within your capital budget, earn the most gil per slot per day.

No automation. It reads the market and tells you what it would do.

## The model

A slot holds one item, one quality, at one price. It earns:

```
list price      = min listing (or -1 gil, or the average sale, depending on strategy)
net per unit    = list price x (1 - tax)
sellers ahead   = distinct retainers already at or below your price
your share      = min(50%, 1 / (sellers ahead + 1))
units/day       = min(stack, market sales/day x your share)
gil/day         = units/day x net per unit
capital         = stack x list price
days to clear   = stack / units/day
```

That last line is what makes this different from a price checker. A 99-unit stack of something that
sells once a week is **not** a 99-unit sale: it is a dead slot holding your gil hostage. Items that
take longer than `dead after` days to clear are flagged and worth zero in the portfolio.

Every row also carries a **30-day sparkline** of quantity-weighted daily prices, built from the
sales the API actually returned: gaps carry the last price forward, and anything outside a median
band is trimmed. A market that halved last month is drawn in red, because that price is not the
price you will sell into next month.

The portfolio solver is a 0/1 knapsack in two dimensions (slots x gil) solved with a Pareto
frontier, so it is exact rather than greedy: every candidate keeps the set of (capital, value)
points that are not dominated by a cheaper and better one. It also reports the shadow prices -
what the weakest slot is worth, and what one more million gil would add per day.

## Profit, not revenue

Revenue is not what you keep. Slot Yield estimates what one unit actually costs you and ranks on
**profit per day**:

| Cost basis | What it assumes |
| --- | --- |
| `craft or buy, whichever is cheaper` (default) | the cheapest honest way to obtain the item |
| `craft it` | recursive craft cost from the game's recipe sheet, falling back to market |
| `buy the finished item` | you are flipping, not making |
| `revenue only` | no cost data, the old behaviour |

The cost engine walks recipes recursively (depth 4, cycle-guarded), prices every ingredient from
your world's quotes first, then the DC-wide scan, then the vendor price, and multiplies through
stack sizes and recipe yields. Materials it cannot price are reported as **partial** rather than
silently free - a gathered material is a material we could not price, not a 0-gil purchase.

That changes the numbers that matter: `capital` becomes what the stock actually costs you, and the
portfolio solver maximises profit per day within that capital.

## Two-stage scan
| Stage | Endpoint | Cost | Purpose |
| --- | --- | --- | --- |
| 1 | `aggregated/{world}/{100 ids}` | ~169 requests for the whole board | rank 16,800 marketable items by "how big is this market" using DC velocity |
| 2 | `{world}/{20 ids}?listings=60&entries=120` | ~20 requests for 400 items | exact per-world, per-quality numbers: real listings, sellers at your price, sales history |

Stage 1 is a ranking heuristic and says so. Stage 2 is where the decisions come from.

Results cache for `cache (hours)` and rescore instantly when you change tax, stack size or
strategy - the quotes are cached, the model is recomputed.

## Between scans

Each scan saves a compact snapshot of the top 150 items by profit for that world. The next scan
diffs against it and the **Changes** tab lists:

- price drops and rises above 5%, with the resulting profit change
- new entries in the tracked list
- items that left the tracked list

A 2% wobble is ignored on purpose; markets move, and a tool that cries about every gil is a tool you
stop reading. The snapshot lives in the plugin's cache with a 60-day expiry, so it works across
sessions and across patches. Pinned slots are always tracked, even when they are outside the top 150
that day, and snapshots taken under a different cost basis are not diffed (profit is not comparable
across cost models, prices are).

There is also **copy CSV** on the Opportunities tab: the filtered, sorted view as RFC-4180 CSV for a
spreadsheet. Prices with commas in item names stay quoted.

## Data freshness

Universalis is crowd-sourced, so a quote can be minutes old on a busy world and days old on a quiet
one. A day-old price is a guess, and the plugin says so:

- every row carries a **confidence** score: freshness (full marks under 30 minutes, decaying to zero
  at a day) x sale samples (ten or more is full marks) x market depth
- anything older than **6 hours**, or with no sale samples at all, is marked **stale**: a `!` next to
  the name, and a "fresh only" filter to hide them
- the scan warning line tells you how many of the priced items were stale
- the Portfolio tab prefers fresh items, and only falls back to stale ones when there are not enough
  fresh candidates to fill your slot count

## Craft from your bags

The **Craft** tab reads your inventory - four bag pages, the crystal pouch, the saddlebag, and the pages of whichever retainer you have open - matches every recipe
whose materials you fully own, and prices the result against the last scan:

- **Job** - the crafting job and its required level, so you know which job to switch to. Nothing is filtered by level: a maxed crafter would only lose entries to a filter that never fires.
- **Crafts / Qty** - how many you can make with what is on you now, and the units that produces.
- **Mats** - what those materials would fetch if you sold them instead (the opportunity cost).
- **Profit** - revenue minus that opportunity cost. **Owned** is the revenue if you count the
  materials as free, which is the right number for gathered stock.
- **HQ** - the same craft valued at the HQ minimum, when the recipe can make HQ and the board has HQ listings. Sorting uses whichever of the two is better, so a high-value HQ craft is not buried under its NQ price.
- **Clear** - days to sell the batch at the market's velocity and your share of it.

Intermediates are followed through: if you hold the ore but not the ingot, the sword still appears,
marked `+sub`, and its material cost is the ingot's recipe cost rather than zero. The chain is
simulated against a shared stock copy, so two recipes cannot both spend the same ore, and it stops at
four levels deep.

Only the active retainer can be read: the client loads a retainer's inventory when you open that retainer, so stock sitting in other retainers is invisible until you visit them. The status line names which retainer was included.

Reading the stock also prices its own candidates (one quote request per twenty items), so the tab does not depend on whether those items happened to make the scan shortlist. The prices it fetches are shared with the My listings tab.

A recipe with any unpriced material shows `partial` instead of a material value, because a missing
price would otherwise look like a free ingredient. `/slotyield craft` does the same from chat.

## My listings

Your own market slots are read from two places, both read-only, both matched by **retainer id** from
the game's retainer manager (ClientStructs), never by name:

1. **The retainer's sell list** - the window you actually use. `AgentRetainer.SellListEntries` holds
   every slot for the active retainer: item, quantity, unit price. Quality comes from the retainer's
   `RetainerMarket` inventory container, because the sell list entry itself does not carry it.
2. **The market board** - `IMarketBoard.OfferingsReceived` gives full listing data (quality, retainer,
   city) whenever you view an item you sell. This is the more accurate source and fills in anything
   the sell list could not resolve.

So opening one retainer's sell list captures up to twenty slots at once; the per-retainer counters
(`3/20`) show how much of each retainer has been seen. Nothing is written to game memory, and the
whole capture is wrapped so a malformed structure can only log, never crash the market board.

Each row names its retainer and the table is grouped by retainer, so it is clear which retainer lists what. The **My listings** tab shows each captured slot with the current market minimum, how far above or
below it you are, expected days to clear, and a verdict (`cheapest`, `at the market minimum`, or how
far above it you are). Captured items are added to the next scan's shortlist, so they are always
priced even if they would never make the top 150.

## Alerts

Alerts are opt-in-on-by-default and cheap: one quote request per twenty watched items, every fifteen
minutes by default (configurable 1-120). They cover two things that cost real gil and are invisible
without polling:

- **Undercut** - the market minimum fell meaningfully below one of your listings, so your slot is now
  queuing behind someone. This fires on the first poll, because it only needs today's board.
- **Price drop** - the minimum fell by more than a set share since the previous poll, on a listing or
  a pinned item. This needs two polls, so the first run only establishes a baseline.

- **Pinned profit floor** - after a scan, a pinned item that climbed above your gil/day floor is announced (`now worth 180k/day, was 40k/day`). The first scan after a reload only records the baseline, so a healthy pin does not shout.

Each alert has a per-item cooldown (default six hours), persisted in the config so a plugin reload does not re-announce the same undercut; entries older than a week are pruned. Alerts also arrive as a notification toast (toggleable), so they are visible mid-pull instead of scrolling away in chat. `/slotyield alerts` runs a poll immediately, which is also the quickest way to check the wiring.

## Automatic repricing

The one feature here that presses buttons in the game. It only runs when you press the button.

**Requirements**

- Stand at a summoning bell and open the retainer list once. The client offers no way to open it
  remotely without another plugin.
- English client: "Sell items" is assumed to be the third entry of the retainer menu, matching the
  reference implementation (Dagobert). On another language, that index has to change.
- Open the retainer list at a bell, then press **Reprice all** (or `/slotyield reprice`).

**What it does per listing**

1. Switch to the retainer that owns the listing (click its row in `RetainerList`, choose "Sell items").
2. Wait for the sell list to actually be open on that retainer - the menu opens first, and the rows
   only exist once the list does.
3. Open the item's price dialog, set the price through the numeric input component at
   `RetainerSell.NodeList[15]`, and **read the component back**, requiring an exact match.
4. Confirm with a synthesized `CHANGE` event aimed at the dialog's Confirm button.
5. Wait for the dialog to close, then next listing.
6. Record the new price in the book, so the same move is never applied twice.

**Safety**

- Moves smaller than the configured gil amount are skipped; the default is 0, so a listing even a few gil above the minimum is still repriced.
- Drops below 50% of the current price are blocked unless you explicitly allow them
  (`/slotyield big`, or the checkbox in the Scan tab).
- `/slotyield stop` stops a running pass at any point.
- Any mismatch (row missing, dialog not open, wrong read-back, dialog not closing) aborts that
  listing, and the summary says how many applied and how many failed.

The sell dialog itself can also price new listings: with **auto-fill the asking price when listing** enabled, opening the sell dialog writes the market price into the asking field (the same strategy as the planner). `/slotyield price` does it on demand. It only reads the item and writes the price field; you still press Confirm.

Treat this as a grey-area tool: it drives the game's UI, and a bug here moves real gil. Test changes
on cheap listings first, and keep the floor on unless you are sure a low minimum is the market rather
than one seller dumping.

## Guards against nonsense

Market board data is full of traps. Three of them are handled explicitly:

- **Troll listings.** A 1-gil listing among 20,000-gil listings is not the market. Listings below
  10% of the median price are dropped before the minimum is taken.
- **Stale or absurd sale history.** The average sale price is clamped to the board's neighbourhood,
  and the 30-day series drops anything outside a median band (0.25x to 4x) before bucketing, so one
  1-gil phantom does not flatten a month of prices.
- **Manufactured volume.** Repeated single-unit sales of an expensive item within 90 seconds of each
  other are how gil gets moved through the market board. If more than half of an item's expensive
  sales look like that, the item is flagged `suspicious volume` and excluded from the portfolio.
  A live scan of Light found 15 such items on the first try, including one "item" showing 104M
  gil/day of fake demand.
- **Incomplete scans are not cached.** If some batches fail, the result is used for the current run
  but not written to disk, so a flaky minute cannot hide items for the whole cache window.

## Install

```
dotnet build -c Release
```

Then either load `bin/Release/` as a dev plugin in `/xlplugins`, or install
`bin/Release/SlotYield/latest.zip`. Targets Dalamud API 15 (.NET 10).

## Commands

| Command | What it does |
| --- | --- |
| `/slotyield` or `/sy` | Toggle the window |
| `/slotyield scan` | Start a scan |
| `/slotyield world <name>` | Pin the world to scan (default: your current world) |
| `/slotyield pin <itemId> [hq]` | Pin a slot |
| `/slotyield unpin <itemId> [hq]` | Unpin |
| `/slotyield diag` | Write a diagnostics report next to the config |
| `/slotyield reset` | Put the window back at a sane size and position |
| `/slotyield clear` | Drop the cache |

## Verification

```
dotnet run --project tests/CoreTests      # model + optimizer, incl. 400 random instances vs brute force
dotnet run --project tests/CostTests      # craft cost recursion, yields, cycles, depth, partial costs
dotnet run --project tests/MarketTests    # Universalis parsing against captured live fixtures
dotnet run --project tests/SmokeTest      # loads the packaged DLL and runs the optimizer
./verify.ps1                              # all of the above
```

`tests/LiveScan` is an end-to-end harness against the real API:

```
dotnet run --project tests/LiveScan -- Light 15 20
```

It prints the top slots, the recommended 20-slot portfolio, and the request/time budget. Item names
come from Garland Tools there, purely for display; the plugin reads names from the game's sheets.

## If something goes wrong

- `/xllog` opens Dalamud's log in game; everything the plugin logs is tagged `[SlotYield]`.
- The log file is `%APPDATA%\XIVLauncher\dalamud.log` (it rotates to `dalamud.old.log` at about
  10 MB, so early lines do not last forever).
- `/slotyield diag` writes a self-contained report to the plugin's config folder: version, world,
  model settings, scan stats, warnings, cache contents and the last error. It is the fastest thing
  to hand someone when the numbers look wrong and the log has already rolled over.

Scans log a one-line summary at Information level (items priced, live candidates, requests,
duration, cache hits) and every scan warning at Warning level. The one part of a scan that touches
game sheets - recipes and vendor prices - is timed, and logs its duration if it takes 15 ms or more.

If the window itself throws while drawing, it does not take the plugin down: it shows the exception
with **Reset window**, **Disable the plugin** and **Write diagnostics** buttons. A window that
somehow opens off-screen is recovered with `/slotyield reset`.

## Assumptions, stated plainly

- **Share is a heuristic.** 1/(competitors+1) is a fair first-order model of a queue, not a
  simulation of buyer behaviour. It is deliberately capped at 50% so an empty market never looks
  like a money printer.
- **Capital is what the stock costs.** When a cost basis is active, `capital = unit cost x stack`;
  in revenue mode it is `list price x stack`. Either way it models gil tied up, which is what limits
  a portfolio.
- **Material prices are proxies.** Ingredients outside the priced shortlist use the DC-wide averages
  from stage 1, so they can be a little optimistic or pessimistic on a quiet world. The cost is
  flagged partial when a material has no price at all.
- **Stage 1 uses DC velocity**, because the batch endpoint does not report per-world velocity.
  Stage 2 corrects it with the world's own sales history.
- **Data is crowd-sourced.** Universalis freshness varies by world; the plugin shows the scan time
  and never silently pretends stale data is current.
- **Tax is yours to set.** 3% outside the three city-states, 5% inside.

Data: [Universalis](https://universalis.app). Thanks to the people who upload their boards so the
rest of us can stop alt-tabbing.
