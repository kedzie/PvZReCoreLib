using HarmonyLib;
using Il2CppReloaded.Gameplay;
using MelonLoader;
using UnityEngine;

namespace PvZReCoreLib.Content.Plants.ZombieCards;

// A hypnotized Zomboni (one a zombie card conjured; the game never produces one) is made to do what a
// hypnotized vehicle is supposed to do: drive right, run over the ENEMY zombies in its way instead of our
// plants, and lay its ice trail behind it without that trail hurting anything of ours.
//
// What the native game does (all of it written for a Zomboni driving LEFT and hostile to plants):
//  - Zombie.UpdatePlaying / UpdateZombiePosition: after UpdateZombieWalking, every tick a Zomboni (type 12,
//    or the Catapult, 22) that has no flat tires calls Zombie.CheckSquish(DriveOver). CheckSquish walks all
//    plants of the row, and for each non-spiky plant that overlaps the attack rect by >= 20 px calls
//    Zombie.SquishAllInSquare, which kills every plant on that tile. Spiky plants are skipped there: they
//    pop the tires through the Plant side (mFlatTires, which then stops the CheckSquish calls).
//  - Zombie.CheckIfPreyCaught returns at once for type 12: a Zomboni never "eats" and never looks for a
//    zombie target, so nothing else would make a hypnotized one fight.
//  - Zombie.UpdateZamboni: above x = 400 it sets mVelX from a curve (0.25 at x = 700 down to 0.05 at
//    x = 300), then, in the lane's ice state, mIceMinX[row] = min(mIceMinX[row], posX + 118) and
//    mIceTimer[row] = 3000 (infinite in Bobsled Bonanza). The board's per-lane ice is a single left
//    edge (mIceMinX) with everything RIGHT of it frozen: Board.CanPlantAt refuses planting at columns
//    right of it, Board.DrawGameObjects/DrawIce draw it from there to x = 800, and the Bobsled Team
//    slides on it. A Zomboni driving right cannot be described by that (it would freeze the lane AHEAD
//    of it and block our planting there), so for a hypnotized one the native ice state is put back after
//    every update and our own trail is drawn instead (HypnotizedIceTrail).
//  - Zombie.TakeBodyDamage -> ZamboniDeath at low health: explosion particles, no damage to anything.
//
// The card launches it from the left edge of the planted tile's lane (ZombieCardBehaviorController:
// column 0, posX = GridToPixelX(0, row) - 30 = 10; the board-edge patch pins a hypnotized zombie to
// x >= 0, so it cannot start further left) and it drives right over the whole lane. Our plants are never
// touched, by any path: the plant crush is Zombie.CheckSquish (replaced below), a Zomboni never "eats"
// (Zombie.CheckIfPreyCaught returns for type 12), and a mind-controlled zombie skips the plant branch of
// CheckIfPreyCaught anyway (it tests mMindControlled before FindPlantTarget). It leaves the board silently
// at mPosX > 850 (Zombie.CheckForBoardEdge -> DieNoLoot, kept by the board-edge patch).
//
// So for a mind-controlled Zomboni (type 12, mMindControlled) we make three things true, without touching
// normal ones:
//  1. CheckSquish is replaced: it crushes every enemy zombie of the lane that the attack rect overlaps by
//     >= 20 px (the native test) with 1800 damage (the Squash's), once per second per zombie. Our own
//     plants are never touched.
//     Two exceptions, both of which END the Zomboni:
//       - an enemy Gargantuar / Red-eye Gargantuar is not run over: the Zomboni waits in front of him
//         and HE smashes it (Zombie_FindPlantTarget_EnemyGargantuarSmashesZomboni_Patch below, native smash
//         animation and timing); it explodes at the smash frame and he takes one GargantuarBlastDamage hit;
//       - an enemy Zomboni: both explode.
//     The explosion is the game's own: Zombie.ZamboniDeath. For a damage-flags value of 32 or less it spawns
//     ParticleEffect.ZamboniExplosion (63) on the Zomboni, calls DieNoLoot + DropLoot and plays the Zomboni
//     death sound; for more it only starts the wheelie/tires-off death, which is why the flags used here
//     (HitsShieldAndBody | DoesntLeaveBody = 18) stay below that. It is reached the normal way,
//     Zombie.TakeDamage -> TakeBodyDamage -> ZamboniDeath, with a damage far above any Zomboni's health.
//  2. mVelX is a fixed driving speed instead of the position curve (which was written for a Zomboni
//     entering from the right and would make a right-driving one speed up).
//  3. The native ice state of the lane is saved before UpdateZamboni and restored after it, and the
//     trail behind the Zomboni is recorded in HypnotizedIceTrail, which draws it over the lane.
internal static class HypnotizedZamboni
{
    // px per tick. The native Zomboni drives at 0.25 where it enters, a hypnotized walker at 0.17.
    internal const float Speed = 0.22f;

