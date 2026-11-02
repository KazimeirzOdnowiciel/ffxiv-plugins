# Free-route audit: what the unattended queue delegated, and on which route

Window: **2026-11-01 17:26:00 → 2026-11-02 00:50** (the routes went live at 17:26, when the gateway restarted
after `delegation.backend: model_map` was written to `config.yaml` at 16:45; `delegation.backend: model_map`,
`default: deep`, `free → upstage/solar-mini4:free` with `stepfun/step-3.7-flash:free` then `deepseek-flash` as
fallbacks). All sources are local; nothing was fetched, no code changed, nothing published.

Live config at audit time (`~/AppData/Local/hermes/config.yaml`):

```yaml
delegation:
  backend: model_map
  backends:
    model_map:
      options:
        - { name: free, provider: nous, model: upstage/solar-mini4:free,
            fallback_providers: [nous/stepfun/step-3.7-flash:free, deepseek/deepseek-flash] }
        - { name: deep, provider: deepseek, model: deepseek-flash }
      default: deep
```

## The finding, in one line

**Since the routes went live, 23 unattended workers ran and one of them delegated: 5 children, all on
`deepseek-flash` (deep), zero on `free`.** The free route has never carried a unit of real work — every free
child in the log is a route probe from 16:31–17:16, before the window opens.

## What was collected and how

| Source | What it holds | What it returned |
|---|---|---|
| `grep 'platform=subagent' logs/agent.log` | one line per child turn: session, model, provider, goal | **13** matches, of which **2 are false positives** — a `skill_manage` warning at 17:05 and 21:48 whose *payload text* quotes the phrase `platform=subagent`. 11 real turn lines, **5 inside the window**, all at 20:12:13. |
| `ls -dt cache/delegation/live/*/` | one dir per `delegate_task` call, `task-N.log` per child | **12 dirs / 25 task logs**, of which **1 dir (`deleg_af2a6d69`) with 5 tasks** is inside the window. |
| `state.db` `sessions where source='subagent'` | the durable record: model, tokens, cost, parent session | **23 subagent sessions** total, **5** since 17:26 — the same five, matching the dir by goal prefix (not by eye). |
| `state.db` `messages` for the parent session | the actual `delegate_task` arguments | one call, `tasks: [5]`, every item with **keys `['context','goal']` only** — no `option`, no `model`, no `provider`. |
| `kanban.db` `task_runs` | which worker owned what | **24 runs** since 17:26 (`t_13400b7a` … this audit); the delegating run is **run 192, `t_1ab5e1af`, 20:10:12 → 20:44:15**. |

Log rotation was checked so the window cannot hide an entry: `agent.log.1` ends at **16:32:05**, `agent.log`
starts at **16:32:06**; the whole window is in the current file, and `agent.log` also covers 16:32→17:26.

## Routed children since 2026-11-01 17:26:00

Parent kanban task for all five: **`t_1ab5e1af`** — *"Code quality review pass (after layout + features, before
1.0.0)"*, session `local-b4ecd61f…`, run 192. Route label: **unset on all five** (the worker passed
`goal`+`context` only), so `model_map.default = deep` supplied the model. Cost is the provider's estimated
figure (`cost_status: estimated`), not an invoice.

| # | Time | Parent | Child goal (one line) | Route label | Model | In tok | Out tok | Cost | Verdict |
|---|---|---|---|---|---|---|---|---|---|
| 0 | 20:12:12 | t_1ab5e1af | Read-only code-quality review, Views layer pt. 1 (9 files): dead code, orphans, stacked docs | unset → default deep | deepseek-flash | 64,685 | 20,686 | $0.0265 | **appropriate on deep** — returned a *ranked findings* list with severities; that is judgement |
| 1 | 20:12:12 | t_1ab5e1af | Read-only code-quality review of `MountbookView.Grid.cs` (880 L) + `.Detail.cs` (1114 L) | unset → default deep | deepseek-flash | 62,635 | 28,677 | $0.0301 | **appropriate on deep** — decided which of two large files' expressions were wrong, and why |
| 2 | 20:12:12 | t_1ab5e1af | Read-only code-quality review, Services layer (~40 files) + `Configuration.Shape.cs` | unset → default deep | deepseek-flash | 161,241 | 41,144 | $0.0732 | **appropriate on deep** — the most enumeration-shaped row, but it ranks defects and names callers; a wrong verdict here is invisible and expensive |
| 3 | 20:12:12 | t_1ab5e1af | Read-only review of lifecycle, wiring, game access (`Plugin.cs`, `MainWindow.cs`, `Game/*`) | unset → default deep | deepseek-flash | 93,407 | 23,974 | $0.0343 | **appropriate on deep** — its output *is* a verdict ("N3 is clean", three subscriptions), Dalamud lifecycle rules |
| 4 | 20:12:12 | t_1ab5e1af | Read-only review of the test suite + preview/golden harness (45 + 13 files) | unset → default deep | deepseek-flash | 129,145 | 29,440 | $0.0524 | **appropriate on deep** — coverage judgement (which gate *should* have a test), ranked |

