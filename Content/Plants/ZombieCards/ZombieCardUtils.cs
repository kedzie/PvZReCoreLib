using Il2CppReloaded.Gameplay;
using MelonLoader;

namespace PvZReCoreLib.Content.Plants.ZombieCards;

public static class ZombieCardUtils
{
    // The wave number a card zombie is created "from". Any wave is equivalent for a hypnotized
    // zombie - the native wave bookkeeping (Board.TotalZombiesHealthInWave, the "enemies left on
    // screen" test that lets the level award drop) skips mind-controlled zombies - but it must NOT
    // be a negative wave: -2/-3 are the cutscene/UI markers (Zombie.IsOnBoard() is false for them,
    // they get no shadow, no loot, no death handling). 0 is what the game itself uses when it
    // places a zombie from a seed packet (Challenge.IZombiePlaceZombie, I, Zombie and Versus).
    private const int FromWave = 0;

    // Spawns 'zombieType' in the given tile and hypnotizes it before it is ever updated or drawn.
    // Returns null if the game refused to create a zombie (zombie list full).
    //
    // Versus: this only calls Board.AddZombieInRow / StartMindControlled. The zombie side's
    // resources and placement rules live in Challenge.IZombieMouseDownWithZombie (TakeSunMoney,
    // SeedPacket.WasPlanted, IZombiePlaceZombie), which is never reached from here, so the
    // conjured zombie is not a zombie-side spawn. Wave bookkeeping is not involved at all
    // (Board.UpdateZombieSpawning returns early in Versus).
    public static Zombie SpawnHypnotizedZombie(Board board, ZombieType zombieType, int col, int row, bool riseFromGround = false)
    {
        // Board.AddZombieInRow allocates the Zombie, runs ZombieInitialize (which also creates its
        // Unity controller) and registers it in Board.m_zombies. shakeBrush=false is what
        // Challenge.IZombiePlaceZombie passes for a player-placed zombie.
        Zombie zombie = board.AddZombieInRow(zombieType, row, FromWave, false);
        if (zombie == null)
        {
            MelonLogger.Warning($"[CoreLib] Board.AddZombieInRow returned null for {zombieType} (zombie list full?).");
            return null;
        }

        // AddZombieInRow leaves a walking zombie off-screen at the right edge. Move it onto the
        // tile the same way Challenge.IZombiePlaceZombie does for a placed zombie: mPosX is
        // GridToPixelX(col, row) - 30 (constant read from GameAssembly.dll), Y follows the row.
        if (riseFromGround)
        {
            zombie.RiseFromGrave(col, row);
        }
        else
        {
            zombie.mPosX = board.GridToPixelX(col, row) - 30;
            zombie.mPosY = zombie.GetPosYBasedOnRow(row);
        }

        Hypnotize(zombie);
        return zombie;
    }

    // What the game does to a zombie that has just eaten a Hypno-shroom (Zombie.AnimateChewSound),
    // minus the plant: StartMindControlled() sets mMindControlled (the flag that makes
    // IsWalkingBackwards() true so it walks right, that target selection, win/lose and wave
    // accounting all key off, and that makes CheckForBoardEdge remove it silently at the right
    // edge instead of counting a loss) and plays the hypnosis sound; the native caller then sets
    // mVelX = 0.17 and mAnimTicksPerFrame = 18, spawns the pink-hearts particle (offset +60,+40
    // from the zombie) and calls UpdateAnimSpeed().
    public static void Hypnotize(Zombie zombie, bool preventLoot = true)
    {
        zombie.StartMindControlled();

        zombie.mVelX = 0.17f;
        zombie.mAnimTicksPerFrame = 18;

        // A conjured zombie must not be a coin farm: Zombie.DropLoot and TrySpawnLevelAward both
        // bail out when mDroppedLoot is already set. (The level award is dropped by the last
        // *enemy* zombie to die, not by this one.)
        if (preventLoot)
        {
            zombie.mDroppedLoot = true;
        }

        zombie.UpdateAnimSpeed();

        try
        {
            zombie.mApp.AddTodParticle(zombie.mPosX + 60f, zombie.mPosY + 40f, zombie.mRenderOrder + 1, ParticleEffect.MindControl);
        }
        catch (Exception e)
        {
            // Purely cosmetic; never let it undo the hypnosis.
            MelonLogger.Warning($"[CoreLib] Could not spawn the hypnosis particle: {e.Message}");
        }
    }

    public static CustomZombieCardDefinition GetCard(SeedType seedType)
    {
        if (!CustomContentRegistry.IsValidCustomPlantType(seedType))
        {
            return null;
        }

        return CustomContentRegistry.GetCustomPlantDefinition(seedType)?.TryCast<CustomZombieCardDefinition>();
    }

    // True only for a card that opted out of Versus (m_allowInVersus = false); cards default to allowed.
    public static bool IsBlockedInVersus(SeedType seedType)
    {
        var card = GetCard(seedType);
        return card != null && !card.m_allowInVersus;
    }
}
