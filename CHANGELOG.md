# Changelog

All notable changes to **Master Bill List** are documented here.

## 1.1.1 — 2026-07-12

### Fixed
- **No more "Tried to bound destroyed UnfinishedThing" error.** When two crafters were dispatched to the same leftover item in the same instant, one could finish it before the other arrived, and the latecomer would log an error trying to claim the already-finished item (harmless, but noisy — and worse with the CommonSense mod, which drops a guard vanilla has). Finish jobs now reserve the item exclusively, so the second crafter cleanly picks different work instead.

## 1.1 — 2026-07-11

### Added
- **Finish leftover unfinished items.** Sharing one bill list across several benches could strand *orphaned* unfinished items — half-made components, sculptures, or apparel that no bill would ever resume, so they piled up (especially alongside "No Job Authors"). Colonists now finish these automatically at any suitable bench, no micromanagement.
- **Right-click an unfinished item → "finish unfinished items"** to prioritize it by hand (works on any unfinished item, orphaned or not).
- **Mod setting** — *"Finish leftover unfinished items even when no bill wants them"* (on by default; turn off to instead respect "do until you have X" targets).

### Notes
- Retroactive: orphans already sitting in an existing colony start getting finished as soon as you load the save — no new game needed.

## 1.0 — 2026-06-07

### Added
- Initial release: per-type, per-map **shared bill lists** for workbenches — a one-click "Use shared bills" toggle, shared "do until you have X" counters across all shared benches, non-destructive opt-out, safe save/load, and automatic coverage of modded workbenches.
