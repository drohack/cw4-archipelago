# Offline play, disconnects, and switching multiworlds

What the mod does when the server is not there, why, and where each rule comes
from. Written because "can I keep playing? does my progress survive? what
happens if I join a different seed?" had no answer in this repo, and the code
turned out to answer it four different wrong ways.

## What Archipelago actually requires

Two hard requirements, from `docs/adding games.md` in the Archipelago repo:

> * Reconnect if the connection is unstable and lost while playing

> * If actions were taken in game that would usually trigger a location check,
>   and those actions can only ever be taken once, but the client was not
>   connected when they happened: The client must send those location checks on
>   connection so that they are not permanently lost, e.g. by reading flags in
>   the game state or save file.

That settles the question this document exists for. **Offline progress is
PUSHED on reconnect. It is never reverted, and the save is never rolled back.**
Reverting a save to "undo" offline play would destroy exactly what that
requirement protects. There is no sanctioned "revert" behaviour anywhere in the
protocol.

`docs/network protocol.md` adds the item side:

> You will need to find a way to save the "last processed item index" to the
> player's local savegame, a local file, or something to that effect.

and

> When the client receives a ReceivedItems packet and the `index` arg is `0`
> (zero) then the client should accept the provided `items` list as its full
> inventory. (Abandon previous inventory.)

We satisfy the intent differently and, for this game, more simply: we rebuild
the received list from the server's authoritative `AllItemsReceived` on every
connect, so there is no local index to drift. What we DO have to persist is the
local bookkeeping the server cannot tell us - see "What must survive" below.

`LocationChecks` is explicitly idempotent - "duplicates do not cause issues
with the Archipelago server" - so re-sending a queue is always safe. That is
what makes push-on-reconnect the correct default rather than a risk.

Note what is NOT required: being playable from a cold start with no server ever
reached. Per `docs/apworld_dev_faq.md`, true start-to-finish offline play is a
property of LOCAL items, and we use remote items (`ItemsHandlingFlags.AllItems`),
which is the right choice here - it is simpler and it is what allows same-slot
co-op. Coming up on a cached slot, which is what we now do, is a convenience on
top, not compliance.

## The reference client is the model

`CommonClient.py` is the behaviour to match where the docs are silent.

**Reconnect is unbounded with exponential backoff.** `starting_reconnect_delay`
is 5, and `current_reconnect_delay *= 2` after each attempt, forever; it stops
only when `disconnected_intentionally` is set by a deliberate disconnect.

We match this, with one deviation: the delay is capped at 60 seconds. Pure
doubling reaches an hour by the twelfth attempt, which a player cannot tell
apart from having given up.

**A refused login is not retried.** `ConnectionRefused` - wrong slot name,
wrong password, wrong game, incompatible version - is a different case from a
transport failure, and the reference client does not spin on it. We split them:
a refusal shows the reason and stops, an unreachable host backs off and keeps
trying.

**A session is identified by (seed_name, team, slot),** and on `Connected`:

```python
identity = (ctx.server_seed_name, ctx.team, ctx.slot)
if ctx.connected_identity is not None and identity != ctx.connected_identity:
    ctx.reset_session_state()
```

with the comment "on a switch to a different session, clear session state
before stale checks/goal are replayed below". That is the answer to "what
triggers a new save": **a different seed, not a different server address.**
Rehosting the same multiworld on a new port is the same session and must keep
your progress; a regenerated seed is a new one and must not inherit any of it.

We do not use `team` (we have no team features), so our identity is
(seed, slot).

**The goal is replayed too.** The reference client keeps `finished_game` as a
"Bool to signal that status should be updated to Goal after reconnecting", and
sends `StatusUpdate` on connect if it is set.

## What must survive a connect, and what must not

All of this now lives in `SessionReconcile.OnConnected`
(`src/CW4Archipelago.Core`), which is pure and tested. It used to be inline in
`ApClient.OnLoginSuccess` where no test could reach it, and it was wrong in
three ways at once - each of them a silent loss or a silent duplication.

| State | Server-authoritative? | Carried across a connect |
|---|---|---|
| Checked locations | Yes | Server's set wins; ours is display only |
| Queued (offline) checks | No | Yes, minus what the server already has |
| Goal reached | No | Yes - a finale beaten offline still counts |
| Trap/boon high-water mark | No | Yes, taking the HIGHER of the two sources |
| Received items | Yes | Rebuilt from the server every time |

Two rules make the table work:

1. **Only carry from a matching (seed, slot).** The in-memory state may belong
   to another multiworld entirely.
2. **The trap mark may only move forwards.** Connecting re-delivers the whole
   received list, so a mark that goes backwards re-fires everything between.

## The four defects this replaced

The first three were live in v0.1.5 with the same root cause: the connect path
built a brand-new `SlotState` and copied only the pending checks into it. The
fourth is in the disconnect path.

1. **Checks crossed between multiworlds.** The guard was `State.Slot == slot`
   with no seed comparison. Location names are identical across seeds, so a
   check earned in seed A was accepted by seed B's server as a genuine check
   there. This is precisely what `reset_session_state()` exists to prevent.
