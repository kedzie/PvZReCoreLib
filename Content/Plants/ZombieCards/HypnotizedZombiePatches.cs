using HarmonyLib;
using Il2CppReloaded.Gameplay;
using UnityEngine;

namespace PvZReCoreLib.Content.Plants.ZombieCards;

// The game's special-move and edge code was written for zombies walking LEFT. A hypnotized zombie
// walks right (Zombie.IsWalkingBackwards), and a few hard-coded left-hand assumptions then show up:
//
//  1. Board edge: Zombie.CheckForBoardEdge has no mind-control exemption for the LEFT edge, so a
//     hypnotized zombie that ends up past the house line (a vault landing, a push-back, a dancer's
//     walk-in) is treated as an enemy reaching the house: it loses the game or sets a mower off.
//  2. Pole vault landing: when the vault animation loops, UpdateZombiePolevaulter does
//     mPosX -= 150 (constant read from GameAssembly.dll) to put the zombie past the plant on its
//     left. A hypnotized vaulter is flying right, so that moved it two tiles back across the screen.
//  3. Dancer walk-in: a hypnotized Dancing Zombie (and its backup dancers, which inherit the flag
//     in SummonBackupDancer) takes its DancingIn steps to the left.

// 1. A hypnotized zombie never takes part in the house / lawn mower logic of the board edge.
//    Zombie.CheckForBoardEdge: for a zombie that IsWalkingBackwards (always true when mind-controlled)
//    it first removes it silently when mPosX > 850 (DieNoLoot - this is how a hypnotized zombie leaves at
//    the right), but when mPosX <= 850 it does NOT return: it falls through to the same left-edge code
//    as an enemy - mX <= the type's left threshold (about -46 .. -53 px) -> Board.FindLawnMowerInRow ->
//    LawnMower.StartMower, or Zombie.WalkIntoHouse (a lost game). So a hypnotized zombie that ends up
//    past the house line (a vault landing, a push-back, a dancer's walk-in, a Zomboni launched from the
//    left edge) set mowers off. For a mind-controlled zombie the native method now only runs while it
//    is past the right limit (the removal), and the left-edge logic is skipped altogether.
[HarmonyPatch(typeof(Zombie), nameof(Zombie.CheckForBoardEdge))]
public class Zombie_CheckForBoardEdge_MindControlled_Patch
{
    // Read from GameAssembly.dll: the right-hand removal limit in CheckForBoardEdge.
    private const float RightRemovalX = 850f;

    public static bool Prefix(Zombie __instance)
    {
        if (__instance == null || !__instance.mMindControlled)
        {
            return true;
        }

        // Keep it on the board (the clamp the old patch did).
        if (__instance.mPosX < 0f)
        {
            __instance.mPosX = 0f;
        }

        return __instance.mPosX > RightRemovalX;
    }
}

// 2. Hypnotized vaulter. The native vault moves the sprite with TWO things: mPosX (by mVelX each tick)
// and the "anim_jump" animation itself, whose sprite travels a further ~150 px in the walking direction
// (for a left-walking zombie: leftwards). When the animation ends the game does mPosX -= 150 and the
// animation offset resets, so nothing jumps: the 150 just moves from the animation into mPosX. For a
// zombie walking right (mirrored) three things are wrong, found from game logs and from watching it:
//  a) The velocity is computed on the PreVault -> InVault tick as (zombieX - plantX - 80) / T, negative
//     for a zombie left of the plant, and a mind-controlled zombie moves by +mVelX, so it slid LEFT the
//     whole jump. The distance is also the wrong one: natively mPosX only has to cover the plant distance
//     minus the 150 the animation carries. For the mirrored jump mPosX must cover (d - 80), with
//     d = plantX - zombieX (the native magnitude is (d + 80)), and the sign is the other way. So the
//     velocity is rescaled by (d - 80) / (d + 80) (positive = right; may be slightly negative when the
//     plant is very close, which is fine: the animation carries the zombie).
//  b) At the end the native mPosX -= 150 is the wrong direction. The mirror image (+150) is what keeps the
//     zombie where the animation put it, so the shift is reversed instead of undone: no teleport.
//  c) (unchanged) it jumps over PLANTS (native FindPlantTarget), i.e. over our own plants; vaulting over
//     enemy zombies instead is a design decision, not done.
[HarmonyPatch(typeof(Zombie), nameof(Zombie.UpdateZombiePolevaulter))]
public class Zombie_UpdateZombiePolevaulter_MindControlled_Patch
{
    private const float DetectDrop = 120f;

    // Native: velX = (zombieX - plantX - 80) / T, so |native distance| = d + 80.
    private const float PlantOffset = 80f;

    public struct VaultState
    {
        public float PosX;
        public ZombiePhase Phase;
    }

    public static void Prefix(Zombie __instance, out VaultState __state)
    {
        __state = default;
        if (__instance != null)
        {
            __state.PosX = __instance.mPosX;
            __state.Phase = __instance.mZombiePhase;
        }
    }

    public static void Postfix(Zombie __instance, VaultState __state)
    {
        if (__instance == null || !__instance.mMindControlled)
        {
            return;
        }

        if (__state.Phase == ZombiePhase.PolevaulterPreVault && __instance.mZombiePhase == ZombiePhase.PolevaulterInVault)
        {
            float magnitude = Math.Abs(__instance.mVelX);
            float scale = 1f;
            try
            {
                var plant = __instance.FindPlantTarget(ZombieAttackType.Vault);
                if (plant != null)
                {
                    float d = plant.mX - __state.PosX;
                    float nativeDistance = d + PlantOffset;
                    if (Math.Abs(nativeDistance) > 20f)
                    {
                        scale = (d - PlantOffset) / nativeDistance;
                    }
                }
            }
            catch (Exception)
            {
                // keep the plain sign flip
            }

            __instance.mVelX = magnitude * scale;
        }

        float moved = __instance.mPosX - __state.PosX;
        if (moved < -DetectDrop)
        {
            // The mirror image of the native landing shift.
            __instance.mPosX = __state.PosX - moved;
            __instance.mX = __instance.mPosX;
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
