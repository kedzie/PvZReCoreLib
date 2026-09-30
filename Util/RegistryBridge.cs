using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using File = Il2CppSystem.IO.File;
using Object = Il2CppSystem.Object;
using Type = Il2CppSystem.Type;

namespace PvZReCoreLib.Util;

[RegisterTypeInIl2CppWithInterfaces(typeof(IResourceLocator))]
public class CustomResourceLocator : Object
{
    public string LocatorId { get; } = "CustomLocator";

    private Il2CppSystem.Collections.Generic.Dictionary<Object, IResourceLocation> _locations = new();
    public Il2CppSystem.Collections.Generic.IEnumerable<Object> Keys => _locations._keys.Cast<Il2CppSystem.Collections.Generic.IEnumerable<Object>>();
    
    public CustomResourceLocator(IntPtr pointer) : base(pointer) { }
    public CustomResourceLocator() : base(ClassInjector.DerivedConstructorPointer<CustomResourceLocator>()) => ClassInjector.DerivedConstructorBody(this);

    public bool Locate(Object key, Type type, out Il2CppSystem.Collections.Generic.IList<IResourceLocation> locations)
    {
        if (_locations.TryGetValue(key, out var location))
        {
            var newList = new Il2CppSystem.Collections.Generic.List<IResourceLocation>();
            newList.Add(location);
            locations = newList.Cast<Il2CppSystem.Collections.Generic.IList<IResourceLocation>>();
            return true;
        }
        locations = null;
        return false;
    }

    public void AddLocation(Object key, IResourceLocation location)
    {
        _locations.Add(key, location);
    }
}

[RegisterTypeInIl2Cpp]
public class PreloadedAssetProvider : ResourceProviderBase
{
    public static System.Collections.Generic.Dictionary<string, Object> PreloadedAssets = new();
    
    public PreloadedAssetProvider(IntPtr pointer) : base(pointer) { }
    public PreloadedAssetProvider() : base(ClassInjector.DerivedConstructorPointer<PreloadedAssetProvider>()) => ClassInjector.DerivedConstructorBody(this);

    public override bool CanProvide(Type t, IResourceLocation location)
    {
        bool bResult = PreloadedAssets.ContainsKey(location.InternalId);
        return bResult;
    }

    public override void Release(IResourceLocation location, Object obj)
    {
    }
    
    public override void Provide(ProvideHandle provideHandle)
    {
        provideHandle.Complete(PreloadedAssets[provideHandle.Location.InternalId], true, null);
    }
}

public class RegistryBridge
{
    #region Variables
    
    public static System.Collections.Generic.Dictionary<string, Il2CppAssetBundle> AssetBundlesMap = new();

    public static CustomResourceLocator CustomResourceLocator;
    
    public static Action OnRegistryBridgeInit;

    #endregion

    #region Constructors

    public static void Init()
    {
        CustomResourceLocator = PersistentStorage.Store(new CustomResourceLocator());
        Addressables.AddResourceLocator(CustomResourceLocator.Cast<IResourceLocator>());
        Addressables.ResourceManager.m_ResourceProviders.Add(PersistentStorage.Store(new PreloadedAssetProvider()).Cast<IResourceProvider>());
        
        OnRegistryBridgeInit?.Invoke();
    }

    #endregion

    #region Methods

    public static Il2CppAssetBundle RegisterAssetBundle(string assetBundleKey, string assetBundlePath)
    {
        var stream = File.OpenRead(assetBundlePath);
        AssetBundlesMap.Add(assetBundleKey, Il2CppAssetBundleManager.LoadFromStream(stream));
        stream.Close();
        
        return AssetBundlesMap[assetBundleKey];
    }
    
    public static void RegisterSpriteFromAssetBundle(string assetBundleKey, string assetPath, 
        Action<string> resultCallback = null, Vector2? pivot = null, string keyOverride = null)
    {
        Action<Sprite> spriteCallback = (Sprite sprite) =>
        {
            var regRes = RegisterObject(sprite, keyOverride);
            resultCallback?.Invoke(regRes);
        };
        LoadSpriteFromAssetBundle(assetBundleKey, assetPath, spriteCallback, pivot);
    }
    public static void LoadSpriteFromAssetBundle(string assetBundleKey, string assetPath, Action<Sprite> resultCallback, 
        Vector2? pivot = null)
    {
        Action<Texture2D> textureCallback = (Texture2D texture) =>
        {
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                pivot ?? new Vector2(0.5f, 0.5f),
                1f
            );
            PersistentStorage.Store(sprite);
            resultCallback.Invoke(sprite);
        };
        LoadAssetFromAssetBundle<Texture2D>(assetBundleKey, assetPath, textureCallback);
    }
    
    public static void RegisterAssetFromAssetBundle<T>(string assetBundleKey, string assetPath,
        Action<string> resultCallback = null, string keyOverride = null) where T : UnityEngine.Object
    {
        Action<T> assetCallback = (T asset) =>
        {
            var regRes = RegisterObject(asset, keyOverride);
            resultCallback?.Invoke(regRes);
        };
        LoadAssetFromAssetBundle<T>(assetBundleKey, assetPath, assetCallback);
    }
    // CONFIRMED live: this mod requires MelonLoader 0.7.3+ (its Il2CppInterop bump broke the
    // original async m_completeCallback-based loading entirely - AccessViolationException combining
    // onto it; a plain assignment stops the crash but the callback never fires; polling
    // isDone/allAssets both crash too - see git history for the full chain). LoadAsset<T> (a raw
    // IntPtr return, no array/Span marshaling) is what actually works on 0.7.3.
    //
    // The LoadAssetWithSubAssets<T> fallback below is NOT a real 0.7.2 compatibility guarantee -
    // 0.7.2 was investigated and explicitly dropped (see CHANGELOG/release notes: v1.15 is the last
    // 0.7.2-supported release, this codebase moves to 0.7.3+ only from here). On this game's Unity
    // 6000 build under 0.7.2, LoadAsset<T>'s icall isn't even registered at all, and this fallback's
    // OWN icall (LoadAssetWithSubAssets_Internal) separately throws MissingMethodException on
    // 'Il2CppSystem.ReadOnlySpan`1.GetPinnableReference()' for at least AudioClip - and that failure
    // happens inside Il2CppInterop's own native->managed trampoline, so it can't even be caught here
    // to fall back further. This is kept purely as harmless, zero-cost insurance in case some future
    // Il2CppInterop build regresses LoadAsset<T> the same way it regressed the old async path - not
    // as a claim that this mod works on 0.7.2.
    public static void LoadAssetFromAssetBundle<T>(string assetBundleKey, string assetPath, Action<T> resultCallback) where T : UnityEngine.Object
    {
        Il2CppAssetBundle assetBundle = AssetBundlesMap[assetBundleKey];

        T asset;
        try
        {
            asset = assetBundle.LoadAsset<T>(assetPath);
        }
        catch (Exception)
        {
            asset = assetBundle.LoadAssetWithSubAssets<T>(assetPath).First();
        }

        PersistentStorage.Store(asset);
        resultCallback.Invoke(asset);
    }
    
    public static string RegisterObject(Object obj, string keyOverride = null)
    {
        string key = keyOverride ?? Guid.NewGuid().ToString();
        
        PreloadedAssetProvider.PreloadedAssets.Add(key, obj);
        var loc = PersistentStorage.Store(new ResourceLocationBase(key, key, typeof(PreloadedAssetProvider).FullName, obj.GetIl2CppType()));
        CustomResourceLocator.AddLocation(key, loc.Cast<IResourceLocation>());
        
        return key;
    }

    #endregion
}