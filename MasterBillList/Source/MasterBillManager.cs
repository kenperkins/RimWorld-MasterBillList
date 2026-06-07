using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MasterBillList
{
    // One per map (RimWorld auto-creates a MapComponent of each subclass for
    // every map). Owns the shared BillStacks — one per workbench type — and
    // hands them out. Map-scoped on purpose: it keeps every member of a shared
    // list on the same map, which is what keeps "Do until you have X" counts
    // correct (see PLAN.md). Save/load of these stacks arrives in Phase 4.
    public class MasterBillManager : MapComponent
    {
        private List<MasterBillEntry> entries = new List<MasterBillEntry>();

        public MasterBillManager(Map map) : base(map) { }

        public int EntryCount => entries.Count;

        // Look up the shared stack for a workbench type, creating it on first
        // use. A BillStack needs an IBillGiver owner; in Phase 2 we may not have
        // a subscriber yet, so representative can be null and gets adopted later
        // (Phase 3, when the first bench opts in).
        public BillStack GetOrCreateSharedStack(ThingDef workbenchDef, IBillGiver representative = null)
        {
            var entry = entries.FirstOrDefault(e => e.defName == workbenchDef.defName);
            if (entry == null)
            {
                entry = new MasterBillEntry
                {
                    defName = workbenchDef.defName,
                    stack = new BillStack(representative)
                };
                entries.Add(entry);
            }
            else if (representative != null && entry.stack.billGiver == null)
            {
                // We had a dormant stack with no owner; adopt the new representative.
                entry.stack.billGiver = representative;
            }

            return entry.stack;
        }

        // Read-only peek used by debug/inspection; null if no shared stack exists yet.
        public BillStack GetSharedStackOrNull(ThingDef workbenchDef)
        {
            return entries.FirstOrDefault(e => e.defName == workbenchDef.defName)?.stack;
        }

        // Called when a subscribed bench is despawning/being destroyed. A shared
        // BillStack stores ONE billGiver (the "representative"); if the leaving
        // bench is it, bill.Map would resolve to a destroyed thing and any
        // TargetCount bill would NullReference on the next scan/UI draw. So
        // repoint to another spawned, subscribed bench of the same type — or to
        // null (dormant) if none remain, which is safe because nothing scans a
        // list with no members.
        public void NotifyRepresentativeDespawning(ThingDef workbenchDef, Building_WorkTable leaving)
        {
            var entry = entries.FirstOrDefault(e => e.defName == workbenchDef.defName);
            if (entry?.stack == null || entry.stack.billGiver != leaving)
                return; // leaving bench wasn't the representative

            Building_WorkTable replacement = null;
            foreach (var t in map.listerThings.ThingsOfDef(workbenchDef))
            {
                if (t == leaving || !(t is Building_WorkTable wt) || !wt.Spawned)
                    continue;

                var c = wt.GetComp<CompMasterBillSubscriber>();
                if (c != null && c.IsSubscribed)
                {
                    replacement = wt;
                    break;
                }
            }

            entry.stack.billGiver = replacement; // null => dormant (no subscribers left)
        }

        public override void ExposeData()
        {
            base.ExposeData();

            // The manager is the ONLY place the shared BillStacks are deep-saved.
            // Subscribed benches have their field swapped to their own stash during
            // save (see Patch_WorkTable_ExposeData), so the shared object is
            // serialized exactly once here and re-linked on load.
            Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && entries == null)
                entries = new List<MasterBillEntry>();
        }
    }
}
