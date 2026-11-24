# Phase 08 code audit (security, transactions, runtime safety, performance)

> Scope: the whole `feature/phase-08-release` branch at `7f5afbe` (Core, Game adapter, phone app, build scripts, CI).
> Method: reading every Harmony hook and the Core code it calls, the persistence and sync parsers, the build output and
> the supply chain; each fix comes with a regression test. **Nothing in this document was observed in the game.**
> "Confirmed" means confirmed in code (a reachable path), not reproduced in play. Physical retests are listed in
> [PHASE_08_TESTING.md](PHASE_08_TESTING.md) (`P08-HD…`).

Severity: **High** = can break a guarantee the mod claims (policy enforcement, inventory, settlement) or lose
items; **Medium** = privacy leak or a correctness risk that needs an unusual condition; **Low** = robustness, bounded
damage. Status: FIXED (in this branch, awaiting physical retest), DOCUMENTED (no code change; limitation stated),
DEFERRED (waits for evidence), NO CHANGE (reviewed, not a defect or not worth the risk).

## 1. Confirmed defects

### A-01 — Player delivery fell back to the game's exact-product rule for a held order (High) — FIXED

| Field | |
| --- | --- |
| Where | `Game/Sales/PlayerSalesPatches.cs` `Match`, `GetErrorPostfix`; `GameSales.ManagedAny` |
| Root cause | "Managed" meant `Find(id) != null`, and `Find` also requires session authority. When authority dropped while a snapshot existed (`FailClosed` after a failed session check, or `UnsupportedSession` when the network state cannot be read), `Validate` returned null and the prefix returned `true` (run vanilla). An exception inside the hook (for example "A validated handover item is no longer a product") also returned `true`. |
| Impact | For that delivery the game's own exact-product match decided instead of the snapshot: the snapshot's quality floor, mixing rule and product list were not applied. The gate (`GetError`) had the same fallback, so the button was enabled. Bounded (one delivery, the game still requires the requested product), but it breaks "a managed order is judged only by its snapshot". |
| Correction | New Core rule `ManagedHandoverGuard` and `HoldsSnapshot(contractId)` on both coordinators (snapshot present and not ended, authority ignored). A held contract that cannot be validated, or whose hook throws, is **refused** (match 0, the same outcome as any refused category delivery); the gate shows "Take It cannot check this delivery right now". Contracts without a snapshot keep the vanilla path. |
| Regression risk | If authority stays unconfirmed for a long time, the player cannot deliver a held order until it returns (the contract stays open and can expire). That is the intended fail-closed trade. In single player authority is only lost after a failed session check and is restored by the next poll (about every 2 s). |
| Verification | `P08_T01`, `P08_T02`, `P08_T04`, `P08_T05`, `P08_U02`, `P08_U03`; full suite. Physical: `P08-HD02`, `P08-HD03`, `P08-HD06`. |

### A-02 — Dealer handover treated a held appointment as unmanaged (High) — FIXED

| Field | |
| --- | --- |
| Where | `Game/Sales/DealerSalesPatches.cs` `RemoveContractItemsPrefix` |
| Root cause | Same as A-01: `coordinator.Find(id)` was null without authority, so the appointment went down the unmanaged branch, where `BlocksUnmanaged` also returns false without authority, so the game's exact-product removal ran. |
| Impact | The dealer's own removal could take units reserved for another appointment and ignore the appointment's quality lot and rules (strict-fulfilment guarantee broken for that handover). |
| Correction | A held appointment without confirmed authority hands over nothing this time (`return false` with an empty list) and is kept; the planning-exception path also refuses for held appointments. |
| Regression risk | Same B-02 path (empty dealer handover) as a dealer short of stock. Its customer behaviour is still NEEDS EVIDENCE. |
| Verification | `P08_T03`, `P08_T04`; physical `P08-HD04`. |

### A-03 — Items lost when a rollback could not give them back (High) — FIXED

| Field | |
| --- | --- |
| Where | `DealerSalesPatches.RemoveContractItemsPrefix`, rollback loop |
| Root cause | When a removal did not match the plan, every removed item was given back with `AddItemToInventory`; an exception there was logged once and the item was dropped. |
| Impact | Irreversible loss of dealer stock (P0 class), only if `AddItemToInventory` throws. Whether it ever does in this game build is unknown (PROBABLE: it does not for a dealer with free slots). |
| Correction | Core `PendingReturnQueue<T>`: a failed return is kept per dealer and retried on that dealer's tick (at most 20 attempts, 64 items per dealer). Items are only ever given back to the dealer they came from, never to a customer. Diagnostics shows `Items waiting to go back to dealers: …` while any are queued. A save change drops the queue with an error line (those items belong to the unloaded game state). Items still lost after the attempt limit are logged as an error with their units. |
| Regression risk | Low: the queue is empty unless the rare failure happens. Queued wrappers keep their IL2CPP objects alive until returned or dropped. |
| Verification | `P08_T06`–`P08_T10`, `P08_U01`, `P08_W04`. Physical: `P08-HD04` (normal path only; the failure cannot be provoked without a debugger). |

