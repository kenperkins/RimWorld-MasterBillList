using Verse;

namespace MasterBillList
{
    // The def-side descriptor for our comp. RimWorld reads a building def's
    // <comps> list and, for each CompProperties, instantiates the matching
    // ThingComp (here, CompMasterBillSubscriber) on every spawned building.
    // We add this to workbench defs at startup (see StartupComps).
    public class CompProperties_MasterBillSubscriber : CompProperties
    {
        public CompProperties_MasterBillSubscriber()
        {
            compClass = typeof(CompMasterBillSubscriber);
        }
    }
}
