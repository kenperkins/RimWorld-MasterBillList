using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MasterBillList
{
    // Runs once at game startup, after all defs are loaded. We walk every
    // ThingDef and attach our subscriber comp to anything that IS a workbench
    // (Building_WorkTable or a subclass). Testing by IsAssignableFrom — rather
    // than a hardcoded list of defNames — means we also cover workbenches added
    // by other mods, not just vanilla ones.
    [StaticConstructorOnStartup]
    public static class StartupComps
    {
        static StartupComps()
        {
            int count = 0;

            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.thingClass == null)
                    continue;
                if (!typeof(Building_WorkTable).IsAssignableFrom(def.thingClass))
                    continue;

                def.comps ??= new List<CompProperties>();

                // Defensive: don't attach twice if this somehow runs again.
                if (def.comps.Any(c => c is CompProperties_MasterBillSubscriber))
                    continue;

                def.comps.Add(new CompProperties_MasterBillSubscriber());
                count++;
            }

            Log.Message($"[MasterBillList] Phase 1: attached CompMasterBillSubscriber to {count} workbench defs.");
        }
    }
}