### A-04 — Contract id cache keyed by native address (Medium) — FIXED

| Field | |
| --- | --- |
| Where | `GameSales.ContractId` (`_lastContractPointer` → `_lastContractId`) |
| Root cause | A native address identifies an object only while the object is alive. After a contract is freed, a new contract at the same address returned the old contract's GUID text. |
| Impact | Theoretical: the wrong snapshot could be applied to a contract. |
| Correction | Cache removed; the GUID is read every call (one small string). |
| Regression risk | A small allocation per hook call; not measurable outside the game (see PERFORMANCE.md). |
| Verification | Build; physical `P08-HD02`. |

### A-05 — Validation cache keyed by item address and stack size only (Medium) — FIXED

| Field | |
| --- | --- |
| Where | `GameSales.Signature` |
| Root cause | The key was address + quantity + catalog generation. A different item at a reused address with the same quantity hit the cache. |
| Impact | Theoretical: a cached "valid" result for a different product, quality or packaging. |
| Correction | The key also hashes the product id, package amount, packaging and quality tier, which are everything the validation reads. Any difference is a miss. |
| Regression risk | A few interop reads per item per call (the handover screen calls this several times per second). Never trades correctness for speed. |
| Verification | Build; physical `P08-HD03` (handover screen responsiveness). |

### A-06 — Dealer id cache keyed by native address, reset only on save change (Low) — FIXED

| Field | |
| --- | --- |
| Where | `GameDealers.DealerId` |
| Correction | Also cleared on every scene load (`GameDealers.ForgetPointers`), together with the validation cache. |
| Verification | Build. |

### A-07 — Shipped DLLs contained the build machine's absolute path (Medium, privacy) — FIXED

| Field | |
| --- | --- |
| Where | Build (`Directory.Build.props`) |
| Root cause | The PE debug directory records the PDB path, `C:\Users\<name>\…\obj\…\*.pdb`, which includes the developer's Windows user name. It was present in `TakeItOrLeaveIt.Il2Cpp.dll` and `TakeItOrLeaveIt.Core.dll` of RC1. |
| Impact | Personal data in a distributed binary. RC1 was only shared privately for testing. |
| Correction | `<PathMap>$(MSBuildThisFileDirectory)=/_/</PathMap>`: the recorded path is now `/_/src/…`. Verified by scanning the built DLLs (no `Users`, no user name). |
| Verification | Byte scan of all three output DLLs; repeated on the rc.2 package. |

### A-08 — Exception messages with absolute paths in logs and diagnostics (Medium, privacy) — FIXED

| Field | |
| --- | --- |
| Where | 38 `ex.Message` uses across Core and Game; `PolicyStore.Load` ("Settings file could not be opened (…)"), `PersistenceQueue` write errors, phone app errors, `{ex}` full exception logs |
| Root cause | .NET I/O exception messages contain the full file path, including the user profile folder. |
| Correction | Core `LogText`: `RedactPaths` (drive, UNC and POSIX roots, quoted paths with spaces, keeping only the file name), `Clip`, `Describe(ex)`, `Entry(message, ex)` (bounded to 8,000 characters). The MelonLoader adapter passes **every** entry through `LogText.Entry`, so no future call site can leak a path. Call sites use `LogText.Describe(ex)` for one-line warnings. |
| Regression risk | A path-shaped fragment in a message is shortened; stack traces keep method names (line numbers need a PDB, which is not shipped). |
| Verification | `P08_S01`–`P08_S05`, `P08_W03`, `P08_W05`; physical `P08-HD01`. |

### A-09 — Untrusted text logged without bounds (Low) — FIXED

| Field | |
| --- | --- |
| Where | `MiniJson` duplicate-key error, `ActiveOrderStore` invalid entry id, scene names |
| Correction | `LogText.Clip`: control characters (including line breaks, which could fake log lines) become spaces; values are clipped (64–120 characters). |
| Verification | `P08_S04`, `P08_S06`, `P08_S07`. |

### A-10 — A damaged `mixedDeliveries` value widened an order (Low) — FIXED

| Field | |
| --- | --- |
| Where | `ActiveOrderStore.ReadOrder` |
| Root cause | `!present || not bool || value`, so a damaged value (for example a string) meant "mixed allowed". It only matters for an order without the Phase 05 `mixing` field. |
| Correction | Absent → historical default (true, unchanged for old files); present but not a boolean → **false** with an issue line. |
| Verification | `P08_V01`, `P08_V02`. |

### A-11 — Documents read without a size limit (Low) — FIXED

| Field | |
| --- | --- |
| Where | `PhysicalSettingsFileSystem.ReadAllText` (settings, order, dealer-order, alert files and their backups) |
| Correction | Files over 4 MiB throw `DocumentTooLargeException` (an `IOException`) **before** reading. Every caller already treats I/O errors as "unreadable, keep the file": nothing is parsed, replaced or deleted. Real documents are a few hundred KB at most. |
| Verification | `P08_V03`–`P08_V05`; physical `P08-HD05`. |

### A-12 — One-time warnings went silent after 500 keys (Low) — FIXED

