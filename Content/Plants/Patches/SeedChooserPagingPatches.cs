using HarmonyLib;
using Il2CppReloaded.Gameplay;
using Il2CppSource.DataModels;
using Il2CppUI.Scripts;
using PvZReCoreLib.Util;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PvZReCoreLib.Content.Plants.Patches;

// The seed-chooser grid is a fixed 8-column x 7-or-8-row area (SeedChooserScreen.Has7Rows()) with
// no native scrolling or paging - vanilla plants already come close to filling it, and every
// custom plant on top eventually overflows the visible area entirely (rows exist in mChosenSeeds
// but nothing on screen reaches them). An earlier attempt hand-built a Unity ScrollRect from
// scratch to work around this and was reverted: it ran an unbounded scene-wide search every frame
// long after the seed chooser closed (severe perf regression), and even when it "worked" the
// overflow content was visible but never actually reachable by scrolling into view.
//
// This replaces that with paging instead of scrolling: only one page's worth of custom plants is
// ever present in the reactive models at a time, added/removed directly (see ForceRefresh in
// SeedChooserDataModelPatches.cs) - the same ClearNoDispose + UpdateEntries rebuild already used
// elsewhere in this codebase to get custom plants showing up in the first place, just windowed.
//
// Deliberately does NOT touch ChosenSeedState at all. mSeedState (SeedInChooser/SeedPacketHidden/
// etc.) is an existing, unrelated native mechanism - a plant can be legitimately hidden or grayed
// out for its own reasons (roof-only restrictions, not-yet-unlocked, and others), independent of
// which page it's on, and still needs to render normally (grayed, locked, whatever native
// decides) whenever it IS on the current page. An earlier version of this both overwrote
// mSeedState to force page membership AND used mSeedState as its own removal criterion - which
// stomped a real native SeedPacketHidden (a roof-restricted custom plant on a rooftop level) and
// made it disappear entirely instead of rendering however native code intended. Page membership
// is tracked independently instead (CurrentPageCustomTypes) and is the only thing paging ever
// decides.
//
// Next/previous buttons are cloned from AlmanacArchive's own "arrows" GameObject (found live via
// Resources.FindObjectsOfTypeAll, since the Almanac screen may not be currently instantiated) so
// this doesn't need to hand-build new button art - only AlmanacArchive's own onClick wiring is
// stripped and replaced with page-turn handlers.
public static class SeedChooserPaging
{
    private const int GridColumns = 8;

    // Confirmed empirically (screenshot: 6 full rows of 8, zero remainder for the real 48 vanilla
    // plants) - a fixed page size matching vanilla's own real layout, not derived from
    // Has7Rows() (that was never a capacity signal - see the type comment above). Custom plants
    // always start on their own fresh page boundary after every vanilla page, never sharing a
    // page with vanilla even if some future vanilla roster doesn't divide evenly by this. With
    // this fixed at 48 and only ~8 custom plants registered right now, everything currently fits
    // on a single custom page - multi-page custom navigation only actually matters once the
    // custom roster exceeds 48 (there's a stated intent to eventually port ~180 plants).
    private const int AdventurePageSize = 48;

    // Versus mode's own chooser is a genuinely smaller, fixed 5-row x 8-column = 40 slot grid
    // (VersusMode.k_numRows = 5, confirmed via screenshot). Other special-level variants (e.g. a
    // secret level's own seed chooser) default to AdventurePageSize until proven otherwise -
    // no worse than the total absence of paging they had before this fix.
    private const int VersusPageSize = 40;

    // Set by SeedChooserScreen_InjectPagingButtons_Patch once it identifies which wrapper the
    // found panel actually belongs to (by name, "Vs"/"Versus" appearing in it) - selects which
    // fixed page size above applies.
    public static bool IsVersusScreen { get; set; }

    private static int PageSize => IsVersusScreen ? VersusPageSize : AdventurePageSize;

    private static int currentPage = 0;
    private static int lastPageCount = 1;

    // CONFIRMED live (via NavDiag) that a fixed short frame delay is not enough: 5 frames after
    // paging onto the 9-cell custom page, all 9 incoming cells still showed 0 populated icons (raw
    // dump identified every one of them only by its template GameObject name, "SPR_SelectedItem",
    // meaning the reactive binder that assigns each cell's real icon Image sprite hadn't run on any
    // of them yet). Whatever async delay that binder actually has isn't a small, fixed number of
    // frames - polling every frame until the active/populated cell count is non-zero AND stable
    // (same reading twice in a row) is the only way to know it's actually safe to compute links,
    // without hardcoding a guess that could just as easily be wrong again on a slower machine.
    private static bool pendingRewirePolling = false;
    private static int pendingRewirePollTicksLeft = 0;
    private static int pendingRewireLastCount = -1;

    // ~3 seconds at 60fps - generous relative to how long Imitater's dialog open/close (which
    // reliably masks this bug) takes a real player to do, since that's the only known lower bound
    // on how long this async populate can actually take.
    private const int PendingRewireTimeoutTicks = 60;

    private static void BeginPendingRewirePoll()
    {
        pendingRewirePolling = true;
        pendingRewirePollTicksLeft = PendingRewireTimeoutTicks;
        pendingRewireLastCount = -1;
    }

    // Called from CoreLibMod.OnUpdate() every frame, unconditionally (cheap no-op when nothing is
    // pending).
    public static void TickDeferredRewire()
    {
        if (!pendingRewirePolling)
        {
            return;
        }

        int count = RewireVisibleGridNavigation();

        if (count > 0 && count == pendingRewireLastCount)
        {
            MelonLoader.MelonLogger.Msg($"[CoreLib][NavDiag] Deferred rewire poll settled at {count} active cells - stopping.");
            pendingRewirePolling = false;
            return;
        }

        pendingRewireLastCount = count;
        pendingRewirePollTicksLeft--;
        if (pendingRewirePollTicksLeft <= 0)
        {
            MelonLoader.MelonLogger.Warning($"[CoreLib][NavDiag] Deferred rewire poll timed out after {PendingRewireTimeoutTicks} frames without a stable non-zero count (last count={count}) - giving up for this page turn.");
            pendingRewirePolling = false;
        }
    }

