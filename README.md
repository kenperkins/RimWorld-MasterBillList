# Master Bill List

A RimWorld 1.6 mod that lets workbenches of the same type **share one bill list per map**.

Toggle **"Use shared bills"** on a bench, edit its bills through the normal Bills tab, and every other opted-in bench of that type reflects the change live — and works the same bills. No more re-creating the same bill on every butcher table, stove, or machining bench.

## Features

- **Per-type, per-map shared bill lists** — all opted-in butcher tables share one list; all stonecutters share another; etc.
- **One-click opt-in** — a "Use shared bills" toggle on each bench (works with multi-select).
- **Non-destructive** — opting out restores the bench's own original bills; nothing is ever deleted.
- **Shared counters for free** — "Do X times" / "Do until you have X" count once across all shared benches, because they share a single bill stack.
- **Safe save/load** — the shared lists persist correctly with no duplication.
- **Robust** — handles deconstructing the representative bench and unfinished-thing recipes (sculptures, components).
- **Covers modded workbenches** automatically (comp is injected by type, not by hardcoded def names).

## How it works (technical)

Each opted-in bench has its `billStack` field pointed at a per-type shared `BillStack` owned by a `MapComponent`. Because the field itself is swapped, both the work scanner (which reads the `BillStack` property) and the Bills tab UI (which reads the `billStack` field) see the same list. A save-time Harmony guard prevents the shared stack from being deep-saved by multiple benches.

## Requirements

- RimWorld 1.6
- [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)

## Building

```sh
cd MasterBillList/Source
dotnet build -c Release
```

Output goes to `MasterBillList/Assemblies/MasterBillList.dll`. Uses `Krafs.Rimworld.Ref` (game reference assemblies) and `Lib.Harmony` via NuGet — no game DLLs needed locally.

## Installing locally

Symlink (or copy) the `MasterBillList/` folder into your RimWorld `Mods/` directory and enable it (after Harmony) in the mod list.
