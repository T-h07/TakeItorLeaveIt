# Phase 05 testing: advanced rules, filters, quality, presets

> **Status: IMPLEMENTATION COMPLETE — PHYSICAL ACCEPTANCE NOT TESTED.** No scenario below has been run in game;
> every status is **NOT TESTED**. Phase 04 physical acceptance ([PHASE_04_TESTING.md](PHASE_04_TESTING.md)) is
> also still outstanding and is not changed by this document.
> Only mark PASS what you observed. Use FAIL (wrong result), BLOCKED (could not set it up) or NOT TESTED.

Player guide: [ADVANCED_RULES.md](ADVANCED_RULES.md). Design and evidence: [PHASE_05_ANALYSIS.md](PHASE_05_ANALYSIS.md).

## 1. Before you start

1. **Back up the save folder** and the mod settings folder `<Schedule I>/UserData/TakeItOrLeaveIt/`.
   Use a save with: at least one base Weed strain **and a mix made from it** (and ideally a mix of that mix), a
   second base strain, at least one hired dealer with customers, product of at least two quality tiers, and one
   other category (Meth or Cocaine).
2. Install the Phase 05 build exactly as in Phase 04 (replace `Mods/TakeItOrLeaveIt.Il2Cpp.dll` and
   `UserLibs/TakeItOrLeaveIt.Core.dll`).
3. Launch, load the backup copy and check `MelonLoader/Latest.log`: the Phase 04 lines, including
   `Harmony patches active: 13 method(s) across 3 module(s).` (Phase 05 adds no game hook), and
   `Phase 05: player sales hooks active; dealer sales hooks active.`
4. Phone → **Take It** → **Diagnostics** → *Rule engine (Phase 05)*: note the *Lineage:* line (how many products
   have a recorded lineage). If it says many are unknown, base-strain scenarios become BLOCKED: send the log.

Default settings for each scenario unless it says otherwise: press **Reset to defaults** (Settings) and **Save**
first. Write down cash, XP and the involved inventories before each scenario.

**Evidence** for every row: the `Latest.log` lines it names (or a screenshot of the phone/handover screen), plus
before/after cash or inventory where money or items move.

## 2. Scenarios

### Area 1 — Install and startup (IN)

| # | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| IN01 | Phase 05 build installed | Launch, load a save | Phase 04 startup lines; 13 patched methods; no `failed` line | Log excerpt | NOT TESTED |
| IN02 | Loaded save | Open Take It | Nav shows Overview, Products, Rules, Sales, Presets, Settings, Diagnostics | Screenshot | NOT TESTED |
| IN03 | Loaded save | Diagnostics | *Rule engine (Phase 05)* lines with schema 2, preset, scopes, lineage count | Screenshot | NOT TESTED |

### Area 2 — Settings migration (MG)

| # | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| MG01 | A Phase 04 settings file with Weed = Selected Only (two products ticked), mixed OFF | Load the save, open Products | Same mode and ticks; Rules screen shows *Exact recipe*; Sales shows *One product per order* | Screenshot | NOT TESTED |
| MG02 | After MG01 | Change nothing, accept a Weed order | Same products permitted as in Phase 04 (mixes of ticked strains NOT added) | `order created` log line | NOT TESTED |
| MG03 | After MG01 | Change any setting, Save | `<save id>.json.schema1.bak` appears next to the settings file and equals the old file; the new file says `"schemaVersion": 2` | File listing, both files | NOT TESTED |
| MG04 | After MG03 | Save again | The `.schema1.bak` file is not changed | File timestamps | NOT TESTED |

### Area 3 — Exact recipe allowlist (EX)

| # | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| EX01 | Weed = Selected Only, *Exact recipe*, only the base strain ticked | Accept a Weed order, offer a mix of that strain | Refused before transfer: *This recipe isn't on your allowed list.* | Handover screenshot, log | NOT TESTED |
| EX02 | Same | Offer the base strain | Accepted, normal payment | Cash before/after | NOT TESTED |
| EX03 | Only the mix ticked | Offer the base strain | Refused | Handover screenshot | NOT TESTED |

