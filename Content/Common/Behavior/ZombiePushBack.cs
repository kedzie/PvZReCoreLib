using System.Collections.Generic;
using HarmonyLib;
using Il2CppReloaded.Gameplay;
using UnityEngine;

namespace PvZReCoreLib.Content.Common.Behavior;

// Generic "push this zombie back N units" primitive - no native knockback/
// stagger system exists to hook (checked: nothing on Zombie beyond an
// unrelated mShieldRecoilCounter and mIsPushingBobsled flag), but none is
// needed - mPosX/mPosY are plain, freely-writable fields just like on
// Plant/Projectile. Drives the zombie smoothly back over a short window
// instead of teleporting it in one frame, with a parabolic vertical hop
// peaking at the midpoint (a simple stand-in for the "normal distribution"
// arc real PvZ2 knockback pops use) - the zombie's own walk/attack
// animation is untouched throughout, this only ever touches position.
public static class ZombiePushBack
{
    private class PushState
    {
        public float StartX;
        public float TargetX;
        public float BaseY;
        public float HopHeight;
        public int ElapsedTicks;
        public int TotalTicks;
    }

    private static readonly Dictionary<Zombie, PushState> Active = new Dictionary<Zombie, PushState>();

    // CONFIRMED live: a per-zombie LIFETIME cumulative-distance cap (this used to be 400f) is the
    // wrong shape of limit entirely - a single real caller's own per-hit distance (Tumbleweed's
    // TileWidth*2 = 160f) exhausted it in just 2-3 hits, after which the zombie permanently
    // stopped being pushed at all (while still taking normal damage) for the rest of its life,
    // since the running total only ever accumulates and never decays. The original concern behind
    // that cap was real (zombies getting flung permanently off-screen), but what actually needs
    // capping is simpler and purely positional: a push should never send a zombie further right
    // than it would naturally already be.
    //
    // A first attempt derived this live from other zombies' current mPosX (on the theory that a
    // freshly-spawned one hasn't walked from the true edge yet) - rejected before ever shipping:
    // a zombie that spawns mid-row (confirmed this genuinely happens) and is the only one on the
    // board, or happens to be the rightmost among current zombies, would be compared against its
    // OWN position and get zero push ever, or the true edge would be underestimated whenever no
    // zombie happened to currently be sitting at it. Board.GridToPixelX(gridX, gridY) is the
    // actual native inverse of the same PixelToGridX conversion already used elsewhere in this
    // codebase - a real geometric answer, independent of any zombie's current position entirely.
    // Column index 8 is the last of PvZ1's lawn's fixed 9 columns (0-indexed) - the count itself
    // never varies board-to-board (only row count does, 5 vs 6 for pool levels, which is why
    // Board exposes GetNumRows() but has no columns equivalent to call).
    private const int LastColumnIndex = 8;

    // distance: world units to push back (positive = away from the house,
    // same convention as mPosX everywhere else in this codebase). ticks:
    // how many Update calls the slide+hop takes. hopHeight: peak vertical
    // offset at the midpoint of the push.
    public static void Push(Zombie zombie, Board board, float distance, int ticks = 20, float hopHeight = 30f)
    {
        // CONFIRMED live: a zombie killed by the same hit that calls this (DamageZombie, always
        // called right before Push by every caller) is already IsDeadOrDying() by the time this
        // runs - excluding it here meant a killed zombie just stayed put instead of sliding back,
        // reading as if the knockback silently failed. Real knockback-style effects should still
        // visibly move the corpse through its death animation, not skip movement because it died -
        // only a genuinely null/already-destroyed zombie is worth refusing.
        if (zombie == null)
        {
            return;
        }

        float rightEdge = board != null ? board.GridToPixelX(LastColumnIndex, zombie.mRow) : float.PositiveInfinity;

        float targetX = Mathf.Min(zombie.mPosX + distance, rightEdge);
        if (targetX <= zombie.mPosX)
        {
            return;
        }

        // Re-pushing a zombie already mid-push restarts from its CURRENT
        // position rather than stacking on top of the old target, so two
        // quick hits don't fling it back double distance.
        Active[zombie] = new PushState
        {
            StartX = zombie.mPosX,
            TargetX = targetX,
            BaseY = zombie.mPosY,
            HopHeight = hopHeight,
            ElapsedTicks = 0,
            TotalTicks = Mathf.Max(1, ticks),
        };
    }

    // Called once per zombie per Zombie.Update() - advances and applies any
    // in-progress push, removing it once complete. No-op for a zombie that
    // isn't currently being pushed.
    public static void Advance(Zombie zombie)
    {
        if (zombie == null || !Active.TryGetValue(zombie, out var state))
        {
            return;
        }

        // Same reasoning as Push()'s own comment - a zombie that starts dying mid-push (from
        // this hit or any other) should keep sliding through its death animation instead of
        // freezing in place. This Postfix is only reached at all while native Zombie.Update()
        // still calls it, so a zombie actually removed from the board naturally stops advancing
        // on its own without needing an explicit dead check here.
        state.ElapsedTicks++;
        float t = Mathf.Clamp01((float)state.ElapsedTicks / state.TotalTicks);

        zombie.mPosX = Mathf.Lerp(state.StartX, state.TargetX, t);
        // Negative: this engine's Y increases downward on screen (confirmed
        // by FirePeashooter's mouth-offset needing the same flip), so a
        // negative offset at the midpoint is what actually reads as a hop up
        // rather than a dip down.
        zombie.mPosY = state.BaseY - 4f * state.HopHeight * t * (1f - t);

        if (t >= 1f)
        {
            zombie.mPosY = state.BaseY;
            Active.Remove(zombie);
        }
    }
}

[HarmonyPatch(typeof(Zombie), nameof(Zombie.Update))]
public class Zombie_Update_PushBack_Patch
{
    public static void Postfix(ref Zombie __instance)
    {
        ZombiePushBack.Advance(__instance);
    }
}
