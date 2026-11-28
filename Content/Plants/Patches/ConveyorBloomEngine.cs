using System.Runtime.CompilerServices;
using BloomEngine.Config;
using BloomEngine.Config.Inputs.Base;
using BloomEngine.ModMenu;
using MelonLoader;

namespace PvZReCoreLib.Content.Plants.Patches;

// The ONLY file that touches BloomEngine types (the in-game mod menu, an optional dependency). Called from
// ConveyorSettings.Init inside a try/catch, only when the BloomEngine assembly is loaded, and marked
// NoInlining, so nothing here is compiled by the runtime - and BloomEngine never resolved - when it is not
// installed. Same pattern as MoreYetis.
internal static class ConveyorBloomEngine
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static (Func<int> chance, Func<int> zombieShare, Func<int> speed) Register(MelonMod mod, int plantDefault, int zombieDefault, int speedDefault,
        Action<int> plantApplied, Action<int> zombieApplied, Action<int> speedApplied)
    {
        var chance = ConfigService.CreateFloat("Custom plants (%)",
            "Conveyor belt: the percent of drops that are a custom plant (the PvZ2 plants of Complete Collection 2 and other mods'). 0 = never.",
            plantDefault, 0f, 100f);
        var share = ConfigService.CreateFloat("Zombie cards (%)",
            "Conveyor belt: the percent of drops that are a hypnotized zombie card. 0 = never. What is left of the 100% after these two is the level's own plants.",
            zombieDefault, 0f, 100f);

        var speed = ConfigService.CreateFloat("Belt speed (x)",
            "Conveyor belt: how many times faster packets drop. 1 = the game's own pace.",
            speedDefault, 1f, 100f);

        chance.WithOnValueApplied(value => plantApplied((int)Math.Round(value)));
        share.WithOnValueApplied(value => zombieApplied((int)Math.Round(value)));
        speed.WithOnValueApplied(value => speedApplied((int)Math.Round(value)));

        ModMenuService.CreateEntry(mod)
            .AddDisplayName("CoreLib conveyor belt")
            .AddDescription("What conveyor belts (including the Dr. Zomboss fight) drop - custom plants, hypnotized zombie cards or the level's own plants - and how fast.")
            .AddConfigInputs(new BaseConfigInput[] { chance, share, speed })
            .Register();

        return (() => (int)Math.Round(chance.Value), () => (int)Math.Round(share.Value), () => (int)Math.Round(speed.Value));
    }
}
