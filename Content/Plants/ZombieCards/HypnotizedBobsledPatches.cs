using HarmonyLib;
using Il2CppReloaded.Gameplay;
using Il2CppSource.Controllers;
using MelonLoader;
using UnityEngine;

namespace PvZReCoreLib.Content.Plants.ZombieCards;

// A hypnotized Bobsled Team (conjured by a zombie card; ZombieCardUtils.SpawnHypnotizedBobsled). The
// team is four zombies: the leader (Zombie type Bobsled, the sled is its "helm", HelmType.Bobsled, 300 HP)
// and three riders the game adds in Zombie.ZombieInitialize and lists in the leader's mFollowerZombieID.
// All four are in ZombiePhase.BobsledSliding with mVelX = 0.6.
//
// What the native game does with them (all of it written for a team coming from the RIGHT onto the ice
// a Zomboni left):
//  - Only the leader is updated (Zombie.UpdateActions -> UpdateZombieBobsled for type 13); it drives its
//    riders' phases. Sliding -> (mPhaseCounter 500 -> 0) Boarding ("anim_jump", the riders hop on) ->
//    Crashing when the sled's helm is destroyed (Zombie.DropHelm for HelmType.Bobsled: phase 19, a 150 tick
//    crash, then every rider is a plain walking zombie again).
//  - Every update, in every phase: mIceTimer[row] = max(mIceTimer[row], 500) and - for the leader - if
//    mIceMinX[row] > posX + 10 (the sled is left of the ice's left edge: off the ice) it does
//    TakeDamage(6, DoesntCauseFlash) to itself. A sled with no ice under it melts in 50 ticks. That is the
//    only way a native sled ends besides being shot.
//  - Riders never eat (Zombie.CheckIfPreyCaught returns for anything with GetBobsledPosition() != -1) and
//    the sled never squishes anything: it is the crash (helm gone) that releases them, and Zombie.UpdateZombieWalking
//    moves all four by mVelX with no plant check. A native sled slides through the plants until it dies.
//
// So for a mind-controlled leader we make these true, without touching normal teams:
//  1. No ice needed, none made. The lane's native ice state is saved before the update and put back
//     after it, with mIceMinX lowered out of the way during it: the sled never melts for lack of ice and
//     leaves no native (right-anchored) ice patch. The sled lays no trail of its own: only a hypnotized
//     Zomboni makes ice (HypnotizedIceTrail).
//  2. Ice decides whether the team holds together, as natively (where a sled off the ice melts), but
//     with our own rule instead of the melt: every tick, while the team is on the sled, it breaks as soon
//     as no part of it - from the last rider to the leader - is over a hypnotized Zomboni's trail in its
//     lane. A sled planted in a lane without a trail breaks at once; one launched behind a Zomboni stays
//     together until it has outrun the trail's end, or the trail fades. The speed is not touched: it is the
//     native mVelX = 0.6 set once in ZombieInitialize.
//  3. The first hit breaks it. The sled breaks the moment its body overlaps an enemy zombie - a Gargantuar
//     is no exception - and the zombie it hit takes no damage. Breaking is the game's own: the leader's
//     helm is hit for its whole health, so Zombie.DropHelm
//     starts the crash for the team (the sled goes, a 150 tick crash, then the leader and the riders are
//     ordinary walking zombies - still hypnotized, they walk on right).
//  4. Fallback: the sled breaks the same way at the end of the lane (SledEndX), well before the board edge: a
//     hypnotized zombie is removed silently at mPosX > 850 (Zombie.CheckForBoardEdge -> DieNoLoot), and the
//     leader's DieNoLoot takes the whole sled team with it (Zombie.DieNoLoot reads GetBobsledPosition).
internal static class HypnotizedBobsled
{
    // Where the sled breaks up: the leader's left edge x. The lawn ends at 760; the riders stand up to
    // 150 px behind the leader and keep sliding for the native 150 tick crash (+90 px) before they walk.
    internal const float SledEndX = 700f;

    // A very low left edge for the native ice melt test (posX + 10 < mIceMinX is false for any posX).
    internal const int NoMeltMinX = -10000;

    internal static bool IsMindControlledSled(Zombie z)
    {
        return z != null && z.mMindControlled && z.mZombieType == ZombieType.Bobsled;
    }

    internal static bool IsOnTheSled(Zombie z)
    {
        return z.mZombiePhase == ZombiePhase.BobsledSliding || z.mZombiePhase == ZombiePhase.BobsledBoarding;
    }

    // The sled breaks on the first enemy zombie it overlaps; that zombie takes no damage.
    internal static void BreakOnFirstHit(Zombie sled)
    {
        if (HypnotizedZamboni.CrushEnemies(sled, sled.GetZombieRect(), 0) > 0)
        {
            Break(sled);
        }
    }

    // The end of the lane: break the sled (see the file header, 4).
    internal static void BreakAtEnd(Zombie sled)
    {
        if (sled.mPosX >= SledEndX)
        {
            Break(sled);
        }
    }

    // Through the normal damage path the leader's helm - the sled - takes damage first, so a hit for
    // exactly its health destroys it and the native DropHelm starts the crash for the whole team.
    private static void Break(Zombie sled)
    {
        if (sled.mHelmType == HelmType.Bobsled && sled.mHelmHealth > 0)
        {
            sled.TakeDamage(sled.mHelmHealth, DamageFlags.DoesntCauseFlash);
        }
    }