    // Set by SeedChooserScreen_InjectPagingButtons_Patch right after it confirms a non-empty,
    // sane Grid (its own search already prefers the smallest candidate when more than one
    // P_SeedChooser exists - see its comment). Reusing this one verified reference instead of
    // re-searching the scene fresh every call avoids redoing that same search repeatedly.
    public static Transform CachedGrid { get; set; }

    // How many of mChosenSeeds' leading entries are native's own real plants (captured right
    // before SeedChooserScreen_CustomSeedsPatch's injection loop appends anything) - used only to
    // tell a real vanilla entry apart from the unrelated reserved/minigame-ID range (SeedTypes
    // 54-81, Beghouled/Slot Machine buttons then zombie cursor pseudo-types) that same injection
    // loop also fills in as hidden placeholder slots on its way up to the highest registered
    // custom SeedType. Neither camp is "custom," so IsValidCustomPlantType alone can't
    // distinguish them.
    private static int VanillaCount = -1;

    // One flat, ordered list of every SeedType that actually occupies a real page slot - real
    // vanilla plants followed by real custom plants, in mChosenSeeds' own order (which is
    // ascending by SeedType per the position==value invariant elsewhere in this codebase). Simple
    // count-based paging falls out of this directly: whichever PageSize-sized slice of this list
    // an entry's index lands in IS its page, for vanilla and custom alike - no separate "vanilla
    // page" vs "custom page" concept needed. Rebuilt by ApplyPaging every screen-open and page
    // turn.
    private static readonly List<SeedType> orderedTypes = new();
    private static readonly Dictionary<SeedType, int> pageIndexByType = new();

    // Called once per screen-open from SeedChooserScreen_CustomSeedsPatch, right after it finishes
    // appending every custom SeedType (valid or not) to mChosenSeeds - vanillaCount is the list
    // length *before* that loop ran, i.e. how many on-screen slots the base game's own plants
    // occupy.
    public static void OnScreenOpened(int vanillaCount)
    {
        VanillaCount = vanillaCount;
        currentPage = 0;
    }

    // The single source of truth ForceRefresh prunes the reactive model against - true means
    // "keep this entry in the model." A SeedType not in pageIndexByType at all (the reserved
    // minigame-ID gap range) is never visible on any page - except Imitater, which paging
    // excludes from pageIndexByType entirely (see ApplyPaging) but must still never be pruned,
    // since it isn't a page-consuming grid entry at all and needs to render exactly as native
    // always has, on every page.
    public static bool ShouldBeVisible(SeedType seedType)
    {
        if (seedType == SeedType.Imitater)
        {
            return true;
        }

        return pageIndexByType.TryGetValue(seedType, out int page) && page == currentPage;
    }

    // CONFIRMED live (via NavDiag) that gating navigation-readiness on whether each cell's icon
    // Image has visually loaded was the wrong signal entirely: a custom plant's icon can take
    // several seconds (or apparently never, within any bounded wait) to actually populate -
    // completely unrelated to whether the cell is structurally correct for the current page. But
    // grid.childCount itself settles to the exact right count (e.g. 9 for a 9-entry custom page)
    // almost immediately after a page turn - a purely structural signal, independent of asset
    // load timing. This is that expected count, computed the same way ApplyPaging assigns pages.
    public static int GetExpectedCurrentPageCount()
    {
        int count = 0;
        foreach (var type in orderedTypes)
        {
            if (pageIndexByType.TryGetValue(type, out int page) && page == currentPage)
            {
                count++;
            }
        }

        return count;
    }

    // Recomputes mX/mY (grid position) for whichever entries land past page 0 - never mSeedState,
    // see the type-level comment above for why.
    public static void ApplyPaging(SeedChooserScreen screen)
    {
        if (VanillaCount < 0)
        {
            return;
        }

        orderedTypes.Clear();
        pageIndexByType.Clear();

        foreach (var cs in screen.mChosenSeeds)
        {
            // Imitater sits inside the "vanilla" SeedType range (48, right after the 48 real
            // plants - confirmed live VanillaCount is 49, counting it) but isn't a real grid
            // entry at all: it renders in its own dedicated slot outside the Grid hierarchy
            // entirely, using this same ChosenSeed's mX/mY for something other than a page
            // position. Treating it as page 49's overflow moved its coordinates onto Peashooter's
            // real grid slot (confirmed live: the Imitater slot started showing Peashooter's
            // tooltip/icon) - excluded here so paging never touches it at all.
            if (cs.mSeedType == SeedType.Imitater)
            {
                continue;
            }

            bool isRealVanilla = (int)cs.mSeedType < VanillaCount;
            bool isRealCustom = CustomContentRegistry.IsValidCustomPlantType(cs.mSeedType);
            if (!isRealVanilla && !isRealCustom)
            {
                continue; // reserved minigame-ID gap slot - doesn't consume a page slot at all
            }

            orderedTypes.Add(cs.mSeedType);
        }

        lastPageCount = Math.Max(1, (orderedTypes.Count + PageSize - 1) / PageSize);
        currentPage = Math.Clamp(currentPage, 0, lastPageCount - 1);

        for (int i = 0; i < orderedTypes.Count; i++)
        {
            pageIndexByType[orderedTypes[i]] = i / PageSize;
        }

        // Only entries past page 0 ever need repositioning - native already laid the first
        // PageSize out correctly on its own (that's the single page it always assumed existed).
        // Everything from PageSize onward (custom plants, or a 49th+ "vanilla" entry like the
        // Imitater slot if VanillaCount ever exceeds PageSize) restarts at row 0/column 0 of
        // whichever fresh page it lands on.
        foreach (var cs in screen.mChosenSeeds)
        {
            int combinedIndex = orderedTypes.IndexOf(cs.mSeedType);
            if (combinedIndex < PageSize)
            {
                continue;
            }

            int indexOnPage = combinedIndex % PageSize;
            int column = indexOnPage % GridColumns;
            int row = indexOnPage / GridColumns;

            cs.mX = column * 0x35 + 0x16;
            cs.mY = screen.Has7Rows() ? row * 0x46 + 0x7b : row * 0x49 + 0x80;
            cs.mStartX = cs.mX;
            cs.mStartY = cs.mY;
            cs.mEndX = cs.mX;
            cs.mEndY = cs.mY;
        }

        MelonLoader.MelonLogger.Msg($"[CoreLib] Seed chooser paging: page {currentPage + 1}/{lastPageCount}, {orderedTypes.Count} total plant(s) across all pages.");
    }

