using HarmonyLib;
using Il2CppReloaded.Gameplay;
using PvZReCoreLib.Util;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PvZReCoreLib.Content.Plants.Patches;

// Controller shortcut for the seed chooser's pages: LB = previous page, RB = next page, so a
// player on a gamepad doesn't have to walk the d-pad over to the on-screen arrow buttons.
//
// This only polls the pad and calls the same GoToPreviousPage/GoToNextPage the arrow buttons
// call (see SeedChooserScreen_InjectPagingButtons_Patch), so page bounds, the grid re-wire and
// the focus restore all behave exactly as for a button click. Gated on the active TreeState the
// same way that patch is, so it never runs outside the chooser.
[HarmonyPatch(typeof(SeedChooserScreen), nameof(SeedChooserScreen.Update))]
public class SeedChooserScreen_ShoulderPaging_Patch
{
    // Same names SeedChooserScreen_InjectPagingButtons_Patch gates on.
    private static readonly string[] SeedChooserStateNames = { "SeedChooser", "ChooseSeeds" };

    private static int lastHandledFrame = -1;

    public static void Postfix(SeedChooserScreen __instance)
    {
        var pad = Gamepad.current;
        if (pad == null)
        {
            return;
        }

        bool previous = pad.leftShoulder.wasPressedThisFrame;
        bool next = pad.rightShoulder.wasPressedThisFrame;
        if (!previous && !next)
        {
            return;
        }

        var activeState = ReplantedGet.TreeStateManager()?.Active;
        if (activeState == null || Array.IndexOf(SeedChooserStateNames, activeState.name) < 0)
        {
            return;
        }

        var dataModel = SeedChooserDataModel_CustomPlantEntriesPatch.CachedDataModel;
        if (dataModel == null)
        {
            return;
        }

        // Update() can run more than once in a frame; one press is one page turn.
        if (Time.frameCount == lastHandledFrame)
        {
            return;
        }
        lastHandledFrame = Time.frameCount;

        SeedChooserPaging.BeginFocusRestore();

        if (next)
        {
            SeedChooserPaging.GoToNextPage(dataModel, __instance);
        }
        else
        {
            SeedChooserPaging.GoToPreviousPage(dataModel, __instance);
        }
    }
}
