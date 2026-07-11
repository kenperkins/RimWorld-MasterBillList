# Architecture

How **Master Bill List** actually works, and the two non-obvious lessons that shaped it.

## Goal

Let workbenches of the same type share one bill list per map, so a recipe added on one bench appears on (and is worked by) every other opted-in bench of that type — live, not a copied snapshot.

## Sharing model: type-keyed, per map

A shared list is identified by **workbench `ThingDef` + map**. All opted-in butcher tables on a map share one `BillStack`; all stonecutters share another; etc.

This is deliberately simpler than a "named lists you assign benches to" model:

- **No list-management UI** — the only UI is a per-bench "Use shared bills" toggle (`Command_Toggle` gizmo). No dialog, no create/rename/delete.
- **Recipe compatibility is automatic** — benches of the same type support the same recipes, so every shared bill is valid on every member.
- **The def name is a stable key** — so save/load needs no "stable list ID + relink" machinery; a bench just looks up its own def after load.

**Map-scoped** because `Bill.Map` resolves through the stack's representative `billGiver`; keeping all members on one map keeps "Do until you have X" counts correct.

## Core mechanism: field-swap (NOT a getter postfix)

When a bench opts in, we point its `billStack` **field** at the shared `BillStack` (stashing the bench's own stack to restore on opt-out — non-destructive).

> **Lesson 1 — you must swap the field, not patch the property getter.**
> `Building_WorkTable` exposes `public BillStack billStack;` (field) and `public BillStack BillStack => billStack;` (property). The obvious approach — Harmony-postfix the property getter — *fails*, because **`ITab_Bills` (the Bills tab UI) reads the `billStack` field directly** (`SelTable.billStack.AddBill(...)`), while only the work scanner uses the property. A getter-only redirect splits the brain: the scanner sees the shared stack, the UI edits the bench's own. Swapping the field itself makes both agree (the property just returns the field).

## Components

- **`StartupComps`** — `[StaticConstructorOnStartup]` that attaches `CompMasterBillSubscriber` to every `ThingDef` whose `thingClass` is assignable to `Building_WorkTable` (so modded benches are covered by type, not hardcoded def names).
- **`CompMasterBillSubscriber`** — holds the `useShared` flag, does the field-swap (`ApplyShared`/`RestoreOwn`), draws the toggle gizmo, re-applies on `PostSpawnSetup`, and notifies the manager on `PostDeSpawn`. Persists only the `useShared` bool.
- **`MasterBillManager : MapComponent`** — owns the shared stacks (one `MasterBillEntry` per workbench defName), hands them out via `GetOrCreateSharedStack`, and repoints the representative on despawn.
- **`MasterBillEntry : IExposable`** — `{ defName, BillStack }`. Deep-saves the stack (with a null `IBillGiver` ctor arg; `BillStack` has no parameterless ctor and doesn't serialize its `billGiver`).
- **`HarmonyPatches`** — two patches (see below).

## Save/load

The duplication trap: `Building_WorkTable.ExposeData` does `Scribe_Deep.Look(ref billStack, ...)`. With the field pointing at the shared stack, every subscriber would deep-save the **same** object → "deep-saved twice" errors and a broken link on reload.

Solution:
- **`MasterBillManager` is the sole deep-owner** of the shared stacks (`Scribe_Collections.Look(ref entries, LookMode.Deep)`).
- **`Patch_WorkTable_ExposeData`** (Harmony prefix/postfix) swaps a subscribed bench's field back to its own stash **only while `Scribe.mode == Saving`**, so no bench ever deep-saves the shared object.
- On load, benches re-subscribe in `PostSpawnSetup` and the first to do so re-adopts the `billGiver`.

## Edge cases

- **Representative repoint** — the shared stack stores one `billGiver` (the first subscriber). If that bench is deconstructed, `bill.Map` would resolve to a destroyed thing and any TargetCount bill would `NullReference`. `MasterBillManager.NotifyRepresentativeDespawning` (called from the comp's `PostDeSpawn`) repoints to another spawned subscriber, or `null` (dormant) if none remain.

- **Unfinished-thing (UFT) bills** — sculptures (`UnfinishedSculpture`) and components (`UnfinishedComponent`) are `Bill_ProductionWithUft`.

  > **Lesson 2 — `FinishUftJob` targets `bill.billStack.billGiver`, not the scanned bench.** So a shared UFT bill funnels finishing to the representative; if it's busy or being deconstructed, pawns get DoBill jobs they can't reserve (a reservation storm). `Patch_DoBill_RetargetToScannedBench` (postfix on `WorkGiver_DoBill.JobOnThing`) retargets such a job to the bench actually being scanned, which the pawn already validated it can reserve. No-op for normal bills (they already target the scanned bench).

## Orphan recovery: finishing leftover unfinished items

Sharing one bill across parallel benches has a nasty side effect, and fixing it is the largest subsystem after sharing itself.

A `Bill_ProductionWithUft` tracks exactly **one** in-progress UFT (`BoundUft`). With a shared bill worked by several benches at once, multiple pawns can pass the "is the slot free?" check in the same tick and each start a UFT — only one binds. The losers' `UnfinishedThing.BoundBill` getter sees `bill.BoundUft != this` and nulls itself out, leaving an **orphan**: a partially-built item no bill claims. Vanilla never resumes it (`WorkGiver_DoBill` only ever offers the single bound UFT; "No Job Authors" only relaxes the creator gate, not that lockout), so orphans pile up forever — the original motivation for this subsystem.

`WorkGiver_FinishOrphanedUft` (a global-scan `WorkGiver_Scanner`) finds orphans and finishes them.

> **Lesson 3 — you can't reuse the shared bill's slot to finish an orphan; mint a transient bill instead.** The obvious approach (bind the orphan into the live bill's `BoundUft` and let vanilla resume it) only works while that slot is free — which, under continuous production, it essentially never is. In-game diagnostics showed every bench reporting the slot occupied, so finish jobs were never issued. Instead each finish job gets its own throwaway `new Bill_ProductionWithUft(recipe)` with `billStack` borrowed from the target bench (so `Bill.DeletedOrDereferenced` stays false) and `repeatMode = Forever`. `JobDriver_DoBill` rebinds the orphan to that transient on start, so the shared slot is never touched and benches can finish orphans in parallel.

Three more things that made it work:

- **WorkGiver injection, not XML** (`OrphanedUftRegistry`, `[StaticConstructorOnStartup]`, same spirit as `StartupComps`): map every UFT recipe to its work type and inject one `WorkGiverDef` per type at `priorityInType` **above** `DoBill`, so finishing leftover work beats starting new (and the single-slot lockout then stops fresh orphans forming while the backlog drains). Covers modded recipes/work types by enumeration.
- **Don't override `PotentialWorkThingRequest`.** Returning `ForGroup(Everything)` trips `GenClosest.EarlyOutSearch`'s "searching everything without restriction" guard and errors the autonomous scan every tick — while right-click still works (it bypasses `ClosestThingReachable`). The base `Undefined` request is correct when you supply a custom `PotentialWorkThingsGlobal` set (the vanilla pattern: `WorkGiver_Slaughter`, `WorkGiver_Train`).
- **Self-heal + intent.** A UFT left bound to a spent transient (a failed job) is detected as an orphan again (its bill isn't in any stack's `Bills` list), so it gets retried. By default orphans are finished unconditionally (materials are already sunk); a mod setting instead gates on a live bill still wanting output (respects "do until you have X").

## The three lessons in one sentence

The plan assumed "everything funnels through the `BillStack` property," "jobs target the scanned bench," and "an orphan can rejoin its bill" — decompiling the 1.6 `Assembly-CSharp.dll` and watching the running game proved all three false (the UI reads the field; UFT jobs target the representative; the single `BoundUft` slot is occupied under load), so the design became a field-swap, a UFT job-retarget, and transient-bill orphan recovery. **Verify a target version's actual code — and observe the running game — before trusting an assumed mechanism.**