    public static void GoToNextPage(SeedChooserDataModel dataModel, SeedChooserScreen screen)
    {
        MelonLoader.MelonLogger.Msg($"[CoreLib] GoToNextPage called: currentPage={currentPage}, lastPageCount={lastPageCount}.");

        if (currentPage + 1 >= lastPageCount)
        {
            return;
        }

        currentPage++;
        ApplyPaging(screen);
        // REVERTED: tried routing page turns through the full native rebuild (ForceRefresh)
        // instead of this incremental path, on the theory that ApplyPageVisibility's Add/
        // RemoveModel reuse skips the proper re-bind (GameObject.SetActive + icon rebind) a fresh
        // ListBinder.CreateEntry would do for a re-added cached entry. Confirmed live: this
        // actually FROZE the game on page turn - worse than the original ~3-8s stall this
        // optimization was built to fix in the first place (more custom plants registered now
        // than when that number was measured). Full rebuild is not viable; the real fix needs to
        // target the incremental path specifically, not bypass it.
        SeedChooserDataModel_CustomPlantEntriesPatch.ApplyPageVisibility(dataModel);

        // REVERTED: tried toggling the grid's own active state off/on to replicate whatever
        // side effect opening/closing the Imitater dialog has (confirmed live that masks the
        // duplicate-cell corruption, with no lag and no native UpdateEntries() call - so not a
        // rebuild). Confirmed live this is WORSE: deactivating the grid nulls Unity's
        // EventSystem.currentSelectedGameObject the instant it happens (since a controller-
        // focused cell lives inside it), and nothing re-selects a new one on reactivation -
        // total controller lock-out requiring a mouse click to regain focus, the same "collapsed
        // to null" failure mode as the original bug this file's history already documents.
        // Whatever Imitater's dialog does differently, it isn't a bare SetActive toggle on the
        // grid itself.
        RewireVisibleGridNavigation();
        BeginPendingRewirePoll();
    }

    public static void GoToPreviousPage(SeedChooserDataModel dataModel, SeedChooserScreen screen)
    {
        MelonLoader.MelonLogger.Msg($"[CoreLib] GoToPreviousPage called: currentPage={currentPage}, lastPageCount={lastPageCount}.");

        if (currentPage <= 0)
        {
            return;
        }

        currentPage--;
        ApplyPaging(screen);
        // REVERTED: tried routing page turns through the full native rebuild (ForceRefresh)
        // instead of this incremental path, on the theory that ApplyPageVisibility's Add/
        // RemoveModel reuse skips the proper re-bind (GameObject.SetActive + icon rebind) a fresh
        // ListBinder.CreateEntry would do for a re-added cached entry. Confirmed live: this
        // actually FROZE the game on page turn - worse than the original ~3-8s stall this
        // optimization was built to fix in the first place (more custom plants registered now
        // than when that number was measured). Full rebuild is not viable; the real fix needs to
        // target the incremental path specifically, not bypass it.
        SeedChooserDataModel_CustomPlantEntriesPatch.ApplyPageVisibility(dataModel);

        // REVERTED: tried toggling the grid's own active state off/on to replicate whatever
        // side effect opening/closing the Imitater dialog has (confirmed live that masks the
        // duplicate-cell corruption, with no lag and no native UpdateEntries() call - so not a
        // rebuild). Confirmed live this is WORSE: deactivating the grid nulls Unity's
        // EventSystem.currentSelectedGameObject the instant it happens (since a controller-
        // focused cell lives inside it), and nothing re-selects a new one on reactivation -
        // total controller lock-out requiring a mouse click to regain focus, the same "collapsed
        // to null" failure mode as the original bug this file's history already documents.
        // Whatever Imitater's dialog does differently, it isn't a bare SetActive toggle on the
        // grid itself.
        RewireVisibleGridNavigation();
        BeginPendingRewirePoll();
    }

