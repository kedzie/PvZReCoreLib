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
    internal static (Func<int> chance, Func<int> zombieShare) Register(MelonMod mod, int chanceDefault, int shareDefault,
        Action<int> chanceApplied, Action<int> shareApplied)
    {
        var chance = ConfigService.CreateFloat("Custom drop chance (%)",
            "Conveyor belt: the percent of drops that are replaced by a custom plant or zombie card. 0 = never, 100 = every drop.",
            chanceDefault, 0f, 100f);
        var share = ConfigService.CreateFloat("Zombie share (%)",
            "Conveyor belt: of those custom drops, the percent that are hypnotized zombie cards. The rest are custom plants.",
            shareDefault, 0f, 100f);

        chance.WithOnValueApplied(value => chanceApplied((int)Math.Round(value)));
        share.WithOnValueApplied(value => shareApplied((int)Math.Round(value)));

        ModMenuService.CreateEntry(mod)
            .AddDisplayName("CoreLib conveyor belt")
            .AddDescription("How often conveyor belts (including the Dr. Zomboss fight) drop custom plants and hypnotized zombie cards.")
            .AddConfigInputs(new BaseConfigInput[] { chance, share })
            .Register();

        return (() => (int)Math.Round(chance.Value), () => (int)Math.Round(share.Value));
    }
}
