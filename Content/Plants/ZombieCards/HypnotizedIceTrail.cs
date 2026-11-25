using HarmonyLib;
using Il2CppReloaded.Gameplay;
using MelonLoader;
using Unity.Mathematics;

namespace PvZReCoreLib.Content.Plants.ZombieCards;

// The ice a hypnotized Zomboni leaves behind it (only the Zomboni makes ice; a hypnotized Bobsled Team
// just slides faster over it, see HypnotizedBobsled).
//
// The game's own lane ice is one number per lane: Board.mIceMinX[row] is its left edge and everything
// to the right of it, up to x = 800, is ice (Board.mIceTimer[row] counts it down from 3000 ticks).
// That cannot describe a trail behind a vehicle driving RIGHT (it would be the part ahead of it), and
// natively it also stops planting right of the edge, so a hypnotized vehicle does not use it: the native
// state is put back after every update (see the Zomboni / Bobsled patches) and the trail lives here.
//
// It hurts nothing, but it is NOT only a picture: like the native ice it stops planting on the tiles it
// covers, for the plant side (Board_CanPlantAt_HypnotizedIceTrail_Patch; the native check is the inlined
// IsIceAt in Board.CanPlantAt: mIceTimer[row] != 0 and mIceMinX[row] <= 750 -> every column from
// PixelToGridX(mIceMinX + 12) rightwards answers PlantingReason.NotHere, the game's own "ice" reason).
// The trail blocks the columns from the one holding x0 + 12 to the one holding x1 - 12, in its lane, for as
// long as it exists (so the block goes with the trail when its 30 s run out). How it is drawn: the game draws a lane's ice through one IceRowController per lane (BackgroundController), driven
// by Board.DrawIce(row) every frame with position = (800, GridToPixelY(8, row) + 20), width = 800 -
// mIceMinX[row] and a fade of Clamp(timer * 255 / 10, 0, 255) - the position is the RIGHT end and the
// ice extends left by "width" (read from the decompile: with mIceMinX = 800 nothing is drawn, and a
// position of 800 with a growing width only makes sense that way). Drawing a trail from x0 to x1 is the
// same call with position x = x1 and width = x1 - x0, made after the native DrawGameObjects for a lane
// the native code did not draw ice in (Board_DrawGameObjects_HypnotizedIceTrail_Patch).
internal static class HypnotizedIceTrail
{
    // Same life as the native ice: 3000 ticks after it last grew.
    private const int Lifetime = 3000;

    // The native draw offset below the lane's reference line.
    private const int DrawYOffset = 20;

    private const int MaxRows = 8;

    private class Trail
    {
        public float x0;
        public float x1;
        public int timer;
        public IntPtr board;
    }

    private static readonly Trail[] trails = new Trail[MaxRows];

    // The trail of the vehicle in 'row' now reaches x (its right end). The first call of a trail also
    // fixes where it starts: at x, or 'startBehind' px to its left.
    internal static void Extend(Board board, int row, float x, float startBehind = 0f)
    {
        if (board == null || row < 0 || row >= MaxRows)
        {
            return;
        }

        var trail = trails[row];
        if (trail == null || trail.timer <= 0 || trail.board != board.Pointer)
        {
            trail = new Trail { x0 = x - startBehind, x1 = x, board = board.Pointer };
            trails[row] = trail;
        }

        if (x > trail.x1)
        {
            trail.x1 = x;
        }

        trail.timer = Lifetime;
    }

    // Does the stretch [left, right] of the lane touch a live trail of that lane? The Bobsled Team asks
    // this: it stays together only while part of it is over the ice.
    internal static bool IsOverSpan(Board board, int row, float left, float right)
    {
        if (board == null || row < 0 || row >= MaxRows)
        {
            return false;
        }

        var trail = trails[row];
        return trail != null && trail.timer > 0 && trail.board == board.Pointer && right >= trail.x0 && left <= trail.x1;
    }

