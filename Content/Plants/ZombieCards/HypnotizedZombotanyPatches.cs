using HarmonyLib;
using Il2CppReloaded.Gameplay;
using MelonLoader;
using UnityEngine;

namespace PvZReCoreLib.Content.Plants.ZombieCards;

// Hypnotized Zombotany: the six plant-headed zombies (ZombieType 26..31). What the native code does for
// each, read from the decompile (all of it in Zombie.UpdateActions / UpdateZombie*Head, gated by mHasHead):
//
//  - Wall-nut Head, Tall-nut Head: no code at all, tough walkers. Nothing to do but the head picture (below).
//  - The head is its own reanimation (Zombie.mSpecialHeadReanimID), attached to the zombie's "Zombie_neck" bone.
//    For a mind-controlled zombie the game reverses its Z rotation (ReanimationController.ReverseZRotation,
//    +0x180) so a head that tilts to the left tilts to the right - but only the Pea Head and Gatling Head
//    update code sets it (every tick), so a hypnotized Jalapeno / Tall-nut / Wall-nut / Squash head kept the
//    left-facing tilt and hung in the wrong place. FlipHead sets it for all of them.
//  - Pea Head: mPhaseCounter runs 150 -> 35 ("anim_shooting") -> 0 (Board.AddProjectile at the head,
//    then the counter is reset to 150). It already knows about mind control: a hypnotized one fires a
//    plain Pea with motion type 6 and the zombie-hitting flag, which flies right. It never looks for a
//    target, so a hypnotized one would shoot forever at an empty lane: we hold its counter until an enemy
//    zombie is in its lane ahead of it.
//  - Gatling Pea Head: the same with a four-shot burst (counter 100, 68, 51, 35, 18 -> 0, reset 150).
//    Held the same way.
//  - Jalapeno Head: when mPhaseCounter reaches 0 (the game sets that when it starts eating a plant) it
//    does Board.DoFwoosh(row), kills EVERY plant in its row (Plant.Die) and dies (DieNoLoot). That would
//    wipe OUR plants and a hypnotized zombie never eats a plant, so for a hypnotized one the native block
//    is switched off (mHasHead is false for the duration of UpdateActions) and the same blast is done
//    here when it touches an enemy zombie: the fire sweeps the lane (DoFwoosh) and every enemy zombie in
//    the row takes 1800 (the Jalapeno plant's damage), then the head zombie is gone.
//  - Squash Head: when it is eating (mIsEating) in phase SquashPreLaunch (92) it stops eating, jumps up
//    (93), falls (94) and Zombie.SquishAllInSquare(col, row) crushes every PLANT of the tile it lands in
//    (95 = done, resting). A hypnotized one never eats a plant, so we start the jump ourselves when an
//    enemy zombie is right in front of it (mIsEating = true, phase 92), shift it one tile right during the
//    jump so the head leaps ahead instead of behind (see SquashJumping), and its landing hurts the enemy
//    zombies in front of it (1800, a Squash's damage) instead of our plants.
internal static class HypnotizedZombotany
{
    // The Jalapeno plant's and Squash plant's damage (Plant.DoSquashDamage: TakeDamage(1800, ...)).
    internal const int BlastDamage = 1800;

    // An enemy zombie whose body is this close ahead of the Squash (px) starts the jump; the landing hurts
    // the ones within LandingReach (the head comes down about a tile ahead).
    internal const float SquashReach = 30f;
    private const float LandingReach = 60f;

    // The lawn is 40..760 wide; a zombie past this is still walking in off screen.
    private const float OnScreenX = 780f;

    // Pea Head: the counter is held at the idle end of its cycle. Gatling: before its first shot (100).
    private const int PeaHold = 40;
    private const int GatlingHold = 105;

    internal static bool IsHypnotizedZombotany(Zombie z)
    {
        if (z == null || !z.mMindControlled)
        {
            return false;
        }

        switch (z.mZombieType)
        {
            case ZombieType.PeaHead:
            case ZombieType.GatlingHead:
            case ZombieType.JalapenoHead:
            case ZombieType.SquashHead:
                return true;
            default:
                return false;
        }
    }

    internal static bool IsZombotanyType(ZombieType t)
    {
        return t == ZombieType.PeaHead || t == ZombieType.WallnutHead || t == ZombieType.JalapenoHead
            || t == ZombieType.GatlingHead || t == ZombieType.SquashHead || t == ZombieType.TallnutHead;
    }

