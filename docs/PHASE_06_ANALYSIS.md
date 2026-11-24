# Phase 06 — Persistence, recovery and stability: analysis

> **Status: implemented and covered by automated tests only. NOT VERIFIED IN GAME.** Physical acceptance testing is
> deferred to Phase 08 by project policy. Every statement about game behaviour below is marked with the evidence it
> rests on; anything that can only be confirmed in game is listed in §9.

Branch `feature/phase-06-persistence`, based on the Phase 05 merge `33f2e48`. Baseline before any change: 543 Core
tests passing.

## 1. Architecture map (before Phase 06)

| File (per save, under `UserData/TakeItOrLeaveIt/saves/`) | Owner | Written by | Thread | When |
| --- | --- | --- | --- | --- |
| `<save-id>.json` (settings) | `JsonPolicyStore` | `ModMenuController.Save` | main (phone **Save** button) | only on an explicit Save |
| `<save-id>.orders.json` | `ActiveOrderStore` | `PlayerSalesHost.Persist` | thread pool (`Task.ContinueWith`); main thread on save switch / quit | when the order book changes |
| `<save-id>.dealer-orders.json` | `DealerOrderStore` (via `ActiveOrderStore`) | same | same | when appointments change |
| `<save-id>.dealer-alerts.json` | `DealerAlertStore` (via `ActiveOrderStore`) | same | same | when message dedup state changes |

Read paths: `ModMenuController.SwitchSession` (settings) and `PlayerSalesHost.OnCatalogState` (the three runtime files),
both on the main thread when `ProductCatalogService` raises `StateChanged`.

Save identity: `SaveIdentity.FromFolderPath(LoadManager.LoadedGameFolderPath)` → `save-` + the first 12 hex digits
(48 bits) of SHA-256 over the lower-cased, `/`-normalised path (`GameSaveProbe.cs:42`,
`SaveLifecycleCoordinator.cs`). Sessions: `ProductCatalogService.BeginSave` assigns an increasing `SaveSession.Id`;
results for an older session are discarded (`RefreshOutcome.StaleDiscarded`).

Nothing is written into the game's own save folders (paths are built only from `MelonEnvironment.UserDataDirectory`;
`TakeItOrLeaveItMod.CreateMenu` / `CreatePlayerSales`).

## 2. What was already correct (kept)

- Temporary file + `File.Replace`/`File.Move`, `FileStream.Flush(true)` on the temporary file.
- Settings are written only on an explicit Save and "Settings saved" is shown only after the write returned.
- Unreadable settings keep mod sales off (`PolicyReadyForSales` false → vanilla), never defaults-as-permission.
- Identity tokens are validated before any path is built (no traversal: hash output only).
- Order snapshots are taken once at acceptance and restored from the file, never rebuilt from current rules.
- Reservations are not stored; they are rebuilt from live dealer stock (`DealerSaleCoordinator.BindSave` clears the ledger).
- Discovery results of an older session are discarded.
- Schema 1 → 2 migration keeps permissions and keeps a one-time `.schema1.bak` (Phase 05).

## 3. Weaknesses found (evidence) and what Phase 06 changed