### Area 4 — Base strain lineage (BS)

| # | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| BS01 | Any settings | Products → Weed | Mix rows show *from <base strain>*; base strains show *base strain* | Screenshot | NOT TESTED |
| BS02 | Weed = Selected Only, *Base strain*, base strain ticked | Products list | The base strain, its mixes and mixes of mixes show *Permitted*; other lineages *Not selected* | Screenshot | NOT TESTED |
| BS03 | Same | Accept a Weed order, offer a mix of a mix | Accepted | Log, cash | NOT TESTED |
| BS04 | Same | Offer a product of another lineage | Refused: *This product isn't from a strain on your Marijuana allowed list.* (the game's category name) | Handover screenshot | NOT TESTED |
| BS05 | A mix with a name similar to a base strain but not made from it (if available) | Check its row | Lineage follows the recipe, not the name | Screenshot | NOT TESTED |

### Area 5 — Blocklist (BL)

| # | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| BL01 | Weed = Blocklist, *Exact recipe*, one product blocked | Accept a Weed order, offer the blocked product | Refused: *This product is blocked by your Marijuana rules.* | Handover screenshot | NOT TESTED |
| BL02 | Same | Offer another Weed | Accepted | Cash | NOT TESTED |
| BL03 | Blocklist, *Base strain*, base strain blocked | Offer a mix of it | Refused (blocked) | Handover screenshot | NOT TESTED |
| BL04 | After BL01 | Switch to Selected Only and back | Both lists are still there | Screenshots | NOT TESTED |

### Area 6 — Unknown lineage (UA)

| # | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| UA01 | Diagnostics shows some products with unknown lineage (otherwise BLOCKED) | Products → that product | Row says *lineage unknown* | Screenshot | NOT TESTED |
| UA02 | Blocklist by base strain with anything blocked | Check that product | *Lineage unknown*, not permitted | Screenshot | NOT TESTED |

### Area 7 — Quality, player sales (QP)

| # | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| QP01 | Weed minimum *Premium+* | Accept a Weed order, offer Standard items | Refused: *The item quality is below your minimum (Premium or better).* Nothing leaves your inventory | Handover screenshot, inventory | NOT TESTED |
| QP02 | Same | Offer Premium or Heavenly items | Accepted; payment as vanilla for that quality | Cash | NOT TESTED |
| QP03 | Minimum *Any* | Offer low-quality items to a demanding customer | Same customer reaction as without the mod (quality still counts) | Cash, customer reaction | NOT TESTED |

### Area 8 — Quality, dealers (QD)

| # | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| QD01 | Weed minimum *Premium+*; dealer holds only Standard Weed | Wait for a dealer appointment | `Dealer appointment created ... Premium or better ...: Waiting` (nothing reserved); dealer texts that permitted units are missing | Log, phone | NOT TESTED |
| QD02 | Give the dealer Premium Weed | Wait for the handover | Premium units handed over; Standard units stay in the dealer's inventory | Dealer inventory before/after | NOT TESTED |
| QD03 | Minimum *Any*, customer requires Premium | Dealer holds Standard + Premium | Only Premium units are reserved and handed over | Log reservation lines | NOT TESTED |

### Area 9 — Effects (EF)

| # | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| EF01 | Rules → Weed: one effect *Required*, *Require all* | Products list | Only products with that effect are *Permitted*; others *Missing effect* | Screenshot | NOT TESTED |
| EF02 | One effect *Excluded* | Accept a Weed order, offer a product with it | Refused: *This product has an effect your rules exclude.* | Handover screenshot | NOT TESTED |
| EF03 | *Require any* with two effects | Products list | Products with either effect are permitted | Screenshot | NOT TESTED |
| EF04 | Make a rule that permits nothing (e.g. require an effect no discovered Weed has) | Overview / Products | Orange warning; Weed orders stay vanilla (`CategoryNotEligible`) | Screenshot, log | NOT TESTED |