    // The sled needs ice: it breaks when no part of the team, from the last rider's x to the leader's x,
    // is over a trail of its lane (see the file header, 2).
    internal static void BreakOffIce(Zombie sled)
    {
        var board = sled.mBoard;
        float left = sled.mPosX;
        foreach (ZombieID id in sled.mFollowerZombieID)
        {
            Zombie rider = board.ZombieGet(id);
            if (rider != null)
            {
                left = Math.Min(left, rider.mPosX);
            }
        }

        if (!HypnotizedIceTrail.IsOverSpan(board, sled.mRow, left, sled.mPosX))
        {
            Break(sled);
        }
    }
}

[HarmonyPatch(typeof(Zombie), nameof(Zombie.UpdateZombieBobsled))]
public class Zombie_UpdateZombieBobsled_MindControlled_Patch
{
    public struct State
    {
        public bool active;
        public int row;
        public int iceMinX;
        public int iceTimer;
    }

    public static void Prefix(Zombie __instance, out State __state)
    {
        __state = default;

        if (!HypnotizedBobsled.IsMindControlledSled(__instance))
        {
            return;
        }

        var board = __instance.mBoard;
        int row = __instance.mRow;
        if (board == null || row < 0 || row >= board.mIceMinX.Length || row >= board.mIceTimer.Length)
        {
            return;
        }

        __state.active = true;
        __state.row = row;
        __state.iceMinX = board.mIceMinX[row];
        __state.iceTimer = board.mIceTimer[row];

        board.mIceMinX[row] = HypnotizedBobsled.NoMeltMinX;
    }

    // Always runs, even if the native code (or another patch) threw.
    public static Exception Finalizer(Exception __exception, Zombie __instance, State __state)
    {
        if (!__state.active)
        {
            return __exception;
        }

        try
        {
            var board = __instance.mBoard;
            board.mIceMinX[__state.row] = __state.iceMinX;
            board.mIceTimer[__state.row] = __state.iceTimer;

            // All four members of the team are type Bobsled and each one is updated here (seen in the log:
            // four updates per tick); the team is driven by its leader (position 0) alone.
            if (HypnotizedBobsled.IsOnTheSled(__instance) && __instance.GetBobsledPosition() == 0)
            {
                HypnotizedBobsled.BreakOffIce(__instance);
                if (HypnotizedBobsled.IsOnTheSled(__instance))
                {
                    HypnotizedBobsled.BreakOnFirstHit(__instance);
                }
                if (HypnotizedBobsled.IsOnTheSled(__instance))
                {
                    HypnotizedBobsled.BreakAtEnd(__instance);
                }
            }
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Hypnotized Bobsled Team update failed: {e.Message}");
        }
        return __exception;
    }
}

// The sled picture is not mirrored for a hypnotized team. The game flips the zombies (their reanimations,
// through mind control) but the sled is two sprites under one child transform of the zombie's prefab
// ("Bobsled", scale 72, with BobsledInside / BobsledOutside at fixed negative-x offsets). ZombieBobsledController.Update
// only sets that transform's position (from the zombie's draw offsets) and sprites, never its scale and
// without any mind-control test, so the sled kept facing left and sitting on the wrong side of a team that
// faces and slides right. Mirrored here after the game's own Update: scale x negative (the two sprites and
// their offsets flip with their parent) and the position mirrored about where the flipped riders are drawn.
// That is not the controller's origin: Zombie.UpdateReanim moves a flipped zombie's reanimation by
// 90 px * mScaleZombie (it adds it to the draw origin whenever IsWalkingBackwards) so it stays in place, and
// the controller root itself stays at px * 2.82 world units - so a sled mirrored about the root ended up
// 90 px (254 world units) to the left of its riders (seen in game). The position is rebuilt from scratch every
// Update, so the mirror is not cumulative; the scale is not, so it is only ever written for a hypnotized
// zombie, and undone when a NATIVE sled finds it negative: the controller objects are pooled, and a sled
// mirrored for a hypnotized team came back mirrored on an enemy sled of Bobsled Bonanza.
[HarmonyPatch(typeof(ZombieBobsledController), "Update")]
public class ZombieBobsledController_Update_MindControlled_Patch
{
    // Zombie.UpdateReanim: draw origin += 90 * mScaleZombie for a zombie that walks backwards.
    private const float MirrorShiftPx = 90f;

    // World units per lawn pixel (GameAssembly constant 2.82, also the slope measured between riders).
    private const float WorldPerPixel = 2.82f;

    public static void Postfix(ZombieBobsledController __instance)
    {
        var zombie = __instance.m_zombie;
        var sled = __instance.m_bobsledTransform;
        if (zombie == null || sled == null)
        {
            return;
        }

        var scale = sled.localScale;
        if (!zombie.mMindControlled)
        {
            if (scale.x < 0f)
            {
                sled.localScale = new Vector3(-scale.x, scale.y, scale.z);
            }
            return;
        }

        sled.localScale = new Vector3(-Mathf.Abs(scale.x), scale.y, scale.z);

        var position = sled.localPosition;
        sled.localPosition = new Vector3(-position.x + MirrorShiftPx * zombie.mScaleZombie * WorldPerPixel, position.y, position.z);
    }
}
