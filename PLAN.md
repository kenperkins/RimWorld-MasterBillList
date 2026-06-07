# Master Bill List — RimWorld Mod Implementation Plan

## For the implementer (read first)

This is a C# RimWorld mod. The goal: let multiple workbenches **subscribe to one shared bill list** instead of each maintaining its own. Edit the list once, and every subscribed bench reflects the change live — not a copy/paste snapshot like existing mods do.

We are using **"Option A": a genuinely shared `BillStack`**, redirected via a Harmony postfix. The alternative (mirroring clones onto each bench) was rejected for this build because Option A gives correct shared-counter behavior for "Do X times" bills *for free* — a single stack means a single counter, which is exactly what we want.

**Before writing any patch code, verify the target version.** Method names and signatures around the bill system (`Building_WorkTable.BillStack`, `Bill_Production.Notify_IterationCompleted`, `BillStack` constructor) have drifted slightly across 1.4 / 1.5 / 1.6. Confirm against the decompiled `Assembly-CSharp.dll` for the version in `About.xml` before relying on any signature below.

---

## Core architecture

### The redirect (the whole trick)

`Building_WorkTable.BillStack` is a property getter (`get_BillStack`). Every consumer — the work scanner (`WorkGiver_DoBill`), the Bills tab UI (`ITab_Bills`), everything — reads bills through it. We Harmony-**postfix** that single getter: if the bench is subscribed to a master list, return the master's `BillStack` instead of the bench's own.

Because all consumers funnel through that one getter, we redirect the entire game by patching one method. We do **not** need a custom bill editor — opening any subscribed bench's Bills tab shows the shared stack and edits propagate automatically.

### Why a shared stack is safe (and where it isn't)

- `WorkGiver_DoBill.JobOnThing(pawn, thing, ...)` finds ingredients and sets the job target relative to the **passed-in bench** (`thing`), not `bill.billStack.billGiver`. So per-bench ingredient search and pathing stay correct even with a shared stack.
- The danger zone is the handful of vanilla spots that read `bill.billStack.billGiver` to mean "the bench" — most importantly `bill.Map`, which resolves through the stack's `billGiver`. If a subscriber is on a *different map* than the stack's representative giver, `TargetCount` ("Do until you have X") would count items on the wrong map.

**Decision: master lists are MAP-SCOPED** (a `MapComponent`). This guarantees the representative giver and all subscribers share a map, so `bill.Map` is always correct and `TargetCount` works. Cross-map sharing is a possible future enhancement that would require patching the count logic; it is explicitly out of scope for v1.

### The "representative billGiver" problem

A `BillStack` is constructed with an `IBillGiver` owner and stores it in `billStack.billGiver`. A shared stack can only point at one. Plan:

- The master list's stack `billGiver` points at a **representative subscriber** (the first bench to join).
- If the representative is deconstructed/destroyed, **repoint** to another current subscriber.
- If the list has zero subscribers, it is dormant (no valid giver); that's fine — nothing scans it.

### Save/load — the duplication trap

`Building_WorkTable.ExposeData` deep-saves its **field** `billStack` via `Scribe_Deep.Look(ref billStack, "billStack", this)`. If three benches all referenced the same stack object, the save system would serialize it three times and reload three separate copies — silently breaking the link.

Solution:
- The **`MapComponent` is the sole deep-owner** of master `BillStack`s. It is the only thing that `Scribe_Deep`s them.
- Each bench's own field `billStack` continues to save normally (it just goes unused while subscribed). The postfix only redirects *reads*, so saves are not corrupted.
- Subscribers persist only a **stable list ID** and re-resolve the object reference on load (see Phase 4).

---

## Project structure

```
MasterBillList/
├── About/
│   ├── About.xml                 # packageId, supportedVersions, Harmony dependency + loadAfter
│   └── Preview.png               # optional
├── Assemblies/
│   └── MasterBillList.dll         # build output
├── Languages/English/Keyed/
│   └── MasterBillList.xml         # UI strings
└── Source/MasterBillList/
    ├── MasterBillList.csproj       # refs: Assembly-CSharp, UnityEngine, 0Harmony
    ├── Mod.cs                      # Harmony bootstrap (PatchAll)
    ├── StartupComps.cs             # [StaticConstructorOnStartup] comp injection
    ├── CompProperties_MasterBillSubscriber.cs
    ├── CompMasterBillSubscriber.cs
    ├── MasterBillManager.cs        # MapComponent: owns lists, save/load, relink
    ├── MasterBillEntry.cs          # one master list: id, name, BillStack
    ├── HarmonyPatches.cs           # get_BillStack postfix + audited patches
    └── Dialog_MasterBillLists.cs   # UI
```

Suggested build tooling: `Krafs.Rimworld.Ref` NuGet for game references and `Lib.Harmony` NuGet, so the project builds without copying game DLLs locally. Output the DLL into `Assemblies/`.

---

## Components

### `CompMasterBillSubscriber` (+ `CompProperties_`)
Per-bench subscription state.
- Stores the subscribed list's **stable ID** (saved), resolves to a `MasterBillEntry` at runtime.
- `IsSubscribed`, `MasterStack` (=> entry?.Stack).
- On subscribe: register with the map manager; if the list has no representative giver yet, become it. Decide handling of the bench's pre-existing bills (see Open Decisions).
- On unsubscribe / despawn / destroy: deregister; if this bench was the representative, ask the manager to repoint.
- `PostExposeData` saves the list ID; `PostSpawnSetup` / map-component `FinalizeInit` performs the relink.

