# Advanced rules (Phase 05)

> **Status: implemented, NOT TESTED IN GAME.** Scenarios: [PHASE_05_TESTING.md](PHASE_05_TESTING.md). Design and
> evidence for every game fact used here: [PHASE_05_ANALYSIS.md](PHASE_05_ANALYSIS.md).

Phase 05 adds stricter, optional rules on top of the Phase 01–04 category orders. The defaults keep the earlier
behaviour: every category *Any Unlocked*, any mix of permitted products, no quality or effect rule, player and
dealer sales on. Older settings files load with exactly the permissions they had.

## 1. Where to find things in the **Take It** phone app

| Screen | What you change there |
| --- | --- |
| **Products** | Mode per category (*Any Unlocked*, *Selected Only* = allowed list, *Blocklist*, *Disabled*) and the list itself. Each row shows the recorded lineage (*base strain*, *from OG Kush*, *lineage unknown*) |
| **Rules** | For the selected category: list matching (exact recipe / base strain), minimum quality, effect rules |
| **Sales** | Player sales and dealer sales switches, mixing mode, which rules you edit (global or one dealer's overrides) |
| **Presets** | Anything Goes, Selected Products Only, Vanilla Preferences (preview, then apply to the draft) |
| **Settings** | Save, Discard, Reset to defaults |
| **Diagnostics** | Rule engine status: settings schema, preset, switches, how many products have a recorded lineage |

Every change is a **draft** until you press **Save**. **Discard** drops it. Orders already accepted keep the rules
they were accepted with.

## 2. Lists: allowed and blocked

- **Selected Only** (allowed list): only the listed products may fill an order of that category.
- **Blocklist**: every unlocked product may, except the listed ones.
- Both lists are kept when you switch modes, so you can go back without losing either.
- Only products discovered in the save are shown; locked products are never permitted, whatever the list says.

### Exact recipe or base strain

On the **Rules** screen, *List matching* decides what a listed product covers:

- **Exact recipe**: that product only.
- **Base strain (with its mixes)**: that product and every product the game recorded as mixed from it, directly or
  through other mixes (OG Kush → mix A → mix B: listing OG Kush covers A and B). A mix the game recorded with two
  parents belongs to both lineages.

Lineage comes **only** from the game's recorded mix recipes, never from names. If the recipes could not be read
for a product, its row says *lineage unknown* and:

- an allowed list by base strain only takes it when that exact product is listed;
- a blocklist by base strain **refuses** it (it might come from a blocked strain), with the reason *Lineage unknown*.

## 3. Quality

*Minimum quality* (Rules screen): *Any*, or *Poor+*, *Standard+*, *Premium+*, *Heavenly+*.

- It is checked on the **physical items**: at your handover, and for dealers on their stock. An item below the
  minimum is refused before anything moves (*The item quality is below your minimum (Premium or better).*).
- Customers keep their own quality standard exactly as in the unmodded game. A dealer only reserves stock at or
  above the stricter of your minimum and the customer's requirement.
- An item whose quality cannot be read is refused only when a minimum is set.
- There is no upper limit on purpose: a cap could make a customer's own standard impossible to meet.

## 4. Effects

Rules screen → *Effects*. Tap an effect to cycle **Any → Required → Excluded**.

- **Require all** / **Require any** decides how required effects combine.
- An excluded effect always wins over a required one.
- A rule that can never be met (for example an effect both required and excluded with *Require all*) is flagged
  in orange and permits nothing, so no impossible order is created.
- A product whose effects could not be read never passes an effect rule.
- Effects saved in the settings that no discovered product has are shown as *(no discovered product has it)*.

## 5. Mixing

Sales screen → *Mixing*:

| Mode | One order may be filled with |
| --- | --- |
| Any permitted products | any mix of permitted products of the category (default, Phase 03 "mixed ON") |
| One product per order | a single product (Phase 03 "mixed OFF") |
| One base strain per order | several products only if the game recorded them as coming from the same base strain |
| Requested product only | only the product the customer asked for, if your rules permit it |

Fair pricing (Phase 04) applies in every mode: a substitute must be worth at least the quoted unit price.

## 6. Player sales, dealer sales and dealer overrides

- **Player sales** off: your own customers' contracts use vanilla rules. **Dealer sales** off: dealer contracts
  use vanilla rules. The global mod switch (Overview) still turns both off at once.
- Switching never adds or removes a game hook; contracts already accepted keep their snapshot, and dealer
  appointments keep their reservations.
- **Dealer overrides**: on the Sales screen pick *Edit overrides* for a dealer. While editing a dealer, the
  Products and Rules screens show that dealer's effective rules; the first change to a category copies the global
  rule into an override for that dealer. *Use global rule* removes one override; *Remove overrides* removes all of
  a dealer's overrides. Overrides never bypass the switches, unlock state or fair pricing.
- Dealers appear in the list once the game has updated them in this session; overrides saved for a dealer that is
  not hired or not seen are shown as inactive.

## 7. Presets

| Preset | Sets | Keeps |
| --- | --- | --- |
| Anything Goes | mod, player and dealer sales on; every category *Any Unlocked*; effect and quality rules cleared; any mix; dealer overrides removed | your allowed and blocked lists |
| Selected Products Only | mod, player and dealer sales on; every category *Selected Only* | lists, matching, effects, quality, mixing, overrides (categories with an empty list are warned about) |
| Vanilla Preferences | player and dealer sales off (the mod stays on, so the app keeps working) | every rule |

*Preview changes* lists what would change; *Apply to draft* changes the draft only (Save or Discard as usual). The
Presets and Overview screens show the preset your draft matches, or **Custom**.

## 8. Order of the rules

One engine decides everything, in this order (details in [PHASE_05_ANALYSIS.md](PHASE_05_ANALYSIS.md) §5):
mod switch → player/dealer switch → scope (dealer override, else global rule, else default mode) → category and
unlock → allowed/blocked list (exact or base strain) → effects → quality (items only) → mixing → quantity and
packaging → fair price → revalidation at handover. Each refusal has a machine-readable reason
(`EligibilityReason`, `DeliveryRejection`) and a readable message on the handover screen and in the app.

## 9. Settings file

Settings are schema 2 (`UserData/TakeItOrLeaveIt/saves/<save id>.json`). The first save over an older file keeps
a one-time copy `<save id>.json.schema1.bak`. Values the mod cannot read make the affected category **Disabled**
(reported in the log and Diagnostics) rather than guessing; an unknown mixing value falls back to one product per
order.

## 10. Co-op (Phase 07)

In a co-op game the **host's** saved rules apply: offers, dealer appointments, dealer overrides and the host's own
handovers follow them. A client with Take It sees the host's rules on every screen, read-only ("Host controls Take
It settings."), and cannot edit, save, reset or apply a preset there; its own settings for its own saves are not
touched. Unsaved changes on the host are never shared. Details: [MULTIPLAYER.md](MULTIPLAYER.md). Not tested in a
real co-op game yet (Phase 08).