### Area 10 — Mixing modes (MX)

| # | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| MX01 | Mixing *Any permitted* | Deliver 3 of one Weed + 2 of another for 5 Weed | Accepted | Cash | NOT TESTED |
| MX02 | *One product per order* | Same delivery | Refused: *Mixed deliveries are disabled for this order: use one product only.* | Handover screenshot | NOT TESTED |
| MX03 | *One base strain per order* | Deliver a base strain + its mix | Accepted | Cash | NOT TESTED |
| MX04 | *One base strain per order* | Deliver products of two lineages | Refused: *limited to one base strain* | Handover screenshot | NOT TESTED |
| MX05 | *Requested product only* | Offer a different permitted product | Refused: *This order only accepts the product the customer asked for.* | Handover screenshot | NOT TESTED |
| MX06 | Any mode, Sour Diesel priced above OG Kush | Accept a Sour Diesel order, offer OG Kush | Refused (fair price) in every mixing mode | Handover screenshot | NOT TESTED |

### Area 11 — Player and dealer switches (SW)

| # | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| SW01 | Player sales OFF, Weed = Selected Only (one product) | Accept a player Weed order | `uses vanilla rules: SalesPathDisabled`; vanilla behaviour and offers | Log | NOT TESTED |
| SW02 | Same | A dealer appointment | Managed as usual (`Dealer appointment created`) | Log | NOT TESTED |
| SW03 | Dealer sales OFF | A dealer appointment | `Dealer contract ... uses vanilla rules: SalesPathDisabled`; no out-of-stock texts | Log, phone | NOT TESTED |
| SW04 | A managed order accepted, then switch its path OFF and Save | Complete the order | It keeps its accepted rules; dealer reservation kept | Log | NOT TESTED |

### Area 12 — Dealer overrides (DO)

| # | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| DO01 | Sales screen after dealers have ticked | Look at the dealer list | Hired dealers listed by name with *uses the global rules* | Screenshot | NOT TESTED |
| DO02 | Global Weed = Selected Only (A); *Edit overrides* for dealer X, Weed = Any Unlocked; Save | X's Weed appointment; another dealer's appointment | X may use any Weed; the other dealer only A | Log (allowed product counts) | NOT TESTED |
| DO03 | After DO02 | Rules → *Use global rule* for Weed, Save | X back to A only | Log | NOT TESTED |
| DO04 | Overrides for X, dealer sales OFF | X's appointment | Vanilla (switch wins over the override) | Log | NOT TESTED |

### Area 13 — Presets (PS)

| # | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| PS01 | Defaults | Presets screen | *Current rules: Anything Goes* | Screenshot | NOT TESTED |
| PS02 | Any | *Preview changes* on Vanilla Preferences | A list of changes and a warning; nothing applied yet | Screenshot | NOT TESTED |
| PS03 | After PS02 | *Apply to draft*, then Discard | Draft back to the saved rules | Screenshot | NOT TESTED |
| PS04 | Weed list with one product, Meth list empty | Apply *Selected Products Only*, Save | Weed uses its list; preview warned that Meth has nothing listed | Screenshots | NOT TESTED |
| PS05 | After a preset | Change one rule | Label shows *Custom* | Screenshot | NOT TESTED |
| PS06 | Vanilla Preferences saved | Reload the save | Still *Vanilla Preferences*; orders vanilla | Screenshot, log | NOT TESTED |

### Area 14 — Customer offers (OF5)

| # | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| OF501 | Weed Blocklist with one product blocked | Wait for player offers | The blocked product is never offered | Phone texts | NOT TESTED |
| OF502 | Player sales OFF | Wait for player offers | Vanilla offers (filter counters do not grow for player offers) | Log | NOT TESTED |
| OF503 | Dealer X override narrows Weed to one product | Wait for X's customers' offers | Only that product (this also confirms that the offer hook's dealer argument identifies the dealer, PROBABLE) | Log, dealer app | NOT TESTED |

### Area 15 — Reservations and restock texts (RS)

| # | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| RS01 | Minimum *Standard+*; dealer holds Poor + Standard Weed | Two appointments | Reservations only count Standard units; never more than held | Log reservation lines | NOT TESTED |
| RS02 | After RS01 | Take the Standard units away | Affected appointment invalidated and waits; shortage text | Log, phone | NOT TESTED |
| RS03 | Dealer holds only blocked Weed | Wait | Out-of-Weed text (if it had permitted Weed before) | Phone | NOT TESTED |

### Area 16 — Phone UI layout (UI)

| # | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| UI01 | Smallest window you use | Visit every screen | `Layout check <screen> ...: OK` for every screen; nothing clipped or overlapping | Log lines, screenshots | NOT TESTED |
| UI02 | Products, Blocklist mode | Header | Four mode buttons visible; *Block all unlocked* / *Block shown* labels | Screenshot | NOT TESTED |
| UI03 | Rules screen with many effects | Scroll | List scrolls; buttons usable | Screenshot | NOT TESTED |
| UI04 | Search field focused | Type | Typing does not move the player (Phase 02 behaviour) | Observation | NOT TESTED |

### Area 17 — Regression (RG5)

| # | Setup | Action | Expected | Evidence | Status |
| --- | --- | --- | --- | --- | --- |
| RG501 | Defaults | Run Phase 03 player sale and Phase 04 dealer appointment basics | Same results as described in PHASE_03/04 testing | Log, cash | NOT TESTED |
| RG502 | Defaults | Complete one sale | Paid once; XP once; no cash or XP from the mod | Cash, XP before/after | NOT TESTED |
| RG503 | Any | Save, quit, reload mid-order | Accepted order keeps its rules (mixing, minimum quality) | Log | NOT TESTED |
| RG504 | Mod OFF | Any sale | Fully vanilla | Log | NOT TESTED |

## 3. Status summary

| Area | Scenarios | Status |
| --- | --- | --- |
| 1 IN | 3 | NOT TESTED |
| 2 MG | 4 | NOT TESTED |
| 3 EX | 3 | NOT TESTED |
| 4 BS | 5 | NOT TESTED |
| 5 BL | 4 | NOT TESTED |
| 6 UA | 2 | NOT TESTED |
| 7 QP | 3 | NOT TESTED |
| 8 QD | 3 | NOT TESTED |
| 9 EF | 4 | NOT TESTED |
| 10 MX | 6 | NOT TESTED |
| 11 SW | 4 | NOT TESTED |
| 12 DO | 4 | NOT TESTED |
| 13 PS | 6 | NOT TESTED |
| 14 OF5 | 3 | NOT TESTED |
| 15 RS | 3 | NOT TESTED |
| 16 UI | 4 | NOT TESTED |
| 17 RG5 | 4 | NOT TESTED |

## 4. Automated tests (development PC)

```powershell
dotnet test tests/TakeItOrLeaveIt.Core.Tests -c Release
```

Phase 05 suites (Core, no game needed): `ProductSelectionTests` (A), `QualityEffectRuleTests` (B),
`SalesScopeTests` (C), `DealerQualityLotTests` (D), `SettingsMigrationTests` (E), `AdvancedRuleStressTests` (F,
seeded randomized runs over rules, mixing, quality, overrides and stock), `PresetAndMenuRuleTests` (G).
Ancestry in these tests comes from fixture parents only; the game's recipe list is checked by BS01–BS05.

## 5. Evidence to send back

`MelonLoader/Latest.log`, the settings folder listing (with the `.bak` file after MG03), screenshots named by
scenario id, and this table with statuses filled in.
