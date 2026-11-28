using Il2CppReloaded.Gameplay;
using Il2CppReloaded.Utils;
using MelonLoader;
using PvZReCoreLib.Content.Common.Audio;
using PvZReCoreLib.Content.Plants.Behavior;
using PvZReCoreLib.Util;
using UnityEngine;

namespace PvZReCoreLib.Content.Plants;

// Plays one clip of a custom plant's own skin as a free-standing effect somewhere else in the plant's lane, while
// the plant itself does something different (e.g. Snap Pea sinks into the ground at home and its bite is drawn
// under the zombie). The effect is a second copy of the plant's skin prefab, so it looks exactly like the plant
// and carries the clip's baked Animation Events:
//
//  * PlaySound plays through the copy's own AudioSource, as on the plant.
//  * ExecuteAttack reaches the owning plant's controller (OnAnimationAttackEvent): the copy hangs below the
//    plant's controller object in the same shape as the real skin, which is where CoreLib's AnimationScripts
//    looks for the owner. So the effect's "hit" frame drives the plant's code, and follows the game speed.
//
// The copy destroys itself when the clip has played (the plant's own Animator chain would otherwise carry on to
// the next clip). Pass the skin prefab the plant's skin uses, loaded with RegistryBridge.LoadAssetFromAssetBundle.
public static class PlantClipEffect
{
    // World units per board pixel at unit board scale (same GameAssembly constant ChillyPepper & co. use).
    private const float WorldPerBoardPixel = 2.82f;

    // Where the plant's art anchor is in the world: the skin's "anim" child, which sits below the animation
    // controller's object, below the plant's controller object (see CC2's PlantEffectSpawner).
    private static Transform FindSkinAnim(Plant plant)
    {
        var controller = plant?.mController;
        if (controller == null)
        {
            return null;
        }

        var animationController = controller.AnimationController;
        var anim = animationController?.gameObject.transform.Find("SpriteRendererSkin/anim");
        if (anim != null)
        {
            return anim;
        }

        foreach (var child in controller.gameObject.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "anim" && child.parent != null && child.parent.name == "SpriteRendererSkin")
            {
                return child;
            }
        }

        return null;
    }

    // Spawns the copy 'boardDx' board pixels to the right of the plant (negative: left), in the plant's own row,
    // playing 'clip'. Returns the holder object (it is destroyed after the clip's length, so a Unity null check
    // tells that the clip is over), or null when it could not be placed.
    public static GameObject Spawn(CustomPlantBehaviorController owner, GameObject skinPrefab, string clip, float boardDx, float fallbackSeconds)
    {
        try
        {
            var plant = owner?.Plant;
            var controllerObject = plant?.mController?.gameObject;
            var realAnim = FindSkinAnim(plant);
            if (skinPrefab == null || controllerObject == null || realAnim == null)
            {
                return null;
            }

            float boardScale = controllerObject.transform.parent != null ? controllerObject.transform.parent.lossyScale.x : 1f;

            var holder = new GameObject("PlantClipEffect");
            holder.transform.SetParent(controllerObject.transform, false);

            var instance = UnityEngine.Object.Instantiate(skinPrefab, holder.transform, false);
            instance.SetActive(true);
            instance.transform.localScale = Vector3.one;

            var copyAnim = instance.transform.Find("anim");
            if (copyAnim == null)
            {
                UnityEngine.Object.Destroy(holder);
                return null;
            }

            // Same size as the real skin, whatever the holder's own scale is.
            float factor = copyAnim.lossyScale.x != 0f ? realAnim.lossyScale.x / copyAnim.lossyScale.x : 1f;
            instance.transform.localScale = new Vector3(factor, factor, factor);

            // Same anchor as the real skin, moved sideways along the lane.
            var target = realAnim.position + new Vector3(boardDx * WorldPerBoardPixel * boardScale, 0f, 0f);
            instance.transform.position += target - copyAnim.position;

            copyAnim.gameObject.AddComponent<AnimationScripts>();

            // The game's own particle layer for the plant's row, keeping the pieces' order within the copy.
            int baseOrder = EffectSorting.For(plant.mRow);
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            int lowest = int.MaxValue;
            foreach (var renderer in renderers)
            {
                lowest = Math.Min(lowest, renderer.sortingOrder);
            }

            foreach (var renderer in renderers)
            {
                renderer.sortingOrder = baseOrder + (renderer.sortingOrder - lowest);
            }

            UnityUtil.PlayAnimatorState(instance, clip);

            float lifetime = UnityUtil.GetAnimatorClipLength(instance, clip, fallbackSeconds);
            UnityEngine.Object.Destroy(holder, lifetime);
            return holder;
        }
        catch (Exception e)
        {
            MelonLogger.Error($"[CoreLib] Plant clip effect failed: {e}");
            return null;
        }
    }
}
