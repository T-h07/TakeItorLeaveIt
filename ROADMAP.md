# Take It Or Leave It — Development Roadmap

> **Project:** Schedule I sales-overhaul mod  
> **Status:** Phases 00-02 merged (Phase 00 verified in game). Phase 03 (player sales) implementation complete, in-game acceptance deferred  
> **Target release:** v1.0.0  
> **Development branch:** `develop`  
> **Goal:** Make selling simpler while giving the player precise control over **what can be sold**.

## 1. What are we building?

In the normal game, a customer may prefer a particular strain, combination of effects, or recipe. That can force you to manufacture a special mix just to fulfill one order.

**Take It Or Leave It** changes that: customers order a **drug category + quantity** (for example, “5 weed”). They no longer insist on a particular recipe. You or your hired dealers can deliver any products that match the category **and the rules you selected in the mod menu**.

But it is **not** simply an “accept everything” switch. You can open the in-game menu and choose which **already-unlocked** strains or recipes are eligible. For example, you could permit **only Granddaddy Purple**, or Granddaddy Purple + OG Kush, or every unlocked weed product. Those same restrictions can apply to both personal sales and dealer sales.

### Three quick examples

| Customer wants | Your current mod rules | What you deliver | Expected result |
| --- | --- | --- | --- |
| 5 weed | Any unlocked weed | 5 OG Kush | **Accepted**, if OG Kush is unlocked |
| 5 weed | Only Granddaddy Purple | 5 Granddaddy Purple | **Accepted**, if unlocked and in stock |
| 5 weed | Only Granddaddy Purple | 5 OG Kush | **Rejected** — player excluded it |
| 5 weed | Granddaddy Purple **and** OG Kush, mixed stacks enabled | 2 Granddaddy Purple + 3 OG Kush | **Accepted** |
| 5 cocaine | Any unlocked weed | 5 weed | **Rejected** — wrong category |
| 5 weed | Only Granddaddy Purple | Dealer has only OG Kush | **Not fulfilled** until eligible stock is available |

**Important:** “Unlocked” means the active game save has legitimately discovered/accessed the product. “In stock” means you or the dealer currently own enough units to deliver. The menu uses **unlocks** to decide what may be selected; the sale uses **inventory** to decide whether an order can be completed.

## 2. Development roadmap at a glance

We develop one phase at a time. Each phase starts on its own `feature/...` branch from `develop`. Once its tests pass and you approve it, we merge it into `develop` and delete the temporary branch.

| Phase | Feature branch | Plain-English goal | What you will get |
| --- | --- | --- | --- |
| **00 — Foundation** | `feature/phase-00-foundation` | Prepare the project and understand how Schedule I works internally | A mod that loads safely, plus a verified technical map |
| **01 — Products & Rules** | `feature/phase-01-policy-engine` | Teach the mod which products exist, which are unlocked, and which are allowed | Reliable filtering logic used everywhere |
| **02 — Mod Menu** | `feature/phase-02-mod-menu` | Give players an in-game interface for their preferences | Category settings and unlocked-product selection |
| **03 — Player Sales** | `feature/phase-03-player-sales` | Make your own customer deals obey the rules | “5 weed” can be fulfilled with any permitted weed |
| **04 — Dealer Sales** | `feature/phase-04-dealer-sales` | Make hired dealers follow the same behavior automatically | Dealers choose eligible stock without exact-recipe requirements |
| **05 — Advanced Rules** | `feature/phase-05-advanced-rules` | Add detailed customizations beyond basic selection | Exact mix filters, quality rules, presets, separate dealer options |
| **06 — Save Safety** | `feature/phase-06-persistence` | Make settings survive restarts and avoid save corruption | Save-specific profiles and safe recovery |
| **07 — Co-op & Compatibility** | `feature/phase-07-multiplayer` | Keep everything working across multiplayer and supported builds | Host-controlled rules, synchronized sales, compatibility checks |
| **08 — v1.0.0 Release** | `feature/phase-08-release` | Test, package, document, and release the mod | A downloadable, verified first stable release |

---

## 3. Detailed descriptions of every phase

### Phase 00 — Project Foundation & Game Research

**Branch:** `feature/phase-00-foundation`

**What this means:** Before writing the selling logic, we need to understand how the installed version of Schedule I stores customers, products, orders, dealers, and inventory. Think of this as building the workspace and finding the correct places to modify. We should not guess game method names or modify random code.

