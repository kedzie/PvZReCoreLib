using UnityEngine.AddressableAssets;

namespace PvZReCoreLib.Util;

public static class AssetRefUtil
{
    // Makes an independent AssetReferenceSprite that resolves to the same asset as 'source',
    // INCLUDING its sub-object name.
    //
    // The existing CoreLib pattern (new AssetReferenceSprite(source.m_AssetGUID)) copies only the
    // GUID. That is fine for a stand-alone sprite, but the game's own seed packet / preview /
    // almanac art lives inside multi-sprite atlas textures (m_AssetGUID is the atlas, the sprite is
    // picked by m_SubObjectName, e.g. "SPR_SeedPackets_Zombies_2"), so a GUID-only copy points at
    // the atlas and not at one sprite in it.
    //
    // A clone rather than the native instance on purpose: an AssetReference carries its own load
    // operation handle, so sharing one instance between a native definition and ours could confuse
    // that bookkeeping. Both resolve to the same RuntimeKey and Addressables ref-counts the asset.
    public static AssetReferenceSprite CloneSprite(AssetReferenceSprite source, bool store = true)
    {
        if (source == null)
        {
            return null;
        }

        var clone = new AssetReferenceSprite(source.m_AssetGUID);

        string subObjectName = source.SubObjectName;
        if (!string.IsNullOrEmpty(subObjectName))
        {
            clone.SubObjectName = subObjectName;
            clone.m_SubObjectType = source.m_SubObjectType;
        }

        return store ? PersistentStorage.Store(clone) : clone;
    }
}
