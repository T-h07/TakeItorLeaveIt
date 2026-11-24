# Security

Take It or Leave It is a MelonLoader mod. It runs inside the game process with the same rights as the game and every
other mod, so "security" here means: **the mod never makes the game less safe than vanilla, never loses or duplicates
items or money because of bad input, and never leaks the player's personal data.** This page is the threat model
behind that statement (Phase 08 hardening). Findings and fixes are in [PHASE_08_CODE_AUDIT.md](PHASE_08_CODE_AUDIT.md).

## 1. What the mod protects

| Asset | Guarantee |
| --- | --- |
| Inventory (player, dealers) | No item is duplicated; an item the mod removes either reaches the customer through the game's own handover or goes back to the dealer it came from |
| Money, XP, commission | Always settled by the game (vanilla); the mod never pays, completes contracts or changes dealer cash |
| Category rules | A contract the mod holds a snapshot for is judged only by that snapshot; when it cannot be judged, the delivery is refused (fail closed), never handed to the game's exact-product rule |
| Save data | The game's saves are never written. The mod's own files are written atomically, newer schemas are never overwritten, damaged files are kept |
| Personal data | Logs and diagnostics contain no absolute paths (they contain the Windows user name); shipped DLLs contain no build-machine paths |

## 2. Out of scope

- **Code in the same process** (other mods, MelonLoader plugins, memory editors). They can call or change anything;
  no mod can defend against them.
- **A modified game client in co-op.** Vanilla Schedule I already trusts each player's client for that player's own
  handover. Take It neither adds nor removes that exposure (see boundary 6).
- **Cheating in single player.** The player owns the machine.

## 3. Trust boundaries

| # | Boundary | Untrusted input | Threats | Controls | Residual risk |
| --- | --- | --- | --- | --- | --- |
| 1 | **Game code ↔ Harmony hooks** | Game build changes (new signatures, renamed members) | A hook binds to the wrong method or a partial patch set runs | Exact-signature resolution; any missing target removes **every** patch (vanilla game); game build checked (`0.4.7f12` = verified); generated RPC methods are never patched | A same-signature method with new meaning in a future build (log: "Not tested" warning) |
| 2 | **Settings files** (`UserData/TakeItOrLeaveIt/saves/*.json`) | Hand edits, damage, other tools, huge files | Permission widening, crashes, memory exhaustion | Strict parser (depth 32, duplicate keys, NaN rejected); unknown values make a category *Disabled*; newer schema never overwritten; 4 MiB size cap before reading (A-11); damaged files kept as `.corrupt-*` copies (max 3) | — |
| 3 | **Runtime files** (active orders, dealer orders, alert state, backups) | Same as 2, plus stale files from another save | Restoring a wider order; another save's orders | Every entry re-validated; damaged values made stricter (A-10); `save` field must match; reservations are never restored (rebuilt from live stock) | — |
| 4 | **Save identity and paths** | Game-provided save folder path | Path traversal into other folders | File names are hash-derived tokens (`SaveFileNames`), checked before use; all files live under the mod's `UserData` folder | — |
| 5 | **Steam lobby rules (host → client)** | Lobby data seen by a client | Forged or corrupted rules shown to clients | Only the lobby owner can write lobby data (Steam); clients accept data only from the current owner; size checked before parsing; protocol, token, revision, epoch and **SHA-256** checked. **The hash detects corruption, not forgery: it is not authentication.** Clients only *display* host rules; they never enforce anything from them | Displayed rules could be wrong if Steam itself were compromised; no sale depends on them |
| 6 | **Client handovers (co-op)** | A client's own delivery to a customer | A client delivers outside the host's rules | None possible: items leave the client's inventory on the client before the host sees anything, and the host-side RPC carries no contract identity (PHASE_08_RELEASE_AUDIT §5) | **Not host-validated** (B-04). Never advertised as host-authoritative |
| 7 | **Game state through IL2CPP interop** | Contracts, items, dealers read at hook time | Stale identities (reused native addresses), exceptions mid-transaction | No address-keyed identity caches across object lifetimes (A-04–A-06); held contracts fail closed on exceptions (A-01, A-02); removal never re-runs the game's removal after a partial attempt; failed rollbacks are queued and retried (A-03) | Whether `AddItemToInventory` can fail in practice is unobserved |
| 8 | **Session authority** | Network state reads (FishNet), player count | Managing contracts on a client; a transient read failure turning enforcement off | Authority only for host/single player; a failed check manages nothing new **and refuses held contracts** instead of reverting them to vanilla | A held contract cannot be delivered until authority is confirmed again |
| 9 | **Phone UI input** | Search text, button presses | Injection into rules, input leaking to the game | Typed text is a filter only; rules change only through `ModMenuController` validation; keyboard capture while typing | — |
| 10 | **S1API (third-party mod)** | Its UI and icon APIs | Exceptions, missing APIs | App disabled on the first error; everything else keeps working | — |
| 11 | **Logs and diagnostics (outbound)** | Exception messages, file contents, scene names | Leaking the Windows user name and folder layout; log flooding; fake log lines | Every log entry redacted by `LogText` (paths reduced to file names) and bounded; untrusted values clipped, control characters removed; one-time keys for per-frame paths; diagnostics show no paths | Messages from other components (MelonLoader, the game) are outside the mod's control |
| 12 | **Build and release supply chain** | NuGet packages, GitHub Actions, local game references, the release ZIP | Malicious or vulnerable dependency, proprietary assemblies in the package, private data in binaries | Pinned package versions; `dotnet list package --vulnerable` clean (2026-11-24); Actions pinned to commit SHAs; CI `contents: read`; game interop references `Private=false` and never packaged; `PathMap` removes build paths from DLLs (A-07); ZIP content and SHA256 checked per RC; the CI artifact is named as lacking the game hooks | Local game references are trusted as exported by the owner |

## 4. Fail-closed rules (summary)

1. Unknown or damaged configuration → the narrower result (category disabled, product dropped, single-product deliveries).
2. A contract the mod holds → judged by its snapshot or refused; never by the game's exact-product rule.
3. A dealer appointment the mod holds → its planned units or nothing; never the game's own removal.
4. Any hook target missing → no patches at all.
5. Unreadable files → kept as they are; never overwritten by defaults.

## 5. Reporting a problem

Open an issue on the GitHub repository with the game version, the mod version (`1.0.0-rc.N`) and `Latest.log`. For
something that could let one player affect another player's game or data, open the issue without reproduction details
and ask for a private channel. The mod contains no obfuscation or anti-debugging code.