    internal const int CrushDamage = 1800;
    internal const int CrushCooldown = 100;

    // The one hit a Gargantuar takes when he smashes a hypnotized Zomboni and it blows up in his face. 1800
    // is the game's own heavy-hit precedent (Cherry Bomb, Jalapeno, Squash: Plant.DoSquashDamage is
    // TakeDamage(1800, ...)), so a Gargantuar (3000 HP) is left with 1200 and a Red-eye one (6000) with
    // 4200. The Zomboni's own explosion deals no damage natively (ZamboniDeath only spawns particles).
    internal const int GargantuarBlastDamage = 1800;

    // Damage that surely destroys a Zomboni whatever its health (about 1350 in PvZ1); sent
    // through TakeDamage so the native death path (ZamboniDeath explosion + DieNoLoot) runs for it.
    internal const int ExplodeDamage = 9999;

    // Horizontal overlap (px) CheckSquish needs before a target counts as run over.
    private const int MinOverlap = 20;

    // victim pointer -> the victim's mZombieAge when it was last crushed. Zombie objects are pooled, so
    // an age lower than the stored one means the object is a different zombie by now.
    private static readonly Dictionary<IntPtr, int> lastCrush = new();

    internal static bool IsMindControlledZomboni(Zombie z)
    {
        return z != null && z.mMindControlled && z.mZombieType == ZombieType.Zamboni;
    }

    // The zombies on the board, read the way the game's own loops do (0 .. DataArray.Count through its
    // indexer). NOT DataArray.m_list.ToList(): m_list is the whole 1024-slot backing array, and copying it
    // through the IL2CPP interop cost ~1.4 ms per scan, several scans per tick (measured in the log).
    internal static List<Zombie> LiveZombies(Board board)
    {
        var zombies = new List<Zombie>();
        var all = board.m_zombies;
        for (int i = 0; i < all.Count; i++)
        {
            var zombie = all[i];
            if (zombie != null)
            {
                zombies.Add(zombie);
            }
        }

        return zombies;
    }

    internal static bool IsGargantuar(Zombie z)
    {
        return z.mZombieType == ZombieType.Gargantuar || z.mZombieType == ZombieType.RedeyeGargantuar;
    }

    // Is this hypnotized Zomboni inside this Gargantuar's smash reach? The same test the native plant
    // search uses: same lane, and his attack rect overlaps its body by >= MinOverlap.
    internal static bool InSmashReach(Zombie garg, Zombie zomboni)
    {
        if (!IsMindControlledZomboni(zomboni) || zomboni.mRow != garg.mRow || zomboni.IsDeadOrDying())
        {
            return false;
        }

        var attack = garg.GetZombieAttackRect();
        var body = zomboni.GetZombieRect();
        return Il2CppReloaded.Gameplay.Common.GetRectOverlap(ref attack, ref body) >= MinOverlap;
    }

    // The hypnotized Zomboni stops while an enemy Gargantuar can smash it, instead of driving through him.
    internal static bool IsHeldByGargantuar(Zombie zomboni)
    {
        foreach (var other in LiveZombies(zomboni.mBoard))
        {
            if (!other.mMindControlled && IsGargantuar(other) && !other.IsDeadOrDying() && InSmashReach(other, zomboni))
            {
                return true;
            }
        }

        return false;
    }

    // The hypnotized Zomboni(s) in an enemy Gargantuar's smash reach.
    internal static List<Zombie> ZombonisInReach(Zombie garg)
    {
        return LiveZombies(garg.mBoard).Where(z => InSmashReach(garg, z)).ToList();
    }

    // The smash lands: those Zombonis explode (TakeDamage 9999 -> native ZamboniDeath) and he takes the blast.
    internal static void SmashZombonis(Zombie garg)
    {
        foreach (var zomboni in ZombonisInReach(garg))
        {
            zomboni.TakeDamage(ExplodeDamage, HypnotizedGargantuar.SmashFlags);
            garg.TakeDamage(GargantuarBlastDamage, HypnotizedGargantuar.SmashFlags);
        }
    }

    // Pointer of the ENEMY Gargantuar whose UpdateZombieGargantuar is running (zero when none).
    internal static IntPtr EnemyGargantuarActive = IntPtr.Zero;