| # | Weakness (evidence in the Phase 05 code) | Effect | Change |
| --- | --- | --- | --- |
| W1 | `PlayerSalesHost.Persist` used **one sequence number for all saves** and skipped a queued write when a newer one existed (`sequence != _writeSequence`). | Save A's last queued write was silently skipped once Save B queued anything: A's file lost its newest orders. | `PersistenceQueue`: one pending document per *save + file*, monotonic generations per key; A's write still goes to A's file (test P06_C03). |
| W2 | `MarkPersisted()` ran before the write; on failure the message said "retried on the next change" but nothing retried; quitting did not write it either. | A failed write could be lost permanently. | Documents stay queued until written; retries with back-off (1, 2, 5, 15, 30 s); errors in log and Diagnostics (P06_B04, P06_C09). |
| W3 | `Shutdown` did not wait for the thread-pool chain. `OnDeinitializeMelon` called `RemoveAll()` first, unguarded. | Last writes could be cut off; an unpatch exception skipped them. | `Shutdown` flushes with a 3 s bound; each deinit step guarded. |
| W4 | Order/dealer/alert files of a **newer schema** loaded as "no entries" and the next write replaced them with schema 1. | Downgrade and data loss after running an older build. | Newer files are read-only for the session; `SafeDocumentFile` refuses to replace them (P06_A03, P06_C04). |
| W5 | Unreadable order/dealer/alert files were overwritten on the next write without a copy. | Damaged bytes lost (no forensic or manual recovery). | Kept as `<file>.corrupt-<UTC>` before replacement, max 3 (P06_A04, P06_C05). |
| W6 | Unreadable settings were copied to `.unreadable.bak`, **overwritten** by every later failure. | Only the newest damage kept. | Timestamped corrupt copies (bounded); old `.unreadable.bak` files are left untouched. |
| W7 | Settings reader dropped wrongly typed values: `blockedProducts:[5]` → item skipped, `minQuality:3` / `mode:1` / `effects:"x"` → treated as missing, category body not an object → default mode. | A malformed field could **loosen** a rule (blocked product permitted, minimum dropped, Any Unlocked). | Wrong type → category Disabled (`DamagedField`), non-string enums → invalid value → Disabled; structural damage → file unreadable (P06_D06, P06_D07). |
| W8 | An invalid key in a **blocklist** was dropped with an issue. | Dropped blocked product = permitted. | Blocklist read incompletely → category Disabled. |
| W9 | `GameDealers` cached dealer ids by **native pointer** and the seen-dealer list for the whole game run; `Reset()` ran only at quit. | After a reload a reused pointer could map to the previous dealer's id (overrides, reservations of the wrong dealer); the menu listed the previous save's dealers. | `Reset()` on every save session change (`PlayerSalesHost.SessionChanged`). |
| W10 | The sales host rebound only when the save **identity** changed. | Reloading the same save kept in-memory state from before the reload. | Rebinds on every new session id; flushes first; keeps memory only if its own writes are still pending. |
| W11 | No document said which save it belonged to. A copied/renamed file loaded into another profile; no recovery candidate could be verified. | Cross-save leakage through copied files; unsafe restore. | Optional `save` field (no schema bump; older readers ignore it). A mismatch = unreadable; backups are used only when they verify for the same save (P06_C06, P06_D03, P06_D04). |
| W12 | No last-known-good copy for any file. | Nothing to recover from. | `<file>.bak` = the previous valid document, refreshed on each successful write (never from a damaged file). |
| W13 | A load I/O error (file locked) was reported like damage and the next save could replace it. | A healthy but locked file could be replaced. | Read failures never classify as damage; the writer refuses to replace a file it cannot read (P06_A08, P06_D05). |
| W14 | Serialisation order of lists followed edit order. | Non-deterministic text, noisy diffs. | Lists sorted (ordinal) on write; a re-save is byte-identical (P06_D10). |

Things checked and found safe: settings are written synchronously on the main thread only (no overlap); the
runtime files had no cross-file torn state that matters (each file is independent); a delayed discovery result from
Save A is already discarded after a switch; `Persist` already refused to write without a bound identity.

## 4. Write path (all four files): `SafeDocumentFile.Write`

1. The new document must classify as valid for this save (it is parsed back before anything is touched).
2. The current file is inspected: **newer schema → refuse** (`NewerSchemaException`); **damaged → copy to
   `.corrupt-<yyyyMMddTHHmmssfffZ>`** (counter on a clash, oldest beyond 3 deleted); **valid → becomes `.bak`**;
   **cannot be read → throw** (nothing is replaced).
3. Write `<file>.tmp` with `FileShare.None` and `Flush(true)`; read it back and compare.
4. Copy the current valid file to `.bak`, then `File.Replace(tmp, file)` (or `File.Move` when there is no file).
5. Any exception: the temporary file is deleted (best effort) and the exception propagates; the destination is untouched.