**What we will do:**
- Set up the C# solution, development folders, build configuration, and a simple automated test workflow.
- Identify whether the installed game uses **Mono or IL2CPP** and which mod-loader/API versions work with it.
- Establish a MelonLoader entry point and Harmony patching support; assess S1API for the phone interface.
- Inspect the real game assemblies and produce `docs/PATCH_MAP.md`: which verified methods handle orders, handovers, customer preferences, dealer scheduling, and stock.
- Add helpful logging so a failed patch or incompatible update is visible instead of silently breaking gameplay.
- Document how to build the project and install a test DLL.

**In-game example:** You launch Schedule I, and the mod loads and prints its version to the log. It **does not change orders yet**.

**We call this done when:** A real game launch loads the DLL without a crash, the identified patch points are documented, and the solution builds for the verified runtime.

### Phase 01 — Discover Unlocked Products & Create the Rule Engine

**Branch:** `feature/phase-01-policy-engine`

**What this means:** We build the “brain” of the mod. It needs to recognize categories (weed, meth, cocaine, and any other supported category), understand which strains/recipes the current save has unlocked, and decide whether a specific item is allowed for sale.

**What we will do:**
- Read product categories, base strains, recipes, stable IDs, and unlock status from the game instead of hardcoding an assumed list.
- Write a central `SaleEligibilityPolicy` used by the menu, player handovers, and dealer deliveries.
- Support three basic modes for **each category**:
  - **Any Unlocked** — accept any discovered product in that category.
  - **Selected Only** — accept only the unlocked products you explicitly checked.
  - **Disabled** — do not use the mod to create or fulfill new sales for that category.
- Implement product checks, category checks, quantity checks, and a way to combine permitted stacks.
- Handle an empty selection safely (no eligible products means no new mod-generated offers in that category).
- Refresh discovery when progression unlocks another product or the player changes saves.

**In-game example:** You have unlocked OG Kush but not Granddaddy Purple. The rule engine knows OG Kush is eligible to appear in the menu and Granddaddy Purple is not. After you unlock Granddaddy Purple, it becomes available without cheating progression.

**We call this done when:** Automated tests prove the correct allow/reject decisions for unlocked, locked, wrong-category, and mixed-stack products. No customers or dealers are modified in this phase.

### Phase 02 — Build the In-Game Mod Menu

**Branch:** `feature/phase-02-mod-menu`

**What this means:** You should not need to edit JSON files every time you change what can be sold. We'll create a **visible, easy-to-use in-game menu**, ideally an app on the player's phone. If native phone integration does not work with the installed game version, we'll use a safe pause-menu or hotkey interface.

**What we will do:**
- Add an **Overview** screen with a global enable/disable toggle and a summary of the current rules.
- Add a **Products** screen with one tab per discovered category, a search box, and checkboxes for unlocked strains/recipes.
- Let you choose **Any Unlocked**, **Selected Only**, or **Disabled** for each category.
- Show a live preview: “Customer wants 5 weed; the products you selected will/won't satisfy this order.”
- Save settings, reset to defaults, and display why a selection cannot be used.
- Prevent locked or unavailable products from being shown as selectable.
- Keep the menu lightweight and usable even when the player discovers hundreds of recipes.

**In-game example:** Open phone → Take It Or Leave It → Weed → Selected Only → check **Granddaddy Purple** → Save. The menu now says: “Only eligible Granddaddy Purple products can fulfill weed orders.”

**We call this done when:** You can open the menu in the game, change a rule, restart, and see the saved rule still selected. **Sales behavior is not changed yet**; the menu is the control panel for the next phases.

### Phase 03 — Player Sales: “Take What I Give You”

**Branch:** `feature/phase-03-player-sales`

**What this means:** This is when the core feature becomes playable. Instead of requiring an exact special mix, a customer asks you for a category and quantity. Your handover succeeds when the items belong to that category **and your menu permits them**.

**What we will do:**
- Update or adapt customer offers so they request **category + quantity**, not exact recipes/effects.
- Patch the verified handover acceptance logic to use our policy engine.
- Allow mixed **eligible** stacks to add up to the requested quantity when enabled.
- Prevent wrong-category, blocked, or insufficient inventory from counting as successful deliveries.
- Bypass customer penalties that come **solely** from supplying a different accepted recipe or effect mix.
- Keep vanilla payment, relationship progression, XP, and item consumption correct.
- Preserve already accepted contracts safely when changing rules mid-order.

**In-game example:** A customer asks for **5 weed**. You selected Granddaddy Purple and OG Kush. Deliver **3 Granddaddy Purple + 2 OG Kush**. The customer accepts the five units, pays normally, and does not complain about the recipe.

