using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Fields;
using Il2CppReloaded;
using Il2CppReloaded.Data;
using Il2CppReloaded.Gameplay;
using Il2CppReloaded.Services;
using Il2CppSource.Utils;
using MelonLoader;
using PvZReCoreLib.Content.Common.Behavior;
using PvZReCoreLib.Content.Common.Skins;
using PvZReCoreLib.Content.Common.Skins.SkinDataTypes.subtypes;
using PvZReCoreLib.Util;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Type = Il2CppSystem.Type;

namespace PvZReCoreLib.Content.Projectiles;

[RegisterTypeInIl2Cpp]
public class CustomProjectileDefinition : ProjectileDefinition
{
    #region Variables
    
    public ProjectileMotion m_motionType;

    public DamageRangeFlags m_damageRangeFlags;
    public DamageFlags m_damageFlags;
    
    public AudioClip m_hitSfx = null;

    // On the roof's slanted half the ground rises under a straight-flying projectile, and the
    // game crashes native peas into it (see CustomProjectileBehaviorController.CheckRoofCrash).
    // Native only does that for its own projectile types, so custom ones need this. Lobbed and
    // FloatOver motion already follow the slope natively and are never affected.
    public bool m_crashesIntoRoofSlope = true;

    // Native pea rule: impact once the ground line (mShadowY) is within this many px of the
    // projectile (mShadowY - mPosY < 28).
    public float m_roofContactGap = 28f;

    public string m_defaultSkin = "";

    #endregion

    #region Constructors

    public CustomProjectileDefinition(IntPtr pointer) : base(pointer)
    {
        var peaDefinition = ReloadedUtils.DataService.GetProjectileDefinition(ProjectileType.Pea);
        
        m_projectileType = CustomContentRegistry.RequestFreeProjectileType();

        m_prefab = PersistentStorage.Store(new AssetReferenceGameObject(peaDefinition.m_prefab.m_AssetGUID));
        m_preorderPrefab = PersistentStorage.EmptyGameObjectRef;
    }
    
    public CustomProjectileDefinition() : base(ClassInjector.DerivedConstructorPointer<CustomProjectileDefinition>()) => ClassInjector.DerivedConstructorBody(this);

    #endregion

    #region Methods

    public virtual Type GetCustomBehaviorType()
    {
        return null;
    }
    
    public void SetDamageRangeFlags(params DamageRangeFlags[] flags)
    {
        int flagColl = 0;
        foreach (var flag in flags)
        {
            flagColl |= (int)flag;
        }
        this.m_damageRangeFlags = (DamageRangeFlags)flagColl;
    }
    public void SetDamageFlags(params DamageFlags[] flags)
    {
        int flagColl = 0;
        foreach (var flag in flags)
        {
            flagColl |= (int)flag;
        }
        this.m_damageFlags = (DamageFlags)flagColl;
    }

    public void SetHitSfx(string assetBundleId, string assetPath)
    {
        Action<AudioClip> sfxLoadAction = (sfx) =>
        {
            this.m_hitSfx = sfx;
        };
        RegistryBridge.LoadAssetFromAssetBundle<AudioClip>(assetBundleId, assetPath, sfxLoadAction);
    }
    
    public void RegisterSkin(IProjectileSkin skinType)
    {
        SkinRegistry.RegisterProjectileSkin(skinType);

        // CONFIRMED live: PersistentStorage.Assign is wrong here and crashes hard
        // (AccessViolationException via an invalid native field-offset lookup, "Field
        // m_defaultSkin was not found on class BloomerangProjectileDefinition"). Unlike
        // CustomPlantDefinition.m_defaultSkin (a genuine NATIVE field inherited from the game's own
        // PlantDefinition, which is exactly what PersistentStorage.Assign is for), m_defaultSkin
        // here is a field WE declared directly on CustomProjectileDefinition - not part of real
        // IL2CPP class metadata for a derived type like BloomerangProjectileDefinition at all - so
        // a plain C# assignment is the correct, safe way to set it. The actual cause of the
        // originally-reported "projectiles invisible, ApplySkin never called" bug is still
        // unconfirmed and needs real diagnostic data, not another guess.
        if (string.IsNullOrEmpty(m_defaultSkin))
        {
            m_defaultSkin = skinType.GetSkinId();
        }
    }

