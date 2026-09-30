using PvZReCoreLib.Content.Common.Skins.SkinDataTypes.subtypes;
using PvZReCoreLib.Content.Projectiles;
using UnityEngine;

namespace PvZReCoreLib.Content.Common.Skins.SkinDataTypes.Subtypes.Projectile;

public class SpriteRendererProjectileSkin : SpriteRendererSkin, IProjectileSkin
{
    #region Variables



    #endregion

    #region Constructors



    #endregion

    #region Methods

    public override void ApplySkin(GameObject obj)
    {
        base.ApplySkin(obj);

        foreach (var o in obj.transform)
        {
            var goT = o.TryCast<Transform>();
            if (goT == null)
            {
                continue;
            }
            
            var go = goT.gameObject;
            // CONFIRMED live: this loop hides the native Pea prefab's own original sprite
            // children so the custom skin shows in their place - but base.ApplySkin(obj) above
            // already added OUR OWN "SpriteRendererSkin" child as another sibling before this
            // loop runs, and this loop only knew to skip "Shadow" - it was deactivating our own
            // just-created skin in the same synchronous call, every single time, before it ever
            // had a chance to render. Excluding it by name fixes that.
            if (go == null || go.name == "Shadow" || go.name == "SpriteRendererSkin")
            {
                continue;
            }

            go.SetActive(false);
        }
        
        ProjectileExtension ext = ProjectileExtension.GetOrCreateExtension<ProjectileExtension>(obj);
        ext.CurrentSkin = this;
    }

    public override void CleanUpSkin(GameObject obj)
    {
        base.CleanUpSkin(obj);
        
        ProjectileExtension ext = ProjectileExtension.GetOrCreateExtension<ProjectileExtension>(obj);
        ext.CurrentSkin = null;
    }

    #endregion
}