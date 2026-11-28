using HarmonyLib;
using Il2CppReloaded.Gameplay;

namespace PvZReCoreLib.Content.Plants.Patches;

// The "tile is occupied" answer (NotHere) becomes Ok for the two opt-in cases in PlantRepair.cs:
// a nut healing its damaged twin (CustomPlantDefinition.m_healsOnReplant) and a plant that goes on top of
// another plant (m_plantsOnTopOfPlant). Postfix like Board_CanPlantAt_StackablePlant_Patch: native CanPlantAt
// keeps every other check (graves, water, roof, Versus zones...) and anything that is not NotHere passes through.
[HarmonyPatch(typeof(Board), nameof(Board.CanPlantAt), new[] { typeof(int), typeof(int), typeof(SeedType) })]
public class Board_CanPlantAt_PlantOnPlant_Patch
{
    public static void Postfix(Board __instance, int theGridX, int theGridY, SeedType theType, ref PlantingReason __result)
    {
        if (__result != PlantingReason.NotHere)
        {
            return;
        }

        try
        {
            // The other order (the vine first, then any plant, native or custom, on top of it) applies to every
            // seed type; the heal / on-top checks are only for custom plants.
            if (PlantRepair.CanPlantOnVine(__instance, theGridX, theGridY, theType)
                || (CustomContentRegistry.IsValidCustomPlantType(theType)
                    && (PlantRepair.CanHealOn(__instance, theGridX, theGridY, theType)
                        || PlantRepair.CanPlantOnTop(__instance, theGridX, theGridY, theType))))
            {
                __result = PlantingReason.Ok;
            }
        }
        catch (Exception e)
        {
            // Runs every frame for the cursor preview; never let a failure escape into the native caller.
            MelonLoader.MelonLogger.Error($"[CoreLib] Plant-on-plant CanPlantAt check failed: {e.Message}");
        }
    }
}
