using System.Linq;
using System.Threading.Tasks;
using Crucibulum;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Crucibulum.Tests
{
    /// <summary>
    /// CrucibleOnlyInForge: the firepit turns crucibles away, so the forge is the only place to melt.
    ///
    /// The firepit takes a crucible by C# class on every path in, so the switch is a Harmony
    /// postfix on the firepit inventory's accept methods. Every path is driven twice against a
    /// real firepit, once with the switch off - which must be vanilla, since off is the default
    /// and the patch is installed regardless - and once with it on. The way *out* is checked too:
    /// a crucible already sitting in a firepit when the switch is thrown must still come out.
    /// </summary>
    public class FirepitCrucible
    {
        [BeforeEach]
        public async Task KeepThePlayerAlive() => await TestLife.Alive();

        [AfterEach]
        public Task SwitchOff()
        {
            CrucibulumModSystem.Config.CrucibleOnlyInForge = false;
            return Task.CompletedTask;
        }

        static void Switch(bool on) => CrucibulumModSystem.Config.CrucibleOnlyInForge = on;

        static BlockPos FirepitPos => P(8, 0, 8);
        static BlockEntityFirepit Firepit => World.BE<BlockEntityFirepit>(FirepitPos);

        const string Crucible = "game:crucible-brown-fired";
        const string MoltenCrucible = "game:crucible-brown-smelted";

        static async Task<BlockEntityFirepit> AFirepit()
        {
            World.SetBlock("game:firepit-cold", FirepitPos);
            await Ticks(2);
            return Firepit;
        }

        static async Task<BlockEntityFirepit> AFirepitHoldingACrucible()
        {
            var be = await AFirepit();
            be.inputSlot.Itemstack = World.Stack(Crucible);
            be.inputSlot.MarkDirty();
            await Ticks(1);
            return be;
        }

        /// <summary>What BlockFirepit.OnBlockInteractStart does with a crucible in hand.</summary>
        static int ClickIn(BlockEntityFirepit be, ItemSlot hand) =>
            hand.TryPutInto(Sapi.World, be.inputSlot, 1);

        /// <summary>What a shift-click in the inventory does: ask for the best slot, then move.</summary>
        static (ItemSlot slot, int moved) ShiftClickIn(BlockEntityFirepit be, ItemSlot hand)
        {
            var op = new ItemStackMoveOperation(Sapi.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, 1);
            WeightedSlot best = be.Inventory.GetBestSuitedSlot(hand, op);
            return (best?.slot, best?.slot == null ? 0 : hand.TryPutInto(best.slot, ref op));
        }

        static string Name(ItemSlot slot) => slot == null ? "no slot" : slot.GetType().Name;

        [VsTest]
        public async Task TheShippedDefaultIsOff()
        {
            Assert.False(new CrucibulumConfig().CrucibleOnlyInForge, "a fresh config has the switch off");
            await Task.CompletedTask;
        }

        // -- a click on the block ---------------------------------------------------------------

        [VsTest]
        public async Task Off_AClickSetsTheCrucibleIn()
        {
            var be = await AFirepit();
            Switch(false);
            var hand = new DummySlot(World.Stack(Crucible, 2));

            Assert.Equal(1, ClickIn(be, hand), "one crucible moved");
            Assert.False(be.inputSlot.Empty, "the firepit holds it");
            Assert.Equal(1, hand.StackSize, "leaving one in hand");
        }

        [VsTest]
        public async Task On_AClickIsRefused()
        {
            var be = await AFirepit();
            Switch(true);
            var hand = new DummySlot(World.Stack(Crucible, 2));

            Assert.Equal(0, ClickIn(be, hand), "nothing moved");
            Assert.True(be.inputSlot.Empty, "the firepit is still empty");
            Assert.Equal(2, hand.StackSize, "and the hand is untouched");
        }

        // -- a drag in the firepit window, and a flip -------------------------------------------

        [VsTest]
        public async Task Off_ADragIntoTheWindowIsAccepted()
        {
            var be = await AFirepit();
            Switch(false);
            var hand = new DummySlot(World.Stack(Crucible));

            Assert.True(be.inputSlot.CanHold(hand), "CanHold, which a drag asks");
            Assert.True(be.inputSlot.CanTakeFrom(hand), "CanTakeFrom, which a put asks");
            Assert.True(be.inputSlot.TryFlipWith(hand), "and a flip goes through");
            Assert.False(be.inputSlot.Empty, "the firepit holds it");
            Assert.True(hand.Empty, "and the hand is empty");
        }

        [VsTest]
        public async Task On_ADragIntoTheWindowIsRefused()
        {
            var be = await AFirepit();
            Switch(true);
            var hand = new DummySlot(World.Stack(Crucible));

            Assert.False(be.inputSlot.CanHold(hand), "CanHold, which a drag asks");
            Assert.False(be.inputSlot.CanTakeFrom(hand), "CanTakeFrom, which a put asks");
            Assert.False(be.inputSlot.TryFlipWith(hand), "and a flip is refused");
            Assert.True(be.inputSlot.Empty, "the firepit is still empty");
            Assert.Equal(1, hand.StackSize, "and the hand still has it");
        }

        // -- a shift-click from the inventory ---------------------------------------------------

        [VsTest]
        public async Task Off_AShiftClickLandsItInTheInputSlot()
        {
            var be = await AFirepit();
            Switch(false);
            var hand = new DummySlot(World.Stack(Crucible));

            var (slot, moved) = ShiftClickIn(be, hand);

            Assert.Equal(1, moved, "moved into " + Name(slot));
            Assert.True(ReferenceEquals(slot, be.inputSlot), "and it was the input slot, not " + Name(slot));
            Assert.False(be.inputSlot.Empty, "the firepit holds it");
        }

        [VsTest]
        public async Task On_AShiftClickMovesNothing()
        {
            // With the input slot closed the fuel slot, which takes anything, is what the firepit
            // offers - and the move into it has to fail too, or the crucible ends up sitting in the
            // fuel slot of a firepit that is supposed to refuse it.
            var be = await AFirepit();
            Switch(true);
            var hand = new DummySlot(World.Stack(Crucible));

            var (slot, moved) = ShiftClickIn(be, hand);

            Assert.Equal(0, moved, "nothing moved into " + Name(slot));
            Assert.True(be.Inventory.All(s => s.Empty), "every firepit slot is still empty");
            Assert.Equal(1, hand.StackSize, "and the hand still has the crucible");
        }

        // -- the fuel slot ----------------------------------------------------------------------

        [VsTest]
        public async Task Off_TheFuelSlotTakesACrucibleAsVanillaDoes()
        {
            // Vanilla's fuel slot is a plain survival slot and takes anything; a crucible in it
            // just does not burn. Off means off, so that quirk is left exactly as it was.
            var be = await AFirepit();
            Switch(false);
            var hand = new DummySlot(World.Stack(Crucible));

            Assert.True(be.fuelSlot.CanHold(hand), "a drag into the fuel slot is accepted");
            Assert.Equal(1, hand.TryPutInto(Sapi.World, be.fuelSlot, 1), "and so is a put");
        }

        [VsTest]
        public async Task On_TheFuelSlotRefusesIt()
        {
            var be = await AFirepit();
            Switch(true);
            var hand = new DummySlot(World.Stack(Crucible));

            Assert.False(be.fuelSlot.CanHold(hand), "a drag into the fuel slot is refused");
            Assert.Equal(0, hand.TryPutInto(Sapi.World, be.fuelSlot, 1), "and so is a put");
            Assert.True(be.fuelSlot.Empty, "the fuel slot is still empty");
        }

        // -- a molten crucible, back for reheating ----------------------------------------------

        [VsTest]
        public async Task Off_AMoltenCrucibleGoesInToReheat()
        {
            var be = await AFirepit();
            Switch(false);
            var hand = new DummySlot(World.Stack(MoltenCrucible));

            Assert.Equal(1, ClickIn(be, hand), "it went in");
            Assert.Equal("crucible-brown-smelted", be.inputSlot.Itemstack.Collectible.Code.Path, "what the firepit holds");
        }

        [VsTest]
        public async Task On_AMoltenCrucibleIsRefusedToo()
        {
            // Reheating a melt is the other reason to put a crucible in a firepit, and the switch
            // says the forge does that now.
            var be = await AFirepit();
            Switch(true);
            var hand = new DummySlot(World.Stack(MoltenCrucible));

            Assert.Equal(0, ClickIn(be, hand), "nothing moved");
            Assert.True(be.inputSlot.Empty, "the firepit is still empty");
        }

        // -- what is not caught ------------------------------------------------------------------

        [VsTest]
        public async Task EitherWay_OreInHandIsNotCaughtByIt()
        {
            // The refusal is for crucibles only. Ore still lands in the input slot, where it sits
            // exactly as it did before - vanilla will not smelt it without a container either way.
            foreach (bool on in new[] { false, true })
            {
                var be = await AFirepit();
                Switch(on);
                var hand = new DummySlot(World.Stack("game:nugget-nativecopper", 4));

                Assert.Equal(1, ClickIn(be, hand), $"the nugget went in with the switch {(on ? "on" : "off")}");
            }
        }

        [VsTest]
        public async Task EitherWay_TheForgeTakesACrucible()
        {
            var forgePos = P(12, 0, 8);
            foreach (bool on in new[] { false, true })
            {
                // Cleared between rounds. Setting a forge over a forge keeps the block entity, so
                // the second round used to be putting a crucible into a forge that already had one
                // - which passed only because the slot would stack them, and it no longer will.
                World.SetBlock("game:air", forgePos);
                await Ticks(1);
                World.SetBlock("game:forge", forgePos);
                await Ticks(2);
                var forge = World.BE<BlockEntityCrucibulumForge>(forgePos);
                Switch(on);
                var hand = new DummySlot(World.Stack(Crucible));

                Assert.Equal(1, hand.TryPutInto(Sapi.World, forge.WorkItemSlot, 1),
                    $"the forge takes it with the switch {(on ? "on" : "off")}");
                Assert.NotNull(forge.CrucibleStack, "and holds it as a crucible");
            }
        }

        // -- the way out -----------------------------------------------------------------------

        [VsTest]
        public async Task Off_ACrucibleComesOutOfAFirepit()
        {
            var be = await AFirepitHoldingACrucible();
            Switch(false);
            var hand = new DummySlot();

            Assert.Equal(1, be.inputSlot.TryPutInto(Sapi.World, hand, 1), "it came out");
            Assert.True(be.inputSlot.Empty, "and the firepit is empty");
        }

        [VsTest]
        public async Task On_ACrucibleAlreadyInAFirepitStillComesOut()
        {
            // Nobody is stranded by throwing the switch on a world with crucibles in firepits.
            var be = await AFirepitHoldingACrucible();
            Switch(true);
            var hand = new DummySlot();

            Assert.Equal(1, be.inputSlot.TryPutInto(Sapi.World, hand, 1), "it came out");
            Assert.True(be.inputSlot.Empty, "and the firepit is empty");
            Assert.Equal("crucible-brown-fired", hand.Itemstack.Collectible.Code.Path, "the crucible is what came out");
        }

        // -- the switch itself -----------------------------------------------------------------

        [VsTest]
        public async Task TheSwitchIsReadLive()
        {
            // Flipping it in a config screen takes effect on the next click, with no restart:
            // the patch is installed once and reads the setting on every call.
            var be = await AFirepit();
            var hand = new DummySlot(World.Stack(Crucible, 3));

            Switch(true);
            Assert.Equal(0, ClickIn(be, hand), "refused with it on");
            Switch(false);
            Assert.Equal(1, ClickIn(be, hand), "accepted with it off again");
            Switch(true);
            Assert.False(be.fuelSlot.CanHold(hand), "and refused once more with it back on");
        }

        [VsTest]
        public async Task ThePatchIsInstalledExactlyOnce()
        {
            // In singleplayer both sides resolve the same assembly, and a PatchAll per side would
            // register every postfix twice. This one is a boolean-and so a double would be
            // invisible - which is exactly why it is asserted rather than trusted.
            var patched = new[]
            {
                typeof(ItemSlotInput).GetMethod(nameof(ItemSlotInput.CanTakeFrom)),
                typeof(ItemSlotInput).GetMethod(nameof(ItemSlotInput.CanHold)),
                typeof(InventorySmelting).GetMethod(nameof(InventorySmelting.CanContain)),
            };
            foreach (var method in patched)
            {
                Assert.NotNull(method, "the patched method still exists");
                var info = Harmony.GetPatchInfo(method);
                Assert.NotNull(info, $"{method.DeclaringType.Name}.{method.Name} is patched");
                Assert.Equal(1, info.Postfixes.Count(p => p.owner == FirepitCruciblePatch.HarmonyId),
                    $"exactly one postfix of ours on {method.DeclaringType.Name}.{method.Name}");
            }
            await Task.CompletedTask;
        }
    }

    /// <summary>The same switch, driven through a real click on the firepit.</summary>
    public class FirepitCrucibleClientSide
    {
        [BeforeEach]
        public async Task KeepThePlayerAlive() => await TestLife.Alive();

        [AfterEach]
        public Task SwitchOff()
        {
            CrucibulumModSystem.Config.CrucibleOnlyInForge = false;
            return Task.CompletedTask;
        }

        // On the surface, not sunk into it: a firepit's selection box is half a block high, and one
        // set into the ground is hidden behind the soil around it from where the player stands.
        static BlockPos FirepitPos => P(8, 1, 8);
        static BlockEntityFirepit Firepit => World.BE<BlockEntityFirepit>(FirepitPos);
        const string Crucible = "game:crucible-brown-fired";

        static async Task AFirepitInReach()
        {
            World.SetBlock("game:firepit-cold", FirepitPos);
            await Ticks(2);
            await Player.StandNear(FirepitPos);
        }

        static async Task ClickWithACrucible()
        {
            await Player.Hold(Crucible, 2);
            await Ticks(2);
            await Interact.UseBlock(FirepitPos);
            await Ticks(6);
        }

        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task Off_AClickSetsItIn()
        {
            await AFirepitInReach();
            CrucibulumModSystem.Config.CrucibleOnlyInForge = false;

            await ClickWithACrucible();

            Assert.False(Firepit.inputSlot.Empty, "the firepit took the crucible");
            Assert.Equal(1, Player.Held.StackSize, "one left in hand");
            await Gui.CloseDialogs();
        }

        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task On_AClickIsRefusedAndTheWindowOpensInstead()
        {
            await AFirepitInReach();
            CrucibulumModSystem.Config.CrucibleOnlyInForge = true;

            await ClickWithACrucible();

            Assert.True(Firepit.inputSlot.Empty, "the firepit did not take the crucible");
            Assert.Equal(2, Player.Held.StackSize, "and the hand still has both");
            Assert.True(await Gui.IsOpen<GuiDialogBlockEntityFirepit>(),
                "the firepit window opened, as it does for any other item that does not go in");
            await Gui.CloseDialogs();
        }
    }
}