    // Confirmed live (via an on-demand nav-dump diagnostic) the actual root cause of a grid
    // keyboard/controller navigation freeze: native's own GridNavigationContainer reconfigures a
    // resized grid's up/down reasonably, but its left/right sibling-to-sibling chain doesn't get
    // rebuilt - cells kept pointing at the SAME 48-cell-page siblings even after we pruned down to
    // an 8-cell custom page, so the moment navigation tried to follow one of those now-destroyed
    // references, Unity's EventSystem selection collapsed to null - and once null, there's no
    // "from" point left for ANY further keyboard/controller input to navigate from, which is why
    // this reads as a total, permanent-for-the-session freeze rather than an occasional misstep.
    //
    // Since it's OUR OWN paging that resizes the grid, we own fixing the chain afterward rather
    // than trusting native to notice - re-linking every CURRENTLY VISIBLE cell to its real
    // same-page row/column neighbors. Deliberately only overwrites a direction when a same-page
    // neighbor actually exists in that direction (see SetHorizontalNav's same pattern above) -
    // boundary cells (first/last row, first/last column) keep whatever native already wired for
    // them (e.g. the bottom row's "down" into the Let's Rock button), since those were never the
    // stale/broken links in the first place.
    // Returns the number of active cells actually found and rewired - 0 means "grid not settled
    // yet" (either not found at all, or grid.childCount doesn't match the current page's expected
    // count yet), so callers (TickDeferredRewire's poll loop) know to retry rather than trust a
    // transient/incomplete result.
    public static int RewireVisibleGridNavigation()
    {
        // Prefer the cached reference (see CachedGrid's own comment) - only fall back to a fresh
        // search if nothing's been cached yet (e.g. this runs before
        // SeedChooserScreen_InjectPagingButtons_Patch ever
        // successfully found one this session).
        Transform grid = CachedGrid;
        if (grid == null)
        {
            // Same "prefer the smallest sane non-empty Grid across every P_SeedChooser
            // candidate" logic as SeedChooserScreen_InjectPagingButtons_Patch - see its own
            // comment for why taking the first match isn't safe (stale, orphan-accumulated
            // copies from earlier restarts can coexist in the scene at once).
            var activeScene = SceneManager.GetActiveScene();
            foreach (var root in activeScene.GetRootGameObjects())
            {
                foreach (var candidateRoot in UnityUtil.FindDeepChildrenByName(root.transform, "P_SeedChooser"))
                {
                    var candidateGrid = candidateRoot.transform.FindChild("Canvas/Layout/Center/Panel/SeedChooser/Grid");
                    if (candidateGrid == null || candidateGrid.childCount == 0)
                    {
                        continue;
                    }

                    if (grid == null || candidateGrid.childCount < grid.childCount)
                    {
                        grid = candidateGrid;
                    }
                }
            }
        }

        if (grid == null || grid.childCount == 0)
        {
            // Not ready yet (or genuinely not found) - safe to skip, a later page-turn or the
            // next UpdateEntries() call will call this again.
            return 0;
        }

        // CONFIRMED live (via NavDiag) this is the right readiness gate, not icon population: a
        // page turn briefly leaves BOTH the old page's cells (not yet destroyed) and the new
        // page's cells (already instantiated) in the grid at once, inflating childCount past the
        // real page size for a frame or two - but it settles to EXACTLY the expected count almost
        // immediately, regardless of how long those new cells' icons take to visually populate
        // (confirmed live: icons can still be completely unpopulated even 3+ seconds later, on the
        // very first time a page is ever shown this session - not a caching/reuse issue at all).
        // Waiting for childCount to match, rather than for icons to load, ties navigation
        // correctness to a fast structural fact instead of a slow (or seemingly never-completing)
        // cosmetic one.
        int expectedCount = GetExpectedCurrentPageCount();
        if (grid.childCount != expectedCount)
        {
            MelonLoader.MelonLogger.Msg($"[CoreLib][NavDiag] RewireVisibleGridNavigation: grid.childCount={grid.childCount} does not yet match expected page count={expectedCount} - not settled, skipping.");
            return 0;
        }

        // Identifies a cell by its bound icon sprite's name (falling back to the raw GameObject
        // name if no populated icon is found) plus GetInstanceID() - used only by the nav-link
        // summary logged at the end of this method, to make that log human-readable.
        string IdentifyCell(Transform t)
        {
            var img = t.GetComponentInChildren<Image>();
            string plantName = img != null && img.sprite != null ? img.sprite.name : t.name;
            return $"{plantName}#{t.gameObject.GetInstanceID()}";
        }

        // Now that grid.childCount is confirmed to match the expected page count exactly (see the
        // gate above), every child IS a genuine current-page cell - no icon-population check
        // needed anymore to tell a real cell apart from a stale leftover, since stale leftovers
        // can no longer be present once childCount itself has settled. Still filtering on
        // activeInHierarchy + having a Selectable as cheap insurance against something unrelated
        // slipping an inactive or non-interactive child in here.
        var activeSelectables = new List<Selectable>();
        for (int i = 0; i < grid.childCount; i++)
        {
            var child = grid.GetChild(i);
            if (!child.gameObject.activeInHierarchy)
            {
                continue;
            }

            var selectable = child.GetComponentInChildren<Selectable>();
            if (selectable == null)
            {
                continue;
            }

            activeSelectables.Add(selectable);
        }

        var selectables = activeSelectables.ToArray();
        int count = selectables.Length;

        for (int i = 0; i < count; i++)
        {
            var selectable = selectables[i];
            if (selectable == null)
            {
                continue;
            }

            int column = i % GridColumns;

            Selectable up = i >= GridColumns ? selectables[i - GridColumns] : null;
            Selectable down = (i + GridColumns) < count ? selectables[i + GridColumns] : null;
            Selectable left = column > 0 ? selectables[i - 1] : null;
            Selectable right = (column < GridColumns - 1 && i + 1 < count) ? selectables[i + 1] : null;

            var nav = selectable.navigation;
            nav.mode = Navigation.Mode.Explicit;
            if (up != null) nav.selectOnUp = up;
            if (down != null) nav.selectOnDown = down;
            if (left != null) nav.selectOnLeft = left;
            if (right != null) nav.selectOnRight = right;
            selectable.navigation = nav;
        }

        // Kept as an ongoing (not temporary) sanity-check log: each active cell's sibling index,
        // its on-screen RectTransform position, and the four links just computed for it - cheap
        // once-per-settled-page-turn now that the childCount gate above prevents this from ever
        // running against a not-yet-settled grid.
        var dumpLines = new List<string>();
        for (int i = 0; i < count; i++)
        {
            var s = selectables[i];
            if (s == null)
            {
                continue;
            }

            // GetComponent<RectTransform>, not a direct cast - confirmed live this throws
            // InvalidCastException for at least one selectable whose own transform isn't a
            // RectTransform (an uncaught exception here silently aborted this entire diagnostic
            // every time, which is why nothing ever printed despite the bug reproducing).
            var rt = s.GetComponent<RectTransform>();
            string posStr = rt != null ? $"({rt.anchoredPosition.x:F0},{rt.anchoredPosition.y:F0})" : "(no RectTransform)";
            var nav = s.navigation;
            string U = nav.selectOnUp != null ? IdentifyCell(nav.selectOnUp.transform) : "-";
            string D = nav.selectOnDown != null ? IdentifyCell(nav.selectOnDown.transform) : "-";
            string L = nav.selectOnLeft != null ? IdentifyCell(nav.selectOnLeft.transform) : "-";
            string R = nav.selectOnRight != null ? IdentifyCell(nav.selectOnRight.transform) : "-";
            dumpLines.Add($"[{i}] {IdentifyCell(s.transform)} pos={posStr} U={U} D={D} L={L} R={R}");
        }
        MelonLoader.MelonLogger.Msg($"[CoreLib][NavDiag] RewireVisibleGridNavigation: {count} active cells (grid.childCount={grid.childCount}):\n" + string.Join("\n", dumpLines));
        return count;
    }
}