2. **A goal reached offline was silently dropped.** `GoalPending` was persisted
   and round-tripped through the store correctly, but never read back into the
   new state, so `FlushPending` always saw false. Beating Founders while the
   server was down never reached the server.
3. **Every trap and boon re-fired on every connect and every launch.**
   `TrapsApplied` was missing from `SlotStore`'s DTO entirely and was not
   carried into the new state, so it reset to zero and `TrapApplier` walked the
   whole received list again, one item per tick. At the default 50 percent trap
   share that is dozens of traps on a reconnect. The design doc claimed the
   mark "survives a reconnect and a restart"; it did neither.

Defect 3 also had a test that read as proof and was not: it asserted that
`ApplyReceivedItems` leaves the field alone in memory, never touched the store,
and was named `TrapsAppliedMark_StartsAtZeroAndPersistsAcrossReconnect`. It has
been renamed to what it actually checks, and the real claims are tested in
`SessionReconcileTests`.

4. **A deliberate disconnect did not stay disconnected.** `OnSocketClosed`
   CONSUMED the intentional-disconnect flag, and the close event arrives more
   than once per disconnect, so the second arrival fell through to
   "disconnected - will retry" and reconnected a few seconds after the player
   pressed DISCONNECT. Only `Connect()` clears the flag now, which is the rule
   CommonClient uses for `disconnected_intentionally`. This one predates the
   unbounded retry, which merely turned "gives up after three tries" into
   "never stays off" - and it was found by reading a preserved harness log,
   where a manual disconnect had silently become a connection again mid-test.

## Offline start

If no server can be reached at launch, the mod comes up on the last slot played
(`SlotStore.LoadLast`, via a `last-session.json` pointer written on every save)
and the player continues with the unlocks they already hold. Checks queue and
flush on the next connection.

Before this, an unreachable server meant an empty state, which meant every
mission locked and nothing playable at all - with a complete cache of the slot
sitting on disk. A mid-session drop had always been allowed to continue; only
the cold start was not, and there was no reason for the difference.

The pointer is its own file rather than a parse of `SaveArchiver`'s
`active.txt`, because that key joins seed and slot with a hyphen and slot names
may contain hyphens, so it cannot be split back apart reliably.

Save isolation is unchanged and still happens on connect: `SaveArchiver` only
moves folders when the slot actually changes, and coming up offline on the slot
that is already active is a no-op.

## Open: we fail one of core's generic tests

Not an offline issue, but found by the same pass and recorded here because
nothing else records it. `docs/tests.md` points at core's generic suite, which
runs against every registered world. Running it (340 tests) produces exactly
one failure, and it is ours:

```
FAIL: test_itempool_not_modified (game='Creeper World 4')
Creeper World 4 modified the itempool during pre_fill
```

`items.place_own_progression` removes items from `multiworld.itempool` in
`pre_fill` and places them itself. That is the behaviour `docs/adding games.md`
lists under "Discouraged or Prohibited":

> All items submitted to the multiworld itempool must not be manually placed by
> the World. If you need to place specific items, there are multiple ways to do
> so, but they should not be added to the multiworld itempool.

Two measurements make this decidable rather than alarming:

- The test **excludes Ocarina of Time and SMZ3**, so long-established worlds do
  the same thing and core exempted them rather than requiring a fix.
- Our placed set is **seed-invariant: exactly 31 items every seed** - 18 mission
  unlocks (20 missions less 2 starters) and 13 unit unlocks - measured over 8
  seeds. It is the whole progression set, not a seed-dependent subset.

That invariance is what makes a compliant version mechanical rather than a
redesign: withhold those 31 in `create_items` instead of adding them to the
pool, place them in `pre_fill` exactly as now, and declare them through
`get_pre_fill_items()` so core's accessibility sweep still sees them. The pool
arithmetic is unchanged - 205 pooled plus 31 self-placed is the same 236
locations.

It has NOT been done, deliberately. `place_own_progression` is the v0.1.3 fix
that took solo generation from about one failure in 18,000 seeds to 0 in 16,000,
and changing it means re-running `tools/audit/realfillrate.py` to prove that
still holds. Since we ship an unofficial APWorld, passing core's generic suite
is not a merge requirement - so this is a correctness preference to weigh
against disturbing freshly-stabilised generation, and that is the owner's call.

## What we deliberately do not do

- **No local items.** Remote items (`AllItems`) keep same-slot co-op possible
  and are what the FAQ calls the simpler choice. True offline-from-scratch play
  would require locally implemented items and a patch-file equivalent.
- **No `LocationScouts`.** It is an encouraged feature, used to show what an
  item is before you pick it up. CW4's caches and totems display nothing about
  their contents, so there is no surface to put it on.
- **No DeathLink.** The player does not die in CW4; units do. There is no event
  that maps to it honestly.
- **No `Sync` packet of our own.** Rebuilding from `AllItemsReceived` on each
  connect makes an index desync unreachable for us; the library owns the index.
