using System.Linq;
using System.Threading.Tasks;
using Crucibulum;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Crucibulum.Tests
{
    /// <summary>
    /// Loading and unloading the crucible.
    ///
    /// The charge deliberately lives in the forge rather than in the crucible stack, the way a
    /// firepit holds it: a crucible carried off with ore in it would be invisible to the firepit,
    /// which reads its ingredients from whatever heat source is holding it.
    ///
    /// These load the charge through ForgeFixtures.AddCharge, which is the suite's own transport
    /// into the slots and does nothing thermal, so they run headless. A player reaches neither of
    /// these by clicking: shift is the crucible itself now, and loading the charge is the window's
    /// job, which puts ore in through the ordinary inventory machinery. What the cold metal costs
    /// is the block entity's tick either way.
    /// </summary>
    public class ForgeCharging
    {
        static BlockPos ForgePos => P(8, 0, 8);
        static BlockEntityCrucibulumForge Forge => World.BE<BlockEntityCrucibulumForge>(ForgePos);

        const string Crucible = "game:crucible-brown-fired";
        const string CopperNugget = "game:nugget-nativecopper";

        static async Task<BlockEntityCrucibulumForge> PlaceForge(string crucible = Crucible)
        {
            World.SetBlock("game:forge", ForgePos);
            await Ticks(2);

            var be = Forge;
            if (crucible != null) be.WorkItemSlot.Itemstack = World.Stack(crucible);
            be.MarkDirty(true);
            return be;
        }

        static DummySlot Holding(string code, int quantity) => new DummySlot(World.Stack(code, quantity));

        /// <summary>
        /// World.Entities' code filter matches the *entity* code, which for a dropped stack is
        /// always "game:item" - so the itemstack has to be looked at directly.
        /// </summary>
        static int DroppedNuggets() =>
            World.Entities(ForgePos, 6)
                .OfType<EntityItem>()
                .Where(e => e.Itemstack?.Collectible.Code.Path == "nugget-nativecopper")
                .Sum(e => e.Itemstack.StackSize);


        [VsTest]
        public async Task NuggetsGoIntoTheCrucibleOneAtATime()
        {
            var be = await PlaceForge();
            var hand = Holding(CopperNugget, 8);

            Assert.Equal(1, be.AddCharge(hand), "nuggets moved by a plain shift-click");
            Assert.Equal(1, Forge.ChargeSlots[0].StackSize, "what ended up in the crucible");
            Assert.Equal(7, hand.StackSize, "what stayed in hand");
        }

        [VsTest]
        public async Task AWholeStackCanGoInAtOnce()
        {
            var be = await PlaceForge();
            var hand = Holding(CopperNugget, 8);

            Assert.Equal(8, be.AddCharge(hand, hand.StackSize), "the whole stack moved");
            Assert.True(hand.Empty, "the hand emptied");
        }

        [VsTest]
        public async Task NoCrucibleMeansNoCharge()
        {
            // The property and the transfer, because for a while only the property was asserted and
            // nothing in the mod consulted it: it read false while the slots took ore anyway.
            var be = await PlaceForge(crucible: null);

            Assert.False(be.CanAcceptCharge, "an empty forge accepts no charge");
            Assert.Equal(0, be.AddCharge(Holding(CopperNugget, 8), 8), "and none goes in");
            Assert.True(Forge.ChargeEmpty, "the slots stayed empty");
            await Task.CompletedTask;
        }

        [VsTest]
        public async Task AMoltenCrucibleTakesNoMoreOre()
        {
            var be = await PlaceForge("game:crucible-brown-smelted");

            Assert.False(be.CanAcceptCharge, "a crucible that has already gone molten");
            Assert.Equal(0, be.AddCharge(Holding(CopperNugget, 8), 8), "and takes no more ore");
            Assert.True(Forge.ChargeEmpty, "the slots stayed empty");
            await Task.CompletedTask;
        }

        [VsTest]
        public async Task ACrucibleGoesIntoAnEmptyForge()
        {
            var be = await PlaceForge(crucible: null);
            var hand = Holding(Crucible, 3);

            Assert.True(be.TrySetCrucible(hand), "setting a crucible in an empty forge");
            Assert.Equal("crucible-brown-fired", Forge.WorkItemStack?.Collectible.Code.Path, "what the forge holds");
            Assert.Equal(1, Forge.WorkItemStack.StackSize, "exactly one");
            Assert.Equal(2, hand.StackSize, "the other two stayed in hand");
        }

        [VsTest]
        public async Task ASecondCrucibleIsRefusedRatherThanMerged()
        {
            // BEForge's "merge heatable item" branch is guarded by !forgable, and the crucible
            // deliberately is not - so a refused crucible must not reach it, or it stacks a second
            // one into the slot and DoSmelt silently eats it.
            var be = await PlaceForge();
            var hand = Holding(Crucible, 3);

            Assert.False(be.TrySetCrucible(hand), "a forge that already holds a crucible");
            Assert.Equal(1, Forge.WorkItemStack.StackSize, "still exactly one crucible in the forge");
            Assert.Equal(3, hand.StackSize, "nothing was taken from the hand");
        }

        [VsTest]
        public async Task AWarmCrucibleCanStillBeToppedUp()
        {
            // Two stacks at different temperatures will not merge, so once the charge had warmed up
            // at all, adding more of the same ore was silently refused - the click did nothing and
            // looked like the forge rejecting it.
            var be = await PlaceForge();
            be.AddCharge(Holding(CopperNugget, 20), 20);

            be.ChargeSlots[0].Itemstack.Collectible.SetTemperature(Sapi.World, be.ChargeSlots[0].Itemstack, 600f);
            be.WorkItemStack.Collectible.SetTemperature(Sapi.World, be.WorkItemStack, 600f);

            Assert.Equal(20, be.AddCharge(Holding(CopperNugget, 20), 20), "a warm crucible still takes ore");
            Assert.Equal(40, Forge.ChargeSlots[0].StackSize, "and it lands in the slot already holding some");
        }

        [VsTest]
        public async Task ColdOreDroppedStraightIntoASlotAlsoCoolsTheMelt()
        {
            // The window puts metal into the charge slots through the ordinary inventory
            // machinery, never through a helper - so for a while this path skipped the cooling
            // penalty that shift-clicking paid, and a crucible could be topped up for free by
            // opening the window instead. The settling now happens on the tick, so every route in
            // pays it.
            var be = await PlaceForge();
            be.AddCharge(Holding(CopperNugget, 20), 20);
            be.WorkItemStack.Collectible.SetTemperature(Sapi.World, be.WorkItemStack, 1000f);
            be.ChargeSlots[0].Itemstack.Collectible.SetTemperature(Sapi.World, be.ChargeSlots[0].Itemstack, 1000f);

            // One firing to take the baseline. The settling is a delta - it fires on metal
            // arriving, not on metal merely being present - so it needs a "before" to compare to.
            await World.TickNow(ForgePos);

            // Exactly what a drag into the window does: straight into the slot, at hand temperature.
            var cold = World.Stack(CopperNugget, 60);
            cold.Collectible.SetTemperature(Sapi.World, cold, 20f);
            be.ChargeSlots[1].Itemstack = cold;
            be.MarkDirty(true);

            await World.TickNow(ForgePos);

            float after = Forge.WorkItemStack.Collectible.GetTemperature(Sapi.World, Forge.WorkItemStack);
            Log($"  crucible 1000 degC + 60 cold nuggets dropped in -> {after:0} degC");
            Assert.Less(after, 1000f, "the crucible cooled");
            Assert.Greater(after, 20f, "but not all the way to the ore's temperature");
        }

        [VsTest]
        public async Task ColdOreCoolsTheMelt()
        {
            // And it costs you: cold metal tipped into a hot crucible drags the whole lot down,
            // weighted by how much was already in there.
            //
            // Charged on the tick rather than where the metal goes in. AddCharge used to do its own
            // mixing, which meant the one route a player actually takes - a drag into the window -
            // was settled up by different arithmetic; now there is one model and this waits for it.
            var be = await PlaceForge();
            be.AddCharge(Holding(CopperNugget, 20), 20);
            be.WorkItemStack.Collectible.SetTemperature(Sapi.World, be.WorkItemStack, 1000f);
            be.ChargeSlots[0].Itemstack.Collectible.SetTemperature(Sapi.World, be.ChargeSlots[0].Itemstack, 1000f);

            be.AddCharge(Holding(CopperNugget, 60), 60);
            await World.TickNow(ForgePos);

            float after = Forge.WorkItemStack.Collectible.GetTemperature(Sapi.World, Forge.WorkItemStack);
            Assert.Less(after, 1000f, "the crucible cooled");
            Assert.Greater(after, 20f, "but not all the way to the ore's temperature");
        }

        [VsTest]
        public async Task FuelStillGoesToTheFuelSlot()
        {
            // The charge filter runs before the vanilla forge's, so it has to refuse anything the
            // forge would rather burn. Coal ore carries a burn temperature above 1000 and would
            // otherwise vanish into the crucible instead of feeding the fire.
            //
            // Asked of the slots themselves, with a crucible in place so the charge slots are open
            // for business - the predicate on its own would not show that the fuel slot is where
            // coal actually ends up. This used to ask a copy of the filter on the block entity that
            // nothing consulted and that never checked the crucible's mouth at all.
            var be = await PlaceForge();

            foreach (string fuel in new[] { "game:ore-bituminouscoal", "game:coke", "game:charcoal" })
            {
                var hand = new DummySlot(World.Stack(fuel, 4));
                Assert.False(be.ChargeSlots[0].CanHold(hand), $"{fuel} is not a charge");
                Assert.Equal(0, be.AddCharge(hand, 4), "and none of it goes into the crucible");
                Assert.True(be.FuelSlot.CanHold(hand), "it is fuel, and the fuel slot wants it");
            }

            Assert.True(be.ChargeSlots[0].CanHold(new DummySlot(World.Stack(CopperNugget, 4))), "a copper nugget is a charge");
            Assert.True(ItemSlotCrucibleCharge.Accepts(World.Stack("game:ingot-copper")),
                "and an ingot is something a crucible melts - whether it fits is the mouth's business");

            await Task.CompletedTask;
        }

        [VsTest]
        public async Task FourDifferentIngredientsFitForAnAlloy()
        {
            // Bronze needs two, and the vanilla crucible offers four slots. Same-item clicks merge,
            // different items take a fresh slot.
            var be = await PlaceForge();

            var copper = Holding(CopperNugget, 4);
            be.AddCharge(copper);
            be.AddCharge(copper);

            var tin = Holding("game:nugget-cassiterite", 4);
            be.AddCharge(tin);

            Assert.Equal(2, Forge.ChargeSlots[0].StackSize, "copper merged into one slot");
            Assert.Equal("nugget-cassiterite", Forge.ChargeSlots[1].Itemstack?.Collectible.Code.Path,
                "tin took the next slot");
        }

        [VsTest]
        public async Task TakingTheCrucibleLeavesTheChargeBehind()
        {
            // Same as a firepit: the ore belongs to the heat source. Pulling the crucible out mid-melt
            // must not delete it.
            var be = await PlaceForge();
            be.AddCharge(Holding(CopperNugget, 4), 4);

            be.WorkItemSlot.Itemstack = null;
            be.MarkDirty(true);
            await Ticks(2);

            Assert.False(Forge.ChargeEmpty, "the charge survived the crucible leaving");

            Assert.True(Forge.TakeCharge(null), "the charge can be pulled back out");
            Assert.True(Forge.ChargeEmpty, "the forge is empty afterwards");

            await Until(() => DroppedNuggets() == 4, 60,
                "with nobody to hand it to, the charge lands on the ground rather than vanishing");
        }

        [VsTest]
        public async Task BreakingTheForgeDropsTheCharge()
        {
            var be = await PlaceForge();
            be.AddCharge(Holding(CopperNugget, 4), 4);

            Sapi.World.BlockAccessor.BreakBlock(ForgePos, null);

            await Until(() => DroppedNuggets() == 4, 60,
                "the charge dropped as an item rather than being cleared away");
        }

        [VsTest]
        public async Task TheChargeSurvivesASaveAndReload()
        {
            // The charge lives in an inventory of our own, next to the forge's, so it is only
            // persisted because ToTreeAttributes says so. Silence here means ore disappearing on
            // chunk unload.
            var be = await PlaceForge();
            be.AddCharge(Holding(CopperNugget, 3), 3);

            var tree = new Vintagestory.API.Datastructures.TreeAttribute();
            be.ToTreeAttributes(tree);

            be.TakeCharge(null);
            Assert.True(Forge.ChargeEmpty, "cleared before reloading");

            be.FromTreeAttributes(tree, Sapi.World);

            Assert.Equal(3, Forge.ChargeSlots[0].StackSize, "the charge came back off the tree");
            await Task.CompletedTask;
        }
    }
}
