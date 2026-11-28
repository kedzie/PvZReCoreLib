using HarmonyLib;
using Il2CppReloaded.Gameplay;
using MelonLoader;
using MelonLoader.Utils;
using PvZReCoreLib.Content.Plants.ZombieCards;

namespace PvZReCoreLib.Content.Plants.Patches;

// Custom plants on conveyor-belt levels.
//
// Challenge.UpdateConveyorBelt() builds a TodWeightedArray of (SeedType, weight) pairs for the level and picks
// one with a weighted random roll, then calls SeedBank.AddSeed(seed, false) to put the packet on the belt.
// The first attempt hooked Challenge.PickAndAddSeed(bank, seeds, count) - but the decompile shows that method
// has CallerCount 0: the compiler inlined it into UpdateConveyorBelt, so the patch was on a method that is
// never called and the belt never saw a custom plant.
//
// SeedBank.AddSeed is a real, separate method (20 callers). So: while UpdateConveyorBelt is running, some
// of the seeds it adds are swapped for a random custom plant. Nothing of the level's own table is touched, the
// belt's capacity and drop timing stay native, and only UpdateConveyorBelt turns the swap on (AddSeed is also
// used when a level starts or a seed is awarded).
// The belt's settings. The base is plain MelonLoader preferences in Mods\CoreLib\ConveyorBelt.cfg (created with
// the defaults on first run; edit the file and restart the game) and needs no other mod. The code only ever
// reads those entries. When the BloomEngine mod menu is installed it gets a page for the two percentages, and
// that page only changes the entries: BloomEngine keeps its own saved value, which is copied into the entry at
// start-up and again on every change in the menu.
internal static class ConveyorSettings
{
    private const int PlantDefault = 25;
    private const int ZombieDefault = 10;
    private const int SpeedDefault = 1;

    private static MelonPreferences_Entry<int> _plantChance;
    private static MelonPreferences_Entry<int> _zombieChance;
    private static MelonPreferences_Entry<int> _beltSpeed;
    private static MelonPreferences_Entry<bool> _roofRules;
    private static MelonPreferences_Category _category;
    private static Func<int> _bloomPlants;
    private static Func<int> _bloomZombies;
    private static Func<int> _bloomSpeed;

    // Percent of the belt's drops that are replaced by a custom plant.
    internal static int CustomPlantPercent => Clamp(_plantChance?.Value ?? PlantDefault);

    // Percent of the belt's drops that are replaced by a hypnotized zombie card. Whatever is left of the 100%
    // (after the custom plants) is the level's own drops.
    internal static int ZombieCardPercent => Clamp(_zombieChance?.Value ?? ZombieDefault);

    // How many times faster the belt drops packets (1 = the game's own pace).
    internal static int BeltSpeed => Math.Max(1, Math.Min(100, _beltSpeed?.Value ?? SpeedDefault));

    // On a roof level, leave out the plants that cannot work there.
    internal static bool RoofRules => _roofRules?.Value ?? true;

    private static int Clamp(int value) => Math.Max(0, Math.Min(100, value));

    internal static void Init(MelonMod mod)
    {
        try
        {
            _category = MelonPreferences.CreateCategory("CoreLib_ConveyorBelt", "CoreLib conveyor belt");
            _category.SetFilePath(Path.Combine(MelonEnvironment.ModsDirectory, "CoreLib", "ConveyorBelt.cfg"));

            _plantChance = _category.CreateEntry("CustomPlantChance", PlantDefault, "Conveyor belt: percent (0-100) of the drops that are a custom plant (the Complete Collection's PvZ2 plants and any other mod's). 0 = never.");
            _zombieChance = _category.CreateEntry("ZombieCardChance", ZombieDefault, "Conveyor belt: percent (0-100) of the drops that are a hypnotized zombie card. 0 = never. What is left of the 100% after the custom plants and zombie cards is the level's own plants.");
            _beltSpeed = _category.CreateEntry("BeltSpeed", SpeedDefault, "Conveyor belt: how many times faster packets drop (1-100). 1 = the game's own pace. Around 100 a packet drops every tick the belt has room.");
            _roofRules = _category.CreateEntry("RoofRules", true, "Conveyor belt: on a roof level, do not drop plants that cannot work there (straight shooters, ground plants).");

            _category.SaveToFile(false);
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Conveyor belt settings could not be set up, using the defaults: {e.Message}");
        }

        // BloomEngine is loaded before us when it is installed (MelonOptionalDependencies). Look for the loaded
        // assembly instead of touching any of its types, so a missing BloomEngine can never cause a load failure.
        if (!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "BloomEngine"))
        {
            return;
        }

        try
        {
            (_bloomPlants, _bloomZombies, _bloomSpeed) = ConveyorBloomEngine.Register(mod, _plantChance?.Value ?? PlantDefault, _zombieChance?.Value ?? ZombieDefault, _beltSpeed?.Value ?? SpeedDefault,
                value => WriteBack(_plantChance, value), value => WriteBack(_zombieChance, value), value => WriteBack(_beltSpeed, value));

            // Bring the file in line with what the menu holds from the start (BloomEngine keeps its own saved value).
            WriteBack(_plantChance, _bloomPlants());
            WriteBack(_zombieChance, _bloomZombies());
            WriteBack(_beltSpeed, _bloomSpeed());
            MelonLogger.Msg("[CoreLib] BloomEngine found - the conveyor belt settings are in its mod menu.");
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] BloomEngine is installed but the conveyor belt menu could not be set up ({e.GetType().Name}: {e.Message}) - using ConveyorBelt.cfg instead.");
            _bloomPlants = null;
            _bloomZombies = null;
            _bloomSpeed = null;
        }
    }

    private static void WriteBack(MelonPreferences_Entry<int> entry, int value)
    {
        try
        {
            if (entry != null && entry.Value != value)
            {
                entry.Value = value;
                _category?.SaveToFile(false);
            }
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Could not write the conveyor belt setting to ConveyorBelt.cfg: {e.Message}");
        }
    }
}

