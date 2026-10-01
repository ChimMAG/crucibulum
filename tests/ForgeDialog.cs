using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Crucibulum;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Crucibulum.Tests
{
    /// <summary>
    /// The crucible window.
    ///
    /// The forge is a BlockEntityContainer rather than an openable one, so every piece of the
    /// open/close/sync protocol is hand-written here rather than inherited - which is exactly the
    /// kind of thing that compiles and then does nothing in-world.
    /// </summary>
    public class ForgeDialog
    {
        /// <summary>A forge is a heat source and these tests stand next to lit ones. See TestLife.</summary>
        [BeforeEach]
        public async Task KeepThePlayerAlive() => await TestLife.Alive();

        static BlockPos ForgePos => P(8, 0, 8);
        static BlockEntityCrucibulumForge Forge => World.BE<BlockEntityCrucibulumForge>(ForgePos);

        static async Task<BlockEntityCrucibulumForge> EmptyHandedAtAForge()
        {
            World.SetBlock("game:forge", ForgePos);
            await Ticks(2);

            await Player.StandNear(ForgePos);
            Player.Me.InventoryManager.ActiveHotbarSlot.Itemstack = null;
            Player.Me.InventoryManager.ActiveHotbarSlot.MarkDirty();
            await Ticks(3);

            return Forge;
        }

        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task APlainClickOpensTheCrucible()
        {
            // Firepit convention: a bare right click on the block opens its window.
            var be = await EmptyHandedAtAForge();
            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
            be.MarkDirty(true);
            await Ticks(2);

            await Interact.UseBlock(ForgePos, BlockFacing.UP);

            await Gui.WaitFor<GuiDialogCrucibleForge>(120);
            Assert.NotNull(Forge.WorkItemStack, "opening the window did not also take the crucible");

            await Input.Press(GlKeys.Escape);
            await Gui.WaitGone<GuiDialogCrucibleForge>(120);
        }

        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task ABareForgeOpensNothing()
        {
            // The window belongs to the crucible. A forge with nothing in it stays a bare forge,
            // and clicking it does what clicking a bare forge has always done: nothing.
            await EmptyHandedAtAForge();

            await Interact.UseBlock(ForgePos, BlockFacing.UP);
            await Ticks(10);

            Assert.False(await Gui.IsOpen<GuiDialogCrucibleForge>(), "no window on an empty forge");
        }

        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task ABareForgeWithSomethingInHandStaysInert()
        {
            // A smith holding an ingot who clicks a bare forge should get what vanilla gives them:
            // nothing. Putting a window in their face on the way to shift-clicking the ingot in
            // would be a small but constant irritation.
            await EmptyHandedAtAForge();
            await Player.Hold("game:ingot-copper", 4);
            await Ticks(2);

            await Interact.UseBlock(ForgePos, BlockFacing.UP);
            await Ticks(6);

            Assert.False(await Gui.IsOpen<GuiDialogCrucibleForge>(), "no window opened");
            Assert.Null(Forge.WorkItemStack, "and nothing was put in - shift does that, as in vanilla");
        }

        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task AForgeHoldingAnIngotStillHandsItOverOnAPlainClick()
        {
            // The regression that would matter most. Smithing is: heat ingot, right click to grab
            // it, hit it on the anvil. A window in the way of that would ruin the forge for its
            // original purpose.
            var be = await EmptyHandedAtAForge();
            be.WorkItemSlot.Itemstack = World.Stack("game:ingot-copper");
            be.MarkDirty(true);
            await Ticks(2);

            await Interact.UseBlock(ForgePos, BlockFacing.UP);
            await Ticks(6);

            Assert.Null(Forge.WorkItemStack, "the ingot came out");
            Assert.False(await Gui.IsOpen<GuiDialogCrucibleForge>(), "and no window opened");
        }

        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task OpeningTheWindowOpensTheForgeInventoryOnTheServer()
        {
            // The proof that the hand-written open/close protocol actually reaches the server. The
            // forge is a plain BlockEntityContainer, so none of this is inherited: if the packets
            // went nowhere the window would still appear, and every slot in it would be inert.
            var be = await EmptyHandedAtAForge();
            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
            be.MarkDirty(true);
            await Ticks(2);

            Assert.False(Forge.Inventory.HasOpened(Player.Me), "nobody has it open to begin with");

            await Interact.UseBlock(ForgePos, BlockFacing.UP);
            await Gui.WaitFor<GuiDialogCrucibleForge>(120);
            await Until(() => Forge.Inventory.HasOpened(Player.Me), 60,
                "the server opened the forge inventory for this player");

            await Input.Press(GlKeys.Escape);
            await Gui.WaitGone<GuiDialogCrucibleForge>(120);
            await Until(() => !Forge.Inventory.HasOpened(Player.Me), 60,
                "and closed it again when the window went away");
        }

        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task TheWindowShowsTheForgesOwnSlots()
        {
            // The dialog is handed the block entity's inventory rather than a copy, so what the
            // player drags is the real thing. Six slots: work item, fuel, and four for the charge.
            var be = await EmptyHandedAtAForge();
            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
            be.AddCharge(new DummySlot(World.Stack("game:nugget-nativecopper", 5)), 5);
            be.MarkDirty(true);
            await Ticks(2);

            await Interact.UseBlock(ForgePos, BlockFacing.UP);
            var dlg = await Gui.WaitFor<GuiDialogCrucibleForge>(120);

            await OnClient();
            var clientBe = (BlockEntityCrucibulumForge)Capi.World.BlockAccessor.GetBlockEntity(ForgePos);
            int count = clientBe.Inventory.Count;
            string charge = clientBe.ChargeSlots[0].Itemstack?.Collectible.Code.Path;
            int chargeQty = clientBe.ChargeSlots[0].StackSize;
            await OnServer();

            Assert.Equal(6, count, "work item, fuel and four charge slots in one inventory");
            Assert.Equal("nugget-nativecopper", charge, "the client sees the charge the server has");
            Assert.Equal(5, chargeQty, "and the right amount of it");

            await Input.Press(GlKeys.Escape);
            await Gui.WaitGone<GuiDialogCrucibleForge>(120);
        }

        [VsTest]
        public async Task TheWindowIsToldTheMeltProgressAndTheVerdict()
        {
            // What SetDialogValues puts on the wire. Runs headless because it is the block entity's
            // half of the contract - the dialog only draws what it is handed here.
            World.SetBlock("game:forge", ForgePos);
            await Ticks(2);

            var be = Forge;
            be.FuelSlot.Itemstack = World.Stack("game:coke", 4);
            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
            be.AddCharge(new DummySlot(World.Stack("game:nugget-nativecopper", 18)), 18);
            be.AddCharge(new DummySlot(World.Stack("game:nugget-cassiterite", 2)), 2);
            be.MarkDirty(true);

            var tree = new Vintagestory.API.Datastructures.TreeAttribute();
            be.SetDialogValues(tree);

            Assert.Equal(1200f, tree.GetFloat("maxTemp"), "the ceiling coke can drive a crucible to");
            Assert.Equal(1, tree.GetInt("haveCrucible"), "the dialog knows there is a crucible");
            Assert.Greater(tree.GetFloat("maxMeltTime"), 0f, "a melt duration to scale the arrow against");

            string status = tree.GetString("statusText");
            Assert.Contains(status, "90%", "copper's share");
            Assert.Contains(status, "Tin bronze", "what it will make");
        }

        [VsTest]
        public async Task AnEmptyForgeTellsTheWindowToSaySo()
        {
            World.SetBlock("game:forge", ForgePos);
            await Ticks(2);

            var tree = new Vintagestory.API.Datastructures.TreeAttribute();
            Forge.SetDialogValues(tree);

            Assert.Equal(0, tree.GetInt("haveCrucible"), "no crucible in it");
            Assert.Contains(tree.GetString("statusText"), "crucible", "the window prompts for one");
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task DraggingOreIntoTheWindowReachesTheServer()
        {
            // The whole point of the window. The forge is a plain BlockEntityContainer, so the slot
            // packet handling is hand-written; if it went nowhere the grid would look right and
            // move nothing. This sends exactly the packet the slot grid sends.
            var be = await EmptyHandedAtAForge();
            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
            be.MarkDirty(true);
            await Ticks(2);

            await Interact.UseBlock(ForgePos, BlockFacing.UP);
            await Gui.WaitFor<GuiDialogCrucibleForge>(120);

            // Ore on the cursor. It has to be on both sides: the client's inventory is its own copy,
            // and the server resolves the move against its own mouse slot, so setting only the
            // client's would send a packet that moves nothing and prove the opposite of the point.
            await OnServer();
            Player.Me.InventoryManager.MouseItemSlot.Itemstack = World.Stack("game:nugget-nativecopper", 6);
            Player.Me.InventoryManager.MouseItemSlot.MarkDirty();
            await Ticks(2);

            await OnClient();
            var clientBe = (BlockEntityCrucibulumForge)Capi.World.BlockAccessor.GetBlockEntity(ForgePos);
            var mouse = Capi.World.Player.InventoryManager.MouseItemSlot;
            mouse.Itemstack = new ItemStack(Capi.World.GetItem(new AssetLocation("game:nugget-nativecopper")), 6);
            mouse.MarkDirty();

            // Exactly the packet the slot grid sends when you click a stack into a slot.
            var op = new ItemStackMoveOperation(Capi.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, 6)
            {
                ActingPlayer = Capi.World.Player
            };
            object packet = clientBe.Inventory.InvNetworkUtil.GetActivateSlotPacket(
                BlockEntityCrucibulumForge.FirstChargeSlot, op);
            Capi.Network.SendBlockEntityPacket(ForgePos.X, ForgePos.Y, ForgePos.Z, packet);

            await OnServer();

            await Until(() => Forge.ChargeSlots[0].StackSize > 0, 120,
                "the ore reached the server's charge slot through the inventory packet");
            Assert.Equal(6, Forge.ChargeSlots[0].StackSize, "all six of them");

            await Input.Press(GlKeys.Escape);
            await Gui.WaitGone<GuiDialogCrucibleForge>(120);
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task DraggingAcrossTheChargeSlotsSharesTheStackOut()
        {
            // Vanilla's left-drag: press with a stack on the cursor, sweep it over several slots,
            // and it is split evenly between them. Players use it to set an alloy's ratio, so the
            // charge row has to do it the way every other slot grid does.
            //
            // The first slot of the sweep changes the blend, the server says so, and that asks the
            // window to lay itself out again - which, done mid-sweep, would end the sweep there.
            // So this holds the button until that word has actually come back, then finishes the
            // sweep, and then checks the window did catch up once the button was let go.
            var sweep = await Sweep.Begin(8);
            await OnClient();
            try
            {
                string before = sweep.Status;

                await sweep.PointAt(0);
                sweep.Press(EnumMouseButton.Left);
                await FramesUntil(() => sweep.Status != before, 600,
                    () => "the server to report the new blend while the button is still held");

                for (int i = 1; i < BlockEntityCrucibulumForge.ChargeSlotCount; i++) await sweep.PointAt(i);
                sweep.Release();

                await FramesUntil(() => sweep.DisplayedStatus == sweep.Status, 120,
                    () => $"the window to show \"{sweep.Status}\" once the button was released; it shows \"{sweep.DisplayedStatus}\"");

                await sweep.ExpectOnServer(new[] { 2, 2, 2, 2 }, onCursor: 0);
            }
            finally
            {
                await sweep.End();
            }
        }

        // The server's word on the blend can arrive in the same frame as the press: the window
        // queues its relayout, the press lands, and the queued relayout runs a frame later with the
        // button down. Real timing only hits that now and then, so these set the status on the
        // client's own copy of the tree - which is what a server update does on arrival - and press
        // in the same breath, before the queue is run.

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public Task ALayoutQueuedAsALeftSweepStartsWaitsForTheRelease() =>
            SweepWithALayoutAlreadyQueued(EnumMouseButton.Left, new[] { 2, 2, 2, 2 }, onCursor: 0);

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public Task ALayoutQueuedAsARightSweepStartsWaitsForTheRelease() =>
            // Right-sweeping drops one into each slot crossed, and keeps its state in the grid too.
            SweepWithALayoutAlreadyQueued(EnumMouseButton.Right, new[] { 1, 1, 1, 1 }, onCursor: 4);

        static async Task SweepWithALayoutAlreadyQueued(EnumMouseButton button, int[] expected, int onCursor)
        {
            var sweep = await Sweep.Begin(8);
            await OnClient();
            try
            {
                await sweep.PointAt(0);
                sweep.Dlg.Attributes.SetString("statusText", "a blend the server has just reported");
                sweep.Press(button);

                for (int i = 1; i < BlockEntityCrucibulumForge.ChargeSlotCount; i++) await sweep.PointAt(i);
                sweep.Release();

                await sweep.ExpectOnServer(expected, onCursor);
            }
            finally
            {
                await sweep.End();
            }
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task ASyncFromBehindTheSweepDoesNotUndoIt()
        {
            // The forge sends its whole tree, inventory and all, after every slot packet, and a
            // sweep moves stacks on the client ahead of the server. A sync describing the slots as
            // they were a step or two back used to land mid-sweep and put them back, and the rest
            // of the sweep then shared out stacks that were no longer there - the last slot came up
            // empty about half the time. Here the stale tree is handed over directly, so it lands
            // mid-sweep every time rather than when the timing happens to line up.
            var sweep = await Sweep.Begin(8);
            await OnClient();
            try
            {
                var clientBe = (BlockEntityCrucibulumForge)Capi.World.BlockAccessor.GetBlockEntity(ForgePos);
                var fromBefore = new TreeAttribute();
                clientBe.ToTreeAttributes(fromBefore);

                await sweep.PointAt(0);
                sweep.Press(EnumMouseButton.Left);
                await sweep.PointAt(1);

                clientBe.FromTreeAttributes(fromBefore, Capi.World);
                Assert.Equal("4,4,0,0", sweep.ClientCharge(), "what the client shows mid-sweep, after a sync from before it began");

                sweep.Release();
                await sweep.ExpectOnServer(new[] { 4, 4, 0, 0 }, onCursor: 0);
                await FramesUntil(() => sweep.ClientCharge() == "4,4,0,0", 120,
                    () => $"the client to agree with the server after the release; it shows {sweep.ClientCharge()}");
            }
            finally
            {
                await sweep.End();
            }
        }

        /// <summary>
        /// <see cref="Until"/> on rendered frames rather than ticks, for what the window has to draw.
        /// The message is built on failure, so it can say what was there instead.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task AMeltMidSweepLeavesNoGhostOreOnTheClient()
        {
            // The client ignores the forge's tree while a sweep is under way, and a melt is the one
            // change only the tree carries: it empties the charge slots directly, with no slot packet
            // of its own. The molten crucible comes back on the release, through the slot packets
            // the pause held; the empty charge has to come from a fresh tree, asked for once the
            // pause lifts. Without it the ore stays on the client, sitting beside a molten crucible.
            var sweep = await Sweep.Begin(8);
            await OnClient();
            try
            {
                await sweep.PointAt(0);
                sweep.Press(EnumMouseButton.Left);
                await sweep.PointAt(1);

                await OnServer();
                typeof(BlockEntityCrucibulumForge)
                    .GetMethod("DoSmelt", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(Forge, null);
                Assert.True(BlockEntityCrucibulumForge.IsMoltenCrucible(Forge.WorkItemStack), "the melt happened on the server");
                Assert.True(Forge.ChargeEmpty, "and took the charge");
                await Ticks(5);

                await OnClient();
                sweep.Release();

                var clientBe = (BlockEntityCrucibulumForge)Capi.World.BlockAccessor.GetBlockEntity(ForgePos);
                await FramesUntil(() => sweep.ClientCharge() == "0,0,0,0" && BlockEntityCrucibulumForge.IsMoltenCrucible(clientBe.WorkItemStack), 120,
                    () => $"the client to show the melt: charge {sweep.ClientCharge()}, crucible {clientBe.WorkItemStack?.Collectible.Code}");
            }
            finally
            {
                await sweep.End();
            }
        }

        static async Task FramesUntil(System.Func<bool> condition, int maxFrames, System.Func<string> what)
        {
            for (int i = 0; i < maxFrames && !condition(); i++) await Frames.Wait(1);
            Assert.True(condition(), $"within {maxFrames} frames: {what()}");
        }

        /// <summary>
        /// A crucible window with copper on the cursor, and a hand to sweep it across the charge row
        /// through the game's own mouse entry point. Use it from the client thread: a slot click plays
        /// a sound, which refuses to anywhere else, and an await only switches the thread of the
        /// method that makes it - so the test itself has to be over there, not just this.
        /// </summary>
        sealed class Sweep
        {
            public GuiDialogCrucibleForge Dlg;
            ClientMain game;
            EnumMouseButton? held;

            public string Status => Dlg.Attributes.GetString("statusText", "");
            public string ClientCharge() => string.Join(",", Enumerable.Range(0, BlockEntityCrucibulumForge.ChargeSlotCount)
                .Select(i => Dlg.Inventory[BlockEntityCrucibulumForge.FirstChargeSlot + i].StackSize));
            public string DisplayedStatus => Dlg.SingleComposer.GetDynamicText("statusText")?.GetText() ?? "";

            public static async Task<Sweep> Begin(int nuggets)
            {
                var be = await EmptyHandedAtAForge();
                be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
                be.MarkDirty(true);
                await Ticks(2);

                await Interact.UseBlock(ForgePos, BlockFacing.UP);
                var dlg = await Gui.WaitFor<GuiDialogCrucibleForge>(120);

                // On both sides, as in DraggingOreIntoTheWindowReachesTheServer.
                await OnServer();
                Player.Me.InventoryManager.MouseItemSlot.Itemstack = World.Stack("game:nugget-nativecopper", nuggets);
                Player.Me.InventoryManager.MouseItemSlot.MarkDirty();
                await Ticks(2);

                await OnClient();
                var mouse = Capi.World.Player.InventoryManager.MouseItemSlot;
                mouse.Itemstack = new ItemStack(Capi.World.GetItem(new AssetLocation("game:nugget-nativecopper")), nuggets);
                mouse.MarkDirty();
                await Frames.Wait(3);

                return new Sweep { Dlg = dlg, game = (ClientMain)Capi.World };
            }

            /// <summary>
            /// Moves the cursor over a charge slot. Both layers have to be told: a press is made at
            /// the platform's idea of the cursor, which Input.MouseMove does not touch, so without
            /// this the press lands wherever the real pointer happens to sit.
            /// </summary>
            public async Task PointAt(int slot)
            {
                var b = Dlg.SingleComposer.GetSlotGrid("chargeSlots").SlotBounds[slot];
                int x = (int)(b.absX + b.OuterWidth / 2), y = (int)(b.absY + b.OuterHeight / 2);

                game.Platform.GetType()
                    .GetMethod("SetMousePosition", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(game.Platform, new object[] { (float)x, (float)y });
                game.OnMouseMove(new MouseEvent(x, y));
                await Frames.Wait(5);
            }

            /// <summary>Synchronous, so a caller can press inside the frame it is already in.</summary>
            public void Press(EnumMouseButton button)
            {
                held = button;
                game.UpdateMouseButtonState(button, true);
            }

            public void Release()
            {
                if (held is not EnumMouseButton button) return;
                held = null;
                game.UpdateMouseButtonState(button, false);
            }

            public async Task ExpectOnServer(int[] expected, int onCursor)
            {
                await OnServer();
                try
                {
                    await Until(() => Forge.ChargeSlots.Select(s => s.StackSize).SequenceEqual(expected), 120,
                        $"the charge slots to hold {string.Join(",", expected)}");
                }
                finally
                {
                    Log($"  charge slots hold {string.Join(",", Forge.ChargeSlots.Select(s => s.StackSize))}, cursor {Player.Me.InventoryManager.MouseItemSlot.StackSize}");
                }
                Assert.Equal(onCursor, Player.Me.InventoryManager.MouseItemSlot.StackSize, "left on the cursor");
            }

            /// <summary>Lets go of the button and shuts the window, whatever state a failure left them in.</summary>
            public async Task End()
            {
                await OnClient();
                Release();
                Dlg.TryClose();
                await Gui.WaitGone<GuiDialogCrucibleForge>(120);
            }
        }

        [VsTest]
        public async Task TheBlockInfoTextIsNotRebuiltEveryCall()
        {
            // GetBlockInfo runs once a frame for whatever the player is looking at, and working out
            // what a crucible will make is not cheap - on a mix that matches no alloy it walks every
            // alloy in the game. Measured at 4.6us and 9.6KB a call, which at 60fps is most of a
            // megabyte a second of garbage from one forge. It is cached; this is the guard.
            //
            // Counting rebuilds rather than bytes: GC counters are process-wide, so with a client
            // attached an allocation measurement is just noise from the render thread.
            World.SetBlock("game:forge", ForgePos);
            await Ticks(2);

            var be = Forge;
            be.FuelSlot.Itemstack = World.Stack("game:coke", 4);
            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
            be.AddCharge(new DummySlot(World.Stack("game:nugget-nativecopper", 8)), 8);
            be.AddCharge(new DummySlot(World.Stack("game:nugget-cassiterite", 12)), 12);
            be.MarkDirty(true);

            var sb = new System.Text.StringBuilder();
            be.GetBlockInfo(null, sb);
            Assert.Contains(sb.ToString(), "will not combine", "the text is the expensive one");

            int before = be.ChargeTextRebuilds;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 200; i++)
            {
                sb.Clear();
                be.GetBlockInfo(null, sb);
            }
            sw.Stop();

            // The cache is a 200ms TTL, so what it may rebuild depends on how long the loop took -
            // microseconds here, but another mod's postfix on the base can stretch a call to tens
            // of milliseconds (Smithing Plus did, before the forge stopped calling the base for a
            // crucible), and a fixed count then fails for a reason that is not the cache's.
            int rebuilds = be.ChargeTextRebuilds - before;
            int elapsedMs = (int)sw.ElapsedMilliseconds;
            int allowed = 2 + elapsedMs / 200;
            Log($"  200 calls in {elapsedMs}ms rebuilt the text {rebuilds} times");
            Assert.Less(rebuilds, allowed, $"200 calls in a row rebuilt the text {rebuilds} times over {elapsedMs}ms");
            Assert.Less(elapsedMs, 2000, "and none of them was slow enough to matter at 60fps");
        }

        [VsTest]
        public async Task ChangingTheChargeRefreshesTheTextAtOnce()
        {
            // Cheap is no good if it is also stale: adding ore has to show up straight away rather
            // than at the cache's leisure.
            World.SetBlock("game:forge", ForgePos);
            await Ticks(2);

            var be = Forge;
            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
            be.AddCharge(new DummySlot(World.Stack("game:nugget-nativecopper", 4)), 4);
            be.MarkDirty(true);

            var sb = new System.Text.StringBuilder();
            be.GetBlockInfo(null, sb);
            Assert.Contains(sb.ToString(), "4x", "four nuggets to start with");

            be.AddCharge(new DummySlot(World.Stack("game:nugget-nativecopper", 6)), 6);

            sb.Clear();
            be.GetBlockInfo(null, sb);
            Assert.Contains(sb.ToString(), "10x", "and ten straight after adding six more");
        }

        [VsTest]
        public async Task TheFuelSlotCannotBeOverfilled()
        {
            // The forge draws its coal bed, and the crucible sitting on it, higher as it fills:
            // y + (fuelLevel - 1) / 64 blocks. Vanilla keeps that in range by refusing fuel over a
            // level of 4.5, but that guard is in the shift-click path rather than in the slot - so
            // when this window still had a fuel slot, a whole stack of 64 went in and lifted the
            // coals and the crucible a full block into the air above the forge.
            //
            // The window no longer offers fuel at all, so nothing can reach past vanilla's own
            // guard today. The cap stays as a guard on the renderer's invariant: the coal bed
            // height is only drawn for levels this small.
            World.SetBlock("game:forge", ForgePos);
            await Ticks(2);

            var be = Forge;
            var hand = new DummySlot(World.Stack("game:coke", 64));
            hand.TryPutInto(Sapi.World, be.FuelSlot, 64);

            Assert.LessOrEqual(be.FuelSlot.StackSize, ItemSlotForgeFuel.MaxFuel,
                "the fuel slot holds no more than the coal bed is drawn for");
            Assert.Greater(hand.StackSize, 0, "and the rest stays in hand");

            // The lift that caused it, at the capped level: a voxel or so, not a block.
            float lift = (be.FuelLevel - 1) / 16f / 4f;
            Assert.Less(lift, 0.1f, $"coal bed rises {lift:0.00} blocks, which should be barely visible");
        }

        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task TheWindowOffersNoFuelSlot()
        {
            // Fuel goes on a forge the way it always has, by shift-clicking coal onto it. Offering
            // a second way to do it in here made the window the odd one out, and it was the only
            // thing that could take fuel back off a lit forge.
            var be = await EmptyHandedAtAForge();
            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
            be.MarkDirty(true);
            await Ticks(2);

            await Interact.UseBlock(ForgePos, BlockFacing.UP);
            var dlg = await Gui.WaitFor<GuiDialogCrucibleForge>(120);

            await OnClient();
            bool hasFuel = dlg.SingleComposer.GetSlotGrid("fuelSlot") != null;
            bool hasCrucible = dlg.SingleComposer.GetSlotGrid("crucibleSlot") != null;
            bool hasCharge = dlg.SingleComposer.GetSlotGrid("chargeSlots") != null;
            await OnServer();

            Assert.False(hasFuel, "no fuel slot in the crucible window");
            Assert.True(hasCrucible, "the crucible slot is still there");
            Assert.True(hasCharge, "and the charge slots");

            await Input.Press(GlKeys.Escape);
            await Gui.WaitGone<GuiDialogCrucibleForge>(120);
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task TheWindowKeepsUpAsTheCrucibleCools()
        {
            // The temperature used to freeze at whatever it read when the fire went out. The tick
            // decided whether to sync by comparing a reading taken before HeatCrucible with the one
            // it returned - but GetTemperature performs the cooling as a side effect of being
            // called, so the two readings always matched and clients were never told anything.
            var be = await EmptyHandedAtAForge();
            be.FuelSlot.Itemstack = null;
            var crucible = World.Stack("game:crucible-brown-fired");
            be.WorkItemSlot.Itemstack = crucible;
            be.AddCharge(new DummySlot(World.Stack("game:nugget-nativecopper", 20)), 20);
            crucible.Collectible.SetTemperature(Sapi.World, crucible, 1100);
            be.MarkDirty(true);
            await Ticks(2);

            await Interact.UseBlock(ForgePos, BlockFacing.UP);
            var dlg = await Gui.WaitFor<GuiDialogCrucibleForge>(120);

            await OnClient();
            string first = dlg.SingleComposer.GetDynamicText("crucibleTemp").GetText();
            await OnServer();

            // Past the half in-game hour of cooldown delay that every heat gain buys, then far
            // enough for whole degrees to move.
            for (int i = 0; i < 16; i++)
            {
                await Hours(0.05);
                await Ticks(10);
            }

            await OnClient();
            string later = dlg.SingleComposer.GetDynamicText("crucibleTemp").GetText();
            await OnServer();

            Log($"  window read {first}, then {later}");
            Assert.NotEqual(first, later, "the open window must follow the crucible down");

            await Input.Press(GlKeys.Escape);
            await Gui.WaitGone<GuiDialogCrucibleForge>(120);
        }

        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task TheWindowRefusesThingsTheCrucibleCannotMelt()
        {
            // The slot filters are the only thing standing between a dialog and a player storing
            // their boots in a forge.
            await EmptyHandedAtAForge();

            Assert.False(ItemSlotCrucibleCharge.Accepts(World.Stack("game:coke")), "coke is fuel, not a charge");
            Assert.False(ItemSlotCrucibleCharge.Accepts(World.Stack("game:stick")), "a stick melts into nothing");
            Assert.True(ItemSlotCrucibleCharge.Accepts(World.Stack("game:nugget-nativecopper")), "a nugget belongs");

            Assert.True(ItemSlotForgeFuel.Accepts(World.Stack("game:coke")), "coke is fuel");
            Assert.False(ItemSlotForgeFuel.Accepts(World.Stack("game:nugget-nativecopper")), "a nugget is not fuel");

            Assert.True(ItemSlotForgeWorkItem.Accepts(World.Stack("game:crucible-brown-fired")), "a crucible fits the work slot");
            Assert.True(ItemSlotForgeWorkItem.Accepts(World.Stack("game:ingot-copper")), "so does an ingot - smithing must still work");
            Assert.False(ItemSlotForgeWorkItem.Accepts(World.Stack("game:stick")), "a stick does not");

            await Task.CompletedTask;
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task TheWindowSaysWhatTemperatureTheChargeNeeds()
        {
            // Asserted on the drawn string, not the attribute behind it: the number reaching the
            // window is only half the job, and the half a player sees is the other one.
            var be = await EmptyHandedAtAForge();
            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
            be.AddCharge(new DummySlot(World.Stack("game:nugget-nativecopper", 20)), 20);
            be.WorkItemStack.Collectible.SetTemperature(Sapi.World, be.WorkItemStack, 900f);
            be.MarkDirty(true);
            await Ticks(30);

            await Interact.UseBlock(ForgePos, BlockFacing.UP);
            var dlg = await Gui.WaitFor<GuiDialogCrucibleForge>(120);

            await OnClient();
            string shown = dlg.SingleComposer.GetDynamicText("crucibleTemp").GetText();
            await OnServer();

            Log($"  window temperature line: {shown}");
            Assert.Contains(shown, "1084", "the window says what it is climbing towards");

            await Input.Press(GlKeys.Escape);
            await Gui.WaitGone<GuiDialogCrucibleForge>(120);
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task TheWindowClosesWhenTheCrucibleLeavesTheForge()
        {
            // The window is the crucible's, so an empty forge should not be left showing one.
            var be = await EmptyHandedAtAForge();
            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
            be.AddCharge(new DummySlot(World.Stack("game:nugget-nativecopper", 20)), 20);
            be.MarkDirty(true);
            await Ticks(4);

            await Interact.UseBlock(ForgePos, BlockFacing.UP);
            await Gui.WaitFor<GuiDialogCrucibleForge>(120);

            be.WorkItemSlot.Itemstack = null;
            be.MarkDirty(true);

            await Gui.WaitGone<GuiDialogCrucibleForge>(240);
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task TheWindowStaysOpenWhileTheCrucibleIsOnTheCursor()
        {
            // Lifting the crucible out of its slot empties the slot a moment before the crucible
            // has gone anywhere - it is riding the mouse cursor. Closing on that would take the
            // drag with it, so the window waits until the cursor is empty again.
            var be = await EmptyHandedAtAForge();
            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
            be.MarkDirty(true);
            await Ticks(4);

            await Interact.UseBlock(ForgePos, BlockFacing.UP);
            await Gui.WaitFor<GuiDialogCrucibleForge>(120);

            await OnClient();
            var mouse = Capi.World.Player.InventoryManager.MouseItemSlot;
            mouse.Itemstack = new ItemStack(Capi.World.GetBlock(new AssetLocation("game:crucible-brown-fired")));
            mouse.MarkDirty();
            await OnServer();

            be.WorkItemSlot.Itemstack = null;
            be.MarkDirty(true);
            await Ticks(20);

            string[] open = await Gui.OpenDialogs();
            Assert.True(open.Any(d => d.Contains("CrucibleForge")),
                "the window stayed open while the crucible was still on the cursor; open: " + string.Join(", ", open));

            // Put it down; now it may go.
            await OnClient();
            Capi.World.Player.InventoryManager.MouseItemSlot.Itemstack = null;
            Capi.World.Player.InventoryManager.MouseItemSlot.MarkDirty();
            await OnServer();

            await Gui.WaitGone<GuiDialogCrucibleForge>(240);
        }
    }
}
