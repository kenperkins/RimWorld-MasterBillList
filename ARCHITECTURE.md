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

## Both lessons in one sentence

The plan assumed "everything funnels through the `BillStack` property" and "jobs target the scanned bench" — decompiling the 1.6 `Assembly-CSharp.dll` proved both false (the UI reads the field; UFT jobs target the representative), so the design moved to a field-swap plus a UFT job-retarget. **Verify a target version's actual code before trusting an assumed mechanism.**
