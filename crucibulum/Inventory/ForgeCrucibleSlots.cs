// Crucibulum - melt metal in a crucible on the forge, for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option) any
// later version. See COPYING.LESSER, or <https://www.gnu.org/licenses/>.

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Crucibulum;

/// <summary>
/// The forge's work item slot, widened to take a crucible.
///
/// The filters exist because there is now a dialog: without them a player could drop a boot in
/// the fuel slot. They have to mirror BEForge.OnPlayerInteract exactly, or the vanilla
/// shift-click paths start silently refusing things they used to accept.
/// </summary>
public class ItemSlotForgeWorkItem : ItemSlotSurvival
{
    /// <summary>
    /// How many crucibles a forge will hold. One, because
    /// <see cref="BlockSmeltingContainer.DoSmelt"/> writes a single molten crucible into the output
    /// and nulls the *whole* input stack, so every vessel stacked behind the first is destroyed the
    /// moment the charge runs.
    ///
    /// Vanilla's own click path never stacks one - its merge branch is guarded by <c>!forgable</c>
    /// and a crucible deliberately is forgable - so the limit never had to be written down before.
    /// The window exposes this slot directly, and a shipped fired crucible stacks to four.
    ///
    /// Only crucibles. Ingots, plates and work items keep whatever the slot always gave them, so
    /// smithing at a forge is untouched.
    /// </summary>
    public const int MaxCrucibles = 1;

    public ItemSlotForgeWorkItem(InventoryBase inventory) : base(inventory) { }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        return base.CanHold(sourceSlot) && Accepts(sourceSlot.Itemstack);
    }

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
    {
        return base.CanTakeFrom(sourceSlot, priority) && Accepts(sourceSlot.Itemstack);
    }

    /// <summary>
    /// The capacity every merge goes through: a click into an empty slot, a same-item top-up, a
    /// shift-click, a hopper. All of them trim what they move to this.
    /// </summary>
    public override int GetRemainingSlotSpace(ItemStack forItemstack)
    {
        int space = base.GetRemainingSlotSpace(forItemstack);
        if (!BlockEntityCrucibulumForge.IsCrucible(forItemstack)) return space;

        return Math.Min(space, Math.Max(0, MaxCrucibles - StackSize));
    }

    /// <summary>
    /// The one path that does not. A flip trades whole stacks and asks neither
    /// <see cref="GetRemainingSlotSpace"/> nor the collectible's own stack size about it, so four
    /// crucibles on the cursor would swap places with an ingot in the forge and all four land here.
    /// Refused outright rather than trimmed, because a flip has no way to move part of a stack.
    /// </summary>
    public override bool TryFlipWith(ItemSlot itemSlot)
    {
        if (BlockEntityCrucibulumForge.IsCrucible(itemSlot?.Itemstack) && itemSlot.StackSize > MaxCrucibles)
        {
            return false;
        }

        return base.TryFlipWith(itemSlot);
    }

    public static bool Accepts(ItemStack stack)
    {
        if (stack == null) return false;
        if (BlockEntityCrucibulumForge.IsCrucible(stack)) return true;

        string first = stack.Collectible.FirstCodePart();
        return first == "ingot" || first == "metalplate" || first == "workitem"
               || stack.Collectible.Attributes?.IsTrue("forgable") == true;
    }
}

/// <summary>Fuel the forge will actually burn: vanilla's rule is a burn temperature over 1000.</summary>
public class ItemSlotForgeFuel : ItemSlotSurvival
{
    /// <summary>
    /// How deep the forge's coal bed goes.
    ///
    /// The forge draws its coal, and everything sitting on it, at
    /// <c>y + (fuelLevel - 1) / 64</c> blocks - the bed rises as it fills. Vanilla keeps that in
    /// range by refusing fuel once the level is over 4.5, but that guard lives in the shift-click
    /// path, not in the slot, so a window that exposes the slot lets a whole stack of 64 in and
    /// lifts the coals and the crucible a full block into the air above the forge.
    ///
    /// Six is what vanilla can actually reach: it will take one more whenever the level has burnt
    /// down to 4.5 or less.
    /// </summary>
    public const int MaxFuel = 6;

    public ItemSlotForgeFuel(InventoryBase inventory) : base(inventory)
    {
        MaxSlotStackSize = MaxFuel;
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        return base.CanHold(sourceSlot) && Accepts(sourceSlot.Itemstack);
    }

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
    {
        return base.CanTakeFrom(sourceSlot, priority) && Accepts(sourceSlot.Itemstack);
    }

    public static bool Accepts(ItemStack stack) =>
        stack?.Collectible.GetCombustibleProperties(null, stack, null) is { BurnTemperature: > 1000 };
}

/// <summary>
/// One of the crucible's four ingredient slots. Mirrors the vanilla crucible's own filter --
/// storageType 4 (Metallurgy) plus something that melts and needs a container to do it -- and
/// the crucible's own mouth, which is the part that is easy to miss.
///
/// There also has to *be* a crucible. The window stays open while the crucible rides the mouse
/// cursor, and a forge holding a molten crucible or a plain ingot keeps its four charge slots on
/// screen, so "what is in slot 0" is not something this can take on trust.
///
/// A firepit's crucible refuses an ingot, and with it a work item and a broken tool head. Not
/// for anything to do with smelting - all three carry combustible props that melt them back into
/// a whole ingot - but because the inventory compares an item's <c>size</c> against the
/// container's <c>maxContentDimensions</c>, and the fired crucible declares a mouth of
/// 0.125 x 0.25 x 0.125 that a nugget fits and the collectible default of 0.5 does not. That is
/// the only thing standing between a tool that has been worn to nothing and a full 100 units of
/// metal back; Smithing Plus, whose broken heads are plain work items, leans on it. This slot once
/// applied the smelting rules and not the size, so the forge took all three.
/// </summary>
public class ItemSlotCrucibleCharge : ItemSlotSurvival
{
    public ItemSlotCrucibleCharge(InventoryBase inventory) : base(inventory)
    {
        StorageType = EnumItemStorageFlags.Metallurgy;
    }

