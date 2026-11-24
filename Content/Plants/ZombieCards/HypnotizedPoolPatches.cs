using HarmonyLib;
using Il2CppReloaded.Gameplay;

namespace PvZReCoreLib.Content.Plants.ZombieCards;

// Pool zombie cards (Snorkel, Dolphin Rider, and the Normal-type "Ducky Tube" card).
//
// How much of this the game already does for a hypnotized zombie (all read from the decompile):
//  - Ducky Tube / Normal / Flag / Conehead / Buckethead: Zombie.CheckForPool swims any such zombie
//    that stands on water in with a splash and a tube, and takes it out of the pool again when
//    mX >= 680 or it leaves the water squares - both directions, so a hypnotized one walks out of the
//    pool's right end by itself. Nothing to patch.
//  - Snorkel: Zombie.UpdateZombieSnorkel is hypnosis-aware once the zombie is swimming
//    (SnorkelWalkingInPool): it leaves the pool at x > 640 when IsWalkingBackwards(), and a zombie
//    it wants to eat (mIsEating) makes it surface (UpToEat / EatingInPool / DownFromEat).
//    ZombieCardUtils.EnterWater drops the card zombie straight into SnorkelIntoPool. Nothing to patch.
//  - Dolphin Rider: its dolphin phases (Walking, IntoPool, Riding, InJump) all assume the zombie is
//    swimming LEFT, and Zombie.CheckIfPreyCaught excludes all of them, i.e. a riding dolphin never
//    fights anything. Only DolphinWalkingInPool (after the jump) is hypnosis-aware (it exits at
//    x > 680 when walking backwards, and may eat). The patches below teach the rest:
//      1. Riding: it rides right (ZombieCardUtils.SpawnHypnotizedZombie keeps the fast dolphin speed)
//         and jumps over the first enemy zombie it meets - the way the game makes a dolphin jump
//         over the first plant (UpdateZombieDolphinRider already does that for plants, hypnotized or
//         not, which is also why a hypnotized riding dolphin hops over friendly plants, just like a
//         hypnotized vaulter vaults over them).
//      2. Landing: the jump ends with mPosX -= 70 (to land left of the plant). A hypnotized dolphin
//         is jumping right, so the shift is put back and 70 added instead.
//      3. Pool end: riding has no exit of its own, it would carry on over dry land. At the pool's
//         right end the zombie is turned into the post-jump swimmer, whose native exit then walks it
//         out of the water.

// Position of the zombie when UpdateZombieDolphinRider started, so the phase change it causes can be
// recognised afterwards.
public struct DolphinTickState
{
    public ZombiePhase Phase;
    public float PosX;
}

[HarmonyPatch(typeof(Zombie), nameof(Zombie.UpdateZombieDolphinRider))]
public class Zombie_UpdateZombieDolphinRider_MindControlled_Patch
{
    // Native: mPosX -= 70 when the jump animation ends (constant [183C33F00] = 70.0; the game log showed
    // a measured drop of 94 in one update, so only the direction is relied on, see the landing below).
    private const float LandingShift = 70f;

    // Where a riding hypnotized dolphin gets out of the water: just past the last water tile, a bit
    // before the native post-jump exit (x > 680) so that exit takes over right away.
    private const float RideEndX = 660f;


    public static void Prefix(Zombie __instance, out DolphinTickState __state)
    {
        __state = default;
        if (__instance != null)
        {
            __state.Phase = __instance.mZombiePhase;
            __state.PosX = __instance.mPosX;
        }
    }

    public static void Postfix(Zombie __instance, DolphinTickState __state)
    {
        if (__instance == null || !__instance.mMindControlled)
        {
            return;
        }

        var phase = __instance.mZombiePhase;

        // 2. Landing: native just did mPosX -= 70; make it +70.
        // The log of a real jump showed the drop is NOT exactly 70 (the dolphin went 306.1 -> 212.1, 94
        // back), so the position is set from where the update started rather than corrected by an
        // assumed amount: the mirror image of the native -70, from the post-jump x.
        if (__state.Phase == ZombiePhase.DolphinInJump && phase == ZombiePhase.DolphinWalkingInPool)
        {
            // The jump animation carries the sprite as far as the native landing shift takes mPosX back
            // (the shift is what the animation offset becomes when it resets), so the mirror image of the
            // measured shift keeps the zombie exactly where the animation left it: no teleport.
            float moved = __instance.mPosX - __state.PosX;
            __instance.mPosX = __state.PosX - moved;
            __instance.mX = __instance.mPosX;
            return;
        }

        if (phase != ZombiePhase.DolphinRiding)
        {
            return;
        }

        // 3. End of the pool.
        if (__instance.mPosX >= RideEndX)
        {
            BecomeSwimmer(__instance);
            return;
        }

        // 1. An enemy zombie in reach: jump over it.
        if (__instance.mHasHead && __instance.FindZombieTarget() != null)
        {
            StartJump(__instance);
        }
    }

    // What UpdateZombieDolphinRider does when a riding dolphin finds a plant to jump over
    // (DolphinRiding -> DolphinInJump): phase, mPhaseCounter = 120 (the length the jump's altitude
    // curve is animated over), mVelX = 0.5 and "anim_dolphinjump" (PlayOnceAndHold, blend 0,
    // rate 10). The native jump sounds (ids 50 and 52) are not played.
    private static void StartJump(Zombie zombie)
    {
        zombie.mZombiePhase = ZombiePhase.DolphinInJump;
        zombie.mPhaseCounter = 120;
        zombie.mVelX = 0.5f;
        zombie.PlayZombieReanim("anim_dolphinjump", ReanimLoopType.PlayOnceAndHold, 0, 10f);
    }

    // The state the native jump leaves the dolphin in (DolphinInJump -> DolphinWalkingInPool): the
    // swimmer's zombie / attack rect (x = 20 / 30), then the swim animation (StartWalkAnim picks
    // anim_swim because mInPool is set).
    private static void BecomeSwimmer(Zombie zombie)
    {
        zombie.mZombiePhase = ZombiePhase.DolphinWalkingInPool;
        ZombieCardUtils.SetZombieRectX(zombie, 20f);
        ZombieCardUtils.SetAttackRectX(zombie, 30f);
        zombie.StartWalkAnim(0);
    }
}

// Placement rules for the zombie cards that may stand on water (see ZombieCardPlacement). Runs next
// to Board_CanPlantAt_RequiresGround_Patch (Land cards, which this leaves alone).
[HarmonyPatch(typeof(Board), nameof(Board.CanPlantAt), new[] { typeof(int), typeof(int), typeof(SeedType) })]
public class Board_CanPlantAt_ZombieCardPlacement_Patch
{
    public static void Postfix(Board __instance, int theGridX, int theGridY, SeedType theType, ref PlantingReason __result)
    {
        var card = ZombieCardUtils.GetCard(theType);
        if (card == null)
        {
            return;
        }

        // Zomboni / Bobsled Team: they drive the whole lane, so not into a lane with water in it.
        if (card.m_avoidPoolRows && __result == PlantingReason.Ok && ZombieCardUtils.RowHasPool(__instance, theGridY))
        {
            __result = PlantingReason.NotHere;
            return;
        }

        if (card.m_placement == ZombieCardPlacement.Land)
        {
            return;
        }

        __result = ZombieCardUtils.AdjustPlanting(__instance, card, theGridX, theGridY, __result);
    }
}