    // The hypnotized Zomboni's tick (replaces Zombie.CheckSquish for it): crush the ordinary enemy zombies
    // it overlaps, and blow up with an enemy Zomboni. Gargantuars are left alone (they smash it).
    internal static void RunOver(Zombie zomboni)
    {
        var board = zomboni.mBoard;
        if (board == null || zomboni.IsDeadOrDying())
        {
            return;
        }

        var rect = zomboni.GetZombieAttackRect();
        var destroyed = new List<Zombie>();

        foreach (var other in LiveZombies(board))
        {
            if (other.Pointer == zomboni.Pointer
                || other.mMindControlled
                || other.mRow != zomboni.mRow
                || other.IsDeadOrDying()
                || !other.EffectedByDamage(DamageRangeFlags.Ground))
            {
                continue;
            }

            var body = other.GetZombieRect();
            if (Il2CppReloaded.Gameplay.Common.GetRectOverlap(ref rect, ref body) < MinOverlap)
            {
                continue;
            }

            if (IsGargantuar(other))
            {
                continue;
            }

            if (other.mZombieType == ZombieType.Zamboni)
            {
                destroyed.Add(other);
            }
            else if (CanCrush(other))
            {
                other.TakeDamage(CrushDamage, HypnotizedGargantuar.SmashFlags);
            }
        }

        if (destroyed.Count == 0)
        {
            return;
        }

        foreach (var enemy in destroyed)
        {
            enemy.TakeDamage(ExplodeDamage, HypnotizedGargantuar.SmashFlags);
        }

        // And the hypnotized Zomboni goes up with it.
        zomboni.TakeDamage(ExplodeDamage, HypnotizedGargantuar.SmashFlags);
    }

    // The once-a-second-per-zombie rule of the crush (see lastCrush).
    private static bool CanCrush(Zombie other)
    {
        int age = other.mZombieAge;
        if (lastCrush.TryGetValue(other.Pointer, out int last) && age >= last && age - last < CrushCooldown)
        {
            return false;
        }

        if (lastCrush.Count > 512)
        {
            lastCrush.Clear();
        }
        lastCrush[other.Pointer] = age;
        return true;
    }

    // Shared by the Zomboni and the Bobsled Team's sled: hurts every enemy zombie of the vehicle's lane
    // that its rect overlaps (damage 0: the sled only wants to know whether it touched one). Returns how
    // many were hit.
    internal static int CrushEnemies(Zombie vehicle, Rect vehicleRect, int damage)
    {
        var board = vehicle.mBoard;
        if (board == null)
        {
            return 0;
        }

        int hits = 0;
        foreach (var other in LiveZombies(board))
        {
            if (other.Pointer == vehicle.Pointer
                || other.mMindControlled
                || other.mRow != vehicle.mRow
                || other.IsDeadOrDying()
                || !other.EffectedByDamage(DamageRangeFlags.Ground))
            {
                continue;
            }

            var body = other.GetZombieRect();
            int overlap = Il2CppReloaded.Gameplay.Common.GetRectOverlap(ref vehicleRect, ref body);
            if (overlap < MinOverlap)
            {
                continue;
            }

            if (damage > 0)
            {
                if (!CanCrush(other))
                {
                    continue;
                }

                other.TakeDamage(damage, HypnotizedGargantuar.SmashFlags);
            }

            hits++;
        }

        return hits;
    }
}

// 1. Run over enemy zombies, never our plants.
[HarmonyPatch(typeof(Zombie), nameof(Zombie.CheckSquish))]
public class Zombie_CheckSquish_MindControlledZamboni_Patch
{
    public static bool Prefix(Zombie __instance)
    {
        if (!HypnotizedZamboni.IsMindControlledZomboni(__instance))
        {
            return true;
        }

        try
        {
            HypnotizedZamboni.RunOver(__instance);
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Hypnotized Zomboni could not crush: {e.Message}");
        }

        return false;
    }
}

// 2 + 3. Fixed driving speed, no native ice, and the hypnotized trail.
[HarmonyPatch(typeof(Zombie), nameof(Zombie.UpdateZamboni))]
public class Zombie_UpdateZamboni_MindControlled_Patch
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

        if (!HypnotizedZamboni.IsMindControlledZomboni(__instance))
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

            if (!__instance.mFlatTires)
            {
                // Held still while an enemy Gargantuar can smash it.
                __instance.mVelX = HypnotizedZamboni.IsHeldByGargantuar(__instance) ? 0f : HypnotizedZamboni.Speed;
            }

            // The trail runs from where the Zomboni started to where it is. Its real position, mPosX: the
            // rects are built from mX, which the game only copies from mPosX at the end of Update, so on
            // the first update after our edge launch they still hold the native spawn x (~811) and
            // anchored the trail there for good (it could only ever grow rightwards from 811).
            HypnotizedIceTrail.Extend(board, __state.row, __instance.mPosX);
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Hypnotized Zomboni update failed: {e.Message}");
        }

        return __exception;
    }
}

