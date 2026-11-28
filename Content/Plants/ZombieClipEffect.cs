using Il2CppReloaded.Gameplay;
using Il2CppReloaded.Utils;
using MelonLoader;
using PvZReCoreLib.Util;
using UnityEngine;

namespace PvZReCoreLib.Content.Plants;

// Plays one clip of an effect prefab (a projectile's hit effect, a bolt, a zap) ON a zombie: the art is centred on the
// zombie's body, not on its feet origin, and is drawn in the game's particle layer of the zombie's row, so it sits in
// front of the zombie it hits. Counterpart of PlantClipEffect (the same idea for the plant's own skin).
//
// With 'fromWorld' the art is a link between two points instead, as a lightning bolt between a projectile and its
// target: it is rotated to point from 'fromWorld' to the zombie's body centre and stretched along its own width to
// span the whole distance (its height stays as drawn).
//
// The effect destroys itself when the clip has played. Returns the holder (a Unity null check tells that the clip is
// over), or null when it could not be placed.
public static class ZombieClipEffect
{
    // World units per board pixel at unit board scale (same GameAssembly constant PlantClipEffect uses).
    private const float WorldPerBoardPixel = 2.82f;

    // The plain hit effect of a projectile: a free-standing copy of the prefab at the zombie's own origin, its
    // Animator playing 'clip' (or its default clip), destroyed after 'lifetime' seconds. Nothing to work out for
    // the caller.
    public static GameObject SpawnHit(Zombie zombie, GameObject prefab, float lifetime, string clip = null, float scale = 1f)
    {
        var controllerObject = zombie?.mController?.gameObject;
        if (controllerObject == null)
        {
            return null;
        }

        return SpawnAtWorld(prefab, controllerObject.transform.position, lifetime, clip, scale);
    }

    // The same at any world position (e.g. at a projectile).
    public static GameObject SpawnAtWorld(GameObject prefab, Vector3 position, float lifetime, string clip = null, float scale = 1f)
    {
        if (prefab == null)
        {
            return null;
        }

        var instance = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
        if (scale != 1f)
        {
            instance.transform.localScale = Vector3.one * scale;
        }

        if (!string.IsNullOrEmpty(clip))
        {
            UnityUtil.PlayAnimatorState(instance, clip);
        }

        UnityEngine.Object.Destroy(instance, lifetime);
        return instance;
    }

    // The centre of everything an object draws (its renderers' combined bounds, in world space).
    public static bool TryGetDrawnCenter(GameObject obj, out Vector3 center)
    {
        center = default;
        if (obj == null)
        {
            return false;
        }

        bool any = false;
        Bounds bounds = default;
        foreach (var renderer in obj.GetComponentsInChildren<Renderer>(false))
        {
            if (renderer == null || !renderer.enabled || renderer.bounds.size.sqrMagnitude <= 0f)
            {
                continue;
            }

            if (!any)
            {
                bounds = renderer.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (any)
        {
            center = bounds.center;
        }

        return any;
    }

    public static GameObject Spawn(Zombie zombie, GameObject prefab, string clip, float fallbackSeconds,
        Vector3? fromWorld = null, float scale = 1f)
    {
        try
        {
            var controllerObject = zombie?.mController?.gameObject;
            if (prefab == null || controllerObject == null)
            {
                return null;
            }

            float boardScale = controllerObject.transform.parent != null ? controllerObject.transform.parent.lossyScale.x : 1f;
            float world = WorldPerBoardPixel * boardScale;

            // Centre of the zombie's body: where it is actually drawn (its renderers' bounds), which does not depend on
            // how the board's pixels map to the world. Falls back to the rect's centre, relative to the controller.
            Vector3 target;
            if (!TryGetDrawnCenter(controllerObject, out target))
            {
                var rect = zombie.GetZombieRect();
                float centerX = rect.x + rect.width / 2f;
                float centerY = rect.y + rect.height / 2f;
                target = controllerObject.transform.position
                         + new Vector3((centerX - zombie.mPosX) * world, -(centerY - zombie.mPosY) * world, 0f);
            }

            var holder = new GameObject("ZombieClipEffect");
            var instance = UnityEngine.Object.Instantiate(prefab, holder.transform, false);
            instance.SetActive(true);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localScale = Vector3.one * scale;

            // Where the art's own centre is, so the art (not its anchor, which is far below the picture) lands on 'target'.
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            Vector3 artCenter = instance.transform.position;
            float artWidth = 0f;
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                }

                artCenter = bounds.center;
                artWidth = bounds.size.x;
            }

            Vector3 offset = artCenter - instance.transform.position;
            instance.transform.localPosition = -offset;

            if (fromWorld.HasValue && artWidth > 0f)
            {
                Vector3 from = fromWorld.Value;
                Vector3 delta = target - from;
                float distance = new Vector2(delta.x, delta.y).magnitude;
                float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

                holder.transform.position = (from + target) / 2f;
                holder.transform.rotation = Quaternion.Euler(0f, 0f, angle);
                holder.transform.localScale = new Vector3(Mathf.Max(0.2f, distance / artWidth), 1f, 1f);
            }
            else
            {
                holder.transform.position = target;
            }

            // The game's own particle layer for the zombie's row, keeping the pieces' order within the effect.
            int baseOrder = EffectSorting.For(zombie.mRow);
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
            MelonLogger.Error($"[CoreLib] Zombie clip effect failed: {e}");
            return null;
        }
    }
}
