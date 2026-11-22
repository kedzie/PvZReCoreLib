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
        public SeedType PseudoSeed;
        public string DefaultName;
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
    };

    public static bool TryGet(ZombieType zombieType, out Entry entry)
    {
        return entries.TryGetValue(zombieType, out entry);
    }
}
