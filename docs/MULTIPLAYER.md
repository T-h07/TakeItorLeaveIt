# Multiplayer (Phase 07)

> **Not tested in a real co-op game.** The session model, rules sharing and co-op sale behaviour are covered by
> automated simulation tests only (suites A–F and S, [PHASE_07_TESTING.md](PHASE_07_TESTING.md)). Simulated
> results are **not** multiplayer verification; real co-op testing is Phase 08. Call-graph evidence:
> [PHASE_07_NETWORK_ANALYSIS.md](PHASE_07_NETWORK_ANALYSIS.md).

## 1. In one paragraph

The **host** runs Take It: its settings decide which products customers ask for, its hired dealers fill
category orders, and its own handovers accept any permitted product. A **client** with the mod sees the host's
rules in the **Take It** app, read-only ("Host controls Take It settings"), and changes nothing. A client's
own handover follows the game's exact-product rule. Payment, XP, the dealer cut and item transfer stay with
the game, exactly once.

## 2. Sessions

Every two seconds the mod reads the network state (`InstanceFinder.IsServer/IsClient`, the player list,
`Player.Local`) and decides one state:

| State | When | What the mod does here |
| --- | --- | --- |
| `NoSession` | No save loaded | Nothing |
| `SinglePlayerHost` | Server, one player | Everything (as in Phases 03–06) |
| `MultiplayerHostInitializing` | Server, more players, this save's settings not ready | Hooks active, new orders stay vanilla until settings load; clients see "Waiting for host rules" |
| `MultiplayerHostReady` | Server, more players, settings ready | Offer filter, the host's own handovers, dealer appointments, restock texts; rules shared |
| `MultiplayerClientSynchronizing` | Client, no valid host rules yet | Nothing; menu says "Waiting for host rules." |
| `MultiplayerClientReady` | Client, host rules received | Nothing; menu shows the host rules read-only |
| `UnsupportedSession` | Network state unreadable, or the host shares no rules | Nothing; vanilla rules |
| `NetworkDisconnected` | Was a client, connection gone | Host rules dropped; "Disconnected from the host" |
| `CompatibilityFailure` | A hook is missing, or the host speaks another protocol | Nothing; the reason is shown |

Capabilities per state are explicit (`SessionCapabilities`): player orders, dealer appointments, offer filter,
restock messages, edit settings, publish settings, write sales files, show host rules. Every host state grants
the host-side ones; every client state grants at most "show host rules". The Phase 03–06 single-player gate
(`SetAuthority`) still exists as a fail-closed fallback; the game uses the session model. If the network state
cannot be read, the mod fails closed (vanilla everywhere).

## 3. Sharing the rules

The host writes its **saved** settings (never an unsaved draft) to Steam lobby data under
`takeitorleaveit.rules`:

```text
{"p":1,"mod":"0.0.1-dev","game":"0.4.7f12","s":"<session token>","r":<revision>,"e":<epoch>,
 "state":"ready","rules":"<settings JSON without the save id>","h":"<sha-256 prefix of rules>"}
```

- `p` protocol version (1). Another protocol → the client shows "update Take It" and no rules.
- `s` random token per host save session; `e` epoch increases with each session; `r` revision increases with
  each change of the rules. A client never goes back to an older revision or an older session.
- `h` is checked against the rules text; a mismatch is rejected and the last valid rules stay.
- Too large (over 7680 bytes) → published as "not ready" with the reason, never truncated.
- Settings not loaded → `"state":"not-ready"` with the reason in `why`.
- Every 30 s the host rewrites the current value (heartbeat), so a client that missed an update or saw a
  replayed old one catches up. A heartbeat is not a new revision.
- A client that sees no value for 30 s decides the host has no Take It ("The host does not share Take It
  rules. Vanilla rules apply.").

Handshake on the client: Idle → Connected → ProtocolCheck → SnapshotRequested → Received → Validated → Ready.
Counters (accepted, duplicates, stale, rejected) are in Diagnostics.

## 4. The client's menu

- All seven screens are kept. The status bar and Overview say who controls the settings.
- Editing, Save, Discard and Reset are refused with "the host controls Take It settings in this game. Nothing is
  written on this computer."
- The client's own settings file for its own saves is never touched; leaving the host's game restores the
  client's local draft.

## 5. Sales in co-op

| Who | What happens |
| --- | --- |
| Host hands over | Category order as in single player: any permitted product, validated before anything leaves the host's inventory |
| Client hands over | The game's exact-product rule. The customer asked for a permitted product (offer filter on the host), so no excluded product can be sold. No substitution |
| Hired dealer (anyone's appointment) | Runs on the host: reservations, strict fulfilment, removal checked by product and quality tier |
| Two players hand over to the same customer | The game settles the contract once; the mod's snapshot is released once; later callbacks are ignored |
| Dealer handover callback repeated | Ignored after the first completion (idempotent) |
| Restock texts | Host's phone only |

Transaction phases per order: Available → Validating → Committing → Committed (or Ended).

## 6. Joining, leaving, host save switch

- **Join in progress:** the client reads the current lobby value and becomes Ready with the latest revision.
- **Client disconnects:** host rules dropped immediately; nothing is written.
- **Host loads another save:** a new session token and epoch; clients switch to the new rules and reject the old
  session's values.
- **Host leaves:** the session ends for everyone. **No host migration.**

## 7. Files

Only the host writes Take It files (through `PersistenceQueue`, as in Phase 06). A client writes no sales
snapshots, no dealer state and no settings while in someone else's game.

## 8. Compatibility modes

See [COMPATIBILITY.md](COMPATIBILITY.md) §3 (A both modded, B host only, C client only, D different Take It
versions, E different game builds).

## 9. Not yet known (Phase 08)

- Whether Steam lobby data reaches clients as expected in this game (ownership, propagation delay, join in
  progress).
- Whether offers the host generates for a client's customer are filtered exactly as on the host (PROBABLE).
- Inventory and money totals in a real two-player session.

## 10. Security notes (Phase 08)

- The rules message carries a **SHA-256 of the rules text. It detects corruption and truncation; it is not
  authentication.** Authenticity comes from Steam: only the lobby owner can write lobby data, and a client accepts
  rules only from the current owner. Clients only display the host's rules; nothing a client enforces depends on them.
- **Client handovers are not host-validated** (B-04): the client's own delivery is judged on the client by the game's
  exact-product rule, and the host learns of it only after the items have left the client's inventory.
- If the host briefly cannot confirm its authority (a failed session check), contracts it already holds are
  **refused** until the next successful check instead of reverting to the game's rule.

Full threat model: [SECURITY.md](SECURITY.md).