**Durability limits (not overstated):** `File.Replace` is a single rename on the same NTFS volume, so readers see
either the old or the new file. `Flush(true)` asks Windows to write the temporary file's data through the cache, but
the directory entry of the rename is not flushed (no portable API on Windows), and disks/controllers may still lie
about write caching. After a power loss the old file, the new file, or (rarely) a zero-length file is possible; the
reader then treats a damaged file as described in §6. A leftover `.tmp` from a hard crash is ignored on load and
overwritten by the next write.

## 5. Write ordering and threads

- Main thread: settings writes (synchronous, user action), serialisation of the runtime books (they are not
  thread-safe), all reads, flushes on save switch and quit.
- One background runner (`Task.Run`, started only when none is active): `PersistenceQueue.RunPending`. It only
  writes strings to files; it never touches Unity or game objects, and it never logs (messages are queued and logged
  by the main thread).
- `PersistenceQueue`: per key `<save-id>|<file>` at most one pending document; `Submit` refuses a generation that is
  not newer than the pending or committed one; one runner at a time (`Monitor` on an I/O gate), so no two writes to
  the same file overlap and a stale snapshot can never be written after a newer one (P06_B01–B03, randomised
  concurrent stress P06_B07).
- Save switch: queue the outgoing save's state → `Flush(2 s)` → reset game-side caches → bind the new save (load its
  files). Anything still pending keeps retrying in the background and can only ever reach the outgoing save's files.
- Quit: queue → `Flush(3 s)`; whatever is still pending is reported; previous files are intact.

## 6. Recovery policy

| File state at load | Settings | Orders / appointments | Alert history |
| --- | --- | --- | --- |
| missing | defaults (documented), sales on | none | empty |
| valid | loaded | loaded | loaded |
| valid, other save (`save` mismatch) | treated as damaged | treated as damaged | treated as damaged |
| damaged | mod sales **off**; file kept; if `.bak` verifies (readable, supported schema, same `save`) it is shown as **unsaved changes** to review and Save; otherwise defaults are shown and nothing is applied | entries not used → those contracts use vanilla rules (never rebuilt from current rules); file kept and copied before it is next replaced | `.bak` used if it verifies (older history can at worst repeat one text; none at all could repeat every text) |
| cannot be read (locked) | mod sales off; nothing offered; never replaced | same as damaged for this session, never replaced | empty for the session, never replaced |
| newer schema | mod sales off; Save refused; file untouched | not used; never written this session | not used; never written this session |

Nothing is ever restored automatically into the active settings: the user confirms by pressing Save. A recovered
draft is labelled in the feedback line and on the Diagnostics screen ("Save data (Phase 06)").

## 7. Schema compatibility (settings)

Schema stays **2**; orders, appointments and alerts stay **1**. The only format change is the optional `save` field,
which older readers ignore, so no version bump is justified.

| Field condition | Rule |
| --- | --- |
| missing, optional | documented default (`enabled`, `playerSales`, `dealerSales`, `mixedDeliveries` = true; `match` = exact; no quality/effect rule; mixing from `mixedDeliveries`) |
| invalid value (unknown mode/match/quality/mixing/effect) | category Disabled (mixing: one product per order) — unchanged from Phase 05 |
| wrong JSON type inside a category (lists, quality, effects, mode, match) | category Disabled (`DamagedField`) |
| wrong JSON type for a global switch, `save`, `categories`, a category body, `dealers` or a dealer body | whole file damaged (§6) |
| invalid key in a blocklist | category Disabled (an invalid allowlist key only narrows and is dropped with an issue) |
| legacy (schema 1) | migrated with the same permissions; exact-recipe lists; one-time `.schema1.bak`, never overwritten; repeat loads/saves are idempotent (P06_D11) |
| unknown field | loaded; reported in the log with its JSON path; **not kept** on the next Save (the previous file is kept as `.bak`). Preserving unknown fields would require carrying them through `PolicyConfiguration`; deferred as not needed while no other writer exists. |
| newer schema | §6 (never rewritten, downgraded or trimmed) |

