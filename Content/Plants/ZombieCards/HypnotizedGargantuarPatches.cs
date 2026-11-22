using HarmonyLib;
using Il2CppReloaded.Gameplay;
using MelonLoader;

namespace PvZReCoreLib.Content.Plants.ZombieCards;

// A hypnotized Gargantuar (one a zombie card conjured; the native game never produces one, see
// below) is made to do what a hypnotized zombie is supposed to do: walk right and smash the
// ENEMY zombies in front of it instead of our own plants, and throw its Imp forwards, on our side.
//
// What the native game does (Zombie.UpdateZombieGargantuar, called by UpdateActions for zombie
// types 23 Gargantuar and 32 RedeyeGargantuar with no mind-control test at all):
//  - Zombie.CheckIfPreyCaught returns early for types 23/32, so a Gargantuar never "eats", and the
//    only native caller of StartMindControlled is Zombie.AnimateChewSound (a zombie chewing a
//    Hypno-shroom). So a Gargantuar can never be hypnotized natively and the code was never written
//    for it: UpdateZombieGargantuar only ever calls Zombie.FindPlantTarget (never FindZombieTarget).
//      * walking: if (below half health and has the Imp and posX - 360 > 40) -> phase 69 anim_throw;
//        else if FindPlantTarget != null -> phase 70 anim_smash.
//      * phase 70, at 74% of the animation: plant = FindPlantTarget; if found, SquishAllInSquare
//        (that plant's tile) kills every plant on it. Then (always) screen shake + sound 40.
//      * phase 69, at 64% of the animation: Board.AddZombie(Imp) at x = posX - 133 (to the LEFT),
//        mVelX = 3 (UpdateZombieImp does posX -= mVelX), phase 71 ImpGettingThrown, lobbed with a
//        vertical speed worked out from (posX - 360) so it lands in the left part of the lawn. The
//        Imp is a plain enemy zombie (never mind-controlled).
//
// So for a mind-controlled Gargantuar we make three things true, without touching normal ones:
//  1. FindPlantTarget (only while a mind-controlled Gargantuar's UpdateZombieGargantuar is running)
//     is answered with "is there an enemy zombie in my attack rect" instead of "is there a plant":
//     the start of the smash needs only a non-null answer (it is never dereferenced), and at the
//     smash moment the zombies are damaged here and null is returned, so no plant is squished but
//     the shake and sound the native code adds afterwards still happen.
//  2. The smash hurts every enemy zombie in the Gargantuar's (mirrored, so forward) attack rect
//     with the same 1800 damage as Squash (Plant.DoSquashDamage: TakeDamage(1800, 18)).
//  3. The throw is run in mirrored lawn space: UpdateZombieGargantuar runs with mPosX temporarily
//     replaced by its mirror image, so the native "enough room to throw" test and the lob (distance
//     worked out from posX) are the mirror image of an enemy's, and afterwards the freshly thrown
//     Imp is mirrored back (position, direction) and hypnotized, so it flies right over the enemy
//     zombies and lands among them. mPosX is restored (also if the native code throws).
//
// Nothing here runs unless the zombie is mind-controlled AND type 23/32.
internal static class HypnotizedGargantuar
{
    // Plant.DoSquashDamage (the Squash): TakeDamage(1800, HitsShieldAndBody | DoesntLeaveBody).
    internal const int SmashDamage = 1800;
    internal const DamageFlags SmashFlags = DamageFlags.HitsShieldAndBody | DamageFlags.DoesntLeaveBody;

    // Horizontal overlap (px) the native FindZombieTarget needs before a zombie counts as hit.
    private const int MinOverlap = 20;

    // The lawn spans x = 40..760; the throw maths assumes "the left of the lawn" is the far side, so
    // a mind-controlled Gargantuar is simulated at 800 - x - width (the mirror image, about x = 400).
    private const float MirrorSum = 800f;

    // Native: the Imp is created at the Gargantuar's posX - 133.
    private const float ImpOffset = 133f;

