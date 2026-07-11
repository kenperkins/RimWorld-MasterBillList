using HarmonyLib;
using UnityEngine;
using Verse;

namespace MasterBillList
{
    // Subclassing Verse.Mod gives us the standard mod entry point. Its
    // constructor runs once while mods are loading — the canonical place to
    // bootstrap Harmony and load mod settings.
    public class MasterBillListMod : Mod
    {
        public static MasterBillListSettings Settings;

        public MasterBillListMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<MasterBillListSettings>();

            var harmony = new Harmony("kenperkins.masterbilllist");

            // PatchAll scans this assembly for [HarmonyPatch] classes. Sharing works
            // via direct field-swap (no patch); the patches cover save/load and the
            // UFT job retarget.
            harmony.PatchAll();

            Log.Message("[MasterBillList] Loaded (sharing + save/load + repoint + UFT retarget + orphan recovery).");
        }

        public override string SettingsCategory() => "Master Bill List";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var ls = new Listing_Standard();
            ls.Begin(inRect);
            ls.CheckboxLabeled(
                "Finish leftover unfinished items even when no bill wants them",
                ref Settings.finishOrphansEvenIfNoBillWantsThem,
                "On (default): colonists complete orphaned unfinished items regardless of bill targets — the materials are already spent.\n\n" +
                "Off: orphans are only finished while a bill of that recipe still wants output (respects \"do until you have X\").");
            ls.End();
        }
    }
}