## 8. Product and dealer references

- Saved product ids are kept verbatim; an id not discovered in the current save is never permitted, never matched by
  name, and stays in the file across unrelated edits; it becomes active again when the product is discovered
  (P06_E04). Settings are never written because discovery failed or is incomplete (Save requires a ready catalog;
  P06_E03).
- Each save has its own settings, orders, appointments and alert history (P06_E01, P06_E02, P06_C03).
- Dealer overrides are keyed by the dealer's NPC `ID` (`GameDealers.DealerId` → `dealer.ID`, a stable game id), never
  by display name. An unknown dealer gets the global rules only (P06_E05). Overrides of dealers that are not hired
  or not seen are kept (non-destructive, shown as inactive) and apply again if the same id reappears; *Remove
  overrides* is the only way they are deleted.
- Active dealer appointments keep their acceptance-time snapshot; restock messages use `ForScope(Dealer(id))` of the
  current rules (Phase 05, unchanged).

## 9. Save identity notes and assumptions for Phase 08

- **Collisions:** 48 bits of SHA-256; the chance that two of n saves collide is about n²/2⁴⁹ (≈ 10⁻¹² for 20 saves).
  Accepted and documented; a collision would share one profile and is not detectable without storing raw paths, which
  we deliberately do not do.
- **Moving or renaming a save folder** gives it a new identity and therefore a new, empty profile; the old profile
  stays on disk untouched. Copying a profile's files to the new identity is not possible by hand any more (the `save`
  field would not match) — intended, as it prevents cross-save leakage.
- **No identity** (path unreadable): nothing is loaded or written; there is no fallback profile (P06_G03).

To confirm in Phase 08 (cannot be verified without the game):

1. `LoadManager.LoadedGameFolderPath` is stable for the same save across launches and distinct per save slot.
2. Returning to the main menu produces a NoSave observation before the next load (so every load is a new session).
3. A 2 s flush on a save switch is not noticeable during the loading screen; a 3 s bound at quit is acceptable.
4. `OnDeinitializeMelon` runs on normal quit (Alt+F4 and crash behaviour unknown; files stay consistent either way).
5. Game contracts that no longer exist after loading an older game save are pruned by the existing reconciliation
   (the mod's runtime files are written continuously, the game's save only when the player saves).
6. Dealer `ID` values are unique and stable per save.

## 10. Tests added (58; total 601)

| Suite | Covers |
| --- | --- |
| A `SafeDocumentFileTests` | write sequence, `.bak`, refusal of invalid documents, newer schema never replaced, bounded corrupt copies, read-back mismatch, crash before rename, leftover `.tmp`, locked file never replaced |
| B `PersistenceQueueTests` | stale generation refused, coalescing, the 10/11 race, retry/back-off, bounded flush, randomised concurrent stress (20 rounds × 400 submissions) |
| C `SalesPersistenceTests` | round trip with `save`, stale caller refused, cross-save pending write (W1), newer files read-only, corrupt copies, foreign files, pre-Phase 06 files, alert recovery, failure reporting |
| D `SettingsFileTests` | newer settings, backup offered as draft, foreign backup refused, locked file, wrongly typed fields (9 cases), global field types, optional defaults, unknown fields, deterministic round trip incl. orphaned dealer override, idempotent schema 1 migration |
| E `SaveProfileIsolationTests` | per-save rules across switches and restart, no cross-save permission, catalog failure never rewrites, undiscovered selection kept and reactivated, dealer overrides per save and dealer |
| F `ContractRecoveryTests` | accepted order keeps OG Kush after "GDP only" + reload while new orders are GDP only; unreadable snapshot not rebuilt |
| G `SaveIdentityTests` | deterministic anonymised identity, unsafe tokens rejected, no identity → nothing loaded or written |
