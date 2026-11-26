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

        // The zombie whose almanac illustration / ground background the card borrows, when that is not
        // the catalog key itself. Redeye Gargantuar is the case: the game has no seed packet and no
        // almanac entry for him (his ZombieDefinition has no preview sprite either), so he shares the
        // ordinary Gargantuar's packet (PseudoSeed) and almanac art.
        public ZombieType? ArtFrom;

        public bool AlmanacArt;

        // This zombie's card in VersusPlus' card sheet (a name like SPR_SeedPackets_Zombies_Versus_31: the number
        // is the card, column n % 7, row n / 7), used for the card's picture when the player has VersusPlus
        // installed (see CustomZombieCardDefinition.ConfigureVersusPlusArt).
        public string VersusSlot;
        public int DefaultSunCost;

        // Where the card may be planted by default (see ZombieCardPlacement). Registrations can
        // override it.
        public ZombieCardPlacement Placement;

        // Water-only cards: the columns (0 = leftmost) the card may be planted in. The native
        // hypnotized pool-exit rules (see HypnotizedPoolPatches) trip right away for a zombie that
        // starts in the last water columns, which would only look like a glitch.
        public int MinCol;
        public int MaxCol;

        // A vehicle that drives the whole lane (Zomboni, Bobsled): the card cannot be planted in a
        // lane that has any pool tile in it, because the vehicle would drive out over the water.
        public bool AvoidPoolRows;

        // The vehicle does not appear on the tile the card is planted on: only the tile's ROW counts, and
        // it starts at the left edge of that lane (column 0) and drives right (see
        // ZombieCardBehaviorController).
        public bool LaunchFromEdge;
    }

    // Mirrors Challenge.IZombieSeedTypeToZombieType (read from the decompile) in reverse. Only
    // zombies that have a native pseudo seed (= native card art) can be listed; more can be added
    // here with one line each (Bungee, Digger, Balloon and Catapult have art too but their whole
    // behaviour is a special spawn/attack and make no sense as a conjured walker; the Zomboni and the
    // Bobsled Team are vehicles with patches of their own, see HypnotizedZamboniPatches). The plain
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

        // Red-eyed Gargantuar ("Giga"): the same zombie as the Gargantuar prefab-wise (both definitions
        // share one prefab). Zombie.ZombieInitialize swaps in the red-eye head image and sets 6000 HP for
        // type 32 on its own, and HypnotizedGargantuarPatches already treats type 32 like 23. No native
        // packet or almanac entry exists, so the Gargantuar's are borrowed.
        { ZombieType.RedeyeGargantuar, new Entry { PseudoSeed = SeedType.ZombieGargantuar, DefaultName = "Giga Gargantuar",
                                                   ArtFrom = ZombieType.Gargantuar,
                                                   VersusSlot = "SPR_SeedPackets_Zombies_Versus_20" } },

        // Zomboni: the game's own packet (SeedType.Zomboni) supplies the art and the cost. Its native
        // behaviour is rewritten for a hypnotized one in HypnotizedZamboniPatches (drives right, crushes
        // the ENEMY zombies it overlaps instead of our plants, lays a harmless ice trail behind it).
        { ZombieType.Zamboni,      new Entry { PseudoSeed = SeedType.Zomboni, DefaultName = "Zomboni", AvoidPoolRows = true, LaunchFromEdge = true } },

        // Bobsled Team: the leader (the sled) comes with three riders the game itself adds, see
        // ZombieCardUtils.SpawnHypnotizedBobsled and HypnotizedBobsledPatches. The game has no packet
        // for it, so the icon is the almanac illustration, like the pool zombies.
        { ZombieType.Bobsled,      new Entry { PseudoSeed = SeedType.None, DefaultName = "Bobsled Team",
                                               AlmanacArt = true, DefaultSunCost = 250, Placement = ZombieCardPlacement.Land, AvoidPoolRows = true, LaunchFromEdge = true } },

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

        // Yeti: no native packet either (almanac art, like the pool zombies). The native code needs no
        // special handling for a hypnotized one: Zombie.UpdateYeti returns at once for a mind-controlled
        // zombie (it never starts its YetiRunning retreat), and Zombie.IsWalkingBackwards - which is also
        // how the Yeti retreats natively - is simply true for a mind-controlled zombie, so he walks right.
        { ZombieType.Yeti,         new Entry { PseudoSeed = SeedType.None, DefaultName = "Yeti Zombie",
                                               AlmanacArt = true, DefaultSunCost = 150, Placement = ZombieCardPlacement.Land } },

        // Zombotany: the plant-headed zombies (ZombieType 26..31, see HypnotizedZombotanyPatches). The game lets every one
        // of them but the Squash into the pool (Board.ZombieTypeCanGoInPool, with a Ducky Tube), so those may be
        // planted on water too. No native
        // packet either, so almanac art; if the game has no almanac entry for one, the card keeps the Basic
        // Zombie packet art (see CustomZombieCardDefinition.ConfigureAlmanacSeedPacketArt).
        { ZombieType.PeaHead,      new Entry { PseudoSeed = SeedType.None, DefaultName = "Peashooter Zombie",
                                               VersusSlot = "SPR_SeedPackets_Zombies_Versus_31", AlmanacArt = true, DefaultSunCost = 125, Placement = ZombieCardPlacement.LandOrWater } },
        { ZombieType.WallnutHead,  new Entry { PseudoSeed = SeedType.None, DefaultName = "Wall-nut Zombie",
                                               VersusSlot = "SPR_SeedPackets_Zombies_Versus_30", AlmanacArt = true, DefaultSunCost = 100, Placement = ZombieCardPlacement.LandOrWater } },
        { ZombieType.TallnutHead,  new Entry { PseudoSeed = SeedType.None, DefaultName = "Tall-nut Zombie",
                                               VersusSlot = "SPR_SeedPackets_Zombies_Versus_30", AlmanacArt = true, DefaultSunCost = 150, Placement = ZombieCardPlacement.LandOrWater } },
        { ZombieType.JalapenoHead, new Entry { PseudoSeed = SeedType.None, DefaultName = "Jalapeno Zombie",
                                               VersusSlot = "SPR_SeedPackets_Zombies_Versus_34", AlmanacArt = true, DefaultSunCost = 150, Placement = ZombieCardPlacement.LandOrWater } },
        { ZombieType.SquashHead,   new Entry { PseudoSeed = SeedType.None, DefaultName = "Squash Zombie",
                                               VersusSlot = "SPR_SeedPackets_Zombies_Versus_33", AlmanacArt = true, DefaultSunCost = 125, Placement = ZombieCardPlacement.Land } },
        { ZombieType.GatlingHead,  new Entry { PseudoSeed = SeedType.None, DefaultName = "Gatling Pea Zombie",
                                               VersusSlot = "SPR_SeedPackets_Zombies_Versus_32", AlmanacArt = true, DefaultSunCost = 250, Placement = ZombieCardPlacement.LandOrWater } },
    };

    public static bool TryGet(ZombieType zombieType, out Entry entry)
    {
        return entries.TryGetValue(zombieType, out entry);
    }
}
