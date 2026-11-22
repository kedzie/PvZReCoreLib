using Il2CppInterop.Runtime;
using Il2CppReloaded;
using Il2CppInterop.Runtime.Injection;
using Il2CppReloaded.Data;
using Il2CppReloaded.Gameplay;
using Il2CppReloaded.Services;
using Il2CppSource.Utils;
using MelonLoader;
using PvZReCoreLib.Util;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Type = Il2CppSystem.Type;

namespace PvZReCoreLib.Content.Plants.ZombieCards;

// A seed packet that is not a plant: planting it on a tile spawns the given zombie there and
// immediately hypnotizes it (what a Hypno-shroom does to a zombie that bites it), so it walks right
// and fights the other zombies. No plant is left behind - the short-lived placeholder plant the
// game creates is removed on its first update tick by ZombieCardBehaviorController.
//
// Where a zombie card may be planted.
public enum ZombieCardPlacement
{
    // Land tiles only (the default): the placeholder plant is rejected on any pool tile
    // (Board_CanPlantAt_RequiresGround_Patch).
    Land = 0,

    // Pool (water) tiles only: for the zombies that only exist in the pool lanes (Snorkel, Dolphin
    // Rider, Ducky Tube). On a level without a pool the card is not offered in the seed chooser and
    // cannot be placed ("can only be planted in the pool").
    Water = 1,

    // Land or water. Only meaningful for the zombies the game itself lets walk into the pool
    // (Board.ZombieTypeCanGoInPool: Normal, Flag, Conehead, Buckethead): on a water tile the zombie
    // becomes a Ducky Tube zombie by itself (Zombie.CheckForPool).
    LandOrWater = 2,
}

// Data driven: one concrete class for every zombie, configured by Configure(...). Register through
// ZombieCardRegistry.Register(ZombieType.TrafficCone) rather than newing this up by hand - that
// also takes care of the registration order the free-SeedType lookup relies on.
[RegisterTypeInIl2Cpp]
public class CustomZombieCardDefinition : CustomPlantDefinition
{
    #region Variables

    // The card's identity (the catalog key: which almanac art / default name it uses).
    public ZombieType m_zombieType = ZombieType.Normal;

    // The zombie type actually created. Same as m_zombieType except for the Ducky Tube card, which
    // spawns a Normal zombie (see ZombieCardCatalog.Entry.SpawnType).
    public ZombieType m_spawnType = ZombieType.Normal;

    // Where the card can be planted, and for a Water card which columns (inclusive).
    public ZombieCardPlacement m_placement = ZombieCardPlacement.Land;
    public int m_minCol = 0;
    public int m_maxCol = 8;

    // Versus: the PLANT-side player can pick and plant the card; the zombie it conjures is hypnotized
    // and fights the zombie side's zombies. (The zombie side has its own chooser list,
    // SeedChooserScreen.mChosenZombies, which only ever holds the native zombie seeds - custom cards
    // live in mChosenSeeds and are never offered to it.) Set false per card to hide it from the
    // Versus chooser (SeedChooserPaging.ApplyPaging) and swallow its Versus click
    // (Board_MouseDownWithPlant_VersusCustomSeed_Patch).
    public bool m_allowInVersus = true;

    // Opt-in: spawn with the game's own "rise from the ground" animation (Zombie.RiseFromGrave, the
    // graveyard/Versus spawn path) instead of appearing already standing. Unverified in game.
    public bool m_riseFromGround = false;

    // A horde card: besides its own zombie, the card conjures m_escortCount hypnotized zombies of
    // m_escortType behind it (spread over the lanes next to it) and, with m_announceHorde, flashes the
    // "huge wave" banner. Used by the Flag Zombie card ("a flag zombie leads a wave").
    public int m_escortCount = 0;
    public ZombieType m_escortType = ZombieType.Normal;
    public bool m_announceHorde = false;

    #endregion

    public CustomZombieCardDefinition(IntPtr pointer) : base(pointer)
    {
        // Same value the other instant-style custom plants use (nothing here is a "shooter"; the
        // behaviour hook that matters, Plant.Update's prefix, runs regardless of subclass).
        m_subClass = PlantSubClass.Normal;
        m_entryStatus = ReloadedEntryStatus.Ready;

        // By default the card must never end up on a pool tile: the placeholder plant is only there
        // for a tick, but the zombie it spawns is a land walker. Reuses the existing ground-only
        // rejection in Board_CanPlantAt_RequiresGround_Patch. Configure() turns this off again for
        // a card whose placement allows water (ZombieCardUtils.AdjustPlanting then does the checks).
        m_requiresGround = true;

        // Never matters (the placeholder dies on its first tick) but keep it sane if something
        // does hit it in that window.
        m_health = 1;
    }

    public override Type GetCustomBehaviorType()
    {
        return Il2CppType.Of<ZombieCardBehaviorController>();
    }

