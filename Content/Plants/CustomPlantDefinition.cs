using Il2CppInterop.Runtime.Injection;
using Il2CppReloaded.Data;
using Il2CppReloaded.Gameplay;
using Il2CppSource.Utils;
using MelonLoader;
using PvZReCoreLib.Content.Common.Skins;
using PvZReCoreLib.Content.Common.Skins.SkinDataTypes;
using PvZReCoreLib.Content.Common.Skins.SkinDataTypes.subtypes;
using PvZReCoreLib.Content.Plants.Mint;
using PvZReCoreLib.Util;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Guid = Il2CppSystem.Guid;
using Type = Il2CppSystem.Type;

namespace PvZReCoreLib.Content.Plants;

[RegisterTypeInIl2Cpp]
public class CustomPlantDefinition : PlantDefinition
{
    #region Variables

    public int m_health = 300;
    public MintFamily m_mintFamily = MintFamily.None;

    // Native Board.CanPlantAt hardcodes its own ground-only rejection
    // (PlantingReason.NeedsGround) against a fixed list of vanilla SeedTypes
    // (Potato Mine, Spikeweed, etc.) the same way Plant.IsSpiky() does - no
    // data field to opt a custom plant into it, so a custom SeedType would
    // otherwise be plantable on lilypads/water regardless. Set true for a
    // plant that needs to burrow into actual ground (see
    // Board_CanPlantAt_RequiresGround_Patch in PlantPatches.cs).
    public bool m_requiresGround = false;

    // A plant that makes no sense on a roof level: one that has to burrow into real ground, or that shoots or
    // rolls straight along the lane, where the roof's slope stops it (lobbers are fine). Conveyor belts do not
    // drop it while the level has a roof (the Dr. Zomboss fight, for one). A plant with m_requiresGround is
    // treated the same way. It does not stop the plant being planted by hand.
    public bool m_notOnRoof = false;

    // Stackable plant (PvZ2 "CanPlantAgainToUpgrade", e.g. Peapod): how many times this plant may
    // be planted on the SAME tile, each planting merging into the existing plant instead of
    // being a second plant. 1 = not stackable (default). See PlantStacking.cs for the mechanism;
    // the controller reads the current count from CustomPlantBehaviorController.StackCount and is
    // told about each merge through OnStackedOn.
    public int m_maxStack = 1;

    // Wall-nut first aid for a custom nut: planting the same plant on a damaged one of its own kind (health
    // below two thirds, the point where a nut shows cracks) heals it to full instead of being refused, and the
    // seed is spent. Native Wall-nut / Tall-nut do the same. See PlantRepair.cs.
    public bool m_healsOnReplant = false;

    // A plant that goes ON TOP of another plant (PvZ2 "armor" layer, e.g. Pea Vine): it may be planted on a tile
    // that already holds any other plant, and stays a separate plant of its own there (zombies chew through
    // whichever they reach first). Only one such plant per tile. See PlantRepair.cs.
    public bool m_plantsOnTopOfPlant = false;

    // A nut (Wall-nut, Tall-nut and the like): zombies that need a nut to act on - the Ladder
    // Zombie places its ladder on one - treat this plant as a Wall-nut for that. The game keys
    // that on the literal seed types Wallnut/Tallnut/Pumpkinshell, so a custom nut would otherwise
    // be chewed through instead of climbed. See Zombie_CanTargetPlant_Patch (PlantPatches.cs).
    public bool m_countsAsNut = false;

    public string m_description = "";

    public AssetReferenceSprite m_almanacBackground;

    // Optional: the almanac illustration, when it should be something other than the seed packet
    // image (which is what every plant used before this existed). Null = use the seed packet image.
    // Zombie cards point this at the game's own zombie almanac art.
    public AssetReferenceSprite m_almanacThumbnail;

    #endregion
    
    public CustomPlantDefinition(IntPtr pointer) : base(pointer)
    {
        PlantDefinition peashooterDef = ReloadedUtils.GetPlantDefinition(SeedType.Peashooter);

        m_seedType = CustomContentRegistry.RequestFreeSeedType();
        m_animationType = ReanimationType.Peashooter;
        m_packetIndex = (int)m_seedType;
        
        m_versusImage = null;
        m_chinaPlantImage = null;

        m_preview = null;

        m_chinaPreviewSprite = null;

        m_prefab = PersistentStorage.Store(new AssetReferenceGameObject(peashooterDef.m_prefab.m_AssetGUID)); // ! Needs to be redone
        m_preorderGameObject = null;
        m_chinaGameObject = null;
        
        m_previewSpriteOffset = Vector2.zero;
        m_previewSpriteScale = 1f;
        
        m_easterEggGameObject = null;
        m_easterEggChance100 = 0;
    }
    
