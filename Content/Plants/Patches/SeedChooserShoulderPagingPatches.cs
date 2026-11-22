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
        // Poll EVERY connected pad, not just Gamepad.current: that is only whichever pad produced
        // the most recent input event. In co-op a stick nudge on player 1's pad in the same frame
        // as player 2's LB/RB press makes Gamepad.current player 1's pad, and the press on the
        // other pad was silently never seen - "controller page turning fails".
        bool previous = false;
        bool next = false;
        Gamepad source = null;

        var pads = Gamepad.all;
        int padCount = pads.Count;
        for (int i = 0; i < padCount; i++)
        {
            var pad = pads[i];
            if (pad == null)
            {
                continue;
            }

            if (pad.leftShoulder.wasPressedThisFrame)
            {
                previous = true;
                source ??= pad;
            }

            if (pad.rightShoulder.wasPressedThisFrame)
            {
                next = true;
                source = pad;
            }
        }

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

        MelonLoader.MelonLogger.Msg($"[CoreLib][CoopDiag] Shoulder button: {(next ? "RB (next page)" : "LB (previous page)")} from pad '{source?.displayName}' (deviceId {source?.deviceId}), {padCount} pad(s) connected.");

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