    private static readonly HashSet<ZombieType> flipLogged = new();

    // The head picture of a hypnotized Zombotany: reverse its Z rotation, as the Pea Head code does itself.
    internal static void FlipHead(Zombie z)
    {
        var reanim = z.mApp?.ReanimationTryToGet(z.mSpecialHeadReanimID);
        var controller = reanim?.mController;
        if (controller == null || controller.ReverseZRotation)
        {
            return;
        }

        controller.ReverseZRotation = true;
        if (flipLogged.Add(z.mZombieType))
        {
            MelonLogger.Msg($"[CoreLib] Hypnotized {z.mZombieType}: head rotation reversed (it was not set by the game).");
        }
    }

    private static bool IsEnemyInRow(Zombie z, Zombie other)
    {
        return other != null
            && other.Pointer != z.Pointer
            && !other.mMindControlled
            && other.mRow == z.mRow
            && !other.IsDeadOrDying()
            && other.mPosX < OnScreenX;
    }

    // An enemy zombie in the lane, to the right of the head zombie (what a pea would hit).
    internal static bool EnemyAhead(Zombie z)
    {
        var board = z.mBoard;
        if (board == null)
        {
            return false;
        }

        var all = board.m_zombies;
        for (int i = 0; i < all.Count; i++)
        {
            var other = all[i];
            if (IsEnemyInRow(z, other) && other.mPosX > z.mPosX)
            {
                return true;
            }
        }

        return false;
    }

    // An enemy zombie in the lane IN FRONT of the (right-walking) head zombie, within 'reach' px of its body
    // (<= 0 = overlapping). Only body rectangles are compared, never the attack rect: its side for a mirrored
    // zombie is not something to rely on, and the first Squash test attacked to the left only.
    internal static bool EnemyInReach(Zombie z, float reach)
    {
        var board = z.mBoard;
        if (board == null)
        {
            return false;
        }

        Rect me = z.GetZombieRect();
        float centre = me.x + me.width / 2f;
        float right = me.x + me.width;
        var all = board.m_zombies;
        for (int i = 0; i < all.Count; i++)
        {
            var other = all[i];
            if (!IsEnemyInRow(z, other))
            {
                continue;
            }

            Rect body = other.GetZombieRect();
            if (body.x + body.width >= centre && body.x - right <= reach)
            {
                return true;
            }
        }

        return false;
    }

    // The native jump (phases 92..94) moves the Squash's head - a reanimation of its own - from the zombie to
    // the LEFT edge of the tile that holds the zombie's mX (Board.GridToPixelX(PixelToGridXKeepOnBoard(mX)) -
    // mPosX is the distance its curve covers), where a plant stood on the left of a zombie walking left. A
    // hypnotized one walks right, so for the length of its UpdateActions the zombie is moved one tile to the
    // right: the head then leaps to the tile AHEAD, where the enemy is (and SquishAllInSquare gets that column).
    internal const float TileWidth = 80f;

    internal static bool IsSquashJumping(Zombie z)
    {
        var phase = z.mZombiePhase;
        return phase == ZombiePhase.SquashPreLaunch || phase == ZombiePhase.SquashRising || phase == ZombiePhase.SquashFalling;
    }

    // Pea Head / Gatling Pea Head: no enemy in the lane, no shooting.
    internal static void HoldFire(Zombie z)
    {
        int hold = z.mZombieType == ZombieType.GatlingHead ? GatlingHold : PeaHold;
        if (z.mPhaseCounter <= hold && !EnemyAhead(z))
        {
            z.mPhaseCounter = hold;
        }
    }

    // The Jalapeno's fire: the whole lane, then the zombie is gone.
    internal static void Explode(Zombie z)
    {
        var board = z.mBoard;
        if (board == null)
        {
            return;
        }

        board.DoFwoosh(z.mRow);
        board.ShakeBoard(3, -4);

        var all = board.m_zombies;
        var victims = new List<Zombie>();
        for (int i = 0; i < all.Count; i++)
        {
            var other = all[i];
            if (other != null
                && other.Pointer != z.Pointer
                && !other.mMindControlled
                && other.mRow == z.mRow
                && !other.IsDeadOrDying()
                && other.EffectedByDamage(DamageRangeFlags.Ground))
            {
                victims.Add(other);
            }
        }

        foreach (var victim in victims)
        {
            victim.TakeDamage(BlastDamage, HypnotizedGargantuar.SmashFlags);
        }

        z.DieNoLoot();
    }