    /// <summary>The crucible seated in the forge, whose mouth sets the limit. Slot 0 is the work item.</summary>
    private ItemStack CrucibleInForge => inventory[0]?.Itemstack;

    public override bool CanHold(ItemSlot sourceSlot)
    {
        return base.CanHold(sourceSlot) && Admits(sourceSlot.Itemstack, CrucibleInForge);
    }

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
    {
        return base.CanTakeFrom(sourceSlot, priority) && Admits(sourceSlot.Itemstack, CrucibleInForge);
    }

    /// <summary>
    /// The whole rule for getting something into the charge, in one place: there has to be a fired
    /// crucible to put it in, it has to be something that crucible melts, and it has to go through
    /// the mouth.
    ///
    /// The container comes first because the other two questions are meaningless without it. A
    /// crucible that is absent or already molten declares no mouth, and reading that as "no limit"
    /// meant the size check - and with it both config switches, which are exemptions from that
    /// check and nothing else - simply did not run: an ingot went in while the crucible sat on the
    /// cursor, or while a molten one was still seated, and melted once a fired one was back.
    ///
    /// Everything that reaches these slots goes through <see cref="CanHold"/> or
    /// <see cref="CanTakeFrom"/> - a drag, a click, a same-item merge, a flip, a shift-click, a
    /// hopper - so this is the only gate there is, and the only one there needs to be.
    /// </summary>
    public static bool Admits(ItemStack stack, ItemStack crucible) =>
        IsFiredCrucible(crucible) && Accepts(stack) && FitsMouth(stack, crucible);

    /// <summary>
    /// A crucible that can still take a charge: fired, and not already run molten.
    ///
    /// A molten crucible is a <see cref="BlockSmeltedContainer"/>, which is *not* a
    /// <see cref="BlockSmeltingContainer"/> - upstream has them as siblings, both straight off
    /// Block - so asking for the smelting one excludes it. Anything else a mod seats here is
    /// welcome on the same terms as the vanilla crucible, mouth and all.
    /// </summary>
    public static bool IsFiredCrucible(ItemStack crucible) => crucible?.Collectible is BlockSmeltingContainer;

    /// <summary>Whether this is something a crucible melts at all. Size is a separate question.</summary>
    public static bool Accepts(ItemStack stack)
    {
        if (stack == null) return false;

        CombustibleProperties props = stack.Collectible.GetCombustibleProperties(null, stack, null);
        if (props is { BurnTemperature: > 1000 }) return false;   // that is fuel, not a charge

        return props?.SmeltedStack != null && props.MeltingPoint > 0 && props.RequiresContainer;
    }

    /// <summary>
    /// Whether the stack fits through the crucible's mouth, decided the way a firepit decides it:
    /// the item's dimensions against the container's <c>maxContentDimensions</c>. A crucible that
    /// declares no limit takes anything, as vanilla's inventory does when the attribute is absent.
    ///
    /// The size question on its own, and not an admission test: "no mouth declared" is the same
    /// answer here for a legitimate modded crucible and for no crucible at all. Which of those it
    /// is, is <see cref="IsFiredCrucible"/>'s question, and <see cref="Admits"/> asks both.
    ///
    /// The two config switches are exemptions from this and nothing else: each admits exactly its
    /// own class of item and leaves the limit in place for everything else.
    /// </summary>
    public static bool FitsMouth(ItemStack stack, ItemStack crucible)
    {
        if (stack == null) return false;

        Size3f mouth = crucible?.ItemAttributes?["maxContentDimensions"].AsObject<Size3f>(null);
        if (mouth == null || mouth.CanContain(stack.Collectible.Dimensions)) return true;

        CrucibulumConfig config = CrucibulumModSystem.Config;
        return (config.MeltIngots && IsIngot(stack))
            || (config.MeltBrokenToolHeads && IsBrokenToolHead(stack));
    }

    public static bool IsIngot(ItemStack stack) => stack?.Collectible.FirstCodePart() == "ingot";

    /// <summary>
    /// A tool head that came off a broken tool, as Smithing Plus marks one: a work item carrying
    /// <c>brokenCount</c>, either on itself or on the tool stack it remembers having repaired. Read
    /// by attribute name so nothing of theirs is referenced; without their mod nothing carries it
    /// and this is simply false. An unfinished work item straight off the anvil has no count and
    /// is not a broken head, and no switch here admits it.
    /// </summary>
    public static bool IsBrokenToolHead(ItemStack stack)
    {
        ITreeAttribute attributes = stack?.Attributes;
        if (attributes == null) return false;
        if (attributes.GetInt("brokenCount") > 0) return true;

        return attributes.GetItemstack("repairedToolStack")?.Attributes?.GetInt("brokenCount") > 0;
    }
}
