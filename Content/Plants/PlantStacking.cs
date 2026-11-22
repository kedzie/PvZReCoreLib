using Il2CppReloaded;
using Il2CppReloaded.Data;
using Il2CppReloaded.Gameplay;
using Il2CppReloaded.Services;
using MelonLoader;
using PvZReCoreLib.Content.Plants.Behavior;

namespace PvZReCoreLib.Content.Plants;

// Stackable plants (PvZ2 "CanPlantAgainToUpgrade"; Peapod: plant it again on the same tile, up to
// CustomPlantDefinition.m_maxStack times, each planting adds a head).
//
// How it works (all of it additive - a plant with m_maxStack = 1 never gets here):
//   1. Board.CanPlantAt is what the click path (Board.MouseDownWithPlant), the cursor preview and
//      the gamepad cursor all ask. For an occupied tile whose plant is not an upgrade target it
//      falls through to PlantingReason.NotHere (the final "mov eax,1" of the decompiled method).
//      The Postfix in StackablePlantPatches.cs turns that NotHere into Ok when the occupant is a
//      live plant of the SAME seed type whose stack is not full. Every other rule (pool/roof/grave,
//      Versus zones, ...) already passed when the occupant was planted on that very tile, and any
//      other rejection reason is left alone.
//   2. MouseDownWithPlant then does its normal thing: spends the sun, runs the seed packet's
//      recharge (SeedPacket.WasPlanted) and calls Board.AddPlant, which creates a second plant of
//      the same type on the tile. (Plant.IsUpgradableTo is false for custom seeds, so the plant
//      already on the tile is NOT killed the way a Repeater is when Gatling Pea is planted.)
//   3. On that new plant's first update tick (Plant_PlantUpdate_Patch -> TryMergeIntoExisting) it
//      finds the older plant on its tile, raises that plant's stack count, tells its controller
//      (OnStackedOn) and removes itself with Plant.Die() - the same "placeholder removes itself
//      on its first tick" shape as the zombie cards. Its own native update never runs.
//
// The count is stored in the native Plant's mTargetX. Native code only reads that field for Cob
// Cannon / Squash / Tanglekelp-style targeting (checked in the decompiled Plant methods), never
// for a shooter; PlantInitialize resets it to -1 (= one planting) and Plant.Serialize/Deserialize
// write it, so a stack count survives a save/load without any extra bookkeeping.
public static class PlantStacking
{
    // Current number of plantings in 'plant' (1 when it is not stacked or max <= 1).
    public static int GetStackCount(Plant plant, int max)
    {
        if (plant == null || max <= 1)
        {
            return 1;
        }

        int stored = plant.mTargetX;
        return stored >= 1 && stored <= max ? stored : 1;
    }

    public static int GetMaxStack(SeedType seedType)
    {
        if (!CustomContentRegistry.IsValidCustomPlantType(seedType))
        {
            return 1;
        }

        var plantDef = AppCore.GetService<IDataService>().Cast<DataService>().GetPlantDefinition(seedType);
        return plantDef != null && plantDef.TryCast<CustomPlantDefinition>() is { } customDef
            ? Math.Max(1, customDef.m_maxStack)
            : 1;
    }

    // The live plant of 'seedType' on tile (col, row) other than 'exclude', or null.
    public static Plant FindPlantAt(Board board, int col, int row, SeedType seedType, Plant exclude = null)
    {
        if (board?.m_plants == null)
        {
            return null;
        }

        foreach (var item in board.m_plants.m_list)
        {
            var other = item.mItem;
            if (other == null
                || (exclude != null && other.Pointer == exclude.Pointer)
                || other.mDead
                || other.mSeedType != seedType
                || other.mPlantCol != col
                || other.mRow != row)
            {
                continue;
            }

            return other;
        }

        return null;
    }

    // True when 'seedType' may be planted on (col, row) as an extra planting of the plant there.
    public static bool CanStackOn(Board board, int col, int row, SeedType seedType)
    {
        int max = GetMaxStack(seedType);
        if (max <= 1)
        {
            return false;
        }

        var existing = FindPlantAt(board, col, row, seedType);
        return existing != null && GetStackCount(existing, max) < max;
    }

    // First-tick check of a freshly planted plant: if another live plant of the same type is on its
    // tile, merge into it and remove this one. Returns true when this plant was merged away (its
    // update must be skipped).
    public static bool TryMergeIntoExisting(CustomPlantBehaviorController controller)
    {
        try
        {
            var plant = controller.Plant;
            int max = controller.MaxStack;
            if (plant == null || max <= 1 || plant.mDead)
            {
                return false;
            }

            var existing = FindPlantAt(plant.mBoard, plant.mPlantCol, plant.mRow, plant.mSeedType, plant);
            if (existing == null)
            {
                return false;
            }

            int count = GetStackCount(existing, max);
            if (count >= max)
            {
                // CanPlantAt refuses this, so it only happens if something else put a second plant
                // here. Leave it as the ordinary plant it is rather than eating the sun.
                MelonLogger.Warning($"[CoreLib] {plant.mSeedType} planted on a full stack at ({plant.mPlantCol},{plant.mRow}); leaving it as a separate plant.");
                return false;
            }

            existing.mTargetX = count + 1;
            CustomPlantBehaviorController.GetFor(existing)?.OnStackedOn(count + 1);

            // Remove the placeholder; Plant.Die also tears down its controller object.
            controller.bMergedAway = true;
            plant.Die();
            return true;
        }
        catch (Exception e)
        {
            MelonLogger.Error($"[CoreLib] Plant stacking failed: {e}");
            return false;
        }
    }
}
