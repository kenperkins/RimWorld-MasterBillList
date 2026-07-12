using RimWorld;
using Verse;
using Verse.AI;

namespace MasterBillList
{
    // Finish job for orphaned UFTs. Identical to vanilla JobDriver_DoBill except it
    // reserves the queued UnfinishedThing EXCLUSIVELY up front.
    //
    // Why: vanilla JobDriver_DoBill.TryMakePreToilReservations only ReserveAsManyAsPossible's
    // targetQueueB (best-effort — it never fails the job if the reservation is lost). That's
    // fine for fungible ingredient stacks, but our finish jobs draw from a shared pool of
    // orphans, so in a same-scan-tick window two crafters can both be dispatched to the same
    // orphan. The loser walks over to bind a UFT the winner already finished-and-destroyed.
    // Vanilla's bind toil guards that (`{ Destroyed: false }`); CommonSense's reimplementation
    // drops the guard and logs "Tried to bound destroyed UnfinishedThing". Claiming the UFT
    // exclusively here makes the losing job fail cleanly at start, so it never reaches the bind.
    public class JobDriver_FinishOrphanedUft : JobDriver_DoBill
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!base.TryMakePreToilReservations(errorOnFailed))
                return false;

            var queue = job.GetTargetQueue(TargetIndex.B);
            if (queue != null && queue.Count == 1 && queue[0].Thing is UnfinishedThing)
            {
                // errorOnFailed: false — losing the race is expected, not an error.
                if (!pawn.Reserve(queue[0], job, 1, -1, null, errorOnFailed: false))
                    return false;
            }
            return true;
        }
    }
}
