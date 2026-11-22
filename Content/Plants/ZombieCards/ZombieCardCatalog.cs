using Il2CppReloaded.Gameplay;

namespace PvZReCoreLib.Content.Plants.ZombieCards;

// Which of the game's own artwork/data a zombie card borrows. The game already ships a
// PlantDefinition for every zombie "pseudo seed" (SeedType.ZombieNormal = 60 ... ZombieCatapult =
// 79, used by I, Zombie and Versus): each carries that zombie's seed packet sprite
// (m_plantImage), Versus packet sprite (m_versusImage), cursor preview sprite + offset, and the
// sun cost. Reading those through the normal data service (DataService.GetPlantDefinition falls
// through to native for 54-80, see GetPlantDefinitionPatch) means a zombie card needs no art
// of its own.
public static class ZombieCardCatalog
{
    public struct Entry
    {
        // SeedType.None when the game has no native zombie packet for this zombie (Snorkel, Dolphin
        // Rider, Ducky Tube). Such an entry must set AlmanacArt and DefaultSunCost: the card then
        // takes its icon from the zombie's ALMANAC illustration and its cursor preview from the
        // zombie's own ZombieDefinition instead.
        public SeedType PseudoSeed;
        public string DefaultName;

        // The zombie the card actually spawns, when that is not the catalog key itself. Ducky Tube
        // is the only case: ZombieType.DuckyTube is an almanac-only type (weight 0, same prefab as
        // the Normal zombie, and not in Board.ZombieTypeCanGoInPool / Zombie.CheckForPool's list, so
        // a real one placed in the pool would walk on top of the water without ever swimming). A
        // "Ducky Tube Zombie" is just a Normal zombie standing in a pool lane, which the game turns
        // into a swimmer on its own.
        public ZombieType? SpawnType;

        public bool AlmanacArt;
        public int DefaultSunCost;

        // Where the card may be planted by default (see ZombieCardPlacement). Registrations can
        // override it.
        public ZombieCardPlacement Placement;

        // Water-only cards: the columns (0 = leftmost) the card may be planted in. The native
        // hypnotized pool-exit rules (see HypnotizedPoolPatches) trip right away for a zombie that
        // starts in the last water columns, which would only look like a glitch.
        public int MinCol;
        public int MaxCol;
    }

    // Mirrors Challenge.IZombieSeedTypeToZombieType (read from the decompile) in reverse. Only
    // zombies that have a native pseudo seed (= native card art) can be listed; more can be added
    // here with one line each (Bungee, Digger, Balloon, Zamboni and Catapult have art too but their
    // whole behaviour is a special spawn/attack and make no sense as a conjured walker). The plain
    // walkers - Normal, TrafficCone, Pail, Door, Newspaper, Football, Imp - are what this was
    // written around; the rest (Polevaulter, JackInTheBox, Ladder, Pogo, Dancer, Gargantuar) are
    // listed but untested (as is Flag).
    private static readonly Dictionary<ZombieType, Entry> entries = new()
    {
        { ZombieType.Normal,       new Entry { PseudoSeed = SeedType.ZombieNormal,       DefaultName = "Basic Zombie" } },
        { ZombieType.TrafficCone,  new Entry { PseudoSeed = SeedType.ZombieTrafficCone,  DefaultName = "Conehead Zombie" } },
        { ZombieType.Pail,         new Entry { PseudoSeed = SeedType.ZombiePail,         DefaultName = "Buckethead Zombie" } },
        { ZombieType.Door,         new Entry { PseudoSeed = SeedType.ZombieScreenDoor,   DefaultName = "Screen Door Zombie" } },
        { ZombieType.Newspaper,    new Entry { PseudoSeed = SeedType.ZombieNewspaper,    DefaultName = "Newspaper Zombie" } },
        { ZombieType.Football,     new Entry { PseudoSeed = SeedType.ZombieFootball,     DefaultName = "Football Zombie" } },
        { ZombieType.Flag,         new Entry { PseudoSeed = SeedType.ZombieFlag,         DefaultName = "Flag Zombie" } },
        { ZombieType.Imp,          new Entry { PseudoSeed = SeedType.ZombieImp,          DefaultName = "Imp" } },
        { ZombieType.Polevaulter,  new Entry { PseudoSeed = SeedType.ZombiePolevaulter,  DefaultName = "Pole Vaulting Zombie" } },
        { ZombieType.JackInTheBox, new Entry { PseudoSeed = SeedType.ZombieJackInTheBox, DefaultName = "Jack-in-the-Box Zombie" } },
        { ZombieType.Ladder,       new Entry { PseudoSeed = SeedType.ZombieLadder,       DefaultName = "Ladder Zombie" } },
        { ZombieType.Pogo,         new Entry { PseudoSeed = SeedType.ZombiePogo,         DefaultName = "Pogo Zombie" } },
        { ZombieType.Dancer,       new Entry { PseudoSeed = SeedType.ZombieDancer,       DefaultName = "Dancing Zombie" } },
        { ZombieType.Gargantuar,   new Entry { PseudoSeed = SeedType.ZombieGargantuar,   DefaultName = "Gargantuar" } },

        // Pool zombies. The game has no I, Zombie / Versus packet for any of them, so the icon is the
        // almanac illustration (a 262x262 square stone tile; the seed packet Image keeps the aspect
        // ratio, so it shows letterboxed in the card) and the sun cost is picked here, in line with
        // the cost of a comparable native zombie (Jack-in-the-Box 100, Polevaulter 75, Normal 50).
        //   Ducky Tube: a Normal zombie, planted on water only.
        //   Snorkel / Dolphin Rider: water only, and only in columns the native "hypnotized zombie
        //   leaves the pool at the right edge" rules (Zombie.UpdateZombieSnorkel: x > 640,
        //   UpdateZombieDolphinRider: x > 680) do not fire at once.
        { ZombieType.DuckyTube,    new Entry { PseudoSeed = SeedType.None, DefaultName = "Ducky Tube Zombie", SpawnType = ZombieType.Normal,
                                               AlmanacArt = true, DefaultSunCost = 50,  Placement = ZombieCardPlacement.Water, MinCol = 0, MaxCol = 8 } },
        { ZombieType.Snorkel,      new Entry { PseudoSeed = SeedType.None, DefaultName = "Snorkel Zombie",
                                               AlmanacArt = true, DefaultSunCost = 100, Placement = ZombieCardPlacement.Water, MinCol = 0, MaxCol = 7 } },
        { ZombieType.DolphinRider, new Entry { PseudoSeed = SeedType.None, DefaultName = "Dolphin Rider Zombie",
                                               AlmanacArt = true, DefaultSunCost = 125, Placement = ZombieCardPlacement.Water, MinCol = 1, MaxCol = 8 } },
    };

    public static bool TryGet(ZombieType zombieType, out Entry entry)
    {
        return entries.TryGetValue(zombieType, out entry);
    }
}