// Separate marker class from PatchMarker<SeedChooserScreen> (used by
// SeedChooserScreen_CustomSeedsPatch) - GetOrCreateExtension looks up its single stored instance
// purely by generic type per target object, so sharing the same PatchMarker<SeedChooserScreen>
// type here would silently read/write the *other* patch's IsPatched flag instead of this one's.
public class SeedChooserPagingButtonsMarker : ClassExtension<SeedChooserScreen>
{
    public bool IsPatched;
    public int Attempts;

    // Separate, much smaller budget for the "AlmanacArchive+arrows were found fine, but the
    // P_SeedChooser scene search keeps failing" failure mode specifically - see its own comment
    // at the check site for why this needed splitting out from Attempts/MaxAttempts.
    public int PanelSearchFailures;
}

[HarmonyPatch(typeof(SeedChooserScreen), nameof(SeedChooserScreen.Update))]
public class SeedChooserScreen_InjectPagingButtons_Patch
{
    // AccessTools.Field(typeof(AlmanacArchive), "arrows") returns null in practice - confirmed
    // live that the IL2CPP interop assembly this mod actually links against only exposes an
    // opaque NativeFieldInfoPtr_arrows bookkeeping entry for this private, un-accessed
    // [SerializeField] field, not a readable value or a generated property. That's an IL2CPP
    // interop generation gap, not a wrong field name - normal .NET reflection can't reach a
    // private native field with no accessor at all. Finding the GameObject by name in the
    // hierarchy instead (AlmanacArchive genuinely is a MonoBehaviour, unlike SeedChooserScreen,
    // so it has a real Transform to search from).
    private static readonly string[] ArrowsCandidateNames = { "Arrows", "arrows", "ArrowButtons", "NavigationArrows" };

    // SeedChooserScreen isn't a Component itself (Widget/WidgetContainer, its base classes, hold
    // no Transform/GameObject) - reaching its actual visible UI needs the same scene-root search
    // the earlier scrollbar attempt used (UnityUtil.FindDeepChildrenByName is a recursive
    // descendant search, run per root GameObject in the scene - real work, not free). That
    // attempt's actual failure was letting this retry completely unbounded, every single Update()
    // call, forever, including deep into gameplay long after the chooser closed.
    //
    // Confirmed live that neither "haven't succeeded yet" nor "CachedDataModel != null" are safe
    // gates for that: SeedChooserScreen.Update() keeps firing during totally unrelated TreeState
    // transitions (SpeechBubble/PlayGame) on the way into a level, AND CachedDataModel is a
    // static field set once on the very first seed-chooser open of the whole session and never
    // cleared - so once true, it's true forever, including during plain gameplay with no chooser
    // anywhere on screen. An attempt counter gated behind either one just silently burns its
    // entire budget in the background before the chooser is ever actually visible again.
    //
    // ReplantedGet.TreeStateManager().Active.name is the actual "what screen is this" signal
    // already used elsewhere in this codebase (see Plant_PlantInitialize_Patch's "AmanacPlants"
    // check) - a plain string compare against a cached reference, cheap enough to run
    // unconditionally every Update() call for the whole session. Gating on it first means the
    // expensive scene search below only ever runs while the seed chooser is genuinely the active
    // screen, not "at some point after it first became true."
    private const int MaxAttempts = 1200;

    // Confirmed live that the seed chooser is reachable through more than one named TreeState
    // depending on game mode/entry point - "SeedChooser" is the transient name passed to
    // TransitionTo, "ChooseSeeds" is the settled name most contexts actually land on (confirmed
    // working across minigames and adventure mode). More may need adding if another mode uses a
    // third name.
    private static readonly string[] SeedChooserStateNames = { "SeedChooser", "ChooseSeeds" };

    // DIAGNOSTIC (temporary): a versus-mode player reported the seed chooser feeling laggy,
    // something never actually tested there before. Leading suspect is this exact method - while
    // unpatched it runs Resources.FindObjectsOfTypeAll<AlmanacArchive>() (a real scene-wide scan)
    // on every single Update() call, and it can only ever succeed once an AlmanacArchive has been
    // instantiated somewhere THIS SESSION. Every adventure-mode test so far happened to already
    // have the Almanac loaded, so this always succeeded on attempt 1 - a versus-only player who
    // never opens the Almanac first would instead pay that scan's real cost every frame for up to
    // MaxAttempts (1200) frames before giving up. These logs are meant to confirm or rule that out
    // directly - remove once the real cause is confirmed.
    private static float firstAttemptRealtime = -1f;

