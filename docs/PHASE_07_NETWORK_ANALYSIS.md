# Phase 07 network analysis: who decides what in co-op

> **Evidence level.** Everything in this file comes from the call graph of the installed build's interop
> assemblies (Schedule I v0.4.7f12, IL2CPP, read with `tools/TakeItOrLeaveIt.Inspector --xref`) and from
> compiling against those assemblies. **None of it has been observed in a real co-op game.** Runtime
> confirmation is Phase 08 ([PHASE_07_TESTING.md](PHASE_07_TESTING.md)). "Verified" below means *verified in the
> call graph*, "PROBABLE" means inferred from it.

## 1. Networking stack

- The game uses **FishNet**. Server RPCs and observer RPCs are generated methods named
  `RpcWriter___Server_<Name>_<hash>`, `RpcLogic___<Name>_<hash>` and `RpcReader___…`. The hash suffix is
  build-specific. **The mod never patches a generated RPC method** (spec rule), and never resolves one by name.
- `InstanceFinder.IsServer` / `InstanceFinder.IsClient` say which role this game instance has. A host is both.
- The lobby is Steam: `Il2CppScheduleOne.Networking.Lobby` (persistent singleton: `IsHost`, `IsInLobby`,
  `LobbyID`, `PlayerCount`, `SetLobbyData`) on top of `Il2CppSteamworks.SteamMatchmaking`
  (`SetLobbyData`, `GetLobbyData`, `DeleteLobbyData`, `GetLobbyOwner`). Steam lets only the lobby owner write
  lobby data; every member can read it.

## 2. Where each sale step runs

| Step | Method (verified call graph) | Runs on | Consequence for the mod |
| --- | --- | --- | --- |
| Customer offer generated | `Customer.OnMinPass` → offer creation; checks `IsServer` | Host | Offer filter (Phase 04) is host-side; offers every player sees come from the host (PROBABLE) |
| Contract accepted | `Customer.ContractAccepted` → `QuestManager.ContractAccepted` (checks `IsServer`) | Host | The mod's acceptance hook, which creates the category-order snapshot, runs only on the host |
| Contract shown to clients | `CreateContract_Networked` (observers RPC) → `CreateContract_Local` | Every client | Clients get the contract without running the acceptance hook: no snapshot on a client |
| Handover screen, match score | `HandoverScreenContractMode.Refresh / GetSuccessChance / GetError / GetWarning` → `Contract.GetProductListMatch` | The machine whose player opened the screen | Local UI only |
| Handover processed | `Customer.ProcessHandover(Contract, List<ItemInstance>, bool, bool)` → `HandoverScreen.TransferCustomerItemsToNPC`, `DealCompletionPopup` | The machine whose player handed over | Items leave **that player's** inventory locally |
| Settlement | `ProcessHandoverServerSide(items, handoverByPlayer, totalPayment, productList, satisfaction, dealerObject)` → `RpcLogic___ProcessHandoverServerSide_<hash>` | Host | Pays money, XP and the dealer cut. **The RPC has no contract argument and the payment is computed by the sender** |
| Contract completion | `Contract.Complete(bool)` (checks `IsServer`) | Host | Snapshot released on the host |
| Dealer attends | `Dealer.OnTick` (checks `IsServer`) → `DealerAttendDealBehaviour` | Host | Dealer appointments are host-only |
| Dealer removes items | `Dealer.RemoveContractItems` (called only from the dealer handover coroutine) | Host | D1 lot check runs on the host |
| Dealer restock text | `MSGConversation.SendMessage(msg, notify, network)`; network path is `MessagingManager.SendMessage_Server` → observers `SendMessage_Client` | The mod sends with `network: false` | Restock texts reach the **host's** phone only |

## 3. What this means for authority

1. **The host is the only authority.** Every decision the mod makes (offer filter, category snapshot,
   dealer reservation, dealer removal check, restock text) already runs on the host in the vanilla flow, so
   the mod adds no new network path. No RPC is added, none is patched.
2. **A client's own handover cannot be validated by the host.** The host learns about a client's handover
   only through `ProcessHandoverServerSide`, which carries no contract, carries a client-computed payment and
   is a hash-named generated RPC. Validating it would require patching that RPC, which is forbidden and
   build-fragile. **Decision:** a client's handover keeps the game's exact-product rule. The client never runs
   the category check (it has no snapshot and its session decision grants no capabilities), so a client can
   never deliver a substitute or an excluded product. Because the offer filter runs on the host, the product
   the customer asks for is always a permitted one.
3. **Simply relaxing `playerCount <= 1` would have been wrong** (forbidden by the spec): a client handing
   over against a host snapshot would have been judged by the client's (absent) rules while the host paid.
   Instead the gate became an explicit session model ([MULTIPLAYER.md](MULTIPLAYER.md) §2) that grants the host
   its host-side features and grants clients nothing.
4. **No custom settlement.** Money, XP, the dealer cut, item transfer and contract completion stay with the
   game. The mod never calls `ChangeCashBalance`, `AddXP` or a payment RPC.
5. **No host migration.** Nothing in the game's call graph transfers authority when the host leaves; the
   session ends for everyone, the client drops the host's rules and shows "Disconnected".

## 4. Rules transport

The host's saved Take It rules are shown to modded clients read-only. Transport options considered:

| Option | Verdict |
| --- | --- |
| Custom FishNet RPC / broadcast | Rejected: needs a registered network object or message type in the game's assemblies; inventing FishNet signatures is forbidden |
| Patch an existing game RPC to carry extra data | Rejected: generated hash names |
| **Steam lobby data** (`SteamMatchmaking.SetLobbyData` under one key) | **Chosen**: only the lobby owner can write (a client cannot spoof it through Steam), every member can read, late joiners read the current value, nothing in the game reads our key |

Key `takeitorleaveit.rules`, at most 7680 bytes (under Steam's per-value limit). Payload and handshake:
[MULTIPLAYER.md](MULTIPLAYER.md) §3.

**Unverified at runtime (Phase 08):** that `Lobby.Instance.LobbyID` is the Steam lobby the session uses, that
`Lobby.IsHost` agrees with `GetLobbyOwner`, how quickly lobby data reaches members, and that lobby data is
readable by a client after joining in progress.

## 5. Trust model

Nothing that arrives from another machine changes inventory, money, contracts or the host's files.

- Lobby data is accepted only when the lobby owner is someone other than this player and this player is not
  the host. A forged or damaged value is rejected (protocol, size, hash, token, revision and epoch checks) and
  the client keeps the last valid rules.
- What a client accepts is used **only for display** in the read-only menu. It never reaches the client's
  own settings file, its live policy or any sale decision.
- The host never reads anything a client writes.

## 6. Blockers and safe-disabled behaviour

| Item | Status | Behaviour |
| --- | --- | --- |
| Category substitution on a client's own handover | Blocked (would require patching a hash-named RPC) | Vanilla exact-product rule on the client; documented |
| Restock texts to every player | Not implemented (would need the game's networked message path) | Host only |
| Host migration | Not supported by the game | Session ends; client shows "Disconnected" |
| Client save identity | Unverified | A client uses `Player.Local` as the "in game" signal |
| Lobby data transport | Compiled, simulation-tested, not runtime-tested | If publishing fails, clients stay at "Waiting for host rules" and nothing else changes |
