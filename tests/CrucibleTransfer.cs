using System.Linq;
using System.Threading.Tasks;
using Crucibulum;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Crucibulum.Tests
{
    /// <summary>
    /// Carrying a crucible between a firepit and the forge.
    ///
    /// Reported as "taking a crucible with contents out of a firepit and putting it into a forge
    /// removes its inventory", with the follow-up that the firepit spat out some copper but the
    /// tin was lost until the crucible had been put back into the firepit and taken out again.
    ///
    /// Neither device keeps ore in the crucible stack: the firepit holds it in four cooking slots
    /// and the forge in four charge slots. What these establish is what the vanilla firepit does
    /// with the cooking slots the moment the crucible leaves, and whether the forge then touches
    /// anything on the way in.
    /// </summary>
    public class CrucibleTransfer
    {
        static BlockPos FirepitPos => P(6, 0, 8);
        static BlockPos ForgePos => P(10, 0, 8);

        const string Crucible = "game:crucible-brown-fired";
        const string MoltenCrucible = "game:crucible-brown-smelted";
        const string CopperNugget = "game:nugget-nativecopper";
        const string TinNugget = "game:nugget-cassiterite";

        static BlockEntityFirepit Firepit => World.BE<BlockEntityFirepit>(FirepitPos);
        static BlockEntityCrucibulumForge Forge => World.BE<BlockEntityCrucibulumForge>(ForgePos);

        // InventorySmelting: 0 fuel, 1 input, 2 output, 3..6 cooking slots.
        const int InputSlot = 1;
        const int FirstCookingSlot = 3;

        static async Task<BlockEntityFirepit> AFirepitHolding(params (int cookingSlot, string code, int qty)[] charge)
        {
            World.SetBlock("game:firepit-cold", FirepitPos);
            World.SetBlock("game:forge", ForgePos);
            await Ticks(2);

            var be = Firepit;
            be.Inventory[InputSlot].Itemstack = World.Stack(Crucible);
            be.Inventory[InputSlot].MarkDirty();
            await Ticks(1);

            foreach (var (slot, code, qty) in charge)
            {
                be.Inventory[FirstCookingSlot + slot].Itemstack = World.Stack(code, qty);
                be.Inventory[FirstCookingSlot + slot].MarkDirty();
            }
            be.MarkDirty(true);
            await Ticks(2);
            return be;
        }

        static int Dropped(string codePath) =>
            World.Entities(FirepitPos, 8)
                .OfType<EntityItem>()
                .Where(e => e.Itemstack?.Collectible.Code.Path == codePath)
                .Sum(e => e.Itemstack.StackSize);

        static string CookingSlotsOf(BlockEntityFirepit be) =>
            string.Join(", ", Enumerable.Range(0, 4).Select(i =>
            {
                var s = be.Inventory[FirstCookingSlot + i];
                return s.Empty ? "-" : $"{s.StackSize}x{s.Itemstack.Collectible.Code.Path}";
            }));

        static void ClearDrops()
        {
            foreach (var e in World.Entities(FirepitPos, 8).OfType<EntityItem>()) e.Die();
        }

        /// <summary>Copper and tin side by side, the way a player fills the window left to right.</summary>
        [VsTest]
        public async Task TakingTheCrucibleOutOfAFirepitDropsTheChargeOnTheGround()
        {
            var pit = await AFirepitHolding((0, CopperNugget, 10), (1, TinNugget, 5));
            ClearDrops();

            ItemStack crucible = pit.Inventory[InputSlot].TakeOutWhole();
            pit.Inventory[InputSlot].MarkDirty();
            await Ticks(2);

            Log($"  cooking slots after take-out: {CookingSlotsOf(pit)}");
            Log($"  dropped: {Dropped("nugget-nativecopper")} copper, {Dropped("nugget-cassiterite")} tin");

            Assert.Equal(10, Dropped("nugget-nativecopper"), "the copper landed on the ground at the firepit");
            Assert.Equal(5, Dropped("nugget-cassiterite"), "and so did the tin");
            Assert.True(pit.Inventory[FirstCookingSlot].Empty && pit.Inventory[FirstCookingSlot + 1].Empty,
                "the firepit kept nothing");

            // Now the forge. The stack arrives carrying nothing, and the forge adds nothing.
            Assert.True(Forge.TrySetCrucible(new DummySlot(crucible)), "the forge took the crucible");
            Assert.True(Forge.ChargeEmpty, "the forge's charge is empty: the ore never travelled in the stack");
            Assert.Null(crucible.Attributes?["contents"], "and the stack itself carries no contents");
        }

        /// <summary>
        /// Copper in the first slot, an empty one, then tin. Vanilla's discard loop passes every
        /// slot's stack - empty ones included - straight to SpawnItemEntity.
        /// </summary>
        [VsTest]
        public async Task AGapBetweenTheCopperAndTheTinStillDropsBoth()
        {
            var pit = await AFirepitHolding((0, CopperNugget, 10), (2, TinNugget, 5));
            ClearDrops();

            pit.Inventory[InputSlot].TakeOutWhole();
            pit.Inventory[InputSlot].MarkDirty();
            await Ticks(2);

            Log($"  cooking slots after take-out: {CookingSlotsOf(pit)}");
            Log($"  dropped: {Dropped("nugget-nativecopper")} copper, {Dropped("nugget-cassiterite")} tin");

            Assert.Equal(10, Dropped("nugget-nativecopper"), "the copper dropped");
            Assert.Equal(5, Dropped("nugget-cassiterite"), "the tin past the gap dropped too");
            Assert.True(pit.Inventory[FirstCookingSlot + 2].Empty, "the tin slot was emptied");
        }

        /// <summary>A crucible that has already melted carries its metal in the stack, and keeps it.</summary>
        [VsTest]
        public async Task AMoltenCrucibleFromAFirepitKeepsItsMetalInTheForge()
        {
            World.SetBlock("game:forge", ForgePos);
            await Ticks(2);

            ItemStack molten = World.Stack(MoltenCrucible);
            var smelted = (BlockSmeltedContainer)molten.Collectible;
            smelted.SetContents(molten, World.Stack("game:ingot-copper"), 500);
            molten.Collectible.SetTemperature(Sapi.World, molten, 1100f);

            Assert.True(Forge.TrySetCrucible(new DummySlot(molten)), "the forge took the molten crucible");
            await World.TickNow(ForgePos);
            await Ticks(5);
            await World.TickNow(ForgePos);

            var contents = smelted.GetContents(Sapi.World, Forge.WorkItemStack);
            Assert.NotNull(contents.Key, "the metal is still in it");
            Assert.Equal("ingot-copper", contents.Key.Collectible.Code.Path, "and it is still copper");
            Assert.Equal(500, contents.Value, "all of it");
        }

        /// <summary>
        /// The other direction. The forge holds the charge in its own slots, so lifting the
        /// crucible out has to deal with the ore too. With no player involved - the slot emptied
        /// programmatically - it goes onto the ground at the forge, as a firepit's cooking slots do.
        /// </summary>
        [VsTest]
        public async Task TakingTheCrucibleOutOfTheForgeWithNoPlayerDropsTheCharge()
        {
            World.SetBlock("game:forge", ForgePos);
            await Ticks(2);
            foreach (var e in World.Entities(ForgePos, 8).OfType<EntityItem>()) e.Die();

            var be = Forge;
            be.WorkItemSlot.Itemstack = World.Stack(Crucible);
            be.ChargeSlots[0].Itemstack = World.Stack(CopperNugget, 10);
            be.ChargeSlots[1].Itemstack = World.Stack(TinNugget, 5);
            be.MarkDirty(true);
            await Ticks(2);

            ItemStack crucible = be.WorkItemSlot.TakeOutWhole();
            be.WorkItemSlot.MarkDirty();
            await Ticks(2);

            int copper = World.Entities(ForgePos, 8).OfType<EntityItem>()
                .Where(e => e.Itemstack?.Collectible.Code.Path == "nugget-nativecopper").Sum(e => e.Itemstack.StackSize);
            int tin = World.Entities(ForgePos, 8).OfType<EntityItem>()
                .Where(e => e.Itemstack?.Collectible.Code.Path == "nugget-cassiterite").Sum(e => e.Itemstack.StackSize);

            Log($"  charge slots after take-out: {string.Join(", ", Forge.ChargeSlots.Select(s => s.Empty ? "-" : $"{s.StackSize}x{s.Itemstack.Collectible.Code.Path}"))}");
            Log($"  on the ground: {copper} copper, {tin} tin");

            Assert.Null(crucible.Attributes?["contents"], "the crucible stack carries none of it");
            Assert.True(Forge.ChargeEmpty, "nothing stayed behind in the forge");
            Assert.Equal(10, copper, "the copper is on the ground at the forge");
            Assert.Equal(5, tin, "and so is the tin");
        }

        /// <summary>A melt that finishes empties the charge itself; the hand-back must not fire on it.</summary>
        [VsTest]
        public async Task AMeltCompletingDropsNothing()
        {
            World.SetBlock("game:forge", ForgePos);
            await Ticks(2);
            foreach (var e in World.Entities(ForgePos, 8).OfType<EntityItem>()) e.Die();

            var be = Forge;
            be.WorkItemSlot.Itemstack = World.Stack(Crucible);
            be.ChargeSlots[0].Itemstack = World.Stack(CopperNugget, 10);
            be.MarkDirty(true);
            await Ticks(2);

            DummySlot output = new DummySlot();
            be.WorkItemStack.Collectible.DoSmelt(Sapi.World, be.ChargeProvider, be.WorkItemSlot, output);
            be.WorkItemSlot.Itemstack = output.Itemstack;
            be.WorkItemSlot.MarkDirty();
            await Ticks(2);

            Assert.True(be.WorkItemStack?.Collectible is BlockSmeltedContainer, "the crucible went molten");
            Assert.True(be.ChargeEmpty, "the charge was consumed");
            Assert.Equal(0, World.Entities(ForgePos, 8).OfType<EntityItem>().Count(), "and nothing hit the floor");
        }
    }
}

