using Il2CppReloaded.Gameplay;
using MelonLoader;
using PvZReCoreLib.Content.Plants.Behavior;
using Il2CppInterop.Runtime.Injection;

namespace PvZReCoreLib.Content.Plants.ZombieCards;

// Attached to the short-lived placeholder plant a zombie card creates. On the plant's first update
// tick it spawns the zombie on the plant's tile, hypnotizes it, and removes the plant - the same
// "do the thing, then Plant.Die()" shape as Tumbleweed and Enlighten Mint. The plant's own native
// update is skipped (PrePlantUpdate returns false), so it never does anything as a plant.
[RegisterTypeInIl2Cpp]
public class ZombieCardBehaviorController : CustomPlantBehaviorController
{
    private bool consumed = false;

    public ZombieCardBehaviorController(IntPtr pointer) : base(pointer)
    {
    }

    public override bool PrePlantUpdate()
    {
        base.PrePlantUpdate();

        if (consumed)
        {
            return false;
        }
        consumed = true;

        try
        {
            if (PlantDefinition.TryCast<CustomZombieCardDefinition>() is { } card)
            {
                ZombieCardUtils.SpawnHypnotizedZombie(Board, card.m_zombieType, Plant.mPlantCol, Plant.mRow, card.m_riseFromGround);
            }
        }
        catch (Exception e)
        {
            MelonLogger.Error($"[CoreLib] Zombie card failed to spawn its zombie: {e}");
        }
        finally
        {
            // Always clear the placeholder, even if the spawn threw - otherwise a plant with no
            // visuals would be left occupying the tile.
            Plant.Die();
        }

        return false;
    }

    // Nothing should be chewing on a plant that is about to vanish.
    public override bool CanBeTargetedBy(ZombieAttackType attackType)
    {
        return false;
    }

    public override void Reset()
    {
        base.Reset();

        consumed = false;
    }
}
