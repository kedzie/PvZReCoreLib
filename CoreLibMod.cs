using MelonLoader;
using PvZReCoreLib;
using PvZReCoreLib.Console;
using PvZReCoreLib.Content;
using PvZReCoreLib.Content.Common.Skins;
using PvZReCoreLib.Content.Plants.Patches;
using PvZReCoreLib.Util;

[assembly: MelonInfo(typeof(CoreLibMod), "CoreLib", "1.6", "Draco9990 & Kedzie")]
[assembly: MelonGame("PopCap Games", "PvZ Replanted")]

namespace PvZReCoreLib;

public class CoreLibMod : MelonMod
{
    #region Variables

    public static string ModId => "ReCodeLib";

    private static bool Initd = false;

    public static Action OnCoreLibInit;

    #endregion

    #region Constructors



    #endregion

    #region Methods

    // CONFIRMED live under MelonLoader 0.7.3: this used to be driven by OnGUI() (checking the
    // active scene every IMGUI callback and calling FirstTimeInit() once "Frontend" was reached).
    // Under 0.7.3, OnGUI() apparently no longer fires reliably for this game/interop combination
    // (Il2CppInterop was bumped to 1.5.1-ci.845 in that release) - FirstTimeInit() never ran at
    // all, silently: CustomContentRegistry.Init() never ran, so its PostInit event never fired, so
    // every mod's own CustomContentRegistry.PostInit += (...) callback (registered fine via the
    // real OnLateInitializeMelon hook) never actually executed - zero custom plants registered,
    // no seed packets, no rendered custom plants, with no exception anywhere since nothing ever
    // threw. CONFIRMED live switching to this fixed that. OnSceneWasLoaded is a real, event-driven
    // MelonMod lifecycle hook for exactly this "run once when scene X loads" need, not a per-frame
    // poll racing against whether OnGUI happens to fire at all. (This also surfaced a second,
    // separate bug - RegistryBridge.LoadAssetFromAssetBundle crashing under 0.7.3's Il2CppInterop
    // - already fixed there; the two are unrelated except that this hook is what exposed it.)
    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        base.OnSceneWasLoaded(buildIndex, sceneName);

        if (!Initd && sceneName == "Frontend")
        {
            FirstTimeInit();
        }
    }

    public override void OnUpdate()
    {
        base.OnUpdate();

        DebugConsole.Update();
        SeedChooserPaging.TickDeferredRewire();
    }

    public void FirstTimeInit()
    {
        Initd = true;
        
        DebugConsole.Start();
        
        PersistentStorage.Init();

        RegistryBridge.Init();

        ReLocalizer.Init();

        SkinRegistry.Init();
        CustomContentRegistry.Init();

        MelonLoader.MelonLogger.Msg($"[CoreLib] Registered {CustomContentRegistry.GetAllCustomPlantTypes().Count} custom plant(s) before Frontend init.");

        try
        {
            AlmanacModel_CustomPlantEntriesPatch.Patch();
        }
        catch (Exception e)
        {
            MelonLoader.MelonLogger.Warning($"[CoreLib] AlmanacModel_CustomPlantEntriesPatch.Patch() failed (likely because AlmanacModel hasn't run yet) - will retry on first Almanac open: {e}");
        }

        RegistryBridge.RegisterAssetBundle(ModId, "Mods/CoreLib/pvzcorelibassetbundle");

        OnCoreLibInit?.Invoke();
    }

    #endregion
}