namespace Crucibulum.Tests
{
    /// <summary>
    /// The same journey through the firepit's own window, packet for packet, the way a player
    /// actually does it: crucible clicked into the input slot, ore clicked into the cooking slots,
    /// crucible clicked back out onto the cursor, then the crucible clicked into a forge.
    /// </summary>
    public class CrucibleTransferViaWindow
    {
        static BlockPos FirepitPos => P(6, 1, 8);
        static BlockPos ForgePos => P(9, 0, 8);

        static BlockEntityFirepit Firepit => World.BE<BlockEntityFirepit>(FirepitPos);
        static BlockEntityCrucibulumForge Forge => World.BE<BlockEntityCrucibulumForge>(ForgePos);

        const int InputSlot = 1;
        const int FirstCookingSlot = 3;

        static string Slots(InventoryBase inv, int from, int count) =>
            string.Join(", ", Enumerable.Range(from, count).Select(i =>
                inv[i].Empty ? "-" : $"{inv[i].StackSize}x{inv[i].Itemstack.Collectible.Code.Path}"));

        static int Dropped(string codePath) =>
            World.Entities(FirepitPos, 8)
                .OfType<EntityItem>()
                .Where(e => e.Itemstack?.Collectible.Code.Path == codePath)
                .Sum(e => e.Itemstack.StackSize);

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

