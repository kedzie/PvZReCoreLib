using HarmonyLib;
using Il2CppReloaded.Gameplay;
using UnityEngine;

namespace PvZReCoreLib.Content.Plants.ZombieCards;

// The game's special-move and edge code was written for zombies walking LEFT. A hypnotized zombie
// walks right (Zombie.IsWalkingBackwards), and a few hard-coded left-hand assumptions then show up:
//
//  1. Board edge: Zombie.CheckForBoardEdge has no mind-control exemption for the LEFT edge, so a
//     hypnotized zombie that ends up past the house line (a vault landing, a push-back, a dancer's
//     walk-in) is treated as an enemy reaching the house and loses the game.
//  2. Pole vault landing: when the vault animation loops, UpdateZombiePolevaulter does
//     mPosX -= 150 (constant read from GameAssembly.dll) to put the zombie past the plant on its
//     left. A hypnotized vaulter is flying right, so that moved it two tiles back across the screen.
//  3. Dancer walk-in: a hypnotized Dancing Zombie (and its backup dancers, which inherit the flag
//     in SummonBackupDancer) takes its DancingIn steps to the left.

// 1. Never let a hypnotized zombie trip the left-edge loss check.
[HarmonyPatch(typeof(Zombie), nameof(Zombie.CheckForBoardEdge))]
public class Zombie_CheckForBoardEdge_MindControlled_Patch
{
    // The loss line is a little left of x = 0 (about -35..-62 depending on the zombie type).
    private const float MinPosX = 0f;

    public static void Prefix(Zombie __instance)
    {
        if (__instance != null && __instance.mMindControlled && __instance.mPosX < MinPosX)
        {
            __instance.mPosX = MinPosX;
        }
    }
}

// 2. Undo the leftwards landing shift for a hypnotized vaulter. The jump itself (velocity =
// (zombieX - plantX - 80) / duration) already carries it across to the right of the plant; only the
// final -150 is wrong, so it is simply put back. A normal per-tick move is a few pixels, so a drop
// of more than 120 in one update can only be that shift.
[HarmonyPatch(typeof(Zombie), nameof(Zombie.UpdateZombiePolevaulter))]
public class Zombie_UpdateZombiePolevaulter_MindControlled_Patch
{
    private const float LandingShift = 150f;
    private const float DetectDrop = 120f;

    public static void Prefix(Zombie __instance, out float __state)
    {
        __state = __instance != null ? __instance.mPosX : 0f;
    }

    public static void Postfix(Zombie __instance, float __state)
    {
        if (__instance == null || !__instance.mMindControlled)
        {
            return;
        }

        if (__instance.mPosX - __state < -DetectDrop)
        {
            __instance.mPosX += LandingShift;
        }
    }
}

// 3. Hold a hypnotized dancer in place while it does its DancingIn walk-in, so it stays where it
// was placed instead of stepping left. Keyed by the zombie object and the frame it was last seen,
// because zombies are pooled and a stale entry must never pin a later zombie.
[HarmonyPatch(typeof(Zombie), nameof(Zombie.Update))]
public class Zombie_Update_MindControlledDancer_Patch
{
    private static readonly Dictionary<IntPtr, (float x, int lastFrame)> pinned = new();

    public static void Postfix(Zombie __instance)
    {
        if (__instance == null || !__instance.mMindControlled || __instance.mZombiePhase != ZombiePhase.DancerDancingIn)
        {
            return;
        }

        int frame = Time.frameCount;
        var key = __instance.Pointer;

        if (pinned.TryGetValue(key, out var entry) && frame - entry.lastFrame <= 3)
        {
            __instance.mPosX = entry.x;
            pinned[key] = (entry.x, frame);
        }
        else
        {
            pinned[key] = (__instance.mPosX, frame);
        }
    }
}
