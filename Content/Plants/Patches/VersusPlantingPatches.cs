using HarmonyLib;
using Il2CppReloaded.Data;
using Il2CppReloaded.Gameplay;

namespace PvZReCoreLib.Content.Plants.Patches;

// CONFIRMED via the decompiled Board.MouseDownWithPlant: when IsVersusMode() is true and the seed
// type held in the cursor is greater than 49 (0x31), the click is routed to the zombie placement
// path (Challenge.IZombieMouseDownWithZombie) instead of planting - Versus keeps its zombie
// pseudo-seeds at 60-80 and treats everything above the plants as one. Custom plants are numbered
// 82+, so in Versus every one of them spawned the same plain zombie (live: five clicks, five
// ZombieType.Normal) instead of planting.
//
// The check is an inline compare inside a large native method, so there is nothing small to hook,
// and the tiny IsVersusMode() itself must not be detoured (see the note on Plant.IsSpiky in
// PlantPatches.cs). Instead the game mode on the level data - the exact field IsVersusMode() reads -
// is flipped to None for the duration of one click that is holding a custom plant, and restored
// afterwards (Finalizer, so a throw can't leave the match stuck in the wrong mode). With the mode
// off, native takes its ordinary planting path. That also drops Versus' own placement rules, so the
// Prefix first asks native's CanPlantAt (mode still on) and swallows the click when Versus rejects
// the cell (VSNotHere / VSSuddenDeath) - otherwise plants could go in the zombie zone. Vanilla seeds and the zombie player's own clicks
// never match the custom-seed condition, so they are untouched.
[HarmonyPatch(typeof(Board), "MouseDownWithPlant")]
public class Board_MouseDownWithPlant_VersusCustomSeed_Patch
{
    private static LevelEntryData flippedLevelData;
    private static ReloadedGameMode savedMode;

    static bool Prefix(Board __instance, int x, int y, int playerIndex)
    {
        flippedLevelData = null;

        var app = __instance.mApp;
        if (app == null || !app.IsVersusMode())
        {
            return true;
        }

        var seedType = __instance.GetSeedTypeInCursor(playerIndex);
        if (!CustomContentRegistry.IsValidCustomPlantType(seedType))
        {
            return true;
        }

        var levelData = app.LevelData;
        if (levelData == null)
        {
            return true;
        }

        // Turning Versus off for the click also turns off native's Versus placement rules - without
        // this, custom plants could be planted behind enemy lines in the zombie zone. Ask native
        // whether the Versus rules allow this cell while the mode is still on, and drop the click if
        // they don't. Every other rejection (occupied cell, water, etc.) is left to native's normal
        // handling once the mode is flipped below.
        int gridX = __instance.PlantingPixelToGridX(x, y, seedType);
        int gridY = __instance.PlantingPixelToGridY(x, y, seedType);
        var reason = __instance.CanPlantAt(gridX, gridY, seedType);
        if (reason == PlantingReason.VSNotHere || reason == PlantingReason.VSSuddenDeath)
        {
            return false;
        }

        savedMode = levelData.m_reloadedGameMode;
        levelData.m_reloadedGameMode = ReloadedGameMode.None;
        flippedLevelData = levelData;
        return true;
    }

    static Exception Finalizer(Exception __exception)
    {
        if (flippedLevelData != null)
        {
            flippedLevelData.m_reloadedGameMode = savedMode;
            flippedLevelData = null;
        }

        return __exception;
    }
}