        /// <summary>Puts a stack on the cursor on both sides, or clears it with null.</summary>
        static async Task OnCursor(string code, int qty)
        {
            await OnServer();
            var s = Player.Me.InventoryManager.MouseItemSlot;
            s.Itemstack = code == null ? null : World.Stack(code, qty);
            s.MarkDirty();
            await Ticks(2);

            await OnClient();
            var c = Capi.World.Player.InventoryManager.MouseItemSlot;
            if (code == null) c.Itemstack = null;
            else
            {
                var loc = new AssetLocation(code);
                CollectibleObject obj = (CollectibleObject)Capi.World.GetItem(loc) ?? Capi.World.GetBlock(loc);
                c.Itemstack = new ItemStack(obj, qty);
            }
            c.MarkDirty();
            await OnServer();
        }

        /// <summary>Exactly the packet the slot grid sends for a left click on a slot.</summary>
        static async Task ClickSlot(BlockPos pos, int slotId, int qty)
        {
            await OnClient();
            var be = (BlockEntityContainer)Capi.World.BlockAccessor.GetBlockEntity(pos);
            var op = new ItemStackMoveOperation(Capi.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, qty)
            {
                ActingPlayer = Capi.World.Player
            };
            object packet = be.Inventory.InvNetworkUtil.GetActivateSlotPacket(slotId, op);
            // The client applies the click to its own copy first, as the grid does, then tells the server.
            be.Inventory.ActivateSlot(slotId, Capi.World.Player.InventoryManager.MouseItemSlot, ref op);
            Capi.Network.SendBlockEntityPacket(pos.X, pos.Y, pos.Z, packet);
            await OnServer();
            await Ticks(4);
        }

