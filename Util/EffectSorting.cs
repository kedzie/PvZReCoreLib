using Il2CppReloaded.Gameplay;
using Il2CppReloaded.Utils;

namespace PvZReCoreLib.Util;

// The sorting order that puts an effect where the game itself would draw one: the given render layer of a lane
// (default the particle layer, in front of the plants and zombies of that row), remapped to Unity's sorting
// layers. Every effect helper (PlantClipEffect, ZombieClipEffect, the mod's own spawners) goes through this, so
// no plant works out layers on its own.
public static class EffectSorting
{
    public static int For(int row, RenderLayer layer = RenderLayer.Particle, int offset = 1)
    {
        return SortingUtils.RemapSortingLayers(Board.MakeRenderOrder(layer, row, offset));
    }
}
