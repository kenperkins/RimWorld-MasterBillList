using HarmonyLib;
using Verse;

namespace MasterBillList
{
    // Subclassing Verse.Mod gives us the standard mod entry point. Its
    // constructor runs once while mods are loading — the canonical place to
    // bootstrap Harmony (and, later, to hold mod settings).
    public class MasterBillListMod : Mod
    {
        public MasterBillListMod(ModContentPack content) : base(content)
        {
            var harmony = new Harmony("kenperkins.masterbilllist");

            // PatchAll scans this assembly for [HarmonyPatch] classes. Phase 3
            // works via direct field-swap (no patch needed); we keep the bootstrap
            // because Phase 4 will patch Building_WorkTable.ExposeData for safe
            // save/load.
            harmony.PatchAll();

            Log.Message("[MasterBillList] Loaded (sharing + save/load + repoint + UFT retarget).");
        }
    }
}
