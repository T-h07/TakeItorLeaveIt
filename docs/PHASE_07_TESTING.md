# Phase 07 — Multiplayer test plan (to run in Phase 08)

> **Every scenario below is NOT TESTED.** Physical in-game and real co-op testing is deferred to Phase 08 by project
> policy. Phase 07 is verified only by builds, call-graph analysis and automated **simulation** tests (section 1).
> Simulated network results are **not** multiplayer verification. Nothing here may be marked PASS without in-game
> evidence from two real machines (log excerpts from host and client, screenshots, inventory and cash before/after).

Build under test: the Phase 07 IL2CPP package (`Mods/TakeItOrLeaveIt.Il2Cpp.dll` + `UserLibs/TakeItOrLeaveIt.Core.dll`)
on Schedule I v0.4.7f12 IL2CPP, MelonLoader 0.7.3, S1API 3.2.3, on **both** machines unless the row says otherwise.
Logs: `<game>/MelonLoader/Latest.log`. The phone app's **Diagnostics** screen starts with "Session and sales (Phase 07)".

**Before starting:** back up both machines' `UserData/TakeItOrLeaveIt` folders and the host's save. For every sale
row, write down the inventory counts and cash of **every** player and every involved dealer before and after.

Status values: PASS / FAIL / BLOCKED / NOT TESTED.

## 1. Automated coverage (simulation only)

| Suite | File | Covers |
| --- | --- | --- |
| A01–A14 | `MultiplayerAuthorityTests.cs` | Session states, capabilities, fail-closed reads, legacy gate |
| B01–B24 | `PolicySyncTests.cs` | Codec, protocol, hash, size limit, revisions, epochs, handshake |
| C01–C16 | `CoopPlayerSalesTests.cs` | Host and client handovers, single settlement, no client orders |
| D01–D12 | `CoopDealerTests.cs` | Host-only dealers, lot check by product and tier, idempotent completion |
| E01–E16 | `NetworkFaultTests.cs` | Latency, loss, duplicates, replays, forged values, disconnect, save switch, heartbeat |
| F01–F15 | `CompatibilityModeTests.cs` | Modes A–E, read-only menu, game build classification |
| S01–S03 ×12 seeds | `MultiplayerStressTests.cs` | Invariants under every fault at once |

## SA — Session and authority

