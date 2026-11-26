using HarmonyLib;
using Il2CppReloaded.Gameplay;
using MelonLoader;
using UnityEngine;

namespace PvZReCoreLib.Content.Plants.ZombieCards;

// Hypnotized zombies biting Dr. Zomboss.
//
// Zombie.FindZombieTarget (the search a zombie runs for something to bite) has no check for the boss, but it
// needs the target in the SAME ROW with at least 20 px of horizontal overlap between the biter's attack
// rectangle and the target's body rectangle, and the boss does not stand in a row a lane's zombie can match. So
// while his head is down (the phases from BossHeadEnter to BossHeadSpit - the head is the part that reaches the
// lawn), a hypnotized zombie whose attack rectangle overlaps his body rectangle by the same 20 px counts him as
// a target whatever the row. The bite itself is native: Zombie.EatZombie -> TakeDamage(4), so a horde is needed.
//
// What is logged: the boss's row and rectangle whenever his phase changes while a hypnotized zombie is on the
// lawn, and the first bite of each head-down. That is what to tune the rule with (the reach is a guess).
internal static class HypnotizedBossBite
{
    private const float MinOverlap = 20f;

    private static ZombiePhase _loggedPhase = (ZombiePhase)(-1);
    private static bool _loggedBite;

    internal static bool IsHeadDown(ZombiePhase phase)
    {
        return phase >= ZombiePhase.BossHeadEnter && phase <= ZombiePhase.BossHeadSpit;
    }

    internal static Zombie FindBoss(Zombie biter)
    {
        var board = biter.mBoard;
        if (board == null)
        {
            return null;
        }

        var boss = board.GetBossZombie();
        if (boss == null || boss.mMindControlled || boss.IsDeadOrDying())
        {
            return null;
        }

        return boss;
    }

    internal static Zombie Choose(Zombie biter)
    {
        if (biter.mZombieType == ZombieType.Boss || biter.IsDeadOrDying())
        {
            return null;
        }

        var boss = FindBoss(biter);
        if (boss == null)
        {
            return null;
        }

        var phase = boss.mZombiePhase;
        Rect body = boss.GetZombieRect();
        if (phase != _loggedPhase)
        {
            _loggedPhase = phase;
            _loggedBite = false;
            MelonLogger.Msg($"[CoreLib] Boss for hypnotized zombies: phase {phase}, row {boss.mRow}, body rect x={body.x:F0} w={body.width:F0} y={body.y:F0} h={body.height:F0}, hypnotized zombie {biter.mZombieType} row {biter.mRow} x={biter.mX:F0}.");
        }

        if (!IsHeadDown(phase))
        {
            return null;
        }

        Rect bite = biter.GetZombieAttackRect();
        float overlap = Mathf.Min(bite.x + bite.width, body.x + body.width) - Mathf.Max(bite.x, body.x);
        if (overlap < MinOverlap)
        {
            return null;
        }

        if (!_loggedBite)
        {
            _loggedBite = true;
            MelonLogger.Msg($"[CoreLib] A hypnotized {biter.mZombieType} (row {biter.mRow}) bites Zomboss (overlap {overlap:F0}).");
        }

        return boss;
    }
}

[HarmonyPatch(typeof(Zombie), nameof(Zombie.FindZombieTarget))]
public class Zombie_FindZombieTarget_BossBite_Patch
{
    public static void Postfix(Zombie __instance, ref Zombie __result)
    {
        if (__result != null || __instance == null || !__instance.mMindControlled)
        {
            return;
        }

        try
        {
            var boss = HypnotizedBossBite.Choose(__instance);
            if (boss != null)
            {
                __result = boss;
            }
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Hypnotized zombie vs Zomboss check failed: {e.Message}");
        }
    }
}