    // Pointer of the mind-controlled Gargantuar whose UpdateZombieGargantuar is currently running
    // (0 when none). Gates the FindPlantTarget redirect to exactly that call.
    internal static IntPtr Active = IntPtr.Zero;
    internal static float ActiveRealX;

    internal static bool IsMindControlledGargantuar(Zombie z)
    {
        return z != null
            && z.mMindControlled
            && (z.mZombieType == ZombieType.Gargantuar || z.mZombieType == ZombieType.RedeyeGargantuar);
    }

    internal static float Mirror(Zombie z, float x)
    {
        return MirrorSum - x - Math.Max(z.mWidth, 0);
    }

    // FindPlantTarget stand-in for the start of the smash: any non-null Plant means "go smash".
    // The native code only tests it against null at that point (UpdateZombieGargantuar, ISIL offset
    // 076-078: rax is not used again before the phase is set to 70), so a real plant is returned
    // when there is one, and a wrapper around the zombie's own pointer otherwise (never read).
    internal static Plant TruthyPlant(Zombie z)
    {
        try
        {
            foreach (var item in z.mBoard.m_plants.m_list)
            {
                if (item.mItem != null)
                {
                    return item.mItem;
                }
            }
        }
        catch (Exception)
        {
            // fall through to the wrapper
        }

        return new Plant(z.Pointer);
    }

    // The test the native Zombie.FindZombieTarget uses (so the smash hits exactly what started it):
    // Common.GetRectOverlap(attack rect, target rect) is the horizontal overlap in px (negative = gap),
    // and a target counts with >= MinOverlap, or with any contact (>= 0) when it is eating - an enemy
    // zombie chewing on the Gargantuar stands right against it, with less than MinOverlap, and the
    // Gargantuar still has to smash it (the game log showed it swinging at such a zombie forever).
    private static bool InSmashRange(Zombie garg, Zombie other)
    {
        var attack = garg.GetZombieAttackRect();
        var body = other.GetZombieRect();

        int overlap = Il2CppReloaded.Gameplay.Common.GetRectOverlap(ref attack, ref body);
        return overlap >= MinOverlap || (overlap >= 0 && other.mIsEating);
    }

    // The zombie-side SquishAllInSquare: hurt every enemy zombie in front of the Gargantuar.
    internal static void SmashEnemyZombies(Zombie garg)
    {
        var board = garg.mBoard;
        if (board == null)
        {
            return;
        }

        foreach (var entry in board.m_zombies.m_list.ToList())
        {
            var other = entry.mItem;
            if (other == null
                || other.Pointer == garg.Pointer
                || other.mMindControlled == garg.mMindControlled
                || other.mRow != garg.mRow
                || other.IsDeadOrDying()
                || !other.EffectedByDamage(DamageRangeFlags.Ground)
                || !InSmashRange(garg, other))
            {
                continue;
            }

            other.TakeDamage(SmashDamage, SmashFlags);
        }
    }

    // The Imp UpdateZombieGargantuar just created: a plain Imp in the "being thrown" phase, standing
    // exactly where the native code puts it (simulatedX - 133) in the Gargantuar's row.
    internal static Zombie FindThrownImp(Zombie garg, float simulatedX)
    {
        float expectedX = simulatedX - ImpOffset;

        foreach (var entry in garg.mBoard.m_zombies.m_list.ToList())
        {
            var z = entry.mItem;
            if (z != null
                && z.mZombieType == ZombieType.Imp
                && z.mZombiePhase == ZombiePhase.ImpGettingThrown
                && !z.mMindControlled
                && z.mRow == garg.mRow
                && Math.Abs(z.mPosX - expectedX) < 1f)
            {
                return z;
            }
        }

        return null;
    }