    // The Squash's landing: every enemy zombie right in front of it (the head came down on the tile ahead).
    internal static void SquashLanding(Zombie z, int col, int row)
    {
        var board = z.mBoard;
        if (board == null)
        {
            return;
        }

        Rect me = z.GetZombieRect();
        float centre = me.x + me.width / 2f;
        float right = me.x + me.width;

        var all = board.m_zombies;
        var victims = new List<Zombie>();
        for (int i = 0; i < all.Count; i++)
        {
            var other = all[i];
            if (other == null
                || other.Pointer == z.Pointer
                || other.mMindControlled
                || other.mRow != z.mRow
                || other.IsDeadOrDying()
                || !other.EffectedByDamage(DamageRangeFlags.Ground))
            {
                continue;
            }

            Rect body = other.GetZombieRect();
            if (body.x + body.width >= centre && body.x - right <= LandingReach)
            {
                victims.Add(other);
            }
        }

        foreach (var victim in victims)
        {
            victim.TakeDamage(BlastDamage, HypnotizedGargantuar.SmashFlags);
        }
    }
}

// Runs before every zombie update; only a mind-controlled Zombotany that shoots, burns or jumps does anything.
[HarmonyPatch(typeof(Zombie), nameof(Zombie.UpdateActions))]
public class Zombie_UpdateActions_HypnotizedZombotany_Patch
{

    public struct State
    {
        public bool headHidden;
        public bool shifted;
    }

    public static void Prefix(Zombie __instance, out State __state)
    {
        __state = default;

        if (__instance != null && __instance.mMindControlled && HypnotizedZombotany.IsZombotanyType(__instance.mZombieType))
        {
            try
            {
                HypnotizedZombotany.FlipHead(__instance);
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"[CoreLib] Hypnotized Zombotany head flip failed: {e.Message}");
            }
        }

        if (!HypnotizedZombotany.IsHypnotizedZombotany(__instance) || !__instance.mHasHead || __instance.IsDeadOrDying())
        {
            return;
        }

        try
        {
            switch (__instance.mZombieType)
            {
                case ZombieType.PeaHead:
                case ZombieType.GatlingHead:
                    HypnotizedZombotany.HoldFire(__instance);
                    break;

                case ZombieType.JalapenoHead:
                    // Our own trigger replaces the native one, and the native block (which burns the plants
                    // of the lane) must not run: mHasHead is what it checks first.
                    __instance.mHasHead = false;
                    __state.headHidden = true;
                    if (HypnotizedZombotany.EnemyInReach(__instance, 0f))
                    {
                        HypnotizedZombotany.Explode(__instance);
                    }
                    break;

                case ZombieType.SquashHead:
                    if (__instance.mZombiePhase == ZombiePhase.ZombieNormal && HypnotizedZombotany.EnemyInReach(__instance, HypnotizedZombotany.SquashReach))
                    {
                        __instance.mIsEating = true;
                        __instance.mZombiePhase = ZombiePhase.SquashPreLaunch;
                    }

                    if (HypnotizedZombotany.IsSquashJumping(__instance))
                    {
                        __instance.mX += HypnotizedZombotany.TileWidth;
                        __state.shifted = true;
                    }
                    break;
            }
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Hypnotized Zombotany update failed: {e.Message}");
        }
    }

    // Always runs, even if the native code threw: put mHasHead and mX back.
    public static Exception Finalizer(Exception __exception, Zombie __instance, State __state)
    {
        try
        {
            if (__state.headHidden)
            {
                __instance.mHasHead = true;
            }

            if (__state.shifted)
            {
                __instance.mX -= HypnotizedZombotany.TileWidth;
            }
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Hypnotized Zombotany restore failed: {e.Message}");
        }

        return __exception;
    }
}

// The Squash's landing crushes plants of the tile (Zombie.SquishAllInSquare). A hypnotized one hurts the
// ENEMY zombies of the tile instead and spares our plants.
[HarmonyPatch(typeof(Zombie), nameof(Zombie.SquishAllInSquare))]
public class Zombie_SquishAllInSquare_HypnotizedSquash_Patch
{
    public static bool Prefix(Zombie __instance, int theX, int theY)
    {
        if (__instance == null || !__instance.mMindControlled || __instance.mZombieType != ZombieType.SquashHead)
        {
            return true;
        }

        try
        {
            HypnotizedZombotany.SquashLanding(__instance, theX, theY);
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Hypnotized Squash landing failed: {e.Message}");
        }

        return false;
    }
}
