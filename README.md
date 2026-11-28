<div align="center">

# PvZReCoreLib

**A MelonLoader library for adding custom plants, projectiles, skins and zombie cards to *Plants vs. Zombies Replanted*.**

![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3%2B-8a2be2) ![Game](https://img.shields.io/badge/PvZ-Replanted-blue) ![Version](https://img.shields.io/badge/version-1.16-2ea043)

</div>

---

CoreLib is the shared foundation of the [Complete Collection 2](https://github.com/kedzie/PvZReCompleteCollection) plants. It turns "I want a new plant" into a definition class and a behaviour class: CoreLib registers the plant with the game's own data service, so it appears in the seed chooser, the almanac, the Versus rules and the conveyor belts like a native one.

## What it gives you

| Area | What you get |
|---|---|
| **Custom plants** | `CustomPlantDefinition` (cost, health, recharge, launch rate, sub-class, almanac text, mint family) and `CustomPlantBehaviorController` with hooks for the launch counter, plant update, production and shooting. |
| **Animation events** | A baked `ExecuteAttack` Animation Event calls `OnAnimationAttackEvent`, so a throw or shot leaves on the clip's own frame and follows the game speed. `PlayAnimation(...)` drives named clips. |
| **Custom projectiles** | `CustomProjectileDefinition` (damage, motion type, damage flags, hit sound, skin) and `CustomProjectileBehaviorController` with `PreDoImpact` and `PreUpdateNormalMotion`, for piercing, splash, knock-back and the like. |
| **Asset bundles and skins** | `RegistryBridge.RegisterAssetBundle` plus sprite-renderer plant and projectile skins loaded from Unity asset bundles; seed packet and preview art from the bundle. |
| **Stackable plants** | `m_maxStack` lets a plant be planted on itself to grow (Peapod). |
| **Zombie cards** | Hypnotized zombie seed packets with a catalog of the game's zombies, including vehicles, Zombotany and Dr. Zomboss interaction. |
| **Conveyor belts** | Custom plants and zombie cards drop on belt levels, and the belt can run up to 100x faster. Settings live in `Mods/CoreLib/ConveyorBelt.cfg`; BloomEngine is an optional menu. |
| **Mint families** | The family system (Enlighten-Mint and friends) for plants that boost each other. |
| **Roof and nut rules** | `m_notOnRoof`, `m_requiresGround`, `m_countsAsNut` so custom plants fit the game's special cases. |

## A plant in two classes

```csharp
[RegisterTypeInIl2Cpp]
public class MyPlantDefinition : CustomPlantDefinition
{
    public MyPlantDefinition(IntPtr pointer) : base(pointer)
    {
        m_subClass = PlantSubClass.Shooter;
        SetName("My Plant", false);
        m_seedCost = 100;
        m_health = 300;
        m_refreshTime = 500;
        m_launchRate = 140;

        SetSeedPacketImage(MyMod.BundleId, "assets/myplant/seedpacket.png");
        RegisterSkin(new SpriteRendererPlantSkin
        {
            skinId = "MyPlant_Default",
            AssetBundleId = MyMod.BundleId,
            SkinPrefabId = "assets/myplant/models/default/plant/myplant.prefab",
        });
    }

    public override Il2CppSystem.Type GetCustomBehaviorType() => Il2CppType.Of<MyPlantBehavior>();
}

[RegisterTypeInIl2Cpp]
public class MyPlantBehavior : CustomPlantBehaviorController
{
    public MyPlantBehavior(IntPtr pointer) : base(pointer) { }

    public override void OnLaunchCounterTriggered() => PlayAnimation("attack");   // start the clip...
    public override void OnAnimationAttackEvent() { /* ...and fire on its ExecuteAttack frame */ }
}
```

Register the bundle and the plant in your mod's `OnInitializeMelon`:

```csharp
RegistryBridge.RegisterAssetBundle(MyMod.BundleId, "Mods/MyMod/mybundle");
CustomContentRegistry.RegisterCustomPlant(ScriptableObject.CreateInstance<MyPlantDefinition>());
```

The [Complete Collection 2](https://github.com/kedzie/PvZReCompleteCollection) source (`Plants/Defs/`) has a working example of every pattern: shooters, lobbers, walls, instant plants, melee plants, multi-state plants and stackable plants.

## Install

Players: extract the release into the game's `Mods` folder (`PvZReCoreLib.dll` and the `CoreLib` folder). Mods that depend on CoreLib tell you which version they need. `CoreLib/ConveyorBelt.cfg` holds the belt settings; overwriting it resets them.

Requires **MelonLoader 0.7.3 or newer**. BloomEngine is optional (`MelonOptionalDependencies`): CoreLib loads fine without it.

## Building

.NET 6 SDK, a MelonLoader-installed copy of the game, and the `HintPath`s in `PvZReCoreLib.csproj` pointed at your install:

```
dotnet build -c Release
```

## Credits and licence

Created by **Draco9990**; this fork, with Draco's permission, carries the newer work by **Kedzie**. See `LICENCE`. VersusPlus by CougatDaCube is read from the player's own install for zombie card pictures (nothing is shipped); BloomEngine by PalmForest is the optional menu.

*Not affiliated with, or endorsed by, EA or PopCap.*
