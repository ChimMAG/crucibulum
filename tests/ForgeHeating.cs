using System.Threading.Tasks;
using Crucibulum;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Crucibulum.Tests
{
    /// <summary>
    /// The shape of a melt: come up to temperature, change state, then hold.
    ///
    /// Each of those is a different amount of work for the fire, and the fuel bill follows. Heating
    /// and melting are real energy - sensible heat, then latent heat. Holding metal that is already
    /// liquid only replaces what the crucible loses to the air.
    /// </summary>
    public class ForgeHeating
    {
        static BlockPos ForgePos => P(8, 0, 8);
        static BlockEntityCrucibulumForge Forge => World.BE<BlockEntityCrucibulumForge>(ForgePos);

        static async Task<BlockEntityCrucibulumForge> ALitForge()
        {
            World.SetBlock("game:air", ForgePos);
            await Ticks(1);
            World.SetBlock("game:forge", ForgePos);
            await Ticks(2);

            var be = Forge;
            be.FuelSlot.Itemstack = World.Stack("game:coke", 8);
            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
            be.TryIgnite();
            be.MarkDirty(true);
            return be;
        }

        static void SetTemp(BlockEntityCrucibulumForge be, float t) =>
            be.WorkItemStack.Collectible.SetTemperature(Sapi.World, be.WorkItemStack, t);

        static ItemStack Molten(float temp, bool solidTest = false)
        {
            var smelted = (Vintagestory.GameContent.BlockSmeltedContainer)
                Sapi.World.GetBlock(new AssetLocation("game:crucible-brown-smelted"));
            var stack = new ItemStack(smelted);
            smelted.SetContents(stack, World.Stack("game:ingot-copper"), 100);
            stack.Collectible.SetTemperature(Sapi.World, stack, temp);
            return stack;
        }

        [VsTest]
        public async Task TheFireKnowsWhichJobItIsDoing()
        {
            var be = await ALitForge();
            Assert.Equal(CrucibleWork.None, be.WorkState, "an empty crucible is no work at all");

            be.AddCharge(new DummySlot(World.Stack("game:nugget-nativecopper", 20)), 20);
            SetTemp(be, 600);
            Assert.Equal(CrucibleWork.Heating, be.WorkState, "ore well below its melting point");

            SetTemp(be, 1150);
            Assert.Equal(CrucibleWork.Melting, be.WorkState, "ore at its melting point");

            foreach (var s in be.ChargeSlots) s.Itemstack = null;
            be.WorkItemSlot.Itemstack = Molten(1150);
            Assert.Equal(CrucibleWork.Holding, be.WorkState, "metal already liquid");

            be.WorkItemSlot.Itemstack = Molten(400);
            Assert.Equal(CrucibleWork.Heating, be.WorkState,
                "a charge let go solid is a melt to do all over again, not a hold");
        }

        [VsTest]
        public async Task HoldingAMeltIsFarCheaperThanMakingOne()
        {
            var be = await ALitForge();
            be.AddCharge(new DummySlot(World.Stack("game:nugget-nativecopper", 20)), 20);
            SetTemp(be, 1150);
            float melting = be.BurnRate;

            foreach (var s in be.ChargeSlots) s.Itemstack = null;
            be.WorkItemSlot.Itemstack = Molten(1150);
            float holding = be.BurnRate;

            Assert.Less(holding, melting * 0.5f, "holding costs a fraction of what melting does");
        }

        [VsTest(TimeoutMs = 90000)]
        public async Task ABiggerChargeIsSlowerToHeat()
        {
            // The same fire poured into more metal raises it more slowly, so a brim-full crucible is
            // a long job before any of it begins to melt. Vanilla does not do this for crucibles -
            // the firepit damps by the container's stack size, which for a crucible is always one.
            var be = await ALitForge();

            float empty = await StepFrom(be, 400);
            Assert.Close(be.ThermalMass(), 1.0, 0.001, "an empty crucible is the baseline");

            be.AddCharge(new DummySlot(World.Stack("game:nugget-nativecopper", 20)), 20);
            be.MarkDirty(true);
            float oneIngot = await StepFrom(be, 400);

            // Straight into the empty slots rather than topping up the first, so this is about
            // mass and not about merge rules.
            be.ChargeSlots[1].Itemstack = World.Stack("game:nugget-nativecopper", 120);
            be.MarkDirty(true);
            Assert.Close(be.ChargeIngotEquivalents(), 7.0, 0.01, "140 nuggets is seven ingots' worth");
            float brimFull = await StepFrom(be, 400);

            Assert.Greater(empty, oneIngot, "one ingot's worth already slows it");
            Assert.Greater(oneIngot, brimFull, "and a full crucible slower still");

            // Deliberately a band rather than the exact ratio: this is measured off real tick
            // firings, and pinning it to the arithmetic makes the test about the harness's timing.
            Assert.Less(brimFull, empty * 0.5f,
                "a brim-full crucible climbs at well under half the rate of an empty one");
        }

        [VsTest]
        public async Task AMoltenCrucibleCarriesItsOwnMassToo()
        {
            // Once poured, the metal lives in the crucible stack rather than the charge slots. A
            // crucible that has gone solid has to be reheated, and it is just as heavy as it was.
            var be = await ALitForge();
            be.WorkItemSlot.Itemstack = Molten(400);
            be.MarkDirty(true);

            Assert.Close(be.ChargeIngotEquivalents(), 1.0, 0.01, "100 units is one ingot");
            Assert.Greater(be.ThermalMass(), 1.0f, "and it weighs the same going back up");
        }

        [VsTest(TimeoutMs = 90000)]
        public async Task DousingTheForgeReallyCoolsTheCrucible()
        {
            // The block entity keeps its own running temperature so the curve is not fought over by
            // the vanilla forge tick. That must not swallow a genuine cooling: a bucket of water or
            // a rainstorm goes through BlockEntityForge.CoolNow, and if the next tick simply put the
            // old number back the crucible would shrug off being doused.
            var be = await ALitForge();
            PutCrucibleAt(be, 1100);
            await World.TickNow(ForgePos);

            be.CoolNow(1f, (slot, pos, temp, sound) =>
                slot.Itemstack?.Collectible.SetTemperature(Sapi.World, slot.Itemstack, temp));

            await World.TickNow(ForgePos);

            float after = be.WorkItemStack.Collectible.GetTemperature(Sapi.World, be.WorkItemStack);
            Assert.Less(after, 400f, "the crucible actually cooled and stayed cooled");
        }

        [VsTest]
        public async Task HoldingStillCostsAtLeastAnIdleForge()
        {
            // Cheap, but not free: the crucible is still losing heat to the air.
            var be = await ALitForge();
            float idle = be.BurnRate;

            be.WorkItemSlot.Itemstack = Molten(1150);
            Assert.GreaterOrEqual(be.BurnRate, idle, "keeping metal liquid is not cheaper than burning empty");
        }

        [VsTest(TimeoutMs = 90000)]
        public async Task TheClimbEasesInRatherThanStoppingDead()
        {
            // Heat flux follows the temperature difference, so the last stretch is the slow one. A
            // linear ramp that slams into the ceiling is the thing this replaced.
            //
            // Measured over a short interval on purpose: give it a full in-game hour and both
            // readings just hit the ceiling, and the test would be measuring the clamp.
            var be = await ALitForge();

            float cold = await StepFrom(be, 20);
            float nearlyThere = await StepFrom(be, 1100);

            Assert.Greater(cold, nearlyThere * 3,
                "a cold crucible climbs far faster than one already near the ceiling");
            Assert.Greater(nearlyThere, 0f, "but the last stretch is still moving");
        }

        [VsTest(TimeoutMs = 90000)]
        public async Task MeltingChokesTheClimb()
        {
            // Latent heat: at the melting point the fire's energy goes into changing the metal's
            // state, so the temperature all but stops rising until the charge is through.
            //
            // Same crucible, same temperature, same distance to the ceiling - the only difference
            // is whether there is a charge in it actually changing state.
            var be = await ALitForge();

            float empty = await StepFrom(be, 1090);
            Assert.Equal(CrucibleWork.None, be.WorkState, "nothing in it to melt");

            // A single nugget, so that the crucible's own thermal mass swamps the charge's and this
            // measures the latent-heat choke rather than the weight of the metal.
            be.AddCharge(new DummySlot(World.Stack("game:nugget-nativecopper", 1)), 1);
            be.MarkDirty(true);
            Assert.Less(be.ThermalMass(), 1.05f, "one nugget barely counts as mass");

            float melting = await StepFrom(be, 1090);
            Assert.Equal(CrucibleWork.Melting, be.WorkState, "1090 C is past copper's melting point");

            Assert.Greater(empty, melting * 3,
                "the climb is choked once the charge is actually changing state");
        }

        /// <summary>
        /// Degrees gained over a short burst of burning from a given temperature. Short on purpose:
        /// a long one saturates at the ceiling and measures nothing.
        /// </summary>
        static async Task<float> StepFrom(BlockEntityCrucibulumForge be, float from)
        {
            // A *fresh* crucible at that temperature, which is what a player does and what makes
            // the block entity re-read the temperature rather than carrying its own running value
            // forward. Writing over the existing stack would be silently ignored, by design.
            PutCrucibleAt(be, from);

            await World.TickNow(ForgePos);   // baseline: takes the clock reading
            PutCrucibleAt(be, from);         // undo whatever that tick nudged
            await Hours(0.01);               // 1.2 real seconds of burning
            await World.TickNow(ForgePos);   // does the heating

            return be.WorkItemStack.Collectible.GetTemperature(Sapi.World, be.WorkItemStack) - from;
        }

        static void PutCrucibleAt(BlockEntityCrucibulumForge be, float temp)
        {
            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
            SetTemp(be, temp);
            be.MarkDirty(true);
        }

        #region Cold metal

        const string CopperNugget = "game:nugget-nativecopper";

        static float CrucibleTemp() =>
            Forge.CrucibleStack.Collectible.GetTemperature(Sapi.World, Forge.CrucibleStack);

        /// <summary>A lit forge whose crucible and charge have both settled at <paramref name="temp"/>.</summary>
        static async Task<BlockEntityCrucibulumForge> ASettledCrucible(float temp, int nuggets)
        {
            var be = await ALitForge();
            be.ChargeSlots[0].Itemstack = World.Stack(CopperNugget, nuggets);
            SetTemp(be, temp);
            be.ChargeSlots[0].Itemstack.Collectible.SetTemperature(Sapi.World, be.ChargeSlots[0].Itemstack, temp);
            be.MarkDirty(true);

            // One tick with nothing changing, so the crucible and the charge share a cooling clock
            // and the next tick's reading is not an artefact of setting them up by hand.
            await World.TickNow(ForgePos);
            return Forge;
        }

        [VsTest]
        public async Task SwappingAHotChargeForAnEquallyHeavyColdOneStillCostsTheHeat()
        {
            // The hole this closes. Cold metal used to be spotted by the charge getting *heavier*,
            // because the charge always read one tick behind the crucible and that was the only way
            // to tell real cold ore from the lag. Trade twenty hot nuggets for twenty cold ones
            // between two ticks and the mass never moves, so nothing was charged - and the sync at
            // the end of the tick then heated the new ore to the crucible's temperature for free.
            var be = await ASettledCrucible(1000f, 20);
            float before = CrucibleTemp();

            // The swap, as a player doing it quickly between two 200ms ticks: same item, same count,
            // cold. No amount of clicking can guarantee landing inside one tick, so it is staged.
            be.ChargeSlots[0].Itemstack = World.Stack(CopperNugget, 20);
            be.ChargeSlots[0].Itemstack.Collectible.SetTemperature(Sapi.World, be.ChargeSlots[0].Itemstack, 20f);
            be.MarkDirty(true);

            await World.TickNow(ForgePos);
            float after = CrucibleTemp();

            Log($"  crucible {before:0} degC -> {after:0} degC after an equal-mass cold swap");
            Assert.Less(after, before - 100f, "the crucible pays for the cold metal");
        }

        [VsTest]
        public async Task AChargeThatHasNotChangedIsNotChargedForAgain()
        {
            // The other side of it, and the reason the mass guard was there: settling up on the
            // charge every tick would drag the crucible down for ever and it would never melt
            // anything. Equalising before the fire is applied is what makes the reading honest.
            var be = await ASettledCrucible(1000f, 20);

            float start = CrucibleTemp();
            for (int i = 0; i < 6; i++) await World.TickNow(ForgePos);
            float end = CrucibleTemp();

            Log($"  crucible {start:0} degC -> {end:0} degC over six quiet ticks");
            Assert.Greater(end, start - 1f, "a settled charge takes nothing out of the crucible");
        }

        [VsTest(TimeoutMs = 90000)]
        public async Task ColdOreAddedThroughTheChargeSlotsCoolsTheCrucible()
        {
            // The same penalty by the route a player actually takes: metal into the charge slots,
            // nothing else. AddCharge no longer does any thermal work of its own, so this and a
            // drag through the window are the same code path from here on.
            var be = await ASettledCrucible(1000f, 5);
            float before = CrucibleTemp();

            var hand = new DummySlot(World.Stack(CopperNugget, 20));
            hand.Itemstack.Collectible.SetTemperature(Sapi.World, hand.Itemstack, 20f);
            Assert.Greater(be.AddCharge(hand, 20), 0, "the nuggets went in");

            await World.TickNow(ForgePos);
            float after = CrucibleTemp();

            Log($"  crucible {before:0} degC -> {after:0} degC after twenty cold nuggets");
            Assert.Less(after, before - 100f, "a fistful of cold ore sets the melt back");
        }

        #endregion
    }
}
