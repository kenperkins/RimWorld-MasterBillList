using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MasterBillList
{
    // Orphan recovery (M1+M3). Sharing one bill across parallel benches races the
    // single Bill_ProductionWithUft.BoundUft slot: when several pawns start the same
    // UFT recipe at once, only one binds. The losers' UnfinishedThing.BoundBill getter
    // sees bill.BoundUft != this and nulls itself out, leaving an orphan that vanilla
    // never resumes (WorkGiver_DoBill only offers the single BoundUft when occupied;
    // No Job Authors only relaxes the creator gate, not that lockout).
    //
    // This WorkGiver finishes orphaned UFTs at a compatible bench. The decisive part
    // (M3): orphans are finished through a TRANSIENT Bill_ProductionWithUft built per
    // job, so finishing never needs the shared bill's slot to be free — which, under
    // continuous production, it essentially never is (proven in-game: every bench
    // reported slotBoundUft occupied). We still gate on a live, ShouldDoNow bill of the
    // recipe existing on the bench, so we respect "do until you have X" targets and
    // don't overproduce. The right-click path also finishes genuinely-bound UFTs (resume
    // their own bill) so "finish this" is consistent on any unfinished item.
    public class WorkGiver_FinishOrphanedUft : WorkGiver_Scanner
    {
        // Deliberately NOT overriding PotentialWorkThingRequest: the base returns
        // Undefined, which GenClosest.EarlyOutSearch permits when a custom search set
        // (PotentialWorkThingsGlobal) is supplied. Returning ForGroup(Everything) here
        // tripped its "searching everything without restriction" guard, which errored
        // the autonomous scan every tick (right-click still worked — it skips the scan).
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        // Orphan = no bill claims it, OR it's bound to one of our transient bills (which
        // live only for one job and are never added to a real stack). The latter lets a
        // finish job that failed mid-way self-heal back into the orphan pool.
        private static bool IsOrphan(UnfinishedThing uft)
        {
            var bb = uft.BoundBill;
            if (bb == null)
                return true;
            return bb.billStack == null || !bb.billStack.Bills.Contains(bb);
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            var map = pawn.Map;
            foreach (var uftDef in OrphanedUftRegistry.UftDefs)
            {
                var list = map.listerThings.ThingsOfDef(uftDef);
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] is UnfinishedThing uft
                        && uft.Recipe != null
                        && IsOrphan(uft)
                        && OrphanedUftRegistry.RecipeWorkType.TryGetValue(uft.Recipe, out var wt)
                        && wt == def.workType)
                    {
                        yield return uft;
                    }
                }
            }
        }

        // Called autonomously only for orphans (the global set), but also directly by
        // the right-click order for ANY unfinished item — to a player a UFT is a UFT.
        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is UnfinishedThing uft) || uft.Recipe == null)
                return null;
            var recipe = uft.Recipe;
            // Each injected giver owns one work type; without this, the right-click path
            // (which bypasses PotentialWorkThingsGlobal) offers e.g. a component under
            // the Art giver — "Cannot finish: Not assigned to art".
            if (!OrphanedUftRegistry.RecipeWorkType.TryGetValue(recipe, out var rwt) || rwt != def.workType)
                return null;
            if (uft.IsForbidden(pawn) || !pawn.CanReserve(uft, 1, -1, null, forced))
                return null;
            if (recipe.FirstSkillRequirementPawnDoesntSatisfy(pawn) != null)
                return null;

            // Genuinely claimed by a real bill => resume that bill (its slot already
            // holds this uft, so no theft). Orphan => finish via a fresh transient bill.
            bool orphan = IsOrphan(uft);
            var boundBill = orphan ? null : uft.BoundBill;

            var map = pawn.Map;
            foreach (var benchDef in recipe.AllRecipeUsers)
            {
                var benches = map.listerThings.ThingsOfDef(benchDef);
                for (int i = 0; i < benches.Count; i++)
                {
                    if (!(benches[i] is Building_WorkTable bench))
                        continue;
                    if (!bench.CurrentlyUsableForBills() || bench.IsBurning())
                        continue;
                    if (!pawn.CanReserve(bench, 1, -1, null, forced) || !pawn.CanReach(bench, PathEndMode.InteractionCell, MaxPathDanger(pawn)))
                        continue;

                    Bill_ProductionWithUft bill;
                    if (boundBill != null)
                    {
                        bill = boundBill; // bench just needs to support the recipe (loop guarantees it)
                    }
                    else
                    {
                        // Orphan finished via a transient bill (shared slot is irrelevant).
                        // By default we finish regardless — the materials are already sunk.
                        // If the player opts to respect targets, gate on a live ShouldDoNow
                        // bill of this recipe still wanting output (BillOnTableForMe). The
                        // bench loop already guarantees bench.def supports the recipe.
                        if (!MasterBillListMod.Settings.finishOrphansEvenIfNoBillWantsThem
                            && uft.BillOnTableForMe(bench) == null)
                            continue;
                        bill = MakeTransientBill(recipe, bench);
                    }
                    return MakeFinishJob(pawn, uft, bill, bench);
                }
            }
            return null;
        }

        // A throwaway bill that drives JobDriver_DoBill's finish toils without consuming
        // the shared bill's single slot. It needs a billStack with a live giver (so
        // Bill.DeletedOrDereferenced is false) but is never added to that stack's list.
        // Forever repeat mode avoids the RepeatCount "bill complete" message/decrement.
        private static Bill_ProductionWithUft MakeTransientBill(RecipeDef recipe, Building_WorkTable bench)
        {
            return new Bill_ProductionWithUft(recipe)
            {
                billStack = bench.BillStack,
                repeatMode = BillRepeatModeDefOf.Forever,
            };
        }

        // Mirrors WorkGiver_DoBill.FinishUftJob, minus the uft.Creator == pawn gate (any
        // pawn may finish) and targeting the scanned bench. JobDriver_DoBill rebinds
        // targetQueueB[0].BoundBill to job.bill on start, so the transient adopts the uft.
        // Uses our JobDef (JobDriver_FinishOrphanedUft) rather than DoBill so the orphan is
        // reserved exclusively — a same-tick duplicate dispatch fails instead of racing.
        private static Job MakeFinishJob(Pawn pawn, UnfinishedThing uft, Bill_ProductionWithUft bill, Building_WorkTable bench)
        {
            Job haul = WorkGiverUtility.HaulStuffOffBillGiverJob(pawn, bench, uft);
            if (haul != null && haul.targetA.Thing != uft)
                return haul;

            Job job = JobMaker.MakeJob(OrphanedUftRegistry.FinishOrphanedUftJob, bench);
            job.bill = bill;
            job.targetQueueB = new List<LocalTargetInfo> { uft };
            job.countQueue = new List<int> { 1 };
            job.haulMode = HaulMode.ToCellNonStorage;
            return job;
        }
    }
}