// The enemy Gargantuar smashes a hypnotized Zomboni, reusing his native smash.
//
// Native: Zombie.UpdateZombieGargantuar (types 23 / 32, no mind-control test) only asks
// Zombie.FindPlantTarget. Walking: a non-null answer starts the smash (the answer is only compared with
// null). Smashing: once, at the smash frame, it asks again and squishes the plant it gets; when the
// animation has played he goes back to walking (phase 0 + StartWalkAnim), so he is never stuck.
//
// For an enemy (not mind-controlled) Gargantuar, inside his own update only:
//  - walking: no plant in reach but a hypnotized Zomboni in reach -> the answer is a stand-in Plant (the
//    one the hypnotized Gargantuar uses), so the native smash starts at its native timing;
//  - smash frame: the Zomboni in reach explodes and he takes the blast; the native search then runs as
//    usual, so a real plant under the same smash is still squished.
// Only the Zomboni, not hypnotized zombies in general: that would be the same patch without the type test
// but would let him kill every hypnotized zombie of ours.
[HarmonyPatch(typeof(Zombie), nameof(Zombie.UpdateZombieGargantuar))]
public class Zombie_UpdateZombieGargantuar_EnemySmashesZomboni_Patch
{
    public static void Prefix(Zombie __instance)
    {
        if (!__instance.mMindControlled)
        {
            HypnotizedZamboni.EnemyGargantuarActive = __instance.Pointer;
        }
    }

    // Always runs, even if the native code threw.
    public static Exception Finalizer(Exception __exception)
    {
        HypnotizedZamboni.EnemyGargantuarActive = IntPtr.Zero;
        return __exception;
    }
}

[HarmonyPatch(typeof(Zombie), nameof(Zombie.FindPlantTarget))]
public class Zombie_FindPlantTarget_EnemyGargantuarSmashesZomboni_Patch
{
    private static bool Applies(Zombie z)
    {
        return z.Pointer == HypnotizedZamboni.EnemyGargantuarActive;
    }

    public static void Prefix(Zombie __instance)
    {
        if (Applies(__instance) && __instance.mZombiePhase == ZombiePhase.GargantuarSmashing)
        {
            HypnotizedZamboni.SmashZombonis(__instance);
        }
    }

    public static void Postfix(Zombie __instance, ref Plant __result)
    {
        if (__result == null && Applies(__instance) && __instance.mZombiePhase != ZombiePhase.GargantuarSmashing
            && HypnotizedZamboni.ZombonisInReach(__instance).Count > 0)
        {
            __result = HypnotizedGargantuar.TruthyPlant(__instance);
        }
    }
}

// A flipped (hypnotized) Zomboni is drawn about 256 px in front of its logical position (seen in game).
//
// Zombie.UpdateReanim builds the zombie's overlay matrix from scratch every call: scale x = -1 when
// IsWalkingBackwards, and a translation (matrix element m02) of GetDrawPos().ImageOffsetX (= posX + 68 for
// a Zomboni) + 15 + 30 - 30 * scale, plus 90 * scale when flipped - a constant written for the narrow
// walking zombies, whose rect and art are about as wide as that correction. The Zomboni is 153 px wide, and
// the flipped picture ended up on the wrong side of its own rect:
//   measured (Latest.log, three ages, mPosX 16 / 76 / 230): the sprite's left edge is 628 / 620 / 634 world
//   units right of the controller root = 223 px (2.82 world units per px, the slope between the samples)
//   right of mPosX, while the mirrored rect (GetZombieRect: x = mWidth - 0 - 153) starts at -33 px.
// Moving the translation by -256 px puts the left edge on the rect's left edge; the art is the rect's width
// (native art and rect are both 0..153), so the right edge follows. The matrix is rebuilt by every
// UpdateReanim, so this is not cumulative, nothing is left on the pooled controller for a later (native)
// Zomboni, and it only ever runs for a hypnotized one.
[HarmonyPatch(typeof(Zombie), nameof(Zombie.UpdateReanim))]
public class Zombie_UpdateReanim_MindControlledZamboni_Patch
{
    private const float MirroredDrawShiftPx = -256f;

    public static void Postfix(Zombie __instance)
    {
        if (!HypnotizedZamboni.IsMindControlledZomboni(__instance) || __instance.mController == null)
        {
            return;
        }

        var matrix = __instance.mController.GetOverlayMatrix();
        matrix.m02 += MirroredDrawShiftPx;
        __instance.mController.SetOverlayMatrix(matrix);
    }
}
