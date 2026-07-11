using Verse;

namespace MasterBillList
{
    public class MasterBillListSettings : ModSettings
    {
        // Default true: orphaned unfinished items are always finished, since their
        // materials are already sunk into the UFT (cancelling only refunds 75%).
        // When false, an orphan is only finished while a bill of its recipe still
        // wants output (respects "do until you have X"), leaving the rest in place.
        public bool finishOrphansEvenIfNoBillWantsThem = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref finishOrphansEvenIfNoBillWantsThem, "finishOrphansEvenIfNoBillWantsThem", defaultValue: true);
        }
    }
}
