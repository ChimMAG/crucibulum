using System.Linq;
using System.Threading.Tasks;
using Crucibulum;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Crucibulum.Tests
{
    /// <summary>
    /// Real clicks, driven through the client the way a player's would be.
    ///
    /// The one these exist for: a crucible is a Block, and blocks in hand are placed on sneak. If
    /// the game reached the held item before Block.OnBlockInteractStart, the crucible would
    /// ground-store on top of the forge instead of going into it and the whole design would be
    /// wrong - and no server-side test would notice, because those call the block entity directly.
    /// </summary>
    public class ForgeClientSide
    {
        /// <summary>A forge is a heat source and these tests stand next to lit ones. See TestLife.</summary>
        [BeforeEach]
        public async Task KeepThePlayerAlive() => await TestLife.Alive();

        static BlockPos ForgePos => P(8, 0, 8);
        static BlockEntityCrucibulumForge Forge => World.BE<BlockEntityCrucibulumForge>(ForgePos);

        const string Crucible = "game:crucible-brown-fired";

        static async Task<BlockEntityCrucibulumForge> AForge()
        {
            World.SetBlock("game:forge", ForgePos);
            await Ticks(2);
            await Player.StandNear(ForgePos);
            return Forge;
        }

        static async Task EmptyHand()
        {
            Player.Me.InventoryManager.ActiveHotbarSlot.Itemstack = null;
            Player.Me.InventoryManager.ActiveHotbarSlot.MarkDirty();
            await Ticks(3);
        }

        /// <summary>
        /// Tests share one player, so anything an earlier test handed them is still lying in the
        /// backpack. Counting what came out of a forge only means something against an empty one.
        /// </summary>
        static async Task EmptyPockets()
        {
            foreach (string cls in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
            {
                IInventory inv = Player.Me.InventoryManager.GetOwnInventory(cls);
                if (inv == null) continue;
                foreach (var slot in inv)
                {
                    if (slot.Empty) continue;
                    slot.Itemstack = null;
                    slot.MarkDirty();
                }
            }
            await Ticks(3);
        }

        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task ShiftRightClickSetsACrucibleInTheForge()
        {
            await AForge();
            await Player.Hold(Crucible, 2);
            await Ticks(2);

            await TestSneak.Click(ForgePos, BlockFacing.UP);

            Assert.NotNull(Forge.WorkItemStack,
                "the forge took the crucible - if this is null the held-item path won the click and " +
                "ground-stored it on top of the forge instead");
            Assert.Equal("crucible-brown-fired", Forge.WorkItemStack.Collectible.Code.Path, "what the forge holds");
            Assert.Equal(1, Forge.WorkItemStack.StackSize, "exactly one");
        }

        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task ShiftRightClickStillFuelsTheForge()
        {
            // Vanilla's own path, through our block class. Coal ore is GroundStorable, so this is
            // also the proof that a ground-storable item in hand does not steal a sneak-click from
            // the block.
            await AForge();
            await Player.Hold("game:ore-bituminouscoal", 4);
            await Ticks(2);

            await TestSneak.Click(ForgePos, BlockFacing.UP);

            Assert.NotNull(Forge.FuelSlot.Itemstack, "coal reached the fuel slot rather than the ground");
            Assert.Equal("ore-bituminouscoal", Forge.FuelSlot.Itemstack.Collectible.Code.Path, "what is burning");
        }

        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task ASecondCrucibleIsRefusedRatherThanMerged()
        {
            var be = await AForge();
            be.WorkItemSlot.Itemstack = World.Stack(Crucible);
            be.MarkDirty(true);
            await Ticks(2);

            await Player.Hold(Crucible, 3);
            await Ticks(2);

            await TestSneak.Click(ForgePos, BlockFacing.UP);

            Assert.Equal(1, Forge.WorkItemStack.StackSize, "still exactly one crucible in the forge");
            Assert.Equal(3, Player.Held.StackSize, "and none taken from the hand");
        }

        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task ShiftRightClickTakesTheCrucibleBackOut()
        {
            var be = await AForge();
            be.WorkItemSlot.Itemstack = World.Stack(Crucible);
            be.MarkDirty(true);
            await Ticks(2);
            await EmptyPockets();

            await TestSneak.Click(ForgePos, BlockFacing.UP);

            Assert.Null(Forge.WorkItemStack, "the crucible left the forge");

            // TryGiveItemstack puts it wherever there is room, which is not necessarily the hand.
            Assert.Equal(1, OnPlayer("crucible-brown-fired"), "the player has it");
        }

        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task APlainClickWithACrucibleInHandSetsItIn()
        {
            // A firepit takes a crucible on a plain click too (BlockFirepit does it with no shift
            // check), so both gestures work here rather than only the one.
            await AForge();
            await Player.Hold(Crucible, 2);
            await Ticks(2);

            await Interact.UseBlock(ForgePos, BlockFacing.UP);
            await Ticks(6);

            Assert.NotNull(Forge.WorkItemStack, "the forge took it on a plain click");
            Assert.Equal(1, Forge.WorkItemStack.StackSize, "exactly one");
        }

        /// <summary>
        /// Hotbar and backpack only: the creative inventory throws from its own Count getter when
        /// enumerated server-side, and TryGiveItemstack never puts anything there anyway.
        /// </summary>
        static int OnPlayer(string codePath)
        {
            int n = 0;
            foreach (string cls in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
            {
                IInventory inv = Player.Me.InventoryManager.GetOwnInventory(cls);
                if (inv == null) continue;
                foreach (var slot in inv)
                    if (slot.Itemstack?.Collectible.Code.Path == codePath) n += slot.StackSize;
            }
            return n;
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task AMoltenCrucibleRendersWithoutThrowing()
        {
            // Not a pixel assertion - the renderer builds a mesh from a shape, an atlas lookup and a
            // model transform, any of which can be missing and would otherwise fail silently at a
            // warning. Also leaves a picture to look at.
            await World.SetCalendarTo(500 * 24 + 12);

            var be = await AForge();
            be.FuelSlot.Itemstack = World.Stack("game:coke", 4);

            var smelted = (BlockSmeltedContainer)Sapi.World.GetBlock(new AssetLocation("game:crucible-brown-smelted"));
            var stack = new ItemStack(smelted);
            smelted.SetContents(stack, World.Stack("game:ingot-copper"), 200);
            be.WorkItemSlot.Itemstack = stack;
            stack.Collectible.SetTemperature(Sapi.World, stack, 1150);
            be.TryIgnite();
            be.MarkDirty(true);

            await Player.StandNear(ForgePos, 2.5);
            await Interact.LookAt(ForgePos);
            await Frames.Wait(20);

            Assert.NotNull(Forge.WorkItemStack, "the molten crucible is still there after rendering");
            await Shot.Take("/tmp/crucibulum-molten.png");
        }

        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task TheInteractionHelpOffersOnlyWhatShiftActuallyDoes()
        {
            // The floating help is built client-side and filtered per-forge, so it is asserted the
            // way the HUD builds it rather than read off a screenshot.
            //
            // It used to advertise three more shift-clicks - add one, add a stack, take the loose
            // charge - and shift-click with an empty hand takes the crucible, so the line the help
            // showed was not the line that ran. Loading the charge is the window's job now, and the
            // help says nothing about it.
            await AForge();
            var be = Forge;
            be.WorkItemSlot.Itemstack = World.Stack(Crucible);
            be.AddCharge(new DummySlot(World.Stack("game:nugget-nativecopper", 20)), 20);
            be.MarkDirty(true);

            // The help is filtered against the *client's* copy of the block entity, so this has to
            // wait for the sync rather than for the server to be ready. Comfortably past the soft
            // sync throttle - a few ticks is not enough, and the entries simply read as absent.
            await Ticks(30);

            await OnClient();
            var block = Capi.World.BlockAccessor.GetBlock(ForgePos);
            var sel = new BlockSelection { Position = ForgePos, Face = BlockFacing.UP };
            var help = block.GetPlacedBlockInteractionHelp(Capi.World, sel, Capi.World.Player);

            var shown = help
                .Where(wi => wi.GetMatchingStacks == null || wi.GetMatchingStacks(wi, sel, null) != null)
                .Select(wi => wi.ActionLangCode)
                .ToArray();
            await OnServer();

            Log("  help shown: " + string.Join(", ", shown));

            foreach (string code in shown)
            {
                Assert.False(code.Contains("charge"),
                    $"the help still offers a charge interaction that no longer exists: {code}");
            }

            Assert.True(shown.Contains("crucibulum:blockhelp-forge-takecrucible"), "shift still takes the crucible");
            Assert.True(shown.Contains("crucibulum:blockhelp-forge-opencrucible"), "and a plain click opens the window");
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task TheRendererWorksOutThatAMeltIsLiquid()
        {
            // The renderer asks HasSolidifed a few times a second and caches the answer. The "never
            // asked" sentinel was long.MinValue, and now - long.MinValue overflows negative for
            // every real value of now, so the check was never due: the cache sat on its false
            // initial value for the renderer's whole life and liquid metal was drawn on the
            // solidified branch. Tin is the case that shows it - it is liquid from 232 degC, well
            // below the 550 the glow ramp starts at, so a molten tin crucible drew no glow at all.
            //
            // Driven by the game's own render loop, which has been calling OnRenderFrame on this
            // renderer since the block entity initialised; this only reads what it concluded.
            var be = await AForge();

            var molten = World.Stack("game:crucible-brown-smelted");
            ((BlockSmeltedContainer)molten.Collectible).SetContents(molten, World.Stack("game:ingot-tin"), 200);
            molten.Collectible.SetTemperature(Sapi.World, molten, 400f);
            be.WorkItemSlot.Itemstack = molten;
            be.MarkDirty(true);

            await Ticks(20);
            await Frames.Wait(40);

            await OnClient();
            var clientBe = (BlockEntityCrucibulumForge)Capi.World.BlockAccessor.GetBlockEntity(ForgePos);
            object renderer = Private(clientBe, "crucibleRenderer");
            bool asked = (bool?)Private(renderer, "moltenChecked") ?? false;
            bool liquid = (bool?)Private(renderer, "moltenCached") ?? false;
            await OnServer();

            Log($"  renderer asked: {asked}, liquid: {liquid}");
            Assert.NotNull(renderer, "the client forge has a crucible renderer");
            Assert.True(asked, "the renderer got as far as asking whether the melt is liquid");
            Assert.True(liquid, "and tin at 400 degC is liquid, so it draws on the molten branch");
        }

        static object Private(object target, string name) =>
            target?.GetType()
                .GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(target);
    }
}
