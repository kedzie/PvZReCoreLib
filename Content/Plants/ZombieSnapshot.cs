using Il2CppReloaded.Gameplay;
using UnityEngine;

namespace PvZReCoreLib.Content.Plants;

// One shared, per-frame look at every zombie on the board, for plants that only need to know "is a zombie within my
// reach" (melee plants, trigger plants). Reading a zombie costs several native calls each (alive?, row, rect...), and
// every call crosses into the IL2CPP side; with 30 plants each scanning 40 zombies a few times a second that adds up
// to a visible stutter. The snapshot reads each zombie once per frame, however many plants ask, and the plants then
// only compare plain numbers.
//
// The snapshot is only as fresh as the frame it was taken in: re-check IsDeadOrDying() on a zombie before hitting it
// if an earlier plant in the same frame may already have killed it.
public static class ZombieSnapshot
{
    public struct Entry
    {
        public Zombie Zombie;
        public int Row;
        public float Left;
        public float Right;

        // Alive, not mind-controlled, not a gravestone, and hurt by ground attacks: what a melee plant may hit.
        public bool Targetable;
    }

    private static readonly List<Entry> entries = new List<Entry>();
    private static readonly List<DataArray<Zombie>.DataArrayItem<Zombie>> items = new List<DataArray<Zombie>.DataArrayItem<Zombie>>();
    private static int itemsFrame = -1;
    private static IntPtr itemsBoard = IntPtr.Zero;
    private static int frame = -1;
    private static IntPtr boardPointer = IntPtr.Zero;

    // The board's zombie list (the DataArray items, read their mItem), copied into a managed list at most once per
    // frame and shared, instead of every plant copying the whole native list on every scan (that was the old
    // "board.m_zombies.m_list.ToList()"). Do not change the returned list; do not keep it past the current update.
    public static List<DataArray<Zombie>.DataArrayItem<Zombie>> Items(Board board)
    {
        if (board == null)
        {
            items.Clear();
            return items;
        }

        int currentFrame = Time.frameCount;
        if (itemsFrame == currentFrame && itemsBoard == board.Pointer)
        {
            return items;
        }

        itemsFrame = currentFrame;
        itemsBoard = board.Pointer;
        items.Clear();

        var list = board.m_zombies?.m_list;
        if (list == null)
        {
            return items;
        }

        foreach (var item in list)
        {
            if (item != null)
            {
                items.Add(item);
            }
        }

        return items;
    }

    public static List<Entry> Get(Board board)
    {
        if (board == null)
        {
            entries.Clear();
            return entries;
        }

        int currentFrame = Time.frameCount;
        if (frame == currentFrame && boardPointer == board.Pointer)
        {
            return entries;
        }

        frame = currentFrame;
        boardPointer = board.Pointer;
        entries.Clear();

        foreach (var item in Items(board))
        {
            var zombie = item?.mItem;
            if (zombie == null)
            {
                continue;
            }

            var entry = new Entry { Zombie = zombie, Row = zombie.mRow };
            if (!zombie.IsDeadOrDying()
                && !zombie.mMindControlled
                && zombie.mZombieType != ZombieType.Gravestone
                && zombie.EffectedByDamage(DamageRangeFlags.Ground))
            {
                var rect = zombie.GetZombieRect();
                entry.Left = rect.x;
                entry.Right = rect.x + rect.width;
                entry.Targetable = true;
            }

            entries.Add(entry);
        }

        return entries;
    }
}
