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
                // A vehicle (Zomboni, Bobsled Team) only uses the lane of the tile: it starts at the left edge
                // of that lane, column 0, and drives right over everything in it.
                int col = card.m_launchFromEdge ? 0 : Plant.mPlantCol;
                var leader = ZombieCardUtils.SpawnHypnotizedZombie(Board, card.m_spawnType, col, Plant.mRow, card.m_riseFromGround);

                // A horde card (the Flag Zombie): the leader brings a crowd and the huge wave banner.
                if (leader != null && card.m_escortCount > 0)
                {
                    ZombieCardUtils.SpawnEscort(Board, card.m_escortType, card.m_escortCount, col, Plant.mRow);
                }

                if (leader != null && card.m_announceHorde)
                {
                    ZombieCardUtils.ShowHordeAdvice(Board);
                }
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