    public CustomPlantDefinition() : base(ClassInjector.DerivedConstructorPointer<CustomPlantDefinition>()) => ClassInjector.DerivedConstructorBody(this);
    
    public virtual Type GetCustomBehaviorType()
    {
        return null;
    }
    
    private void SetTextLocal(string text, bool localize, string fieldName)
    {
        string n = text;
        if (!localize)
        {
            n = "[RAW]" + text;
        }
        
        PersistentStorage.Assign(this, fieldName, n);
    }

    public void SetSeedPacketImage(string assetBundleId, string assetPath)
    {
        m_plantImage = PersistentStorage.Store(new AssetReferenceSprite(Guid.NewGuid().ToString()));
        RegistryBridge.RegisterSpriteFromAssetBundle(assetBundleId, assetPath, keyOverride: m_plantImage.m_AssetGUID);
    }
    
    public void SetPreviewImage(string assetBundleId, string assetPath)
    {
        m_previewSprite = PersistentStorage.Store(new AssetReferenceSprite(Guid.NewGuid().ToString()));
        RegistryBridge.RegisterSpriteFromAssetBundle(assetBundleId, assetPath, keyOverride: m_previewSprite.m_AssetGUID, pivot: new Vector2(0, 1));
    }

    public void SetName(string text, bool localize = true)
    {
        SetTextLocal(text, localize, nameof(m_plantName));
    }
    public void SetTooltip(string text, bool localize = true)
    {
        SetTextLocal(text, localize, nameof(m_plantToolTip));
    }
    public void SetDescription(string text, bool localize = true)
    {
        string n = text;
        if (!localize)
        {
            n = "[RAW]" + text;
        }

        m_description = n;
    }

    public void RegisterSkin(IPlantSkin skinType)
    {
        SkinRegistry.RegisterPlantSkin(skinType);

        if (string.IsNullOrEmpty(m_defaultSkin))
        {
            PersistentStorage.Assign(this, nameof(m_defaultSkin), skinType.GetSkinId());
        }
    }
    
    public void SetAlmanacBackground(string assetBundleId, string assetPath)
    {
        m_almanacBackground = PersistentStorage.Store(new AssetReferenceSprite(Guid.NewGuid().ToString()));
        RegistryBridge.RegisterSpriteFromAssetBundle(assetBundleId, assetPath, keyOverride: m_almanacBackground.m_AssetGUID);
    }
}

[RegisterTypeInIl2Cpp]
public class CustomAlmanacEntryData : AlmanacEntryData
{
    public string m_descriptionHeader;
    
    public CustomAlmanacEntryData(IntPtr pointer) : base(pointer)
    {
    }
    
    public CustomAlmanacEntryData() : base(ClassInjector.DerivedConstructorPointer<CustomAlmanacEntryData>())
    {
        ClassInjector.DerivedConstructorBody(this);
    }

    public void LoadFrom(CustomPlantDefinition plantDef)
    {
        SetTextLocal(plantDef.m_plantName, false, nameof(m_entryName));
        SetTextLocal(plantDef.m_description, false, nameof(m_entryDescription));
        m_descriptionHeader = plantDef.m_plantToolTip;

        if (plantDef.m_refreshTime > 3000)
        {
            SetTextLocal("Very Slow", false, nameof(m_entryRecharge));
        }
        else if (plantDef.m_refreshTime > 2000)
        {
            SetTextLocal("Slow", false, nameof(m_entryRecharge));
        }
        else if (plantDef.m_refreshTime > 1000)
        {
            SetTextLocal("Medium", false, nameof(m_entryRecharge));
        }
        else
        {
            SetTextLocal("Fast", false, nameof(m_entryRecharge));
        }

        m_entryThumbnail = PersistentStorage.EmptySpriteRef;
        m_entryThumbnailBackground = PersistentStorage.EmptySpriteRef;
        
        m_entrySunCost = plantDef.m_seedCost;

        // AssetRefUtil.CloneSprite == new AssetReferenceSprite(guid) plus the sub-object name, which
        // is empty for every bundle sprite (so unchanged for them) but required for the game's own
        // atlas sprites (zombie cards).
        m_entryPortrait = AssetRefUtil.CloneSprite(plantDef.m_plantImage);
        
        m_entryThumbnail = AssetRefUtil.CloneSprite(plantDef.m_almanacThumbnail ?? plantDef.m_plantImage);
        m_entryThumbnailBackground = AssetRefUtil.CloneSprite(plantDef.m_almanacBackground);

        m_entryType = AlmanacEntryType.Plant;
        m_seedType = plantDef.m_seedType;
        m_zombieType = ZombieType.Invalid;
    }

    private void SetTextLocal(string text, bool localize, string fieldName)
    {
        string n = text;
        if (!localize)
        {
            n = "[RAW]" + text;
        }
        
        PersistentStorage.Assign(this, fieldName, n);
    }
}