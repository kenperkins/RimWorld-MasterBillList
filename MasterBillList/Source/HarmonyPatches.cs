using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MasterBillList
{
    // UFT fix. For unfinished-thing recipes (sculptures, components, ...) the
    // vanilla finish-job (WorkGiver_DoBill.FinishUftJob) targets
    // bill.billStack.billGiver — the shared stack's single representative bench —
    // instead of the bench actually being scanned. With shared bills that means
    // finishing funnels to the representative, and if it's busy or being
    // deconstructed the pawn gets a DoBill job it can't reserve (the reservation
    // storm seen in testing). The pawn already proved it can reserve the scanned
    // bench, so retarget the job there. Normal (non-UFT) jobs already target the
    // scanned bench, so this is a no-op for them.
    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
    public static class Patch_DoBill_RetargetToScannedBench
    {
        public static void Postfix(Thing thing, ref Job __result)
        {
            if (__result == null || __result.def != JobDefOf.DoBill)
                return;
            if (!(thing is Building_WorkTable wt))
                return;
            if (__result.targetA.Thing == thing)
                return; // already correct (normal bills)

            var comp = wt.GetComp<CompMasterBillSubscriber>();
            if (comp != null && comp.IsSubscribed)
                __result.targetA = wt; // finish at the bench we're actually scanning
        }
    }

    // The save-duplication guard.
    //
    // Building_WorkTable.ExposeData does Scribe_Deep.Look(ref billStack, ...).
    // A subscribed bench's billStack field points at the SHARED stack, so if we
    // let it save normally, every subscriber would deep-save the same object and
    // reload as separate copies — silently breaking the link.
    //
    // So during SAVE only, swap the bench's own stash back into the field for the
    // duration of ExposeData, then restore the shared reference. The
    // MasterBillManager remains the sole deep-owner of shared stacks.
    [HarmonyPatch(typeof(Building_WorkTable), nameof(Building_WorkTable.ExposeData))]
    public static class Patch_WorkTable_ExposeData
    {
        public static void Prefix(Building_WorkTable __instance, ref BillStack __state)
        {
            __state = null;
            if (Scribe.mode != LoadSaveMode.Saving)
                return;

            var comp = __instance.GetComp<CompMasterBillSubscriber>();
            if (comp != null && comp.IsSubscribed)
            {
                __state = __instance.billStack;            // remember the shared stack
                __instance.billStack = comp.SaveStandInStack; // save the bench's own instead
            }
        }

        public static void Postfix(Building_WorkTable __instance, BillStack __state)
        {
            if (__state != null)
                __instance.billStack = __state;            // restore the shared reference
        }
    }
}
