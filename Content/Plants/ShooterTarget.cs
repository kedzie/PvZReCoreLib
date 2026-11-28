using Il2CppReloaded.Gameplay;
using MelonLoader;

namespace PvZReCoreLib.Content.Plants;

// Test harness for replacing a shooter's own zombie scan with the game's native Plant.FindTargetZombie(row, weapon):
// one native call instead of walking the zombie list. Whether the native search works for a custom plant is unverified
// (it looks at the plant's attack rect and damage flags, which the game derives from the seed type), so for now each
// call still RETURNS the plant's own scan result, and only logs where the native one differs:
//
//   [ShooterTarget] <plant>: n checks, k differ (native null / scan found a, native found / scan null b, other zombie c)
//
// Once the numbers are clean over a real game, the plants return the native result and drop the scan.
public static class ShooterTarget
{
    private class Stats
    {
        public int Checks;
        public int NativeNull;
        public int ScanNull;
        public int Other;
        public int Logged;
    }

    private static readonly Dictionary<string, Stats> stats = new Dictionary<string, Stats>();

    public static Zombie Compare(Plant plant, string name, Zombie scanResult)
    {
        try
        {
            var native = plant.FindTargetZombie(plant.mRow, PlantWeapon.Primary);

            if (!stats.TryGetValue(name, out var s))
            {
                s = new Stats();
                stats[name] = s;
            }

            s.Checks++;
            bool same = (native == null && scanResult == null)
                        || (native != null && scanResult != null && native.mRelatedZombieID == scanResult.mRelatedZombieID);
            if (!same)
            {
                if (native == null)
                {
                    s.NativeNull++;
                }
                else if (scanResult == null)
                {
                    s.ScanNull++;
                }
                else
                {
                    s.Other++;
                }

                if (s.Logged < 12)
                {
                    s.Logged++;
                    MelonLogger.Msg($"[ShooterTarget] {name} row {plant.mRow}: native={(native == null ? "null" : $"x{native.mPosX:0} row{native.mRow}")} scan={(scanResult == null ? "null" : $"x{scanResult.mPosX:0} row{scanResult.mRow}")} plantX={plant.mX:0}");
                }
            }

            if (s.Checks % 500 == 0)
            {
                MelonLogger.Msg($"[ShooterTarget] {name}: {s.Checks} checks, {s.NativeNull + s.ScanNull + s.Other} differ (native null / scan found {s.NativeNull}, native found / scan null {s.ScanNull}, other zombie {s.Other})");
            }
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[ShooterTarget] {name}: native FindTargetZombie failed: {e.Message}");
        }

        return scanResult;
    }
}