    // The data service hands the almanac lists out as IReadOnlyList<T>, whose interop wrapper only
    // exposes the indexer (no Count, not enumerable). The object behind it is a plain List<T>, so
    // cast to that for Count; if that ever stops being true, probe the indexer until it throws.
    private static System.Collections.Generic.List<AlmanacEntryData> ReadAll(Il2CppSystem.Collections.Generic.IReadOnlyList<AlmanacEntryData> source)
    {
        var result = new System.Collections.Generic.List<AlmanacEntryData>();
        if (source == null)
        {
            return result;
        }

        var asList = source.TryCast<Il2CppSystem.Collections.Generic.List<AlmanacEntryData>>();
        if (asList != null)
        {
            for (int i = 0; i < asList.Count; i++)
            {
                if (asList[i] != null)
                {
                    result.Add(asList[i]);
                }
            }
            return result;
        }

        for (int i = 0; i < 512; i++)
        {
            AlmanacEntryData entry;
            try
            {
                entry = source[i];
            }
            catch (Exception)
            {
                break;
            }

            if (entry != null)
            {
                result.Add(entry);
            }
        }
        return result;
    }

    // Must run before CustomContentRegistry.RegisterCustomPlant: registration snapshots the sun
    // cost / recharge / images into the Versus fields and the almanac entry.
    public void Configure(ZombieType zombieType, string name = null, string description = null, string tooltip = null,
        int? sunCost = null, int? rechargeTime = null, bool allowInVersus = true, bool riseFromGround = false,
        ZombieCardPlacement? placement = null, int escortCount = 0, ZombieType escortType = ZombieType.Normal,
        bool announceHorde = false)
    {
        if (!ZombieCardCatalog.TryGet(zombieType, out var entry))
        {
            throw new ArgumentException($"[CoreLib] No zombie card art is known for {zombieType} (see ZombieCardCatalog).");
        }

        m_zombieType = zombieType;
        m_spawnType = entry.SpawnType ?? zombieType;
        m_allowInVersus = allowInVersus;
        m_riseFromGround = riseFromGround;
        m_escortCount = escortCount;
        m_escortType = escortType;
        m_announceHorde = announceHorde;

        m_placement = placement ?? entry.Placement;
        m_minCol = entry.MinCol;
        m_maxCol = entry.MaxCol > 0 ? entry.MaxCol : 8;

        // A card that may stand on water must not be turned away by the ground-only rejection; the
        // water rules are applied by ZombieCardUtils.AdjustPlanting instead.
        m_requiresGround = m_placement == ZombieCardPlacement.Land;

        string displayName = name ?? entry.DefaultName;
        string where = m_placement switch
        {
            ZombieCardPlacement.Water => "an empty pool square",
            ZombieCardPlacement.LandOrWater => "an empty lawn or pool square",
            _ => "an empty lawn square",
        };
        SetName(displayName, false);
        SetTooltip(tooltip ?? $"Plants a hypnotized {displayName} that fights for you.", false);
        SetDescription(description ?? $"Not a plant at all. Plant this card on {where} and a {displayName} appears there already hypnotized. It walks to the right and attacks the zombies it meets, exactly like a zombie that has eaten a Hypno-shroom.", false);

        // Native zombie seed (I, Zombie / Versus) definition: source of the sun cost and the art.
        // The pool zombies have none (entry.PseudoSeed == None): their art comes from the almanac
        // (below) and Normal's definition stands in for the cursor-preview scale.
        PlantDefinition source = entry.PseudoSeed != SeedType.None
            ? ReloadedUtils.GetPlantDefinition(entry.PseudoSeed)
            : ReloadedUtils.GetPlantDefinition(SeedType.ZombieNormal);

        m_seedCost = sunCost ?? (entry.PseudoSeed != SeedType.None ? source.m_seedCost : entry.DefaultSunCost);
        m_refreshTime = rechargeTime ?? DefaultRechargeTime;

        // Versus reads its own fields (confirmed in the decompile: Plant.GetCost reads +0x4C
        // m_versusCost, Plant.GetRefreshTime reads +0x54 m_versusBaseRefreshTime, or +0x58
        // m_versusSuddenDeathRefreshTime once VersusMode.Phase == SuddenDeath; the chooser and seed
        // bank read m_versusImage). Same cost and recharge as Adventure; the sudden-death recharge is
        // cut to a quarter, the same ratio Iceweed uses (750 -> 188).
        m_versusCost = m_seedCost;
        m_versusBaseRefreshTime = m_refreshTime;
        m_versusSuddenDeathRefreshTime = m_refreshTime / 4;

        // Seed packet art: m_plantImage is what Adventure shows, m_versusImage what the Versus
        // chooser and seed bank show - the zombie's own Versus packet sprite.
        m_plantImage = AssetRefUtil.CloneSprite(source.m_plantImage);
        m_versusImage = AssetRefUtil.CloneSprite(source.m_versusImage);
        m_previewSprite = AssetRefUtil.CloneSprite(source.m_previewSprite);
        m_previewSpriteOffset = source.m_previewSpriteOffset;
        m_previewSpriteScale = source.m_previewSpriteScale;

        if (entry.AlmanacArt)
        {
            ConfigureAlmanacSeedPacketArt(zombieType);
        }

        ConfigureAlmanacArt(zombieType);
    }

