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
// off, native takes its ordinary planting path. Vanilla seeds and the zombie player's own clicks
// never match the custom-seed condition, so they are untouched.
[HarmonyPatch(typeof(Board), "MouseDownWithPlant")]
public class Board_MouseDownWithPlant_VersusCustomSeed_Patch
{
    private static LevelEntryData flippedLevelData;
    private static ReloadedGameMode savedMode;

    static void Prefix(Board __instance, int playerIndex)
    {
        flippedLevelData = null;

        var app = __instance.mApp;
        if (app == null || !app.IsVersusMode())
        {
            return;
        }

        if (!CustomContentRegistry.IsValidCustomPlantType(__instance.GetSeedTypeInCursor(playerIndex)))
        {
            return;
        }

        var levelData = app.LevelData;
        if (levelData == null)
        {
            return;
        }

        savedMode = levelData.m_reloadedGameMode;
        levelData.m_reloadedGameMode = ReloadedGameMode.None;
        flippedLevelData = levelData;
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