**We call this done when:** A real gameplay test passes valid and invalid handovers, gives money/XP exactly once, removes exactly the delivered quantity, and leaves no broken order state.

**Milestone:** **First gameplay-functional prototype** — personal sales work.

**Implementation status (Phase 03):** done, in-game acceptance deferred. Chosen approach (from the verified call
graph, [PHASE_03_PATCH_ANALYSIS.md](docs/PHASE_03_PATCH_ANALYSIS.md)): offers stay vanilla; a player contract
accepted while the mod is on gets an acceptance-time snapshot "N units of category X, any of these products";
the game's own match function answers from that snapshot (quality kept, recipe dropped) and the game performs
payment, XP, item transfer and completion. Exact quantity only, mixing switchable, single player only. Details:
[PLAYER_SALES.md](docs/PLAYER_SALES.md); tests: [PHASE_03_TESTING.md](docs/PHASE_03_TESTING.md).

### Phase 04 — Hired Dealer Sales & Appointments

**Branch:** `feature/phase-04-dealer-sales`

**What this means:** Your hired dealers should have the **same freedom as you**. A dealer must not fail a five-weed appointment merely because it holds a different weed recipe than the customer originally preferred.

**What we will do:**
- Connect dealer appointment generation and fulfillment to the shared product policy.
- Allow dealers to choose available permitted products instead of searching for one exact recipe.
- Support quantities spread across multiple eligible inventory stacks.
- Safely reserve inventory so two appointments cannot spend the same units.
- Handle canceled appointments, insufficient stock, customer no-shows, and interrupted deliveries.
- Preserve dealer commissions, earnings, customer satisfaction, and inventory totals.

**In-game example:** Your dealer has **2 eligible Granddaddy Purple + 3 eligible OG Kush**. A customer orders **5 weed**. With both strains selected and mixed stacks enabled, the dealer automatically fulfills it. If OG Kush is excluded, the dealer waits for enough eligible Granddaddy Purple instead of substituting a forbidden item.

**We call this done when:** Dealers can repeatedly complete permitted orders without stock mismatch, skipped customers, lost goods, duplicate payment, or commission errors.

**Milestone:** **Single-player beta** — both personal and dealer sales function.

### Phase 05 — Advanced Customization & Presets

**Branch:** `feature/phase-05-advanced-rules`

**What this means:** Basic checkboxes are useful, but some players want much stricter control. This phase adds more powerful rules while keeping the default interface simple.

**What we will do:**
- Distinguish **base strain** selection from **exact recipe** selection:
  - **Base strain:** Permit Granddaddy Purple and eligible discovered variants derived from that strain.
  - **Exact recipe:** Permit only the particular discovered Granddaddy Purple mix you picked.
- Add **allowlists** (“only these products”) and **blocklists** (“everything except these”).
- Add choices for **quality**, **effects**, and whether eligible stacks can be mixed.
- Add separate enable/disable switches for **player sales** and **dealer sales**, with an optional dealer override.
- Add readable presets such as **Anything Goes**, **Selected Products Only**, and **Vanilla Preferences**.
- Define a predictable order of rules so toggles do not contradict each other.
- Keep payment changes conservative; sales customization should not accidentally produce free money.

**In-game example:** You choose **Weed → Selected Only → Exact Recipe → Granddaddy Purple + your discovered effect combination**. Plain Granddaddy Purple and other mixes are now rejected if they are not the exact approved recipe. A different category, such as cocaine, can still be set to Any Unlocked.

**We call this done when:** Switching presets and combining filters always produces the expected result, and the menu explains *why* an item is accepted or rejected.

### Phase 06 — Settings, Save Files & Stability

**Branch:** `feature/phase-06-persistence`

**What this means:** If you spend time configuring rules, they must survive restarting the game. They also must not accidentally apply one character's unlocked products to another character or corrupt normal Schedule I saves.

**What we will do:**
- Store mod configuration **separately from vanilla game data**, with a profile for each active save.
- Load the right policy automatically when entering a save; avoid leaking unlocks/settings between saves.
- Save changes safely and include migrations so newer mod versions can read older settings.
- Treat saved selections that become unknown or locked as inactive rather than force-unlocking them.
- Handle the special case of changing settings while a customer/dealer appointment is already active.
- Include reset, diagnostics, and compatibility fallback behavior.
- Test repeated saves, reloads, updates, and crashes.

**In-game example:** Save A allows only Granddaddy Purple. Save B allows all unlocked weed. Opening Save B automatically loads **Save B's** rules, not Save A's. Restarting the game preserves both.