internal static class ConveyorBeltCustomPlants
{
    // True while Challenge.UpdateConveyorBelt runs.
    internal static bool Active;
    internal static bool Allowed;
    private static bool _roof;
    private static bool _pool;

    private static int _swapped;

    internal static void Begin(Challenge challenge)
    {
        Active = true;
        // Wall-nut Bowling has nothing to plant: the belt holds the nuts you roll.
        Allowed = challenge.mApp == null || !challenge.mApp.IsWallnutBowlingLevel();
        _roof = false;
        _pool = true;
        try
        {
            _roof = challenge.mBoard != null && challenge.mBoard.StageHasRoof();
            _pool = challenge.mBoard == null || challenge.mBoard.StageHasPool();
        }
        catch (Exception)
        {
            // keep the plants rather than lose the belt
        }
    }

    // Faster belt: take extra ticks off the drop countdown (Challenge.mConveyorBeltCounter), so the game's own
    // interval - including its slow-down as the belt fills - is divided by the speed. The game still decides
    // what drops. Nothing is taken off while the belt is full (a drop then has nowhere to go).
    internal static void SpeedUp(Challenge challenge)
    {
        int speed = ConveyorSettings.BeltSpeed;
        if (speed <= 1 || challenge.mBoard == null || !challenge.mBoard.HasConveyorBeltSeedBank())
        {
            return;
        }

        var bank = challenge.mBoard.mSeedBank;
        if (bank == null || bank.GetNumSeedsOnConveyorBelt() >= bank.mNumPackets)
        {
            return;
        }

        int counter = challenge.mConveyorBeltCounter;
        if (counter > 1)
        {
            challenge.mConveyorBeltCounter = Math.Max(1, counter - (speed - 1));
        }
    }

    internal static void End()
    {
        Active = false;
        Allowed = false;
    }

    // The custom plants and the zombie cards that may be dropped on this level.
    internal static void Candidates(List<SeedType> plants, List<SeedType> zombies)
    {
        foreach (var seedType in CustomContentRegistry.GetAllCustomPlantTypes())
        {
            var definition = CustomContentRegistry.GetCustomPlantDefinition(seedType);

            // The hypnotized zombie cards are custom plant definitions too and ride the belt like the plants,
            // except the water-only ones on a level with no pool.
            if (definition?.TryCast<CustomZombieCardDefinition>() is { } card)
            {
                if (card.m_placement == ZombieCardPlacement.Water && !_pool)
                {
                    continue;
                }

                zombies.Add(seedType);
                continue;
            }

            // Not on a roof level (the Zomboss fight): plants that cannot live there.
            if (_roof && ConveyorSettings.RoofRules && definition?.TryCast<CustomPlantDefinition>() is { } plant
                && (plant.m_notOnRoof || plant.m_requiresGround))
            {
                continue;
            }

            plants.Add(seedType);
        }
    }

    internal static void MaybeSwap(ref SeedType seedType)
    {
        if (!Active || !Allowed)
        {
            return;
        }

        // Never replace a Flower Pot or a Lily Pad: on a roof or a pool level nothing can be planted without one.
        if (seedType == SeedType.Flowerpot || seedType == SeedType.Lilypad)
        {
            return;
        }

        // One roll over 100: the first slice is custom plants, the next one zombie cards, the rest is the
        // level's own drop (nothing is swapped).
        int roll = UnityEngine.Random.Range(0, 100);
        int plantPercent = ConveyorSettings.CustomPlantPercent;
        if (roll >= plantPercent + ConveyorSettings.ZombieCardPercent)
        {
            return;
        }

        var plants = new List<SeedType>();
        var zombies = new List<SeedType>();
        Candidates(plants, zombies);

        // The rolled kind; if that group is empty on this level, the other one.
        bool wantZombie = roll >= plantPercent;
        var group = wantZombie ? zombies : plants;
        if (group.Count == 0)
        {
            group = wantZombie ? plants : zombies;
        }

        if (group.Count == 0)
        {
            return;
        }

        var picked = group[UnityEngine.Random.Range(0, group.Count)];
        _swapped++;
        MelonLogger.Msg($"[CoreLib] Conveyor belt: custom plant {(int)picked} replaces seed {(int)seedType} (swap #{_swapped}).");
        seedType = picked;
    }
}

[HarmonyPatch(typeof(Challenge), nameof(Challenge.UpdateConveyorBelt))]
public class Challenge_UpdateConveyorBelt_CustomPlants_Patch
{
    public static void Prefix(Challenge __instance)
    {
        try
        {
            ConveyorBeltCustomPlants.SpeedUp(__instance);
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Conveyor belt speed-up failed: {e.Message}");
        }

        ConveyorBeltCustomPlants.Begin(__instance);
    }

    // Always runs, even if the native code threw.
    public static Exception Finalizer(Exception __exception)
    {
        ConveyorBeltCustomPlants.End();
        return __exception;
    }
}

[HarmonyPatch(typeof(SeedBank), nameof(SeedBank.AddSeed))]
public class SeedBank_AddSeed_ConveyorCustomPlants_Patch
{
    public static void Prefix(ref SeedType theSeedType)
    {
        try
        {
            ConveyorBeltCustomPlants.MaybeSwap(ref theSeedType);
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Conveyor belt custom plant swap failed: {e.Message}");
        }
    }
}
