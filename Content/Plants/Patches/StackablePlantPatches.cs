using HarmonyLib;
using Il2CppReloaded.Gameplay;

namespace PvZReCoreLib.Content.Plants.Patches;

// Lets a stackable custom plant (CustomPlantDefinition.m_maxStack > 1) be planted onto a tile that
// already holds a plant of the same type with room left in its stack - see PlantStacking.cs for the
// whole mechanism. Postfix, not Prefix: native CanPlantAt keeps doing every other check, and this
// only turns the plain "tile is occupied" answer (NotHere) into Ok. Everything that is not NotHere
// (graves, water, Versus zones, ...) passes through untouched.
[HarmonyPatch(typeof(Board), nameof(Board.CanPlantAt), new[] { typeof(int), typeof(int), typeof(SeedType) })]
public class Board_CanPlantAt_StackablePlant_Patch
{
    public static void Postfix(Board __instance, int theGridX, int theGridY, SeedType theType, ref PlantingReason __result)
    {
        if (__result != PlantingReason.NotHere)
        {
            return;
        }

        if (!CustomContentRegistry.IsValidCustomPlantType(theType))
        {
            return;
        }

        try
        {
            if (PlantStacking.CanStackOn(__instance, theGridX, theGridY, theType))
            {
                __result = PlantingReason.Ok;
            }
        }
        catch (Exception e)
        {
            // CanPlantAt runs every frame for the cursor preview; never let a failure here escape
            // into the native caller - fall back to the native answer.
            MelonLoader.MelonLogger.Error($"[CoreLib] Stackable CanPlantAt check failed: {e.Message}");
        }
    }
}
