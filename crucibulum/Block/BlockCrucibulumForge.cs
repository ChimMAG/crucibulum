// Crucibulum - melt metal in a crucible on the forge, for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option) any
// later version. See COPYING.LESSER, or <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.MathTools;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Crucibulum;

/// <summary>
/// The vanilla forge block, repointed at <see cref="BlockEntityCrucibulumForge"/> and given first
/// refusal on the interactions vanilla does not handle. BlockEntityForge.OnPlayerInteract is not
/// virtual, so the hook has to happen here rather than in the block entity.
/// </summary>
public class BlockCrucibulumForge : BlockForge
{
    private WorldInteraction[] crucibleInteractions;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api.Side != EnumAppSide.Client) return;
        ICoreClientAPI capi = (ICoreClientAPI)api;

        crucibleInteractions = ObjectCacheUtil.GetOrCreate(api, "crucibulumForgeInteractions", () =>
        {
            List<ItemStack> crucibleStacks = new();
            List<ItemStack> plateStacks = new();

            foreach (CollectibleObject obj in api.World.Collectibles)
            {
                if (BlockEntityCrucibulumForge.IsGatePlate(obj))
                {
                    List<ItemStack> plates = obj.GetHandBookStacks(capi);
                    if (plates != null) plateStacks.AddRange(plates);
                    continue;
                }

                if (obj is not BlockSmeltingContainer) continue;
                List<ItemStack> stacks = obj.GetHandBookStacks(capi);
                if (stacks != null) crucibleStacks.AddRange(stacks);
            }

            // Each gate gesture appears twice, differing only in whether the line says to bring
            // tongs. A WorldInteraction's text is fixed when the array is built and the array is
            // cached for the session, so a requirement that comes and goes with the fire cannot be
            // appended to a line later - it has to be a second line that stands in for the first.
            WorldInteraction GateHelp(string langCode, string hotKey, bool withTongs,
                System.Func<BlockEntityCrucibulumForge, BlockSelection, bool> offered) => new()
            {
                ActionLangCode = withTongs ? langCode + "-tongs" : langCode,
                HotKeyCode = hotKey,
                MouseButton = EnumMouseButton.Right,
                GetMatchingStacks = (wi, bs, es) =>
                {
                    var be = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityCrucibulumForge;
                    if (be?.HasGate != true) return null;
                    if (be.GateNeedsTongs != withTongs) return null;

                    return offered(be, bs) ? System.Array.Empty<ItemStack>() : null;
                }
            };

            return new[]
            {
                new WorldInteraction
                {
                    // No hotkey: a crucible goes in on a plain click, the way it does at a firepit.
                    ActionLangCode = "crucibulum:blockhelp-forge-addcrucible",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = crucibleStacks.ToArray(),
                    GetMatchingStacks = (wi, bs, es) =>
                        api.World.BlockAccessor.GetBlockEntity(bs.Position) is BlockEntityCrucibulumForge { WorkItemStack: null }
                            ? wi.Itemstacks
                            : null
                },
                new WorldInteraction
                {
                    ActionLangCode = "crucibulum:blockhelp-forge-takecrucible",
                    HotKeyCode = "shift",
                    MouseButton = EnumMouseButton.Right,
                    GetMatchingStacks = (wi, bs, es) =>
                    {
                        var be = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityCrucibulumForge;
                        return be?.CrucibleStack != null ? System.Array.Empty<ItemStack>() : null;
                    }
                },
                new WorldInteraction
                {
                    ActionLangCode = "crucibulum:blockhelp-forge-opencrucible",
                    MouseButton = EnumMouseButton.Right,
                    GetMatchingStacks = (wi, bs, es) =>
                    {
                        // Only where a plain click actually opens it, which is only a forge with a
                        // crucible in it. A bare forge, or one holding an ingot, does something else.
                        var be = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityCrucibulumForge;
                        return be?.CrucibleStack != null ? System.Array.Empty<ItemStack>() : null;
                    }
                },
                // The gate. Without these the whole thing is undiscoverable: there is nothing
                // in the world to say a plate can be fitted to a forge at all, and the player who
                // reported this had to derive every gesture by experiment.
                new WorldInteraction
                {
                    ActionLangCode = "crucibulum:blockhelp-forge-fitgate",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = plateStacks.ToArray(),
                    GetMatchingStacks = (wi, bs, es) =>
                    {
                        var be = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityCrucibulumForge;
                        return be != null && !be.HasGate && CrucibulumModSystem.Config.EnableBlastGate
                            ? wi.Itemstacks
                            : null;
                    }
                },
                // Offered where the click will actually work it: anywhere on a bare forge, and on
                // the plate itself once the forge is holding something, since there the plain click
                // belongs to the work item.
                GateHelp("crucibulum:blockhelp-forge-workgate", null, false, WorkGateOffered),
                GateHelp("crucibulum:blockhelp-forge-workgate", null, true, WorkGateOffered),

                // Shift with an empty hand takes the crucible first, so this is only the gesture it
                // actually is: a forge with a gate and no crucible in it.
                GateHelp("crucibulum:blockhelp-forge-takegate", "shift", false, TakeGateOffered),
                GateHelp("crucibulum:blockhelp-forge-takegate", "shift", true, TakeGateOffered)
            };
        });
    }

    private static bool WorkGateOffered(BlockEntityCrucibulumForge be, BlockSelection bs) =>
        CrucibulumModSystem.Config.EnableBlastGate
        && (be.WorkItemSlot.Empty || be.IsGateHit(bs.HitPosition));

    private static bool TakeGateOffered(BlockEntityCrucibulumForge be, BlockSelection bs) =>
        be.CrucibleStack == null;

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (TryBlockBehaviors(world, byPlayer, blockSel, out bool behaviorResult))
        {
            return behaviorResult;
        }

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityCrucibulumForge be
            && be.OnPlayerInteractCrucible(world, byPlayer, blockSel))
        {
            return true;
        }

        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    /// <summary>
    /// Runs the block behaviours over the click, which is what Block.OnBlockInteractStart would
    /// have done. BlockForge does not: it hands the click straight to its block entity and never
    /// chains up, so every behaviour declared on a forge blocktype is deaf to a right click.
    ///
    /// On a vanilla forge that costs nothing visible, but ChiselTools' decorative forge keeps the
    /// whole "put a chiselled block on it" gesture in a block behaviour, BBChiseledCover, and their
    /// block class worked only because it called that behaviour by hand before deferring to the
    /// forge. Replacing their class with this one therefore took the chiselling away - the forge
    /// still worked as a forge, which is exactly how it was reported: it would not accept a
    /// chiselled block.
    ///
    /// Restoring the engine's own contract rather than reaching for their behaviour by name, so any
    /// other mod that hangs a behaviour on a forge gets its click too. Their cover behaviour claims
    /// only game:chiseledblock and a wrench, so nothing a forge already does is at risk.
    ///
    /// Returns true when a behaviour claimed the click, with its answer in <paramref name="result"/>.
    /// </summary>
    private bool TryBlockBehaviors(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, out bool result)
    {
        result = true;
        bool claimed = false;

        foreach (BlockBehavior behavior in BlockBehaviors)
        {
            EnumHandling handled = EnumHandling.PassThrough;
            bool behaviorResult = behavior.OnBlockInteractStart(world, byPlayer, blockSel, ref handled);

            if (handled != EnumHandling.PassThrough)
            {
                result &= behaviorResult;
                claimed = true;
            }

            if (handled == EnumHandling.PreventSubsequent) return true;
        }

        return claimed;
    }

    /// <summary>
    /// ChiselTools' decorative forge names itself after whatever has been chiselled onto it. That
    /// naming lives in the block class this one replaces, while the chiselling itself lives in a
    /// block entity behaviour that survives the swap - so ask the behaviour rather than reimplement
    /// it, and leave every other forge on the ordinary name.
    ///
    /// By type and method name, because this mod does not reference theirs. That is exactly the
    /// kind of lookup that compiles cleanly and fails at runtime, so it fails to the ordinary name
    /// - which is what an uncovered forge shows anyway - and CompatChiselTools asserts that it
    /// still resolves against the mod as shipped.
    /// </summary>
    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos)
    {
        string covered = ChiselledCoverName(world, pos);
        return string.IsNullOrEmpty(covered) ? base.GetPlacedBlockName(world, pos) : covered;
    }

    private const string CoverBehavior = "BEBChiseledCover";
    private const string CoverNameMethod = "GetChiseledName";

    private static MethodInfo coverName;
    private static Type coverNameOwner;

    public static string ChiselledCoverName(IWorldAccessor world, BlockPos pos)
    {
        BlockEntity be = world.BlockAccessor.GetBlockEntity(pos);
        if (be?.Behaviors == null) return null;

        foreach (BlockEntityBehavior beh in be.Behaviors)
        {
            Type t = beh.GetType();
            if (t.Name != CoverBehavior) continue;

            // One lookup per behaviour type, not per tooltip.
            if (!ReferenceEquals(t, coverNameOwner))
            {
                coverNameOwner = t;
                coverName = t.GetMethod(CoverNameMethod, Type.EmptyTypes);
            }

            if (coverName == null) return null;
            try
            {
                return coverName.Invoke(beh, null) as string;
            }
            catch (Exception)
            {
                return null;
            }
        }

        return null;
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        WorldInteraction[] vanilla = base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
        return crucibleInteractions == null ? vanilla : crucibleInteractions.Append(vanilla);
    }
}