| ID | Setup | Host actions | Client actions | Expected | Log evidence | Inventory & payment verification | Status |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SA01 | Host alone | Load a save | — | Diagnostics "Session: SinglePlayerHost"; sales work as in Phase 03–06 | `Take It session: SinglePlayerHost` | n/a | NOT TESTED |
| SA02 | SA01, invite open | Wait for the client to join | Join | Host becomes MultiplayerHostReady | `Take It session: MultiplayerHostReady`; `Take It rules shared with clients (revision 1).` | n/a | NOT TESTED |
| SA03 | SA02 | — | Open the Take It app | Status bar "Host controls Take It settings."; Overview shows the host's rules | `Joined another player's game…`; `Host rules: …` | n/a | NOT TESTED |
| SA04 | SA03 | — | Try to change a rule, Save, Discard, Reset | All refused; nothing written | Client: no file write lines | Client `UserData/TakeItOrLeaveIt/saves/` unchanged (listing before/after) | NOT TESTED |
| SA05 | SA02 | Change a rule and Save | Watch Overview | Client shows the new rules within a few seconds (revision 2) | Host: `revision 2`; client: `Host rules: …` | n/a | NOT TESTED |
| SA06 | SA05 | Change a rule, do **not** Save | Watch Overview | Client keeps the saved rules (drafts are never shared) | No new revision | n/a | NOT TESTED |

## CP — Player sales in co-op

| ID | Setup | Host actions | Client actions | Expected | Log evidence | Inventory & payment verification | Status |
| --- | --- | --- | --- | --- | --- | --- | --- |
| CP01 | Weed = GDP, OG Kush | Accept a weed order, hand over OG Kush instead of the asked strain | — | Accepted as a category order | Category order + `Committed` | Host −N units; cash + payment once; XP once | NOT TESTED |
| CP02 | CP01 setup | Accept a weed order | Hand it over with a **different** permitted strain | Refused by the game (exact-product rule on the client) | Client: no category order lines | Nothing moves; no payment | NOT TESTED |
| CP03 | CP01 setup | Accept a weed order | Hand over the exact product the customer asked for | Accepted by the game; paid once | Host: snapshot released on completion | Client −N; host cash/XP as vanilla, once | NOT TESTED |
| CP04 | Weed = GDP only | Watch several customer offers | Watch offers to the client | Only GDP is requested | Offer filter lines on the host | n/a | NOT TESTED |
| CP05 | Excluded Sour Diesel in client inventory | — | Try to hand over Sour Diesel for a GDP order | Refused; nothing removed | — | Client inventory unchanged; no payment | NOT TESTED |
| CP06 | One active order | Open the handover screen | Open the handover screen for the same customer at the same time | The contract completes once | One `Committed` | Exactly one payment, one XP award | NOT TESTED |
| CP07 | Host has an active category order | Change the rules and Save | — | The accepted order keeps its snapshot rules | Snapshot unchanged | n/a | NOT TESTED |

## CD — Dealers in co-op

| ID | Setup | Host actions | Client actions | Expected | Log evidence | Inventory & payment verification | Status |
| --- | --- | --- | --- | --- | --- | --- | --- |
| CD01 | Host's dealer with GDP and OG Kush | Let a dealer appointment run | Watch | Dealer fills the category order from permitted stock | `removed N unit(s)`; reservation released | Dealer −N; dealer cut and host cash once | NOT TESTED |
| CD02 | Dealer stock below minimum quality | Let an appointment run | — | Waits / hands over nothing | Short-appointment line | Nothing moves | NOT TESTED |
| CD03 | Dealer short of permitted stock | Let it run | — | Restock text on the **host's** phone only | Restock message line (host) | n/a | NOT TESTED |
| CD04 | Client hired a dealer (if the game allows) | — | Let an appointment run | Managed on the host like any dealer | Host log lines only | Dealer and cash totals as vanilla | NOT TESTED |
| CD05 | Appointment in progress | Take stock from the dealer during the walk | — | Replanned or waits; never sells an excluded product | Reconcile line | Dealer stock consistent | NOT TESTED |

## NF — Join, leave and faults

| ID | Setup | Host actions | Client actions | Expected | Log evidence | Inventory & payment verification | Status |
| --- | --- | --- | --- | --- | --- | --- | --- |
| NF01 | Host has changed rules several times | — | Join in progress | Client shows the latest revision | `Host rules: …` with the host's revision | n/a | NOT TESTED |
| NF02 | SA03 | — | Leave the game | Client menu returns to the local rules; nothing written | `Left the host's game: host rules dropped.` | Client files unchanged | NOT TESTED |
| NF03 | SA03 | Quit to menu | — | Client shows "Disconnected"; no host migration | `NetworkDisconnected` | n/a | NOT TESTED |
| NF04 | SA03 | Quit to menu and load another save, re-host | Rejoin | New session; client shows the new save's rules | New revision 1 in a new session | n/a | NOT TESTED |
| NF05 | Client mid-handover | — | Disconnect during the handover | Game decides; at most one settlement | — | No duplicate items or cash on either side | NOT TESTED |
| NF06 | SA03 for 5 minutes | Idle | Idle | Heartbeat every 30 s; no new revisions | No repeated warning lines | n/a | NOT TESTED |

## CM — Compatibility modes

| ID | Setup | Host actions | Client actions | Expected | Log evidence | Inventory & payment verification | Status |
| --- | --- | --- | --- | --- | --- | --- | --- |
| CM01 (A) | Both modded, same version | Host | Join | As SA02–SA03 | — | n/a | NOT TESTED |
| CM02 (B) | Host modded, client vanilla | Run CP01, CD01 | Play normally | Host features work; client vanilla | Host lines only | As CP01 | NOT TESTED |
| CM03 (C) | Host vanilla, client modded | Host | Join, wait 30 s | "The host does not share Take It rules. Vanilla rules apply." | `No Take It rules from the host…` | Client sales vanilla | NOT TESTED |
| CM04 (D) | Different Take It versions | Host | Join | Same protocol: rules + note; other protocol: no rules, "update" message | Diagnostics lines | n/a | NOT TESTED |
| CM05 (E) | Different game builds (if obtainable) | Host | Join | Diagnostics "Not a supported combination" | Diagnostics | n/a | NOT TESTED |
| CM06 | Any untested game build | Start the game | — | Startup warning "not a tested build"; hooks checked; vanilla if one is missing | `Compatibility: SupportedWithWarnings; game build: NotTested.` | n/a | NOT TESTED |

## PE — Persistence in co-op

| ID | Setup | Host actions | Client actions | Expected | Log evidence | Inventory & payment verification | Status |
| --- | --- | --- | --- | --- | --- | --- | --- |
| PE01 | Active orders and appointments | Save, quit, reload, re-host | Rejoin | Host restores its snapshots; client writes nothing | Host restore lines | Totals unchanged | NOT TESTED |
| PE02 | Client has its own single-player save with custom rules | — | Join a host, leave, load the own save | Own rules unchanged | — | Client profile file identical (hash before/after) | NOT TESTED |