    // The card icon of a zombie without a native seed packet: the zombie's almanac illustration
    // (AlmanacEntryData.m_entryThumbnail, e.g. SPR_Almanac_Zombies_SnorkelZombie, in the almanac
    // icon atlas) for both the Adventure and the Versus packet, and the cursor preview sprite of the
    // zombie's own ZombieDefinition (SPR_Preview_Zombie_snorkle ...). If either lookup fails the
    // Normal zombie's packet art Configure() already put in place is kept, so the card still works.
    private void ConfigureAlmanacSeedPacketArt(ZombieType zombieType)
    {
        try
        {
            var dataService = AppCore.GetService<IDataService>().Cast<DataService>();
            foreach (var candidate in ReadAll(dataService.ZombieAlmanacData))
            {
                if (candidate.m_zombieType == zombieType && candidate.m_entryThumbnail != null)
                {
                    m_plantImage = AssetRefUtil.CloneSprite(candidate.m_entryThumbnail);
                    m_versusImage = AssetRefUtil.CloneSprite(candidate.m_entryThumbnail);
                    break;
                }
            }

            var zombieDefinition = ReloadedUtils.GetZombieDefinition(zombieType);
            if (zombieDefinition != null && zombieDefinition.PreviewSprite != null)
            {
                m_previewSprite = AssetRefUtil.CloneSprite(zombieDefinition.PreviewSprite);
                m_previewSpriteOffset = zombieDefinition.PreviewSpriteOffset;
            }
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Zombie card almanac seed packet art lookup failed for {zombieType}, keeping the Normal zombie packet: {e.Message}");
        }
    }

    // 30 seconds (game time units are hundredths of a second: Peashooter 750 = 7.5 s, Hypno-shroom
    // 3000 = 30 s). Shows as "Slow" in the almanac.
    public const int DefaultRechargeTime = 3000;

    // The Plants almanac entry is built from this definition (CustomAlmanacEntryData.LoadFrom). Borrow
    // the game's own Zombie almanac illustration and ground background for this zombie when they can
    // be found; fall back to the seed packet image and any native background otherwise, so the entry
    // never ends up with a null sprite reference (LoadFrom dereferences m_almanacBackground).
    private void ConfigureAlmanacArt(ZombieType zombieType)
    {
        try
        {
            var dataService = AppCore.GetService<IDataService>().Cast<DataService>();

            AlmanacEntryData zombieEntry = null;
            foreach (var candidate in ReadAll(dataService.ZombieAlmanacData))
            {
                if (candidate.m_zombieType == zombieType)
                {
                    zombieEntry = candidate;
                    break;
                }
            }

            if (zombieEntry != null)
            {
                m_almanacThumbnail = AssetRefUtil.CloneSprite(zombieEntry.m_entryThumbnail);
                m_almanacBackground = AssetRefUtil.CloneSprite(zombieEntry.m_entryThumbnailBackground);
            }

            if (m_almanacBackground == null)
            {
                foreach (var plantEntry in ReadAll(dataService.PlantAlmanacData))
                {
                    if (plantEntry.m_entryThumbnailBackground != null)
                    {
                        m_almanacBackground = AssetRefUtil.CloneSprite(plantEntry.m_entryThumbnailBackground);
                        break;
                    }
                }
            }
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Zombie card almanac art lookup failed for {zombieType}, using the seed packet image instead: {e.Message}");
        }

        if (m_almanacBackground == null)
        {
            m_almanacBackground = PersistentStorage.EmptySpriteRef;
        }
    }
}

public static class ZombieCardRegistry
{
    // One-line registration of a zombie card:
    //     ZombieCardRegistry.Register(ZombieType.TrafficCone);
    // Create-then-register is deliberately one call: CustomPlantDefinition's constructor picks the
    // lowest free SeedType from what is already registered, so two cards must not be created before
    // the first one is registered.
    public static CustomZombieCardDefinition Register(ZombieType zombieType, string name = null, string description = null,
        string tooltip = null, int? sunCost = null, int? rechargeTime = null, bool allowInVersus = true, bool riseFromGround = false,
        ZombieCardPlacement? placement = null, int escortCount = 0, ZombieType escortType = ZombieType.Normal,
        bool announceHorde = false)
    {
        var definition = ScriptableObject.CreateInstance<CustomZombieCardDefinition>();
        definition.Configure(zombieType, name, description, tooltip, sunCost, rechargeTime, allowInVersus, riseFromGround, placement,
            escortCount, escortType, announceHorde);
        CustomContentRegistry.RegisterCustomPlant(definition);
        return definition;
    }
}
