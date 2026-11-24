using Il2CppReloaded.Gameplay;
using MelonLoader;
using UnityEngine;

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
        // The Bobsled Team is four zombies on a sled that the game itself builds around one leader.
        if (zombieType == ZombieType.Bobsled)
        {
            return SpawnHypnotizedBobsled(board, col, row);
        }

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
        bool inWater = false;
        if (riseFromGround)
        {
            zombie.RiseFromGrave(col, row);
        }
        else
        {
            zombie.mPosX = board.GridToPixelX(col, row) - 30;
            zombie.mPosY = zombie.GetPosYBasedOnRow(row);

            // A pool tile: the zombie has to be put into the water state the game would have walked
            // it into. Normal/Flag/Cone/Bucket need nothing - Zombie.CheckForPool sees them standing
            // in water on its first update and swims them in with the ducky tube. Snorkel and Dolphin
            // Rider are skipped by CheckForPool and enter the pool from their own update code.
            inWater = board.IsPoolSquare(col, row);
            if (inWater)
            {
                EnterWater(zombie);
            }
        }

        // The Zomboni keeps the speed its patch gives it (HypnotizedZamboniPatches); the slow hypnotized
        // walking speed would make it crawl.
        bool vehicle = zombie.mZombieType == ZombieType.Zamboni;
        Hypnotize(zombie, keepNativeSpeed: vehicle);
        if (vehicle)
        {
            zombie.mVelX = HypnotizedZamboni.Speed;
        }

        if (inWater && zombie.mZombieType == ZombieType.DolphinRider && zombie.mZombiePhase == ZombiePhase.DolphinRiding)
        {
            // Hypnotize() sets the slow hypnotized walking speed, which is right for the swimmers but
            // would make the riding dolphin crawl: native riding speed is the 0.89-0.91 the dolphin
            // gets at spawn (Zombie.PickRandomSpeed, phases DolphinWalking/WithoutDolphin).
            zombie.mVelX = DolphinRideSpeed;
        }

        return zombie;
    }

    private const float DolphinRideSpeed = 0.9f;

    // Puts a freshly created pool zombie into the state it is in once it has jumped into the pool,
    // by doing exactly what the game's own code does at that moment (values read from the decompile
    // and GameAssembly.dll constants) instead of waiting for the zombie to walk to the pool's right
    // edge, which a hypnotized zombie never does.
    //
    //  Snorkel Zombie: Zombie.UpdateZombieSnorkel, SnorkelWalking -> SnorkelIntoPool: phase,
    //    mVelX = 0.2 and "anim_jumpinpool" (PlayOnceAndHold, blend 20, rate 16). Native then plays
    //    the splash, dips the zombie under (mAltitude curve) and moves to SnorkelWalkingInPool, which
    //    is fully hypnosis-aware: it surfaces to eat the zombies in its way
    //    (mIsEating -> SnorkelUpToEat/EatingInPool/DownFromEat) and leaves the pool at x > 640 when
    //    walking backwards.
    //  Dolphin Rider: Zombie.UpdateZombieDolphinRider, DolphinIntoPool -> DolphinRiding: mInPool,
    //    attack rect x = -29, "anim_ride" (Loop, rate 12). The leftwards "mPosX -= 70" of that
    //    transition is left out - the zombie is already on its tile. Riding, jumping over the first
    //    obstacle and the pool exit are handled by HypnotizedPoolPatches.
    private static void EnterWater(Zombie zombie)
    {
        try
        {
            switch (zombie.mZombieType)
            {
                case ZombieType.Snorkel:
                    zombie.mZombiePhase = ZombiePhase.SnorkelIntoPool;
                    zombie.mVelX = 0.2f;
                    zombie.PlayZombieReanim("anim_jumpinpool", ReanimLoopType.PlayOnceAndHold, 20, 16f);
                    break;

                case ZombieType.DolphinRider:
                    zombie.mZombiePhase = ZombiePhase.DolphinRiding;
                    zombie.mInPool = true;
                    SetAttackRectX(zombie, -29f);
                    zombie.PlayZombieReanim("anim_ride", ReanimLoopType.Loop, 0, 12f);
                    zombie.PoolSplash(true);
                    break;
            }
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Could not put the {zombie.mZombieType} card zombie into the water: {e.Message}");
        }
    }

    internal static void SetAttackRectX(Zombie zombie, float x)
    {
        Rect rect = zombie.mZombieAttackRect;
        rect.x = x;
        zombie.mZombieAttackRect = rect;
    }

    internal static void SetZombieRectX(Zombie zombie, float x)
    {
        Rect rect = zombie.mZombieRect;
        rect.x = x;
        zombie.mZombieRect = rect;
    }

    // True when any tile of the lane is water (always false on a level without a pool).
    public static bool RowHasPool(Board board, int row)
    {
        try
        {
            if (!board.StageHasPool())
            {
                return false;
            }

            for (int col = 0; col < 9; col++)
            {
                if (board.IsPoolSquare(col, row))
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
            // an interop hiccup must not turn a land lane into a refused one
        }

        return false;
    }

    // Native riders sit 50 px apart behind the leader (Zombie.ZombieInitialize: follower x = leader x +
    // (index + 1) * 50). The whole sled is mirrored here, so the riders stand behind (left of) the leader.
    private const float SledRiderSpacing = 50f;
    private const float SledLength = 3 * SledRiderSpacing + 5f;

    // A hypnotized Bobsled Team. Board.AddZombieInRow(Bobsled) makes the whole team: the sled leader (type
    // Bobsled, the sled itself is its helm, 300 HP) and three riders (random plain zombies) that it
    // registers in mFollowerZombieID, all in the "sliding" phase with mVelX = 0.6. The team is moved onto
    // the tile, mirrored (riders behind the leader, on its left) and every member is hypnotized WITHOUT
    // the slow hypnotized walking speed: the sliding speed and animations are the native ones. What the
    // sled does after that is in HypnotizedBobsledPatches. The card passes column 0: the leader is put as far
    // left as keeps the last rider on the board (SledLength), so the team starts at the left edge.
    public static Zombie SpawnHypnotizedBobsled(Board board, int col, int row)
    {
        Zombie leader = board.AddZombieInRow(ZombieType.Bobsled, row, FromWave, false);
        if (leader == null)
        {
            MelonLogger.Warning("[CoreLib] Board.AddZombieInRow returned null for the Bobsled Team (zombie list full?).");
            return null;
        }

        var team = new List<Zombie> { leader };
        try
        {
            foreach (ZombieID id in leader.mFollowerZombieID)
            {
                Zombie rider = board.ZombieGet(id);
                if (rider != null)
                {
                    team.Add(rider);
                }
            }
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Could not read the Bobsled Team riders: {e.Message}");
        }

        // The native leader stands off-screen at the right edge with the riders to its right; keep each
        // member's distance from the leader but mirror it, and keep the last rider on the board.
        float leaderNativeX = leader.mPosX;
        float targetX = Math.Max(board.GridToPixelX(col, row) - 30, SledLength);
        float y = leader.GetPosYBasedOnRow(row);

        foreach (var member in team)
        {
            member.mPosX = targetX - (member.mPosX - leaderNativeX);
            member.mPosY = y;
        }

        Hypnotize(leader, keepNativeSpeed: true);
        for (int i = 1; i < team.Count; i++)
        {
            HypnotizeRider(team[i]);
        }

        return leader;
    }

    // Zombie.StartMindControlled on a Bobsled rider would cut the rider loose from its team: for any
    // zombie that has a related zombie it clears the related-zombie link on both sides (ISIL, the
    // Dancer / Backup Dancer chain code), and Zombie.GetBobsledPosition is read from exactly that link,
    // so a "freed" rider would stop being part of the sled (it would start eating and walk by its own
    // animation's ground speed on top of the sliding). Setting the flag directly keeps the link; what
    // StartMindControlled adds on top (the hypnosis sound, the Portal x) is not needed.
    private static void HypnotizeRider(Zombie rider)
    {
        rider.mMindControlled = true;
        rider.mDroppedLoot = true;

        try
        {
            rider.mApp.AddTodParticle(rider.mPosX + 60f, rider.mPosY + 40f, rider.mRenderOrder + 1, ParticleEffect.MindControl);
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Could not spawn the hypnosis particle on a Bobsled rider: {e.Message}");
        }
    }

    // Planting rules for a zombie card that may stand on water (Water / LandOrWater placement).
    // 'native' is what Board.CanPlantAt (plus whatever patches ran before) returned for the tile;
    // Land cards are handled by the ground-only rejection and left alone here.
    //
    // What native says for a custom seed on a pool tile is not certain (CoreLib's own notes say a
    // custom seed is plantable on water/lily pads regardless, the decompile of CanPlantAt shows a
    // NotOnWater return for a non-aquatic seed), so both are handled: an Ok / NotOnWater result on
    // a water tile is accepted when the tile holds no plant, and never on a land tile for a
    // Water-only card.
    public static PlantingReason AdjustPlanting(Board board, CustomZombieCardDefinition card, int gridX, int gridY, PlantingReason native)
    {
        if (card.m_placement == ZombieCardPlacement.Land)
        {
            return native;
        }

        bool plain = native == PlantingReason.Ok || native == PlantingReason.NotOnWater;
        if (!plain)
        {
            return native;
        }

        bool pool = board.IsPoolSquare(gridX, gridY);

        if (card.m_placement == ZombieCardPlacement.Water)
        {
            if (!pool)
            {
                // The game's own "this plant can only go in the pool" message.
                return PlantingReason.OnlyInPool;
            }

            if (gridX < card.m_minCol || gridX > card.m_maxCol)
            {
                return PlantingReason.NotHere;
            }
        }

        if (pool && native == PlantingReason.NotOnWater)
        {
            // Empty water: native refused only because the seed is not aquatic. A lily pad (or any
            // other plant) on the tile would have made native answer differently, but check anyway.
            return board.GetTopPlantAt(gridX, gridY, PlantPriority.Any) == null ? PlantingReason.Ok : PlantingReason.NotHere;
        }

        return native;
    }

    // True for a card that can only be planted on water while the level has no pool at all: such a
    // card is left out of the seed chooser (SeedChooserPaging.ApplyPaging). Anything unexpected
    // (no board yet, an interop failure) keeps the card visible.
    public static bool IsHiddenOnStage(SeedType seedType, Board board)
    {
        var card = GetCard(seedType);
        if (card == null || card.m_placement != ZombieCardPlacement.Water || board == null)
        {
            return false;
        }

        try
        {
            return !board.StageHasPool();
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Lane offsets the escorts are spread over: the leader's own lane first, then the ones next to it.
    private static readonly int[] EscortRowOffsets = { 0, -1, 1, -2, 2 };

    // Left-to-right wobble inside one rank of the escort, so a rank is not a ruler-straight line.
    private static readonly float[] EscortJitter = { 0f, 14f, -10f, 8f, -16f };

    // Each escort zombie is rolled on its own: this share (percent) are Buckethead, this share Conehead, the
    // rest the card's own escort type (Normal). Cone and bucket can stand on water like a Normal zombie.
    private const int EscortBucketheadPercent = 10;
    private const int EscortConeheadPercent = 20;

    private static ZombieType PickEscortType(ZombieType plain)
    {
        int roll = UnityEngine.Random.Range(0, 100);
        return roll < EscortBucketheadPercent ? ZombieType.Pail
            : roll < EscortBucketheadPercent + EscortConeheadPercent ? ZombieType.TrafficCone
            : plain;
    }

    private const float EscortRankSpacing = 52f;
    private const float EscortFirstRank = 46f;

    // Conjures 'count' hypnotized zombies behind a card zombie that was placed in (col, row): ranks of
    // five, each rank further left, spread over the leader's lane and the lanes around it. Where the
    // left edge of the lawn leaves no room (a card planted in the first columns) the ranks stack up
    // against it instead of leaving the board. Returns how many were created (the zombie list can be
    // full).
    public static int SpawnEscort(Board board, ZombieType zombieType, int count, int col, int row)
    {
        int rows = Math.Max(board.GetNumRows(), 1);
        float leaderX = board.GridToPixelX(col, row) - 30;
        int spawned = 0;

        for (int i = 0; i < count; i++)
        {
            int rank = i / EscortRowOffsets.Length;
            int slot = i % EscortRowOffsets.Length;

            // Lanes outside the board mirror to the other side of the leader.
            int escortRow = row + EscortRowOffsets[slot];
            if (escortRow < 0 || escortRow >= rows)
            {
                escortRow = row - EscortRowOffsets[slot];
            }
            escortRow = Math.Clamp(escortRow, 0, rows - 1);

            float x = leaderX - EscortFirstRank - rank * EscortRankSpacing + EscortJitter[slot];
            if (x < 0f)
            {
                x = slot * 9f;
            }

            Zombie escort = board.AddZombieInRow(PickEscortType(zombieType), escortRow, FromWave, false);
            if (escort == null)
            {
                break;
            }

            escort.mPosX = x;
            escort.mPosY = escort.GetPosYBasedOnRow(escortRow);
            Hypnotize(escort);
            spawned++;
        }

        return spawned;
    }

    // The big banner of a native huge wave ("a huge wave of zombies is approaching!"). The native
    // Board.ShowHugeWaveAdvice does exactly Board.mAdvice.SetLabel("[ADVICE_HUGE_WAVE]", HugeWave)
    // plus the help index; the text here is our own line, with the native one as the fallback.
    public const string HordeAdvice = "A LOT OF ZOMBIES ARE APPROACHING!!";

    public static void ShowHordeAdvice(Board board)
    {
        try
        {
            board.mAdvice.SetLabel(HordeAdvice, MessageStyle.HugeWave);
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Could not show the horde banner text ({e.Message}); using the native huge wave one.");
            try { board.ShowHugeWaveAdvice(); } catch (Exception) { }
        }
    }

    // What the game does to a zombie that has just eaten a Hypno-shroom (Zombie.AnimateChewSound),
    // minus the plant: StartMindControlled() sets mMindControlled (the flag that makes
    // IsWalkingBackwards() true so it walks right, that target selection, win/lose and wave
    // accounting all key off, and that makes CheckForBoardEdge remove it silently at the right
    // edge instead of counting a loss) and plays the hypnosis sound; the native caller then sets
    // mVelX = 0.17 and mAnimTicksPerFrame = 18, spawns the pink-hearts particle (offset +60,+40
    // from the zombie) and calls UpdateAnimSpeed().
    public static void Hypnotize(Zombie zombie, bool preventLoot = true, bool keepNativeSpeed = false)
    {
        zombie.StartMindControlled();

        if (!keepNativeSpeed)
        {
            zombie.mVelX = 0.17f;
            zombie.mAnimTicksPerFrame = 18;
        }

        // A conjured zombie must not be a coin farm: Zombie.DropLoot and TrySpawnLevelAward both
        // bail out when mDroppedLoot is already set. (The level award is dropped by the last
        // *enemy* zombie to die, not by this one.)
        if (preventLoot)
        {
            zombie.mDroppedLoot = true;
        }

        if (!keepNativeSpeed)
        {
            zombie.UpdateAnimSpeed();
        }

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
