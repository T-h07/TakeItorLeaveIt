# Performance

> **All numbers on this page are SYNTHETIC.** They come from deterministic workloads in the unit-test project
> (suite W, `tests/TakeItOrLeaveIt.Core.Tests/SyntheticPerformanceTests.cs`) on the development PC, not from the game.
> They compare code paths with each other; they do not predict frame times in Schedule I (IL2CPP, .NET 6 runtime
> inside Unity). No test asserts a time limit: they assert results and work counts only.

## 1. Rules

- Correctness first: no cache without a reliable invalidation; no safety check removed for speed.
- An optimization needs a measurement before and after. Nothing was optimized on intuition in Phase 08.
- Per-frame paths must not log (one-time keys) and must stay cheap when nothing changed.

## 2. Hot paths

| Path | How often | Cost control |
| --- | --- | --- |
| Handover screen (`GetProductListMatch`, `GetError`, `Refresh`) | Several times per second while the screen is open | Validation cached per contract + item signature (A-05: the signature now covers id, amount, packaging and quality) |
| Dealer tick (`Dealer.OnTick` postfix) | Every dealer, every game tick | Reconcile throttled to every 3 s per dealer and skipped when stock is unchanged; pending-return retry returns at once when the queue is empty |
| Mod `OnUpdate` | Every frame | Stopwatch reads; discovery poll about once per second; session poll every couple of seconds |
| Offer filter | Once per generated offer | One pass over the candidate list |
| Logging | Bounded (one-time keys, ≤ 500) | `LogText` redaction, with a fast path for lines without paths |

## 3. Synthetic measurements (2026-11-24)

Environment: development PC, 12 logical processors, .NET SDK 10.0.400, test runtime net10.0, Release build. Each
figure is the range over three runs; `B/op` is managed allocation on the test thread.

| Workload (suite W) | Iterations | Time per operation | Allocation |
| --- | --- | --- | --- |
| `P08_W01` validate a delivery of 8 stacks against a 3-product order | 20,000 | 28–31 µs | 7,264 B |
| `P08_W02` dealer reconcile, 20 appointments, stock unchanged | 5,000 | 1.0–1.1 µs | 200 B |
| `P08_W03` log entry with one absolute path (redacted) | 20,000 | 24–54 µs | 1,416 B |
| `P08_W05` log entry without a path | 20,000 | 0.3–0.5 µs (was 12.6–12.9 µs) | 0 B |
| `P08_W04` pending-return retry, 64 items | 1 pass | 64 `giveBack` calls; 0 for another dealer | — |

## 4. Changes in Phase 08

| Change | Reason | Measured effect |
| --- | --- | --- |
| `LogText.RedactPaths` fast path: skip the regexes when the text has no `:\`, `:/`, `\\` or `/` | Most log lines contain no path; every entry now goes through redaction | 12.7 µs → 0.4 µs per path-free entry (`P08_W05`, before/after on the same machine) |
| Validation-cache signature reads product id, amount, packaging and quality per item (A-05) | Correctness: the address alone is not an identity | Not measurable outside the game: a few interop reads per item per handover-screen call. Checked physically by `P08-HD03` (no stutter with the handover screen open) |
| Contract id read on every hook call instead of an address-keyed cache (A-04) | Correctness | Same: one GUID-to-string per call; checked by `P08-HD03` |

## 5. Not changed

- `PolicyStore` parses a settings file twice on load; `CleanEffects` is O(n²) over a few dozen effects. Both run once
  per load, far from any frame path.
- Dealer reconcile at scale is already covered by `P04_T48_reconcile_stays_cheap_at_scale` (work counts).