Supporting detail per row (all from `task-N.log` + `sessions`):

- **0** — 351.2 s, 25 API calls, 61 messages. Final summary claims scope honestly (nine files read, two only
  grepped). Produced the finding the parent later fixed: the table formats 3–4 strings per row per frame.
- **1** — 200.4 s, 18 calls. Established `TextWidth()` returns **screen pixels** before judging the
  pixel-vs-logical-unit mixing — the one fact the whole review hangs on.
- **2** — 352.4 s, 61 calls, 8.1 M cache reads (86 % of the window's cache traffic). Grep-confirmed callers in
  `.Tests`, `.Previews`, `.Tools` before declaring anything dead.
- **3** — 192.7 s, 26 calls. Reported *no* high-severity findings, which is the answer that would have been
  most tempting to fabricate on a cheap route.
- **4** — 276.4 s, 58 calls. Found `LayoutMeasure.WatchRowFits` had zero test references — a gap the parent
  then closed with a test proved by reverting its fix.

## Totals

| | Children | In tok | Out tok | Cache read | Cache write | Est. cost |
|---|---|---|---|---|---|---|
| **free** | **0** | 0 | 0 | 0 | 0 | $0.0000 |
| **deep** | **5** | 511,113 | 143,921 | 17,829,632 | 0 | $0.2165 |
| **total rows** | **5** | 511,113 | 143,921 | 17,829,632 | 0 | $0.2165 |

Rows in the table: 5. Free + deep = 5. ✓

Context for scale — the same window's **23 other worker runs delegated nothing** (24 runs total, 23 completed
before this audit): 3,763,646 input / 2,074,034 output tokens and **$3.62** spent on kanban workers, of which
the delegation above is $0.2165 (6 %). All of that reading was done inline by the parent, on the paid model.
Within `t_1ab5e1af` itself the split cost $0.2165 in children against $0.1680 in the parent — the children
cost *more than the parent did*, and bought wall-clock instead: all five ran in parallel, 5 m 54 s
(20:12:11 → 20:18:05) where the same five serially would have been ~23 min of child time inside a 34 min
parent run.

## Free children outside the window (context, not rows)

Five free-route children exist in total, and every one is a deliberate probe from the setup session, before
17:26 — each asked the child to echo a token and nothing else:

| Time | Dir | Model | Goal |
|---|---|---|---|
| 16:31:04 | `deleg_0f972c47` | stepfun/step-3.7-flash:free | `echo CHILD-OK-77` |
| 16:32:05 | `deleg_74792328` | stepfun/step-3.7-flash:free | `echo ROUTE-FREE` |
| 16:39:34 | `deleg_79c56fa0` | upstage/solar-mini4:free | `echo SOLAR-ROUTE` |
| 16:55:19 | `deleg_d7b61780` #0 | upstage/solar-mini4:free | `echo FREE-CHILD` |
| 17:16:09 | `deleg_a5305127` | upstage/solar-mini4:free | "Reply with the single word ROUTE-OK" |

They prove the route resolves and answers (`nous` → solar-mini4, and stepfun when solar was tried first), which
is the only thing this audit can say in free's favour: **it works and has never been used for work.** The other
18 task logs in those 12 dirs are `deepseek-flash`, all from before the window (10-31 13:21, 11-01 03:32,
09:40, 12:59, 13:08).

## Verdict

The automation is **not** misrouting judgement onto the free route — it is not routing at all, and on this
window it barely delegated. Every one of the five children was a multi-file code review, the exact class
`AGENTS.md` says is *always* `deep`, and every one behaved like it: ranked severities, a verdict that
contradicted the easy answer (row 3 found nothing high), and a fact established before judging (row 1's
pixel-vs-logical-unit distinction). Sending any of them to `solar-mini4:free` would have been the mistake the
rule warns about, and nobody made it. But nobody *decided* either: the parent sent `goal`+`context` with no
`option` key on all five tasks, so the paid model arrived by `model_map.default`, not by a judgement — the
session started at 20:10, 2 h 44 min after the routes were live, so `option` was in its schema and simply went
unused. The real shape of the problem is the other half of the same sentence: 23 workers, one delegation, and
$3.62 of inline reading. The rule's free cases — enumerate-then-total, parallel scans, structure extraction,
mechanical edits provable by diff — are exactly what those runs did inside their own context (the 3.6 M input
tokens are the bill for it), and not one of them was ever split off to the cheap route. Free is therefore not
being abused; it is unused, and the queue's spend is entirely paid-model reading that the rule intended to be
cheap. The fix is not a new rule — it is one worker being told, in a task body, to route its bulk reading.