### `MasterBillEntry`
- `int loadID` (stable, assigned by manager), `string label`, `BillStack stack`.
- `ExposeData` deep-saves the `BillStack`. On load the `billGiver` is left null and set during relink.

### `MasterBillManager : MapComponent`
- `List<MasterBillEntry> lists`, `int nextID`.
- `ExposeData`: `Scribe_Collections.Look(ref lists, "lists", LookMode.Deep)` — **the only deep owner of the stacks.**
- API: `CreateList(name)`, `DeleteList(id)`, `RenameList(id, name)`, `GetByID(id)`, `RegisterSubscriber(comp)`, `UnregisterSubscriber(comp)`, `RepointRepresentative(entry)`.
- `FinalizeInit`: after load, set each entry's `stack.billGiver` to a current subscriber (representative).

### `HarmonyPatches`
- **Primary:** postfix on `Building_WorkTable.BillStack` getter →
  ```csharp
  [HarmonyPatch(typeof(Building_WorkTable), nameof(Building_WorkTable.BillStack), MethodType.Getter)]
  static class Patch_BillStack {
      static void Postfix(Building_WorkTable __instance, ref BillStack __result) {
          var comp = __instance.GetComp<CompMasterBillSubscriber>();
          if (comp != null && comp.IsSubscribed && comp.MasterStack != null)
              __result = comp.MasterStack;
      }
  }
  ```
  Keep this **hot-path cheap** — cache the comp reference on the comp/bench, don't allocate.
- **Audit patches** (only add the ones that prove necessary in Phase 6 testing): anything reading `bill.billStack.billGiver` for position/visuals. The ingredient-radius gizmo ring draws at the representative bench — cosmetic, low priority.

### `StartupComps` — comp injection
`[StaticConstructorOnStartup]` iterating `DefDatabase<ThingDef>.AllDefsListForReading`; for each def where `typeof(Building_WorkTable).IsAssignableFrom(def.thingClass)`, add `new CompProperties_MasterBillSubscriber()` to `def.comps`. This covers modded workbenches, not just vanilla ones.

### `Dialog_MasterBillLists` (UI)
- Gizmo on selected workbench(es): "Master bill list" → create new / join existing / leave / rename.
- Multi-select: selecting several benches and choosing a list assigns all of them at once.
- The dialog lists all master lists on the map with member counts and create/delete/rename controls.
- No custom bill editor needed — bills are edited through the standard Bills tab of any subscribed bench.

---

## Phased build (each phase ends with a concrete check)

**Phase 0 — Scaffold.** Folder structure, `About.xml` (declare Harmony dependency + `loadAfter`), `.csproj`, Harmony bootstrap with `PatchAll`. ✅ *Check: mod loads, a startup log line prints, no red errors.*

**Phase 1 — Comp injection.** Comp + properties + `StartupComps`. ✅ *Check: inspect any vanilla and any modded workbench in-game; both report having `CompMasterBillSubscriber`.*

**Phase 2 — Manager + data.** `MasterBillManager` MapComponent, `MasterBillEntry`, create/delete via debug actions (no UI). ✅ *Check: debug action creates a list; it appears in the manager.*

**Phase 3 — The redirect.** `get_BillStack` postfix. Subscribe a bench via debug. ✅ *Check: open the bench's Bills tab → it shows the master stack; add a bill on bench A → it appears on bench B's tab; both benches actually work the bill.*

**Phase 4 — Save/load.** Stable IDs, `Scribe` in manager only, relink in `FinalizeInit`, representative assignment. ✅ *Check: save with 3 subscribed benches + bills, reload → still one shared list, link intact, bills present, pawns resume work.*

**Phase 5 — UI.** Gizmo + `Dialog_MasterBillLists`, including multi-select assign. ✅ *Check: full create/join/leave/rename flow works from the UI with no debug actions.*

**Phase 6 — Edge cases & billGiver audit.** Representative deconstruct → repoint; list with zero members goes dormant cleanly; deconstruct a subscriber; verify `TargetCount` counts correctly; verify `Do X times` decrements once per craft across all benches (not per-bench). Add any `billGiver` audit patches found necessary. ✅ *Check: each scenario behaves; no null-refs in log.*

**Phase 7 — Polish.** Translation keys, optional mod settings, conflict sanity check with Better Workbench Management. ✅ *Check: clean load alongside common bill-related mods.*

---

## Known gotchas (carry these into implementation)

- **Save duplication:** only the MapComponent may deep-save the stacks; benches must persist an ID, not the object.
- **Representative dangling:** repoint on deconstruct/destroy; dormant when zero members.
- **Hot getter:** `get_BillStack` runs every scan/UI frame — cache, don't allocate.
- **Comp injection must include modded benches** (iterate by `IsAssignableFrom`, don't hardcode defNames).
- **Unfinished-thing bills** (`Bill_ProductionWithUft`, e.g. some drug/complex-furniture recipes) bind to a specific in-progress thing/worker. A shared stack with two benches starting the same UFT bill is the most likely real bug — **test this explicitly** in Phase 6 and special-case if needed.
- **Signature drift:** re-verify all patched signatures against the target version's decompiled source.

---

## Open decisions (confirm before/with Phase 2)

1. **Target RimWorld version** for `About.xml` and signature verification (1.5? 1.6? both?).
2. **On subscribe**, what happens to the bench's existing bills — discard, or merge into the master list once?
3. **Multi-select UX** for v1 — assign-all-to-one-list now, or single-bench only and batch later?
4. **Mod settings** needed for v1, or hardcode behavior?

(Map-scoped lists and "no custom bill editor" are already decided above; revisit only if requirements change.)