**We call this done when:** Both saves retain the right settings, no vanilla files are damaged, and config upgrades or unexpected shutdowns are recoverable.

### Phase 07 — Multiplayer & Game Version Compatibility

**Branch:** `feature/phase-07-multiplayer`

**What this means:** In co-op, multiple players might try to sell items or edit rules simultaneously. The mod needs a clear authority for decisions and must not let the same inventory be consumed or paid twice.

**What we will do:**
- Make the **host** authoritative for allowed products, order fulfillment, inventory changes, and payouts.
- Synchronize accepted policies and outcomes with joining players.
- Handle reconnects, simultaneous attempts, and dealer activity while co-op players are active.
- Test the supported Schedule I version and whichever Mono/IL2CPP setups we can actually build and run.
- Detect unsupported updates or missing hooks and disable unsafe functionality gracefully.
- Document which game versions, loader versions, and multiplayer arrangements were tested.

**In-game example:** One co-op player tries to deliver five excluded weed units. Their client cannot bypass the host's policy, so the deal is rejected without removing stock or paying money.

**We call this done when:** Co-op testing shows consistent order outcomes and inventory/money totals, and every claimed supported build has been tested. Unsupported combinations are explicitly documented.

### Phase 08 — Final Testing, Packaging & v1.0.0 Release

**Branch:** `feature/phase-08-release`

**What this means:** We stop adding features and focus on producing a stable mod others can install. A compiled DLL alone is **not** enough; the real in-game sales and settings must be tested.

**What we will do:**
- Run a full acceptance checklist covering the menu, unlock discovery, player sales, dealer sales, save/load, and multiplayer where supported.
- Fix crashes, broken patches, UI issues, and incorrect inventory or payment behavior.
- Add installation/removal instructions, configuration examples, a changelog, and known limitations.
- Package the supported mod DLL(s) and any required dependencies; verify the package installs cleanly.
- Add the approved license and release notes.
- Merge tested code to `release`, tag the approved release commit `v1.0.0`, and bring the exact approved code into `main`.

**In-game example:** Someone downloads v1.0.0, installs it, opens the Take It Or Leave It menu, chooses their permitted products, and both player and dealer transactions follow the selected rules without any manual file edits.

**We call this done when:** The test checklist passes on the documented game build, the release artifact is verified, and `main` matches the approved tagged release.

---

## 4. Mod menu — what the player can change

The full menu design lives in [`docs/MOD_MENU_SPEC.md`](docs/MOD_MENU_SPEC.md). Here is what each screen means in plain English.

| Screen | Purpose | Example |
| --- | --- | --- |
| **Overview** | Turn the mod on/off and see which rules are active | “Weed: Selected Only (2 products)” |
| **Products** | Choose which **unlocked** strains or recipes may be sold per category | Check Granddaddy Purple; uncheck OG Kush |
| **Customer Sales** | Choose whether customers ignore original strain/effect/quality requirements | Allow any approved mix for a 5-weed request |
| **Dealer Sales** | Control whether dealers use the same rules and combine stacks | Dealer can fulfill 5 weed with 2 + 3 eligible units |
| **Profiles & Presets** | Save, reset, and quickly swap rule configurations | Anything Goes → Granddaddy Purple Only |
| **Diagnostics** | See detected game version, active hooks, and errors | “Dealer patch unavailable on this build” |

### Product selection example: “Only Granddaddy Purple”

1. You unlock Granddaddy Purple through ordinary gameplay.
2. Open **Take It Or Leave It → Products → Weed**.
3. Choose **Selected Only**.
4. Check **Granddaddy Purple**, uncheck the other unlocked weed items, and save.
5. The menu shows Granddaddy Purple as the sole permitted base strain.
6. Personal handovers **and** dealer orders now accept only inventory matching that rule.
7. When Phase 05 is implemented, you can narrow that base-strain rule further to **a specific discovered recipe** if desired.

**No product unlock bypass:** The menu never gives you a strain or recipe you have not discovered. Unlocks affect which choices appear; an actual sale still requires real stock.

## 5. Important defaults and safeguards

These defaults favor the simple experience you described without creating exploits:

- **Global mod:** on (once installed and compatible).
- **Customer offers:** category + quantity, not a required special recipe.
- **Product rule:** Any Unlocked for each supported category until you choose otherwise.
- **Player sales:** use the active category rule.
- **Dealer sales:** use the same active category rule.
- **Mixed eligible stacks:** allowed where the game's inventory/contract system supports them safely.
- **Wrong drug category:** always rejected.
- **No eligible stock:** sale cannot be fulfilled; never generate free items.
- **Money/XP/commissions:** normal game handling; each sale pays exactly once.
- **Changing rules mid-order:** newly generated orders use the new rules; mod-generated active orders keep their original validation policy by default.
- **Compatibility issue:** disable the affected hook with a clear log message instead of risking save damage.