    #endregion
}

[RegisterTypeInIl2Cpp]
public class CustomProjectileBehaviorController : CustomBehaviorController
{
    #region Variables

    public Il2CppReferenceField<Projectile> mProjectile;
    public Projectile Projectile => mProjectile.Value;
    
    public Il2CppReferenceField<ProjectileDefinition> mProjectileDefinition;
    public ProjectileDefinition ProjectileDefinition => mProjectileDefinition.Value;

    #endregion

    #region Constructors

    public CustomProjectileBehaviorController(IntPtr pointer) : base(pointer)
    {
    }

    #endregion

    #region Methods

    #region Roof slope

    private bool roofBaselineCaptured;
    private float roofStartShadowY;
    private float roofAllowedRise;

    // Called when the ground rises into this projectile on the roof. Default is just to remove it
    // (not DoImpact(null): subclasses' PreDoImpact expect a zombie). Override for an effect.
    public virtual void OnRoofCrash() { }

    public override void Reset()
    {
        base.Reset();
        roofBaselineCaptured = false;
    }

    // Just before the first native Update, i.e. after the plant has applied its own spawn offsets.
    public void CaptureRoofBaseline()
    {
        if (roofBaselineCaptured)
        {
            return;
        }
        roofBaselineCaptured = true;

        var customDef = ProjectileDefinition?.TryCast<CustomProjectileDefinition>();
        if (customDef == null || !customDef.m_crashesIntoRoofSlope)
        {
            roofAllowedRise = float.PositiveInfinity;
            return;
        }

        // Native crashes a pea when (mShadowY - mPosY) drops below the contact gap. Slope only
        // moves mShadowY, so that is the same as the ground rising by (initial gap - contact gap).
        // Clamped so a projectile spawned lower than the gap (e.g. a ground roller) crashes as
        // soon as the ground rises at all, instead of dying on flat ground.
        roofStartShadowY = Projectile.mShadowY;
        roofAllowedRise = Mathf.Max(Projectile.mShadowY - Projectile.mPosY - customDef.m_roofContactGap, 0f);
    }

    public void CheckRoofCrash()
    {
        if (!roofBaselineCaptured || Projectile.mDead)
        {
            return;
        }

        var motion = Projectile.mMotionType;
        if (motion == ProjectileMotion.Lobbed || motion == ProjectileMotion.FloatOver)
        {
            return;
        }

        if (Projectile.mBoard == null || !Projectile.mBoard.StageHasRoof())
        {
            return;
        }

        if (roofStartShadowY - Projectile.mShadowY > roofAllowedRise + 0.01f)
        {
            OnRoofCrash();
            Projectile.Die();
        }
    }

    #endregion

    public Action PreUpdateEvent;
    public virtual bool PreUpdate() { PreUpdateEvent?.Invoke(); return true; }
    public Action PostUpdateEvent;
    public virtual void PostUpdate() { PostUpdateEvent?.Invoke(); }
    
    public Action PreUpdateNormalMotionEvent;
    public virtual bool PreUpdateNormalMotion() { PreUpdateNormalMotionEvent?.Invoke(); return true; }
    public Action PostUpdateNormalMotionEvent;
    public virtual void PostUpdateNormalMotion() { PostUpdateNormalMotionEvent?.Invoke(); }
    
    public Action<Zombie> PreDoImpactEvent;
    public virtual bool PreDoImpact(Zombie theZombie) { PreDoImpactEvent?.Invoke(theZombie); return true; }
    public Action<Zombie> PostDoImpactEvent;
    public virtual void PostDoImpact(Zombie theZombie) { PostDoImpactEvent?.Invoke(theZombie); }

    #endregion

    #region Helpers

    public void DamageZombie(Zombie theZombie)
    {
        var customDef = ProjectileDefinition.TryCast<CustomProjectileDefinition>();
        if (customDef == null)
        {
            var flags = Projectile.GetDamageFlags(theZombie);
            theZombie.TakeDamage(ProjectileDefinition.m_damage, flags);    
        }
        else
        {
            theZombie.TakeDamage(ProjectileDefinition.m_damage, customDef.m_damageFlags);
        }
        
        var audioSrv = AppCore.GetService<IAudioService>().Cast<AudioService>();
        if (customDef != null && customDef.m_hitSfx != null)
        {
            var sfxVolume = AppCore.GetService<ISettingsService>().SoundEffectVolume;
            audioSrv.m_audioSources.GetAudioSource(Constants.Sound.SOUND_PLANT).m_audioSource.PlayOneShot(customDef.m_hitSfx, sfxVolume);
        }
    }

