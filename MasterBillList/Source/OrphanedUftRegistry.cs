using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace MasterBillList
{
    // Runs once at startup (after defs are resolved, so WorkTypeDef.workGiversByPriority
    // is already built). Two jobs:
    //   1. Cache which UFT thing defs exist and map each UFT recipe to the work type
    //      it's done under, so WorkGiver_FinishOrphanedUft can filter orphans by type.
    //   2. Inject one WorkGiverDef per relevant work type, all backed by our giver,
    //      so the autonomous work scanner picks up orphans wherever bills get done.
    //
    // Code-injection (no XML defs) mirrors StartupComps' philosophy: cover modded
    // benches/recipes/work types by enumeration, not a hardcoded list.
    [StaticConstructorOnStartup]
    public static class OrphanedUftRegistry
    {
        public static readonly List<ThingDef> UftDefs = new List<ThingDef>();
        public static readonly Dictionary<RecipeDef, WorkTypeDef> RecipeWorkType = new Dictionary<RecipeDef, WorkTypeDef>();

        // A DoBill-flavored job whose driver reserves the orphan UFT exclusively (see
        // JobDriver_FinishOrphanedUft). Registered in DefDatabase so a saved in-progress
        // finish job resolves its def by name on load.
        public static JobDef FinishOrphanedUftJob { get; private set; }

        static OrphanedUftRegistry()
        {
            InjectJobDef();

            var doBillGivers = DefDatabase<WorkGiverDef>.AllDefsListForReading
                .Where(g => g.workType != null
                            && g.giverClass != null
                            && typeof(WorkGiver_DoBill).IsAssignableFrom(g.giverClass))
                .ToList();

            var uftDefSet = new HashSet<ThingDef>();
            foreach (var recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                if (recipe.unfinishedThingDef == null)
                    continue;

                var wt = ResolveWorkType(recipe, doBillGivers);
                if (wt == null)
                    continue;

                RecipeWorkType[recipe] = wt;
                uftDefSet.Add(recipe.unfinishedThingDef);
            }
            UftDefs.AddRange(uftDefSet);

            int injected = 0;
            foreach (var wt in RecipeWorkType.Values.Distinct().ToList())
            {
                InjectWorkGiver(wt);
                injected++;
            }

            Log.Message($"[MasterBillList] Orphan recovery: mapped {RecipeWorkType.Count} UFT recipe(s) " +
                        $"across {UftDefs.Count} UFT def(s); injected {injected} WorkGiverDef(s).");
        }

        private static void InjectJobDef()
        {
            var def = new JobDef
            {
                defName = "MBL_FinishOrphanedUft",
                driverClass = typeof(JobDriver_FinishOrphanedUft),
                reportString = "finishing unfinished item.",
                allowOpportunisticPrefix = true,
                collideWithPawns = false,
            };
            DefDatabase<JobDef>.Add(def);
            FinishOrphanedUftJob = def;
        }

        private static WorkTypeDef ResolveWorkType(RecipeDef recipe, List<WorkGiverDef> doBillGivers)
        {
            if (recipe.requiredGiverWorkType != null)
                return recipe.requiredGiverWorkType;

            foreach (var benchDef in recipe.AllRecipeUsers)
            {
                foreach (var g in doBillGivers)
                {
                    if (g.fixedBillGiverDefs != null && g.fixedBillGiverDefs.Contains(benchDef))
                        return g.workType;
                }
            }
            return null;
        }

        private static void InjectWorkGiver(WorkTypeDef wt)
        {
            var list = wt.workGiversByPriority;

            // Run BEFORE DoBill within the work type: finishing an orphan's partial
            // work must beat starting a fresh bill, or pawns always start new and the
            // shared bill's single slot is never free for an orphan to drain. We only
            // ever yield a job when an orphan actually exists AND has a free-slot bill,
            // so this is transparent (no starvation) the rest of the time. Higher
            // priority also leans on the vanilla single-slot lockout: while one orphan
            // occupies the slot, other pawns can't start new UFTs, so they stop
            // generating fresh orphans while the backlog drains.
            int maxPriority = list.Count > 0 ? list.Max(g => g.priorityInType) : 0;

            var def = new WorkGiverDef
            {
                defName = "MBL_FinishOrphanedUft_" + wt.defName,
                label = "finish unfinished items",
                giverClass = typeof(WorkGiver_FinishOrphanedUft),
                workType = wt,
                priorityInType = maxPriority + 1,
                verb = "finish",
                gerund = "finishing unfinished items",
                scanThings = true,
                scanCells = false,
                emergency = false,
                directOrderable = true,
                requiredCapacities = new List<PawnCapacityDef> { PawnCapacityDefOf.Manipulation },
                tagToGive = JobTag.MiscWork,
            };

            DefDatabase<WorkGiverDef>.Add(def);

            // Keep workGiversByPriority in descending priorityInType order (how
            // WorkTypeDef.ResolveReferences built it); priorityInType max+1 lands first.
            int idx = 0;
            while (idx < list.Count && list[idx].priorityInType >= def.priorityInType)
                idx++;
            list.Insert(idx, def);
        }
    }
}