    // The native rule, mirrored: a tile of the lane is blocked when it contains the point 12 px inside
    // either end of the trail, or lies between them. 80 px is the tile width.
    internal static bool Blocks(Board board, int col, int row)
    {
        if (row < 0 || row >= MaxRows)
        {
            return false;
        }

        var trail = trails[row];
        if (trail == null || trail.timer <= 0 || trail.board != board.Pointer || trail.x1 - trail.x0 < 1f)
        {
            return false;
        }

        float left = board.GridToPixelX(col, row);
        return left + TileWidth > trail.x0 + EdgeInset && left <= trail.x1 - EdgeInset;
    }

    private const float TileWidth = 80f;
    private const float EdgeInset = 12f;

    // One game tick (after Board.UpdateIce): count the lifetimes down.
    internal static void Tick(Board board)
    {
        for (int row = 0; row < MaxRows; row++)
        {
            var trail = trails[row];
            if (trail == null)
            {
                continue;
            }

            if (trail.board != board.Pointer)
            {
                // a trail of a board that is gone
                trails[row] = null;
                continue;
            }

            if (trail.timer > 0)
            {
                trail.timer--;
            }

            if (trail.timer <= 0)
            {
                trails[row] = null;
            }
        }
    }

    // After the native draw of the board's objects: show the lanes' trails.
    internal static void Draw(Board board)
    {
        var background = board.mApp?.BackgroundController;
        if (background == null)
        {
            return;
        }

        int rows = Math.Min(MaxRows, board.mIceMinX.Length);
        for (int row = 0; row < rows; row++)
        {
            var trail = trails[row];
            if (trail == null || trail.timer <= 0 || trail.board != board.Pointer || trail.x1 - trail.x0 < 1f)
            {
                continue;
            }

            // The native code drew a lane's own ice there: leave that one alone.
            if (board.mIceTimer[row] != 0 && board.mIceMinX[row] != 800)
            {
                continue;
            }

            int fade = Math.Clamp(trail.timer * 255 / 10, 0, 255);
            float y = board.GridToPixelY(8, row) + DrawYOffset;

            background.SetIceRowDraw(row, true);
            background.SetIceRowFade(row, fade);
            background.SetIceRowPosition(row, new float2(trail.x1, y));
            background.SetIceRowWidth(row, trail.x1 - trail.x0);
        }
    }
}

[HarmonyPatch(typeof(Board), "UpdateIce")]
public class Board_UpdateIce_HypnotizedIceTrail_Patch
{
    public static void Postfix(Board __instance)
    {
        try
        {
            HypnotizedIceTrail.Tick(__instance);
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Hypnotized ice trail tick failed: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(Board), "DrawGameObjects")]
public class Board_DrawGameObjects_HypnotizedIceTrail_Patch
{
    public static void Postfix(Board __instance)
    {
        try
        {
            HypnotizedIceTrail.Draw(__instance);
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Hypnotized ice trail draw failed: {e.Message}");
        }
    }
}

// Ice blocks planting, as natively: the plant-side player cannot plant on a tile a trail covers. Runs after
// the other CanPlantAt patches and only turns an "Ok" into the native ice answer. Zombie cards are exempt: the
// "plant" is a placeholder that is gone on its first tick and the ice does nothing to a zombie, and the
// Bobsled Team has to be launchable from a tile on the Zomboni's ice (it rides it).
[HarmonyPatch(typeof(Board), nameof(Board.CanPlantAt), new[] { typeof(int), typeof(int), typeof(SeedType) })]
public class Board_CanPlantAt_HypnotizedIceTrail_Patch
{
    public static void Postfix(Board __instance, int theGridX, int theGridY, SeedType theType, ref PlantingReason __result)
    {
        if (__result != PlantingReason.Ok || ZombieCardUtils.GetCard(theType) != null)
        {
            return;
        }

        try
        {
            if (HypnotizedIceTrail.Blocks(__instance, theGridX, theGridY))
            {
                __result = PlantingReason.NotHere;
            }
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[CoreLib] Hypnotized ice trail planting check failed: {e.Message}");
        }
    }
}