    public void PlayAudio(AudioClip sfx)
    {
        var audioSrv = AppCore.GetService<IAudioService>().Cast<AudioService>();
        var sfxVolume = AppCore.GetService<ISettingsService>().SoundEffectVolume;
        audioSrv.m_audioSources.GetAudioSource(Constants.Sound.SOUND_PLANT).m_audioSource.PlayOneShot(sfx, sfxVolume);
    }

    // Same pool-by-name pattern as CustomPlantBehaviorController - see there for why.
    private readonly Dictionary<string, List<AudioClip>> mSoundPools = new Dictionary<string, List<AudioClip>>();

    public void RegisterSoundPool(string eventName, params AudioClip[] clips)
    {
        mSoundPools[eventName] = new List<AudioClip>(clips);
    }

    public void OnAnimationSoundEvent(string eventName)
    {
        if (mSoundPools.TryGetValue(eventName, out var pool) && pool.Count > 0)
        {
            PlayAudio(pool[UnityEngine.Random.Range(0, pool.Count)]);
        }
    }

    #endregion
}

[HarmonyPatch(typeof(Projectile), nameof(Projectile.Update))]
public class Projectile_Update_Patch
{
    public static bool Prefix(ref Projectile __instance)
    {
        if (__instance == null || __instance.mController == null || __instance.mController.gameObject == null)
        {
            return true;
        }

        if(__instance.mController.gameObject.TryGetComponent(out CustomProjectileBehaviorController customProjectileBehavior))
        {
            customProjectileBehavior.CaptureRoofBaseline();
            return customProjectileBehavior.PreUpdate();
        }

        return true;
    }
    
    public static void Postfix(ref Projectile __instance)
    {
        if (__instance == null || __instance.mDead || __instance.mController == null || __instance.mController.gameObject == null)
        {
            return;
        }

        if(__instance.mController.gameObject.TryGetComponent(out CustomProjectileBehaviorController customProjectileBehavior))
        {
            customProjectileBehavior.PostUpdate();
            customProjectileBehavior.CheckRoofCrash();
        }
    }
}

[HarmonyPatch(typeof(Projectile), nameof(Projectile.UpdateNormalMotion))]
public class Projectile_UpdateMotion_Patch
{
    public static bool Prefix(ref Projectile __instance)
    {
        if (__instance == null || __instance.mController == null || __instance.mController.gameObject == null)
        {
            return true;
        }

        if(__instance.mController.gameObject.TryGetComponent(out CustomProjectileBehaviorController customProjectileBehavior))
        {
            return customProjectileBehavior.PreUpdateNormalMotion();
        }

        return true;
    }
    
    public static void Postfix(ref Projectile __instance)
    {
        if (__instance == null || __instance.mDead || __instance.mController == null || __instance.mController.gameObject == null)
        {
            return;
        }
        
        if(__instance.mController.gameObject.TryGetComponent(out CustomProjectileBehaviorController customProjectileBehavior))
        {
            customProjectileBehavior.PostUpdateNormalMotion();
        }
    }
}

[HarmonyPatch(typeof(Projectile), nameof(Projectile.DoImpact))]
public class Projectile_DoImpact_Patch
{
    public static bool Prefix(ref Projectile __instance, ref Zombie theZombie)
    {
        if (__instance == null || __instance.mController == null || __instance.mController.gameObject == null)
        {
            return true;
        }

        if(__instance.mController.gameObject.TryGetComponent(out CustomProjectileBehaviorController customProjectileBehavior))
        {
            return customProjectileBehavior.PreDoImpact(theZombie);
        }

        return true;
    }
    
    public static void Postfix(ref Projectile __instance, ref Zombie theZombie)
    {
        if (__instance == null || __instance.mDead || __instance.mController == null || __instance.mController.gameObject == null)
        {
            return;
        }
        
        if(__instance.mController.gameObject.TryGetComponent(out CustomProjectileBehaviorController customProjectileBehavior))
        {
            customProjectileBehavior.PostDoImpact(theZombie);
        }
    }
}