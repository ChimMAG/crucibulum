using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Crucibulum;

namespace Crucibulum.Tests
{
    /// <summary>
    /// Setup the suite needs and the mod does not.
    ///
    /// Both of these were public methods on the block entity, shipped in the assembly and reachable
    /// by anything, for the sake of the tests and screenshot scenes that call them. AddCharge was
    /// the worse of the two: it had its own idea of what cold metal costs a crucible, so fifty-odd
    /// tests were checking a second implementation of a rule rather than the one the game runs -
    /// which is how an equal-mass charge swap slipped past the real one unnoticed. The transport
    /// lives here now; every thermal rule is the block entity's tick and nothing else's.
    ///
    /// Extension methods, so the call sites read exactly as they did.
    /// </summary>
    internal static class ForgeFixtures
    {
        /// <summary>
        /// Fits a plate and works it round to a notch, through the two calls a player's clicks make
        /// rather than by assigning the state. A forge that already has a gate keeps it and is just
        /// wound round, so nothing is dropped on the floor behind the test's back.
        /// </summary>
        public static void FitGateForTesting(
            this BlockEntityCrucibulumForge be, ItemStack plate, GatePosition position)
        {
            if (!be.HasGate) be.FitGate(new DummySlot(plate), null);

            for (int i = 0; i < BlastGate.Positions && be.GatePosition != position; i++) be.CycleGate(null);

            be.MarkDirty(true);
        }

        /// <summary>
        /// Moves up to <paramref name="quantity"/> into the charge: into a slot already holding the
        /// same thing if there is one, so four ingredients still fit for an alloy, otherwise the
        /// first empty slot.
        ///
        /// DirectMerge, because Collectible.TryMergeStacks refuses an auto-merge of two stacks more
        /// than thirty degrees apart and asks to be called again at that priority - which is the
        /// second click a player gives it in a window. The merge averages the two temperatures by
        /// mass, and the crucible pays for the cold metal on the next tick like any other arrival.
        /// </summary>
        public static int AddCharge(this BlockEntityCrucibulumForge be, ItemSlot fromSlot, int quantity = 1)
        {
            if (fromSlot == null || fromSlot.Empty) return 0;

            ItemSlot target = null;

            foreach (ItemSlot s in be.ChargeSlots)
            {
                if (target != null) break;
                if (!s.Empty
                    && s.Itemstack.Equals(be.Api.World, fromSlot.Itemstack, GlobalConstants.IgnoredStackAttributes)
                    && s.StackSize < s.Itemstack.Collectible.MaxStackSize)
                {
                    target = s;
                }
            }
            foreach (ItemSlot s in be.ChargeSlots)
            {
                if (target == null && s.Empty) target = s;
            }

            if (target == null) return 0;

            ItemStackMoveOperation op = new ItemStackMoveOperation(
                be.Api.World, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, quantity);

            int moved = fromSlot.TryPutInto(target, ref op);
            if (moved == 0) return 0;

            fromSlot.MarkDirty();
            be.MarkDirty(true);
            return moved;
        }
    }
}