Where we cannot reliably implement a behavior in the installed game version, we will document the limitation rather than silently claiming support.

## 6. Required acceptance tests

Before any relevant phase is considered complete, we will verify scenarios like these:

1. **Unlocks:** OG Kush is unlocked; Granddaddy Purple is locked → Granddaddy Purple cannot be selected.
2. **New unlock:** After discovering Granddaddy Purple, it appears in the menu.
3. **Allowlist:** Only Granddaddy Purple selected → 5 Granddaddy Purple accepted, 5 OG Kush rejected.
4. **Mixed stacks:** Both strains selected → 2 + 3 allowed units satisfy a five-unit order.
5. **Cross-category:** Five weed can never complete an order for five cocaine.
6. **Exact recipe:** Selected effect recipe accepted; unselected variant of the same base strain rejected.
7. **Dealer:** Dealer consumes the correct eligible stock, honors restrictions, and receives correct payment.
8. **Insufficient inventory:** No appointment completes unless the required eligible quantity exists.
9. **Transactions:** Neither handovers nor dealer sales double-charge, double-pay, or double-consume.
10. **Save A / Save B:** Settings and unlocked products never leak between characters.
11. **Mid-contract changes:** Existing orders finish according to the documented rule, with no stuck inventory.
12. **Co-op:** The host rejects unauthorized sales; clients cannot bypass restrictions.
13. **Game update:** Missing/incompatible hooks fail safely with a readable diagnostic.

## 7. Release and Git workflow

```text
main                  stable versions only
  ↑
release               tested release candidates + version tags
  ↑
develop               completed features merged by PR
  ↑
feature/phase-XX-*     one isolated feature at a time
```

**For each phase:**
1. Create its `feature/phase-XX-name` branch **from `develop`**.
2. Implement the feature and add automated tests.
3. Create a pull request targeting `develop`.
4. Test in the actual game and record PASS/FAIL results.
5. Merge only after approval, then delete the merged feature branch.

**For a version release:** Freeze features, promote the tested code through `release`, create the version tag on the approved commit, then synchronize `main`. Only a tested build gets a release tag.

### What is deliberately *not* included

- Automatically unlocking new drugs/recipes.
- Creating products out of thin air or ignoring stock quantities.
- Accepting weed when a customer ordered cocaine (or another category).
- Modifying or wiping vanilla save progression.
- Promising compatibility with a game build we have not inspected and tested.

## 8. Proposed code layout

```text
src/
  TakeItOrLeaveIt.Core/         # what is allowed to sell, and why
  TakeItOrLeaveIt.Game/         # connects those rules to Schedule I
  TakeItOrLeaveIt.UI/           # in-game menu
  TakeItOrLeaveIt.Persistence/  # settings per save
  TakeItOrLeaveIt.Multiplayer/  # co-op/host coordination
tests/
  TakeItOrLeaveIt.Core.Tests/   # tests that do not require the running game
docs/
  MOD_MENU_SPEC.md              # detailed menu design
  PATCH_MAP.md                  # verified game hooks, written in Phase 00
```

The actual game-facing API calls, class names, and dependencies will be chosen only after Phase 00 inspection. S1API may help with in-game phone integration, but it is a candidate, **not yet a confirmed dependency**.

## 9. Project milestones

| Milestone | After phase | Meaning |
| --- | --- | --- |
| **Technical foundation** | 00 | Project compiles and loads, game hooks mapped |
| **Menu prototype** | 02 | You can configure unlocked-product restrictions in-game |
| **Playable prototype** | 03 | Your own sales work using those rules |
| **Single-player beta** | 04 | Your dealers follow the same rules |
| **Feature-complete beta** | 06 | Advanced policies and persistence are complete |
| **Release candidate** | 07 | Supported co-op/build compatibility has been checked |
| **Stable v1.0.0** | 08 | Tested package, documentation, and version tag published |

No dates are committed. We advance when a phase passes its acceptance tests, not simply because its code was written.

## References

- [Mod menu specification](docs/MOD_MENU_SPEC.md)
- [S1API documentation](https://ifbars.github.io/S1API/)
- [Schedule I modding / MelonLoader installation](https://s1modding.github.io/docs/modusers/install_melonloader/)

> **This document is the implementation plan, not a claim that these features already exist.** The first coding step is Phase 00.