`GameSales.Once` stopped logging without notice. It now logs one warning when the limit is reached.

### A-13 — Stale startup label (P3, B-16) — FIXED

"Phase 05: player sales hooks active; …" now reads "Sales hooks: player sales hooks active; …".

## 2. Theoretical risks and documented limitations (not defects in code)

| ID | Area | Finding | Decision |
| --- | --- | --- | --- |
| R-01 | Co-op | A client's own handover is judged on the client by the game; the host only sees a hash-named RPC with a client-computed payment after the items have left the client's inventory (audit §5 of PHASE_08_RELEASE_AUDIT). | DOCUMENTED. Not host-validated and never claimed to be (B-04, owner decision pending). No hook added. |
| R-02 | Dealers | A removal of a higher tier than the reserved lot is accepted and force-reconciled (B-03). | DEFERRED until P08-QL01/QL02 show which slots the game takes. Unchanged. |
| R-03 | Co-op | The rules-sync SHA-256 detects corruption and truncation only; it is not authentication. Authenticity comes from Steam: only the lobby owner can write lobby data, and the client accepts rules only from the current owner (`SteamLobbyRulesChannel`). | DOCUMENTED in SECURITY.md and MULTIPLAYER.md. |
| R-04 | Offers | If the offer filter throws, that one offer keeps the game's candidates. | NO CHANGE. Enforcement happens at acceptance and delivery (snapshot), so an unfiltered offer cannot sell an excluded product. Failing closed here would empty offers (a gameplay change). |
| R-05 | Steam | A modified client or another mod in the same process can call anything the game exposes. | DOCUMENTED (out of scope: same-process code is fully trusted). |
| R-06 | Persistence | `SaveFileNames` accepts Windows reserved device names (`CON`, `NUL`, …) as tokens. | NO CHANGE: identities are hex hashes, never such names. |
| R-07 | Parsing | `MiniJson` `\u` escapes are parsed with `int.TryParse(HexNumber)`, which tolerates surrounding spaces. | NO CHANGE: only accepts more malformed input as text; cannot widen permissions (values are still validated). |
| R-08 | Lifetime | IL2CPP wrappers in static fields (`PendingReturns`, coordinators) outlive scenes. | Covered: caches reset on scene load and save change; coordinators are dropped at shutdown. |
| R-09 | Supply chain | GitHub Actions referenced by moving tags (`@v4`). | FIXED: pinned to commit SHAs (comment keeps the major version). |
| R-10 | Supply chain | `xunit` 2.9.3 is marked legacy (successor xunit.v3). | NO CHANGE: test-only, no vulnerability; a migration is a refactor without a defect. |

## 3. Reviewed and sound (no change)

- **Harmony targets**: 13 methods in 3 modules, resolved by exact signature; a missing one removes every patch
  (`PatchManager`), no generated RPC (`…_<hash>`) is patched.
- **Settlement**: no hook pays, awards XP, completes a contract or changes dealer cash; the game settles.
- **Settings parser** fails closed (unknown mode, match, quality or effect → category disabled; invalid ids dropped).
- **MiniJson**: depth limit 32, duplicate keys rejected, NaN/Infinity rejected.
- **SafeDocumentFile**: newer schema never overwritten; temp file + read-back + atomic replace; at most 3 corrupt copies.
- **PersistenceQueue**: generation ordering; only text crosses to the background writer.
- **PolicySync.Decode**: size checked before parsing; protocol, header, token, revision, epoch and hash checked
  before the rules are parsed.
- **Order snapshots**: a priced snapshot without a usable quote is downgraded to requested-only; a missing lineage
  group makes the product its own group (stricter).
- **Dependencies**: `dotnet list package --vulnerable --include-transitive`: none (2026-11-24). Game interop
  references are `Private=false` and never packaged; CI runs with `permissions: contents: read`.

## 4. Refactoring (H08)

No refactor without a defect: the changes above are fixes. `PolicyStore` parses a settings file twice and
`CleanEffects` is O(n²) over at most a few dozen effects; both run on load only and were left alone.

## 5. Commits

| Commit | Findings |
| --- | --- |
| `b3169e0` build: map source paths so shipped DLLs carry no build-machine path | A-07 |
| `681d122` ci: pin GitHub Actions to commit SHAs | R-09 |
| `ef24d9d` fix(logging): redact absolute paths and bound untrusted text in logs | A-08, A-09 |
| `b737605` fix(persistence): cap document size and never widen a damaged order flag | A-10, A-11 |
| `35b13e5` fix(sales): fail closed for held contracts and keep items a rollback could not return | A-01 … A-06, A-12, A-13 |
| `29c165e` test: synthetic performance suite W and PERFORMANCE.md | H09 |
| `0c3228b` build: version 1.0.0-rc.2 | Version |
| `0068d86` fix(dealers): restore the original unit count in the removal loop | Undoes an unintended edit in `35b13e5` (same value); **rc.2 source commit** |

Blocker entries: B-16 … B-21 in [PHASE_08_RELEASE_BLOCKERS.md](PHASE_08_RELEASE_BLOCKERS.md).
