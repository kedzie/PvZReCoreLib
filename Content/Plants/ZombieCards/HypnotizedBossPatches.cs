using HarmonyLib;
using Il2CppReloaded.Gameplay;
using MelonLoader;
using UnityEngine;

namespace PvZReCoreLib.Content.Plants.ZombieCards;

// Dr. Zomboss' fireballs and iceballs and the hypnotized zombies (a card's zombie, or one a Hypno-shroom
// turned).
//
// Natively the ball is a Reanimation of the boss zombie (Zombie.mBossFireball, mBossFireballRow, and a flag
// that tells fire from ice). Zombie.UpdateBossFireball moves it along its row and on EVERY tick calls
// Zombie.SquishAllInSquare(PixelToGridX(ball x), row, DriveOver) - the plant-killing call - and
// LawnMower.SquishMower for the mowers it touches. Nothing in it looks at zombies, so a hypnotized zombie
// (the plant side's fighter) walked through a ball unharmed.
//
// While a (non-hypnotized) boss runs UpdateBossFireball, the tile it squishes now also kills the hypnotized
// zombies standing in it: whatever its type, a ball is lethal to plants, so it is to their fighters too. Only
// UpdateBossFireball turns this on, so a Gargantuar smash or a Squash landing (the same SquishAllInSquare)
// are not affected.
internal static class HypnotizedBossBall
{
    // Pointer of the boss whose UpdateBossFireball is running (0 = none).
    internal static IntPtr Active = IntPtr.Zero;

    private const float TileWidth = 80f;

    // Enough to kill anything, Gargantuars included.
    private const int KillDamage = 9999;

    internal static void KillHypnotizedInTile(Zombie boss, int col, int row)
    {
        var board = boss.mBoard;
        if (board == null)
        {
            return;
        }

        float left = board.GridToPixelX(col, row);
        float right = left + TileWidth;

        var victims = new List<Zombie>();
        var all = board.m_zombies;
        for (int i = 0; i < all.Count; i++)
        {
            var z = all[i];
            if (z == null || !z.mMindControlled || z.mRow != row || z.IsDeadOrDying())
            {
                continue;
            }

            Rect body = z.GetZombieRect();
            if (body.x + body.width >= left && body.x <= right)
            {
                victims.Add(z);
            }
        }

        foreach (var victim in victims)
        {
            victim.TakeDamage(KillDamage, HypnotizedGargantuar.SmashFlags);
        }
    }
}

[HarmonyPatch(typeof(Zombie), nameof(Zombie.UpdateBossFireball))]
public class Zombie_UpdateBossFireball_HypnotizedVictims_Patch
{
    public static void Prefix(Zombie __instance)
    {
        if (__instance != null && !__instance.mMindControlled)
        {
            HypnotizedBossBall.Active = __instance.Pointer;
        }
    }

    // Always runs, even if the native code threw.
    public static Exception Finalizer(Exception __exception)
    {
        HypnotizedBossBall.Active = IntPtr.Zero;
        return __exception;
    }
}

[HarmonyPatch(typeof(Zombie), nameof(Zombie.SquishAllInSquare))]
public class Zombie_SquishAllInSquare_BossBall_Patch
{
    public static void Postfix(Zombie __instance, int theX, int theY)
    {
        if (HypnotizedBossBall.Active == IntPtr.Zero || __instance == null || __instance.Pointer != HypnotizedBossBall.Active)
        {
            return;
        }

        try
        {
            HypnotizedBossBall.KillHypnotizedInTile(__instance, theX, theY);
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Boss ball vs hypnotized zombies failed: {e.Message}");
        }
    }
}