    public static void Postfix(SeedChooserScreen __instance)
    {
        var patchMarker = SeedChooserPagingButtonsMarker.GetOrCreateExtension<SeedChooserPagingButtonsMarker>(__instance);
        if (patchMarker.IsPatched)
        {
            return;
        }

        var activeState = ReplantedGet.TreeStateManager()?.Active;

        if (activeState == null || Array.IndexOf(SeedChooserStateNames, activeState.name) < 0)
        {
            // Not actually on the seed chooser screen right now - don't spend anything, and
            // don't count this against the attempt budget below.
            return;
        }

        var dataModel = SeedChooserDataModel_CustomPlantEntriesPatch.CachedDataModel;
        if (dataModel == null)
        {
            // Same "runs before the data we need exists" timing issue the custom-entry
            // injection already has to work around - keep retrying (uncounted) until the
            // cached data model shows up. Bounded regardless now, since we only reach here
            // while genuinely on the seed chooser screen.
            return;
        }

        if (patchMarker.Attempts == 0)
        {
            firstAttemptRealtime = Time.realtimeSinceStartup;
            MelonLoader.MelonLogger.Msg($"[CoreLib][SeedChooserDiag] First paging-button attempt this screen-open. TreeState='{activeState.name}'.");
        }

        patchMarker.Attempts++;
        if (patchMarker.Attempts > MaxAttempts)
        {
            MelonLoader.MelonLogger.Warning($"[CoreLib] Gave up looking for the seed chooser grid / an AlmanacArchive instance after {MaxAttempts} attempts ({Time.realtimeSinceStartup - firstAttemptRealtime:F1}s of real time) while on the seed chooser screen - paging buttons will not be added this session.");
            patchMarker.IsPatched = true;
            return;
        }

        var almanacSearchSw = System.Diagnostics.Stopwatch.StartNew();
        AlmanacArchive archiveTemplate = null;
        foreach (var candidate in Resources.FindObjectsOfTypeAll<AlmanacArchive>())
        {
            archiveTemplate = candidate;
            break;
        }
        almanacSearchSw.Stop();

        if (archiveTemplate == null)
        {
            // Logged every ~30 attempts (roughly twice a second at 60fps) rather than every
            // single frame, to avoid flooding the log while still giving enough time resolution
            // to see how long this repeats for.
            if (patchMarker.Attempts % 30 == 0 || patchMarker.Attempts <= 3)
            {
                MelonLoader.MelonLogger.Msg($"[CoreLib][SeedChooserDiag] Attempt {patchMarker.Attempts}/{MaxAttempts}: no AlmanacArchive instance found yet (this scan took {almanacSearchSw.Elapsed.TotalMilliseconds:F2}ms, {Time.realtimeSinceStartup - firstAttemptRealtime:F1}s elapsed since first attempt).");
            }

            // The Almanac screen's prefab may genuinely not be loaded yet this session (e.g.
            // player hasn't opened it) - keep retrying (bounded) rather than giving up on the
            // first miss.
            return;
        }

        if (patchMarker.Attempts == 1 || patchMarker.PanelSearchFailures % 30 == 0)
        {
            // Throttled the same way the "not found" branch above is - this fires every attempt
            // while the seed chooser's own UI is still mid-build (normal, brief, in the working
            // case) or every attempt for up to 60 frames in the versus-mode incompatible case
            // below, so logging it unconditionally would be noisy for both.
            MelonLoader.MelonLogger.Msg($"[CoreLib][SeedChooserDiag] AlmanacArchive found on attempt {patchMarker.Attempts} ({Time.realtimeSinceStartup - firstAttemptRealtime:F1}s since first attempt, this scan took {almanacSearchSw.Elapsed.TotalMilliseconds:F2}ms).");
        }

        GameObject arrowsSource = null;
        foreach (var candidateName in ArrowsCandidateNames)
        {
            var found = UnityUtil.FindDeepChildrenByName(archiveTemplate.transform, candidateName);
            if (found.Count > 0)
            {
                arrowsSource = found[0].gameObject;
                break;
            }
        }

        if (arrowsSource == null)
        {
            // None of the guessed names matched - dump the real hierarchy under the found
            // AlmanacArchive so the actual child name can be read from the log instead of
            // guessed again (e.g. if a future game update renames this asset).
            var childNames = new List<string>();
            void DumpChildren(Transform t, int depth)
            {
                // Index-based, not foreach - foreach (Transform child in t) throws
                // InvalidCastException live under IL2Cpp interop (confirmed the hard way
                // elsewhere in this file - see the Buttons-bar diagnostic below).
                for (int i = 0; i < t.childCount; i++)
                {
                    var child = t.GetChild(i);
                    childNames.Add(new string(' ', depth * 2) + child.name);
                    DumpChildren(child, depth + 1);
                }
            }
            DumpChildren(archiveTemplate.transform, 0);
            MelonLoader.MelonLogger.Warning("[CoreLib] Could not find an Arrows-like child under AlmanacArchive - seed chooser paging buttons will not be added. Actual hierarchy:\n" + string.Join("\n", childNames));
            patchMarker.IsPatched = true;
            return;
        }

        // Locate the seed chooser's own panel Transform to parent the cloned buttons under.
        //
        // Generic search instead of a single hardcoded wrapper name ("P_SeedChooser") - confirmed
        // live that at least two OTHER variants exist with their own differently-named wrapper
        // (versus mode's "P_VsSeedChooser", and a secret level's own variant), each hitting this
        // exact "never found" failure. Rather than keep hardcoding one more literal name every
        // time a new special level turns up, this searches for ANY descendant literally named
        // "SeedChooser" (the one part that's stayed consistent across every variant found so far)
        // with a real "Grid" child beneath it, then derives the outer wrapper by walking up the
        // fixed relative structure every variant shares (SeedChooser -> Panel -> Center/center ->
        // Layout -> Canvas -> wrapper root) instead of needing to know or match the wrapper's own
        // name at all. This also makes path-casing differences ("Center" vs "center") irrelevant
        // for free, since nothing here depends on the exact intermediate names either.
        //
        // Multiple candidates can in principle coexist in the scene at once (e.g. a stale copy
        // left behind by an earlier restart, or - confirmed live to be the ACTUAL cause of a real
        // regression - a previously-visited different screen's own panel, deactivated but still
        // holding onto its old populated Grid children). Filtering to activeInHierarchy first is
        // what actually distinguishes a genuine stale leftover from the real current screen; the
        // "prefer smallest Grid" tie-break alone isn't enough; it doesn't care about activity state
        // and let a smaller stale grid outrank the real active one once already.
        Transform seedChooserPanel = null;
        Transform seedChooserRoot = null;
        Transform bestGrid = null;
        bool isVersusScreen = false;
        var activeScene = SceneManager.GetActiveScene();
        foreach (var root in activeScene.GetRootGameObjects())
        {
            foreach (var candidatePanelObject in UnityUtil.FindDeepChildrenByName(root.transform, "SeedChooser"))
            {
                var panelTransform = candidatePanelObject.transform;
                if (!panelTransform.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var grid = panelTransform.FindChild("Grid");
                if (grid == null || grid.childCount == 0)
                {
                    continue;
                }

                if (bestGrid == null || grid.childCount < bestGrid.childCount)
                {
                    bestGrid = grid;
                    seedChooserPanel = panelTransform;

                    var candidateRoot = panelTransform;
                    for (int hop = 0; hop < 5 && candidateRoot.parent != null; hop++)
                    {
                        candidateRoot = candidateRoot.parent;
                    }
                    seedChooserRoot = candidateRoot;
                    isVersusScreen = seedChooserRoot.name.IndexOf("Vs", StringComparison.OrdinalIgnoreCase) >= 0
                        || seedChooserRoot.name.IndexOf("Versus", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
        }

        if (seedChooserPanel == null)
        {
            // Confirmed live (versus mode): archiveTemplate and arrowsSource are found
            // instantly and reliably every single frame, but this scene-wide "P_SeedChooser"
            // search NEVER succeeds - versus mode's "Choose Your Plants!"/"Pick Ur Zombeez"
            // screen is an entirely different Widget/hierarchy from the adventure/co-op seed
            // chooser this whole method was written against (confirmed via screenshot: 5 rows
            // of 8, no Almanac/Shop buttons at all). Riding the outer Attempts/MaxAttempts=1200
            // budget for this specific failure meant retrying the full expensive scene-root
            // recursive search on EVERY frame for 42.8 real seconds before giving up - exactly
            // the "totally unresponsive" versus-mode freeze that got reported. Once
            // archiveTemplate/arrowsSource both already succeeded, the ONLY legitimate reason
            // left to keep retrying is "the seed chooser's own UI hasn't finished building yet"
            // - which resolves within the first handful of frames in every case that's ever
            // actually worked. A much smaller dedicated cap here means an incompatible screen
            // like versus's gives up in ~1 second instead of ~43.
            patchMarker.PanelSearchFailures++;
            const int maxPanelSearchFailures = 60;
            if (patchMarker.PanelSearchFailures < maxPanelSearchFailures)
            {
                return;
            }

            var rootNames = new List<string>();
            foreach (var root in activeScene.GetRootGameObjects())
            {
                rootNames.Add(root.name);
            }

            MelonLoader.MelonLogger.Warning($"[CoreLib] Gave up looking for any 'SeedChooser'-named hierarchy with a populated Grid after {maxPanelSearchFailures} frames ({Time.realtimeSinceStartup - firstAttemptRealtime:F1}s since first attempt) - this screen's own layout may be fundamentally different (no such object exists at all, not just under a different name). Paging buttons will not be added this session. Scene root objects: " + string.Join(", ", rootNames));
            patchMarker.IsPatched = true;
            return;
        }

        SeedChooserPaging.CachedGrid = bestGrid;

        // IsVersusScreen can only be known once this search actually succeeds and identifies
        // which wrapper the found panel belongs to - but ApplyPaging already ran once before
        // that, on screen-open, using whatever IsVersusScreen was left over from the PREVIOUS
        // screen. Recomputing now that it's actually correct fixes the page boundaries for the
        // very first real page, rather than leaving them wrong until the next page turn happens
        // to trigger a refresh.
        if (SeedChooserPaging.IsVersusScreen != isVersusScreen)
        {
            SeedChooserPaging.IsVersusScreen = isVersusScreen;
            SeedChooserPaging.ApplyPaging(__instance);
            if (dataModel != null)
            {
                SeedChooserDataModel_CustomPlantEntriesPatch.ApplyPageVisibility(dataModel);
            }
        }

        patchMarker.IsPatched = true;

        // Parented under seedChooserPanel itself - reaching across to the ViewStore/ViewAlmanac
        // row (a different, sibling UI tree entirely) was abandoned after live screen-space
        // measurements (RectTransformUtility.WorldToScreenPoint) confirmed correct relative
        // positioning on paper while the actual on-screen result stayed wrong regardless - strong
        // evidence something about that cross-hierarchy comparison doesn't hold here (possibly yet
        // another IL2Cpp interop gap, matching several other APIs this session that silently
        // returned plausible-looking but wrong values). Anchoring purely within seedChooserPanel,
        // the same parent Imitator itself lives in and renders correctly, avoids that class of
        // problem entirely - no cross-hierarchy reads involved at all.
        var arrowsClone = UnityEngine.Object.Instantiate(arrowsSource, seedChooserPanel);
        arrowsClone.name = "CoreLib_SeedChooserPagingArrows";
        arrowsClone.SetActive(true);

        var buttons = arrowsClone.GetComponentsInChildren<Button>(true);

        // Confirmed live: sibling order 0/1 matches left/right position and previous/next handler
        // assignment correctly - only the icons themselves needed fixing (see the localScale flip
        // below), not this mapping.
        for (int i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];

            // RemoveAllListeners() only clears RUNTIME listeners, not PERSISTENT ones (Unity's own
            // documented behavior) - the clone still carries AlmanacArchive's serialized
            // nextEntry()/previousEntry() persistent listener pointing at the ORIGINAL Archive
            // instance in the wrong context. If that throws when invoked here, it can abort
            // UnityEvent's invocation list before our own listener (added after it) ever runs -
            // the likely reason clicks produced zero log output despite many attempts. Replacing
            // the whole event object discards persistent listeners too, since there's no public
            // runtime API to remove them individually outside the Editor.
            button.onClick = new Button.ButtonClickedEvent();

            // Confirmed live: handlers/positions are correct (left button already fires
            // GoToPreviousPage, right fires GoToNextPage) - only the icon each one displays reads
            // backwards (left looks like a "next"/right-pointing arrow and vice versa). Mirroring
            // each button's own rendered content horizontally swaps its apparent direction without
            // touching which handler is attached to which position - sprite-agnostic, so it works
            // whether the two icons are separate sprites or one mirrored via scale already.
            var iconTransform = button.transform;
            iconTransform.localScale = new Vector3(-iconTransform.localScale.x, iconTransform.localScale.y, iconTransform.localScale.z);

            bool isPrevious = i == 0;
            button.onClick.AddListener((UnityAction)(() =>
            {
                if (isPrevious)
                {
                    SeedChooserPaging.GoToPreviousPage(dataModel, __instance);
                }
                else
                {
                    SeedChooserPaging.GoToNextPage(dataModel, __instance);
                }
            }));
        }

        // Force explicit separation between the two cloned buttons - confirmed live via a layout
        // dump each one is a real 330x300 rect (they clone from AlmanacArchive's own "arrows",
        // sized for that screen). This offset is in the group's own unscaled local space, so it
        // scales down together with the buttons themselves below.
        if (buttons.Length >= 2)
        {
            var previousRect = buttons[0].GetComponent<RectTransform>();
            var nextRect = buttons[1].GetComponent<RectTransform>();
            if (previousRect != null)
            {
                previousRect.anchoredPosition = new Vector2(-320, 0);
            }

            if (nextRect != null)
            {
                nextRect.anchoredPosition = new Vector2(320, 0);
            }
        }

        // The top-level Imitator container itself (not the deeply-nested SeedBackground selectable
        // used below for nav-wiring) - its own RectTransform gives a direct anchoredPosition/
        // sizeDelta reading in the exact same coordinate system our own buttons are about to be
        // parented into (seedChooserPanel), with no cross-hierarchy conversion involved at all.
        var imitatorTransform = seedChooserPanel.FindChild("Imitator");
        var imitatorRect = imitatorTransform?.GetComponent<RectTransform>();
        var imitatorSlot = imitatorTransform?.FindChild("P_GamePlay_SeedChooser_Item (1)/Offset/SeedBackground")?.GetComponent<Selectable>();

        // Anchored using Imitator's own convention (anchorMin/Max=(1,0), pivot=(0,0) - Imitator's
        // own anchoredPosition.x is "how far right of the panel's right edge its own left edge
        // sits"). Positioned just clear of Imitator's own right edge (its anchoredPosition.x +
        // sizeDelta.x), at the same height (anchoredPosition.y) - both read live rather than
        // hardcoded, so this holds even if Imitator's own layout ever changes. 0.6 scale brings
        // each 330x300 button down to a more visible ~200x180; the pair (each button's own
        // half-width 165, plus 320 separation, times the 0.6 scale) extends 291 either side of
        // this group's own local origin, so its own left edge needs a further 291 clearance
        // beyond Imitator's edge on top of a small gap.
        var rect = arrowsClone.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.localScale = new Vector3(0.6f, 0.6f, 1f);
            rect.anchorMin = new Vector2(1, 0);
            rect.anchorMax = new Vector2(1, 0);
            rect.pivot = new Vector2(0, 0);

            if (imitatorRect != null)
            {
                const float pairHalfWidth = 291f;
                const float gapAfterImitator = 40f;
                float groupX = imitatorRect.anchoredPosition.x + imitatorRect.sizeDelta.x + gapAfterImitator + pairHalfWidth;
                rect.anchoredPosition = new Vector2(groupX, imitatorRect.anchoredPosition.y);
            }
            else
            {
                // Imitator not found for some reason - fall back to a fixed offset comfortably
                // past its known real span (x=[-18,237]).
                rect.anchoredPosition = new Vector2(550, 120);
            }
        }

        MelonLoader.MelonLogger.Msg($"[CoreLib] Button position: imitator anchoredPos={imitatorRect?.anchoredPosition}, sizeDelta={imitatorRect?.sizeDelta}, our final anchoredPos={rect?.anchoredPosition}.");

        // Our buttons clone from AlmanacArchive with mode=None (confirmed live via a one-time
        // hierarchy dump - not reachable by gamepad/keyboard, mouse-only). This screen's whole
        // menu uses plain Unity Explicit Selectable.navigation for its left/right chain (a custom
        // layer on top just decides which chain is "current," per Il2CppReloaded.Input's
        // *NavigationContainer types) - confirmed live the real chain runs
        // Grid -> Imitator's seed slot -> ViewStore -> ViewAlmanac -> ViewLawnButton, all via
        // left/right only (no vertical links at this row). Splices our two buttons into that
        // chain between the Imitator slot and ViewStore, matching where the user asked for them.
        var viewStore = seedChooserRoot.FindChild("Canvas/Layout/Center/Buttons/NotCoop/ViewStore")?.GetComponent<Selectable>();

        if (buttons.Length >= 2)
        {
            if (imitatorSlot != null && viewStore != null)
            {
                var previousButton = buttons[0];
                var nextButton = buttons[1];

                SetHorizontalNav(imitatorSlot, right: previousButton);
                SetHorizontalNav(previousButton, left: imitatorSlot, right: nextButton);
                SetHorizontalNav(nextButton, left: previousButton, right: viewStore);
                SetHorizontalNav(viewStore, left: nextButton);
            }
            else
            {
                MelonLoader.MelonLogger.Warning("[CoreLib] Could not find the Imitator seed slot and/or ViewStore button to splice seed chooser paging buttons into the gamepad/keyboard navigation chain - paging buttons will stay mouse-only this session.");
            }
        }
    }

    // Only overwrites the directions passed - null leaves whatever that Selectable already had
    // (e.g. the Imitator slot's own left=Grid, or ViewStore's own right=ViewAlmanac, both stay
    // untouched here).
    static void SetHorizontalNav(Selectable selectable, Selectable left = null, Selectable right = null)
    {
        var nav = selectable.navigation;
        nav.mode = Navigation.Mode.Explicit;
        if (left != null) nav.selectOnLeft = left;
        if (right != null) nav.selectOnRight = right;
        selectable.navigation = nav;
    }
}

