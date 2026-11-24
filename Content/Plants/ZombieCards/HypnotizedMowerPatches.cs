using HarmonyLib;
using Il2CppReloaded.Gameplay;

namespace PvZReCoreLib.Content.Plants.ZombieCards;

// Lawn mowers ignore hypnotized zombies - all of them, not only the card zombies.
//
// What the native game does (LawnMower.Update): for a mower that is Ready (waiting at the house) or
// Triggered (rolling), every tick it walks all zombies and for each one in its row that is not dead, is
// affected by damage and overlaps the mower's attack rect it calls LawnMower.MowZombie(zombie). It never
// looks at Zombie.mMindControlled. For a Ready mower MowZombie is what sets it off (StartMower), and for a
// rolling one it mows the zombie down - so a hypnotized zombie standing or walking over the mower at the
// left edge (a Zomboni or a Bobsled Team launched from the left edge, a dancer, a Pole Vaulter landing
// back there) started the mower, and a rolling mower killed our own zombies.
// The other native trigger, the left-edge check in Zombie.CheckForBoardEdge, is handled by
// Zombie_CheckForBoardEdge_MindControlled_Patch (HypnotizedZombiePatches.cs). No other caller of
// LawnMower.StartMower exists in the decompile.
[HarmonyPatch(typeof(LawnMower), nameof(LawnMower.MowZombie))]
public class LawnMower_MowZombie_MindControlled_Patch
{
    public static bool Prefix(Zombie theZombie)
    {
        return theZombie == null || !theZombie.mMindControlled;
    }
}
