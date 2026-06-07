using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MasterBillList
{
    // Caches the gizmo icon once at startup. Prefer the vanilla "copy bill
    // settings" icon; if that path ever changes, fall back to a guaranteed
    // built-in so we never show a pink missing-texture box.
    [StaticConstructorOnStartup]
    public static class MBLTex
    {
        public static readonly Texture2D ShareBills =
            ContentFinder<Texture2D>.Get("UI/Commands/CopySettings", reportFailure: false)
            ?? TexCommand.ForbidOff;
    }

    // Per-bench subscription state + the opt-in toggle.
    //
    // IMPORTANT (learned by decompiling 1.6): consumers read bills via TWO
    // routes — WorkGiver_DoBill uses the BillStack *property*, but ITab_Bills
    // uses the billStack *field* directly. A getter-only redirect therefore
    // splits the brain. So instead we SWAP THE FIELD: when subscribed, point
    // the bench's billStack field at the shared stack. The property returns the
    // field, so both routes then agree. The bench's own stack is stashed and
    // restored on opt-out (non-destructive).
    public class CompMasterBillSubscriber : ThingComp
    {
        private bool useShared;

        // The bench's private stack, held aside while it's using the shared one.
        private BillStack ownStack;

        public Building_WorkTable Bench => parent as Building_WorkTable;
        public bool IsSubscribed => useShared;

        private MasterBillManager Manager => Bench?.Map?.GetComponent<MasterBillManager>();

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            // Re-apply the field swap on spawn (covers load once Phase 4 persists
            // the flag, and keeps things consistent if respawned/minified).
            if (useShared)
                ApplyShared();
        }

        public override void PostDeSpawn(Map map, DestroyMode mode)
        {
            base.PostDeSpawn(map, mode);

            // If this bench was the shared stack's representative, hand the role
            // off to another subscriber so the stack's billGiver never dangles.
            if (useShared && map != null)
            {
                map.GetComponent<MasterBillManager>()
                   ?.NotifyRepresentativeDespawning(parent.def, parent as Building_WorkTable);
            }
        }

        public void SetSubscribed(bool value)
        {
            if (useShared == value)
                return;

            useShared = value;
            if (value)
                ApplyShared();
            else
                RestoreOwn();
        }

        private void ApplyShared()
        {
            var bench = Bench;
            var mgr = Manager;
            if (bench == null || mgr == null)
                return;

            // First opted-in bench of this type becomes the stack's representative.
            var shared = mgr.GetOrCreateSharedStack(bench.def, bench);
            if (bench.billStack == shared)
                return; // already pointing at it

            ownStack = bench.billStack;   // stash the bench's own bills
            bench.billStack = shared;     // redirect the field itself
        }

        // The stack to write under the bench during SAVE (instead of the shared
        // one). It's the bench's own stash; a fresh empty stack if we somehow
        // have none. Used by Patch_WorkTable_ExposeData.
        public BillStack SaveStandInStack => ownStack ?? new BillStack(Bench);

        private void RestoreOwn()
        {
            var bench = Bench;
            if (bench == null)
                return;

            // Hand back the stashed own stack (or a fresh empty one if we don't
            // have it — e.g. after a load before Phase 4 persists ownStack).
            bench.billStack = ownStack ?? new BillStack(bench);
            ownStack = null;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!(parent is Building_WorkTable))
                yield break;

            yield return new Command_Toggle
            {
                defaultLabel = "MasterBillList_UseSharedBills_Label".Translate(),
                defaultDesc = "MasterBillList_UseSharedBills_Desc".Translate(),
                icon = MBLTex.ShareBills,
                isActive = () => useShared,
                toggleAction = () => SetSubscribed(!useShared),
            };
        }

        public override string CompInspectStringExtra()
        {
            return useShared ? (string)"MasterBillList_UsingSharedBills".Translate() : null;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            // Persist the opt-in flag. Deep-save of the shared stacks (so bills
            // survive a reload without duplicating) is Phase 4.
            Scribe_Values.Look(ref useShared, "useShared", defaultValue: false);
        }
    }
}
