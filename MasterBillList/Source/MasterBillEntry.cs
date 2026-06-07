using RimWorld;
using Verse;

namespace MasterBillList
{
    // One shared bill list, identified by the workbench def it belongs to.
    // The MasterBillManager is the SOLE deep-owner of these (and thus of the
    // BillStacks) — that's what prevents the save-duplication trap, since each
    // subscribed bench's own field is swapped away during save.
    public class MasterBillEntry : IExposable
    {
        // The workbench ThingDef.defName this shared list serves (e.g. "TableStonecutter").
        public string defName;

        // The genuinely shared BillStack. Stable per (def, map).
        public BillStack stack;

        public void ExposeData()
        {
            Scribe_Values.Look(ref defName, "defName");

            // BillStack has no parameterless ctor, so pass a ctor arg for load.
            // We pass a null IBillGiver here; the representative billGiver is
            // re-established when the first subscribed bench re-applies the swap
            // after load (see CompMasterBillSubscriber.ApplyShared).
            Scribe_Deep.Look(ref stack, "stack", new object[] { null });
        }
    }
}