        /// <summary>
        /// A firepit is half a block high, so the usual aim at the top face sails over it and
        /// selects the ground behind. Aim a quarter-block up instead and click when it is selected.
        /// </summary>
        static async Task UseFirepit()
        {
            await Player.StandNear(FirepitPos, 2);
            await Interact.LookAt(new Vec3d(FirepitPos.X + 0.5, FirepitPos.Y + 0.25, FirepitPos.Z + 0.5));
            bool selected = false;
            for (int i = 0; i < 60 && !selected; i++)
            {
                await OnClient();
                var sel = Capi.World.Player.CurrentBlockSelection;
                selected = sel != null && sel.Position.Equals(FirepitPos);
                if (!selected) await Frames.Wait(2);
            }
            var now = Capi.World.Player.CurrentBlockSelection;
            Log($"  selection: {now?.Position} {(now == null ? "" : Sapi.World.BlockAccessor.GetBlock(now.Position)?.Code?.ToString())}");
            await OnServer();
            Assert.True(selected, "the firepit is selected");
            await Input.Click(EnumMouseButton.Right, 3);
            await Ticks(2);
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task OreClickedIntoTheFirepitWindowDropsWhenTheCrucibleIsClickedOut()
        {
            World.SetBlock("game:firepit-cold", FirepitPos);
            World.SetBlock("game:forge", ForgePos);
            await Ticks(2);
            await Player.StandNear(FirepitPos);
            foreach (var e in World.Entities(FirepitPos, 8).OfType<EntityItem>()) e.Die();

            foreach (string cls in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
            {
                IInventory inv = Player.Me.InventoryManager.GetOwnInventory(cls);
                if (inv == null) continue;
                foreach (var slot in inv) { if (!slot.Empty) { slot.Itemstack = null; slot.MarkDirty(); } }
            }
            await Ticks(2);

            await UseFirepit();
            await Gui.WaitFor<GuiDialogBlockEntityFirepit>(120);

            await OnCursor("game:crucible-brown-fired", 1);
            await ClickSlot(FirepitPos, InputSlot, 1);
            Assert.NotNull(Firepit.Inventory[InputSlot].Itemstack, "the crucible went in");

            await OnCursor("game:nugget-nativecopper", 10);
            await ClickSlot(FirepitPos, FirstCookingSlot, 10);
            await OnCursor("game:nugget-cassiterite", 5);
            await ClickSlot(FirepitPos, FirstCookingSlot + 1, 5);
            Log($"  server cooking slots loaded: {Slots(Firepit.Inventory, FirstCookingSlot, 4)}");
            Assert.Equal(10, Firepit.Inventory[FirstCookingSlot].StackSize, "copper in the crucible");
            Assert.Equal(5, Firepit.Inventory[FirstCookingSlot + 1].StackSize, "tin in the crucible");

            // Empty cursor, click the crucible: it comes out onto the cursor.
            await OnCursor(null, 0);
            await ClickSlot(FirepitPos, InputSlot, 1);
            await Ticks(4);

            Log($"  server cooking slots after take-out: {Slots(Firepit.Inventory, FirstCookingSlot, 4)}");
            await OnClient();
            var clientPit = (BlockEntityFirepit)Capi.World.BlockAccessor.GetBlockEntity(FirepitPos);
            Log($"  client cooking slots after take-out: {Slots(clientPit.Inventory, FirstCookingSlot, 4)}");
            Log($"  cursor holds: {Capi.World.Player.InventoryManager.MouseItemSlot.Itemstack?.Collectible.Code}");
            await OnServer();
            foreach (var e in World.Entities(FirepitPos, 8).OfType<EntityItem>())
            {
                var rel = e.ServerPos.XYZ.Sub(FirepitPos.X, FirepitPos.Y, FirepitPos.Z);
                Log($"  entity {e.Itemstack?.StackSize}x{e.Itemstack?.Collectible.Code.Path} at firepit+({rel.X:0.00}, {rel.Y:0.00}, {rel.Z:0.00})");
            }
            int copperGround = Dropped("nugget-nativecopper"), tinGround = Dropped("nugget-cassiterite");
            int copperPlayer = OnPlayer("nugget-nativecopper"), tinPlayer = OnPlayer("nugget-cassiterite");
            Log($"  on the ground: {copperGround} copper, {tinGround} tin; already picked up by the player: {copperPlayer} copper, {tinPlayer} tin");

            Assert.Null(Firepit.Inventory[InputSlot].Itemstack, "the crucible left the firepit");
            Assert.Equal(10, copperGround + copperPlayer, "every copper nugget is either on the ground or already in the player's pockets");
            Assert.Equal(5, tinGround + tinPlayer, "and every tin nugget");

            await Input.Press(GlKeys.Escape);
            await Gui.WaitGone<GuiDialogBlockEntityFirepit>(120);

            // On to the forge, with the crucible on the cursor -> into the hand, then a plain click.
            // Clear the cursor on both sides and put the (now empty) crucible in the hand instead.
            await OnCursor(null, 0);
            await Player.Hold("game:crucible-brown-fired", 1);
            await Player.StandNear(ForgePos);
            await Ticks(2);
            await Interact.UseBlock(ForgePos, BlockFacing.UP);
            await Ticks(6);

            Assert.NotNull(Forge.WorkItemStack, "the forge took the crucible");
            Log($"  forge charge slots: {string.Join(", ", Forge.ChargeSlots.Select(s => s.Empty ? "-" : $"{s.StackSize}x{s.Itemstack.Collectible.Code.Path}"))}");
            Assert.True(Forge.ChargeEmpty, "the forge shows exactly what arrived: an empty crucible");
        }

        static async Task EmptyPockets()
        {
            foreach (string cls in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
            {
                IInventory inv = Player.Me.InventoryManager.GetOwnInventory(cls);
                if (inv == null) continue;
                foreach (var slot in inv) { if (!slot.Empty) { slot.Itemstack = null; slot.MarkDirty(); } }
            }
            await Ticks(2);
        }

        static async Task<BlockEntityCrucibulumForge> AChargedForge()
        {
            World.SetBlock("game:forge", ForgePos);
            await Ticks(2);
            foreach (var e in World.Entities(ForgePos, 8).OfType<EntityItem>()) e.Die();

            var be = Forge;
            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
            be.ChargeSlots[0].Itemstack = World.Stack("game:nugget-nativecopper", 10);
            be.ChargeSlots[1].Itemstack = World.Stack("game:nugget-cassiterite", 5);
            be.MarkDirty(true);
            await Ticks(2);
            await Player.StandNear(ForgePos);
            await EmptyPockets();
            return be;
        }

        static int OnGround(string codePath) =>
            World.Entities(ForgePos, 8).OfType<EntityItem>()
                .Where(e => e.Itemstack?.Collectible.Code.Path == codePath).Sum(e => e.Itemstack.StackSize);

        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task ShiftClickingTheCrucibleOutHandsTheChargeBackToo()
        {
            await AChargedForge();

            await TestSneak.Click(ForgePos, BlockFacing.UP);

            Log($"  player has: {OnPlayer("crucible-brown-fired")} crucible, {OnPlayer("nugget-nativecopper")} copper, {OnPlayer("nugget-cassiterite")} tin;"
              + $" ground: {OnGround("nugget-nativecopper")} copper, {OnGround("nugget-cassiterite")} tin");

            Assert.Null(Forge.WorkItemStack, "the crucible left the forge");
            Assert.True(Forge.ChargeEmpty, "and took the charge with it");
            Assert.Equal(1, OnPlayer("crucible-brown-fired"), "the player has the crucible");
            Assert.Equal(10, OnPlayer("nugget-nativecopper"), "and the copper");
            Assert.Equal(5, OnPlayer("nugget-cassiterite"), "and the tin");
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task DraggingTheCrucibleOutOfTheWindowHandsTheChargeBack()
        {
            await AChargedForge();

            await Interact.UseBlock(ForgePos, BlockFacing.UP);
            await Gui.WaitFor<GuiDialogCrucibleForge>(120);

            // Empty cursor, click the crucible slot: the crucible comes out onto the cursor and the
            // window, which belongs to the crucible, closes.
            await OnCursor(null, 0);
            await ClickSlot(ForgePos, 0, 1);
            await Ticks(4);

            await OnClient();
            Log($"  cursor holds: {Capi.World.Player.InventoryManager.MouseItemSlot.Itemstack?.Collectible.Code}");
            await OnServer();
            Log($"  player has: {OnPlayer("nugget-nativecopper")} copper, {OnPlayer("nugget-cassiterite")} tin;"
              + $" ground: {OnGround("nugget-nativecopper")} copper, {OnGround("nugget-cassiterite")} tin");

            Assert.Null(Forge.WorkItemStack, "the crucible left the forge");
            Assert.True(Forge.ChargeEmpty, "nothing stayed behind");
            Assert.Equal(10, OnPlayer("nugget-nativecopper"), "the copper went to the player at the window");
            Assert.Equal(5, OnPlayer("nugget-cassiterite"), "and the tin");

            await Gui.CloseDialogs();
            await OnCursor(null, 0);
        }

        #region One crucible

        /// <summary>
        /// A forge holding one crucible, with its window open - which is the only state the work
        /// item slot is ever on screen in, since the window belongs to the crucible.
        /// </summary>
        static async Task<BlockEntityCrucibulumForge> AWindowOnASeatedCrucible()
        {
            World.SetBlock("game:forge", ForgePos);
            await Ticks(2);
            foreach (var e in World.Entities(ForgePos, 8).OfType<EntityItem>()) e.Die();

            var be = Forge;
            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
            foreach (var s in be.ChargeSlots) s.Itemstack = null;
            be.MarkDirty(true);
            await Ticks(2);

            await Player.StandNear(ForgePos);
            await EmptyPockets();
            await Interact.UseBlock(ForgePos, BlockFacing.UP);
            await Gui.WaitFor<GuiDialogCrucibleForge>(120);
            return Forge;
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task TheWorkSlotTakesOneCrucibleAndRefusesASecond()
        {
            // Fired crucibles stack to four, and BlockSmeltingContainer.DoSmelt writes one molten
            // crucible over the whole input stack - so every vessel the slot lets in behind the
            // first is destroyed the moment the charge runs. Vanilla's click path never stacks one,
            // because its merge branch is guarded by !forgable; the window puts the slot on screen
            // and the guard with it.
            await AWindowOnASeatedCrucible();

            await OnCursor("game:crucible-brown-fired", 3);
            await ClickSlot(ForgePos, 0, 3);

            Log($"  work slot: {Forge.WorkItemSlot.StackSize}x{Forge.WorkItemStack?.Collectible.Code.Path}");
            await OnClient();
            int onCursor = Capi.World.Player.InventoryManager.MouseItemSlot.StackSize;
            await OnServer();
            Log($"  still on the cursor: {onCursor}");

            Assert.Equal(1, Forge.WorkItemSlot.StackSize, "the forge holds one crucible, not four");

            await Gui.CloseDialogs();
            await OnCursor(null, 0);
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task ADifferentCrucibleSwapsOneForOne()
        {
            // The flip path, which is the one that asks neither the slot's remaining space nor the
            // collectible's stack size: two crucibles that will not merge trade places wholesale, so
            // a stack of four red ones would land on top of the brown one without this.
            await AWindowOnASeatedCrucible();

            await OnCursor("game:crucible-red-fired", 4);
            await ClickSlot(ForgePos, 0, 4);

            Log($"  work slot: {Forge.WorkItemSlot.StackSize}x{Forge.WorkItemStack?.Collectible.Code.Path}");
            Assert.Equal(1, Forge.WorkItemSlot.StackSize, "still exactly one vessel in the forge");

            await Gui.CloseDialogs();
            await OnCursor(null, 0);
        }

        [VsTest(TimeoutMs = 180000)]
        public async Task ACrucibleStackedByAnOlderBuildIsHandedBackRatherThanMelted()
        {
            // A world saved before the slot refused a second vessel can still have one seated, and
            // the melt is exactly the moment it would disappear. Assigned straight into the slot
            // because that is the only way to reproduce a save the current slot would not allow.
            World.SetBlock("game:forge", ForgePos);
            await Ticks(2);
            foreach (var e in World.Entities(ForgePos, 8).OfType<EntityItem>()) e.Die();

            var be = Forge;
            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired", 3);
            be.ChargeSlots[0].Itemstack = World.Stack("game:nugget-nativecopper", 20);
            be.FuelSlot.Itemstack = World.Stack("game:coke", 8);
            be.TryIgnite();
            be.MarkDirty(true);

            // The block entity accrues melt time on its own 200ms listener, in real time, so the
            // calendar is moved once to bring the crucible up and then the melt is waited out.
            await World.TickNow(ForgePos);
            await Hours(1);
            await World.TickNow(ForgePos);
            await Until(() => IsMolten(Forge), 900, "the crucible turning molten");

            Log($"  work slot: {Forge.WorkItemSlot.StackSize}x{Forge.WorkItemStack?.Collectible.Code.Path}"
              + $", on the ground: {OnGround("crucible-brown-fired")}");

            Assert.True(IsMolten(Forge), "the charge melted");
            Assert.Equal(1, Forge.WorkItemSlot.StackSize, "one molten crucible came out");
            Assert.Equal(2, OnGround("crucible-brown-fired"), "and the two behind it were handed back, not eaten");
        }

        static bool IsMolten(BlockEntityCrucibulumForge be) =>
            be.WorkItemStack?.Collectible is BlockSmeltedContainer;

        #endregion
    }
}
