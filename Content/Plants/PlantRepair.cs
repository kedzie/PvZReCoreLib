using Il2CppReloaded;
using Il2CppReloaded.Data;
using Il2CppReloaded.Services;
using Il2CppReloaded.Gameplay;
using MelonLoader;
using PvZReCoreLib.Content.Plants.Behavior;

namespace PvZReCoreLib.Content.Plants;

// Two ways a custom plant may be planted on a tile that is already occupied (both are opt-in flags on
// CustomPlantDefinition, both only turn the native "tile is occupied" answer into Ok - see
// Patches/PlantOnPlantPatches.cs - and both work like PlantStacking: the new planting is a placeholder that
// acts on its first update tick):
//
//  * m_healsOnReplant (nuts): the same plant on a damaged one of its own kind. The placeholder heals the old
//    plant to full health and removes itself, so the seed is spent for the repair, as with a native Wall-nut.
//  * m_plantsOnTopOfPlant (Pea Vine): any other plant on the tile. The placeholder is simply kept as a second
//    plant; nothing to do on the first tick. It also works the other way round: any plant may be planted on a
//    tile that holds only such a plant (CanPlantOnVine).
public static class PlantRepair
{
    // Health fraction below which a nut counts as damaged enough to be healed: two thirds is where the
    // damage art starts (cracked nut), and where the original game's Wall-nut first aid starts too.
    private const int DamagedNumerator = 2;
    private const int DamagedDenominator = 3;

    private static CustomPlantDefinition GetDefinition(SeedType seedType)
    {
        if (!CustomContentRegistry.IsValidCustomPlantType(seedType))
        {
            return null;
        }

        var plantDef = AppCore.GetService<IDataService>().Cast<DataService>().GetPlantDefinition(seedType);
        return plantDef?.TryCast<CustomPlantDefinition>();
    }

    public static bool IsDamaged(Plant plant)
    {
        return plant != null
               && !plant.mDead
               && plant.mPlantMaxHealth > 0
               && plant.mPlantHealth * DamagedDenominator < plant.mPlantMaxHealth * DamagedNumerator;
    }

    // True when 'seedType' may be planted on (col, row) to heal the same plant that is there.
    public static bool CanHealOn(Board board, int col, int row, SeedType seedType)
    {
        if (GetDefinition(seedType) is not { m_healsOnReplant: true })
        {
            return false;
        }

        return IsDamaged(PlantStacking.FindPlantAt(board, col, row, seedType));
    }

    // True when 'seedType' may be planted on (col, row) on top of whatever plant is there.
    public static bool CanPlantOnTop(Board board, int col, int row, SeedType seedType)
    {
        if (GetDefinition(seedType) is not { m_plantsOnTopOfPlant: true } || board?.m_plants == null)
        {
            return false;
        }

        bool occupied = false;
        foreach (var item in board.m_plants.m_list)
        {
            var other = item.mItem;
            if (other == null || other.mDead || other.mPlantCol != col || other.mRow != row)
            {
                continue;
            }

            if (other.mSeedType == seedType)
            {
                // One of these per tile.
                return false;
            }

            occupied = true;
        }

        return occupied;
    }

    // True when the only plant(s) on (col, row) are m_plantsOnTopOfPlant plants (a Pea Vine planted first) and
    // 'seedType' is a different kind of plant: it may then be planted under/with the vine, like a Pumpkin, in
    // whichever order. Anything else on the tile (another plant already under the vine) keeps the native answer.
    public static bool CanPlantOnVine(Board board, int col, int row, SeedType seedType)
    {
        if (board?.m_plants == null || GetDefinition(seedType) is { m_plantsOnTopOfPlant: true })
        {
            return false;
        }

        bool vineThere = false;
        foreach (var item in board.m_plants.m_list)
        {
            var other = item.mItem;
            if (other == null || other.mDead || other.mPlantCol != col || other.mRow != row)
            {
                continue;
            }

            if (GetDefinition(other.mSeedType) is not { m_plantsOnTopOfPlant: true })
            {
                return false;
            }

            vineThere = true;
        }

        return vineThere;
    }

    // First-tick check of a freshly planted plant: a nut planted on a damaged one of its kind heals it and
    // removes itself. Returns true when this plant was consumed (its update must be skipped).
    public static bool TryHealExisting(CustomPlantBehaviorController controller)
    {
        try
        {
            var plant = controller.Plant;
            if (plant == null || plant.mDead || GetDefinition(plant.mSeedType) is not { m_healsOnReplant: true })
            {
                return false;
            }

            var existing = PlantStacking.FindPlantAt(plant.mBoard, plant.mPlantCol, plant.mRow, plant.mSeedType, plant);
            if (existing == null)
            {
                return false;
            }

            existing.mPlantHealth = existing.mPlantMaxHealth;

            controller.bMergedAway = true;
            plant.Die();
            return true;
        }
        catch (Exception e)
        {
            MelonLogger.Error($"[CoreLib] Plant repair failed: {e}");
            return false;
        }
    }
}