    // Puts the thrown Imp on our side and sends it the other way: x mirrored, flight direction
    // reversed (UpdateZombieImp does posX -= mVelX, so a negative mVelX flies right; landing resets
    // the walking speed through StartWalkAnim -> PickRandomSpeed).
    internal static void TurnImpAround(Zombie garg, Zombie imp)
    {
        imp.StartMindControlled();
        imp.mDroppedLoot = true;

        imp.mPosX = MirrorSum - imp.mPosX - Math.Max(imp.mWidth, 0);
        imp.mVelX = -imp.mVelX;

        try
        {
            imp.mApp.AddTodParticle(imp.mPosX + 30f, imp.mPosY + 30f, imp.mRenderOrder + 1, ParticleEffect.MindControl);
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Could not spawn the hypnosis particle on the thrown Imp: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(Zombie), nameof(Zombie.UpdateZombieGargantuar))]
public class Zombie_UpdateZombieGargantuar_MindControlled_Patch
{
    public struct State
    {
        public bool active;
        public float realX;
        public float simulatedX;
        public bool hadObject;
    }

    public static void Prefix(Zombie __instance, out State __state)
    {
        __state = default;

        if (!HypnotizedGargantuar.IsMindControlledGargantuar(__instance))
        {
            return;
        }

        __state.active = true;
        __state.realX = __instance.mPosX;
        __state.simulatedX = HypnotizedGargantuar.Mirror(__instance, __state.realX);
        __state.hadObject = __instance.mHasObject;

        HypnotizedGargantuar.Active = __instance.Pointer;
        HypnotizedGargantuar.ActiveRealX = __state.realX;
        __instance.mPosX = __state.simulatedX;
    }

    // The Imp throw, if it happened this tick: native clears mHasObject and creates the Imp.
    public static void Postfix(Zombie __instance, State __state)
    {
        if (!__state.active || !__state.hadObject || __instance.mHasObject)
        {
            return;
        }

        try
        {
            var imp = HypnotizedGargantuar.FindThrownImp(__instance, __state.simulatedX);
            if (imp != null)
            {
                HypnotizedGargantuar.TurnImpAround(__instance, imp);
            }
            else
            {
                MelonLogger.Warning("[CoreLib] Hypnotized Gargantuar threw its Imp but the Imp was not found; it stays an enemy.");
            }
        }
        catch (Exception e)
        {
            MelonLogger.Error($"[CoreLib] Could not hypnotize the Imp thrown by a hypnotized Gargantuar: {e}");
        }
    }

    // Always runs, even if the native code (or another patch) threw.
    public static Exception Finalizer(Exception __exception, Zombie __instance, State __state)
    {
        if (__state.active)
        {
            __instance.mPosX = __state.realX;
            HypnotizedGargantuar.Active = IntPtr.Zero;
        }

        return __exception;
    }
}

// Inside a mind-controlled Gargantuar's update, "is there a plant to smash" becomes "is there an
// enemy zombie to smash", and the smash lands on zombies. See the file header.
[HarmonyPatch(typeof(Zombie), nameof(Zombie.FindPlantTarget))]
public class Zombie_FindPlantTarget_MindControlledGargantuar_Patch
{
    public static bool Prefix(Zombie __instance, ref Plant __result)
    {
        if (HypnotizedGargantuar.Active == IntPtr.Zero || __instance == null || __instance.Pointer != HypnotizedGargantuar.Active)
        {
            return true;
        }

        // The target search is about where the Gargantuar really is, not the mirrored x the throw
        // maths is running with, so put the real x back for the duration of the search.
        float simulatedX = __instance.mPosX;
        __instance.mPosX = HypnotizedGargantuar.ActiveRealX;
        try
        {
            if (__instance.mZombiePhase == ZombiePhase.GargantuarSmashing)
            {
                // The smash moment: hit the zombies; null makes the native code skip the plant
                // squish but still do the screen shake and the smash sound.
                HypnotizedGargantuar.SmashEnemyZombies(__instance);
                __result = null;
            }
            else
            {
                // Walking: start the smash when an enemy zombie is in range.
                __result = __instance.FindZombieTarget() != null ? HypnotizedGargantuar.TruthyPlant(__instance) : null;
            }
        }
        finally
        {
            __instance.mPosX = simulatedX;
        }

        return false;
    }
}
