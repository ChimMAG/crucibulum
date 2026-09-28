using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Crucibulum;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Crucibulum.Tests
{
    /// <summary>
    /// The blast gate: a sheet of metal fitted across the forge's air inlet.
    ///
    /// A forge is not set to a temperature, it is given air. Vanilla already models that - the
    /// ceiling is <c>MaxTemperature * (1 + extraOxygenRate)</c> and a bellows drives the multiplier
    /// up - so the gate is the same lever below one rather than a new mechanic. Nothing here should
    /// let a player choose a number.
    /// </summary>
    public class ForgeBlastGate
    {
        /// <summary>A forge is a heat source and these tests stand next to lit ones. See TestLife.</summary>
        [BeforeEach]
        public async Task KeepThePlayerAlive() => await TestLife.Alive();

        static BlockPos ForgePos => P(8, 0, 8);
        static BlockEntityCrucibulumForge Forge => World.BE<BlockEntityCrucibulumForge>(ForgePos);

        static async Task<BlockEntityCrucibulumForge> AForge(bool lit = true)
        {
            World.SetBlock("game:air", ForgePos);
            await Ticks(1);
            World.SetBlock("game:forge", ForgePos);
            await Ticks(2);

            var be = Forge;
            if (lit)
            {
                be.FuelSlot.Itemstack = World.Stack("game:coke", 4);
                be.TryIgnite();
            }
            be.MarkDirty(true);
            return be;
        }

        [VsTest]
        public async Task AForgeWithoutAGateIsUntouched()
        {
            // The whole feature has to be invisible until someone fits a plate, or every forge in
            // every world quietly changes behaviour.
            var be = await AForge();
            Assert.False(be.HasGate, "no gate to start with");
            Assert.Close(1f, be.AirFactor, 0.0001f, "full draught");
            Assert.Close(be.MaxTemperature * (1 + 0f), be.CrucibleMaxTemperature() - CrucibulumModSystem.Config.CrucibleTempBonus, 1f,
                "and the ceiling is exactly what it always was");
        }

        [VsTest]
        public async Task APlateFittedBecomesTheGate()
        {
            var be = await AForge();
            var hand = new DummySlot(World.Stack("game:metalplate-copper", 3));

            Assert.True(be.FitGate(hand, null), "the plate was taken");
            Assert.True(be.HasGate, "the forge has a gate");
            Assert.Equal("copper", be.GateMetal, "made of what was fitted");
            Assert.Equal(2, hand.StackSize, "one plate, not the stack");

            Assert.True(be.TakeGate(null), "and it comes back out");
            Assert.False(be.HasGate, "leaving a plain forge");
            await Task.CompletedTask;
        }

        [VsTest]
        public async Task TheGateThrottlesTheFireAndTheFuel()
        {
            var be = await AForge();
            float openCeiling = be.CrucibleMaxTemperature();
            float openBurn = be.BurnRate;

            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Shut);
            await Ticks(2);

            Log($"  open: {openCeiling:0}degC at burn {openBurn:0.###} | shut: {Forge.CrucibleMaxTemperature():0}degC at burn {Forge.BurnRate:0.###}");

            Assert.Less(Forge.CrucibleMaxTemperature(), openCeiling, "a shut gate lowers the ceiling");
            Assert.Less(Forge.BurnRate, openBurn, "and burns less fuel, because that is what less air means");
        }

        [VsTest]
        public async Task EveryPositionSitsBetweenShutAndOpen()
        {
            var be = await AForge();
            float last = float.MaxValue;

            foreach (var position in new[] { GatePosition.Open, GatePosition.Half, GatePosition.Quarter, GatePosition.Shut })
            {
                be.FitGateForTesting(World.Stack("game:metalplate-copper"), position);
                float ceiling = be.CrucibleMaxTemperature();
                Log($"  {position}: {ceiling:0} degC");
                Assert.Less(ceiling, last, $"{position} is cooler than the notch before it");
                last = ceiling;
            }
            await Task.CompletedTask;
        }

        [VsTest]
        public async Task AWorkItemIsHeldAtWhatTheDampedFireReaches()
        {
            // The point of the thing, and the part vanilla fights: its tick drives the work item at
            // MaxTemperature regardless, and that property is not virtual. Ours has to pull it back.
            var be = await AForge();
            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Shut);

            var ingot = World.Stack("game:ingot-copper");
            be.WorkItemSlot.Itemstack = ingot;
            ingot.Collectible.SetTemperature(Sapi.World, ingot, 1100f);
            be.MarkDirty(true);

            float ceiling = be.MaxTemperature * BlastGate.AirFactor(GatePosition.Shut);
            for (int i = 0; i < 40; i++)
            {
                await Hours(0.05);
                await Ticks(10);
            }

            float temp = Forge.WorkItemStack.Collectible.GetTemperature(Sapi.World, Forge.WorkItemStack);
            Log($"  ingot settled at {temp:0} degC against a damped ceiling of {ceiling:0} degC");
            Assert.Less(temp, 1100f, "the ingot came down off full forge heat");
            Assert.Less(temp, ceiling + 60f, "to about what the damped fire can hold");
        }

        [VsTest]
        public async Task TheReadoutBlamesTheGateRatherThanTheFuel()
        {
            // Coke can melt copper easily. If the gate is what is stopping it, saying "this fuel
            // tops out" sends someone off to find better fuel they already have.
            var be = await AForge();
            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
            be.AddCharge(new DummySlot(World.Stack("game:nugget-nativecopper", 20)), 20);
            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Shut);
            be.MarkDirty(true);
            await Ticks(2);

            var sb = new StringBuilder();
            Forge.GetBlockInfo(null, sb);
            string info = sb.ToString();
            Log("  block info: " + info.Replace("\n", " | ").Trim());

            Assert.Contains(info, "Blast gate", "the readout says where the gate is");
            Assert.Contains(info, "gate is holding", "and blames the gate, not the coke");
        }

        [VsTest]
        public async Task TheGateComesBackWhenTheForgeIsBroken()
        {
            var be = await AForge();
            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Half);
            await Ticks(2);

            // Items only. World.Entities returns everything in radius, the player included, and
            // clearing leftovers with an unfiltered loop kills him - no damage source, no death
            // reason, just a death screen that then blocks every interaction test after it.
            foreach (var e in World.Entities(ForgePos, 6).OfType<EntityItem>()) e.Die(EnumDespawnReason.Removed);
            await Ticks(2);

            be.OnBlockBroken(null);
            World.SetBlock("game:air", ForgePos);
            await Until(() => World.Entities(ForgePos, 6).OfType<EntityItem>()
                .Any(x => x.Itemstack?.Collectible.Code.Path.StartsWith("metalplate") == true), 60, "the plate to drop");

            Assert.True(true, "the plate came back rather than being destroyed with the forge");
        }

        [VsTest]
        public async Task TheGateSurvivesASaveAndReload()
        {
            var be = await AForge();
            be.FitGateForTesting(World.Stack("game:metalplate-iron"), GatePosition.Quarter);

            var tree = new Vintagestory.API.Datastructures.TreeAttribute();
            be.ToTreeAttributes(tree);

            be.TakeGate(null);
            Assert.False(Forge.HasGate, "cleared before reloading");

            be.FromTreeAttributes(tree, Sapi.World);

            Assert.True(Forge.HasGate, "the gate came back off the tree");
            Assert.Equal(GatePosition.Quarter, Forge.GatePosition, "at the notch it was left on");
            Assert.Equal("iron", Forge.GateMetal, "in the metal it was made of");
            await Task.CompletedTask;
        }

        [VsTest]
        public async Task TurningTheFeatureOffLeavesTheForgeAlone()
        {
            var be = await AForge();
            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Shut);
            float damped = be.CrucibleMaxTemperature();

            CrucibulumModSystem.Config.EnableBlastGate = false;
            try
            {
                Assert.Close(1f, Forge.AirFactor, 0.0001f, "a disabled gate throttles nothing");
                Assert.Greater(Forge.CrucibleMaxTemperature(), damped, "and the fire is back to full draught");
            }
            finally
            {
                CrucibulumModSystem.Config.EnableBlastGate = true;
            }
            await Task.CompletedTask;
        }

        /// <summary>Opens the crucible window on a forge that has a gate fitted.</summary>
        static async Task<GuiDialogCrucibleForge> AWindowOverAGatedForge(bool withGate = true)
        {
            // Unlit, and the player out of harm's way. These tests stand someone next to a forge
            // repeatedly, and a lit one is a heat source: left burning across the class the player
            // cooks, dies, and the death screen then blocks every interaction that follows. The
            // gate needs no fire to be read or worked.
            var be = await AForge(lit: false);
            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
            if (withGate) be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Open);
            be.MarkDirty(true);

            // Long enough for the block entity's tree to reach the client, which is what the window
            // is drawn from - a few ticks and the gate simply reads as absent.
            await Ticks(30);
            await Player.StandNear(ForgePos);

            // With a plate in hand this click fits a gate instead of opening the window, and the
            // failure then reads as the forge refusing to open. Tests share one player, and several
            // in this class hand a plate back to them.
            Log($"  in hand: {Player.Me.InventoryManager.ActiveHotbarSlot.Itemstack?.Collectible.Code.Path ?? "nothing"}");
            await EmptyHand();

            await Interact.UseBlock(ForgePos, BlockFacing.UP);
            return await Gui.WaitFor<GuiDialogCrucibleForge>(120);
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task TheWindowCarriesTheGate()
        {
            // A forge holding a crucible opens this window on a click, so the click that works the
            // gate on a bare forge is not available - the button is the only way to reach it
            // without pulling the crucible out first.
            var dlg = await AWindowOverAGatedForge();

            await OnClient();
            var button = dlg.SingleComposer.GetButton("gateButton");
            string label = (button as GuiElementTextButton)?.Text;
            await OnServer();

            Log($"  gate button reads: \"{label}\"");
            Assert.NotNull(button, "the window offers the gate");
            Assert.Contains(label ?? "", "Blast gate", "and says what it is");

            await Input.Press(GlKeys.Escape);
            await Gui.WaitGone<GuiDialogCrucibleForge>(120);
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task AForgeWithNoGateHasNoGateControl()
        {
            // Separate test rather than a second window in the one above: opening, closing and
            // reopening back to back left the second open racing the first close, and it failed
            // intermittently on a dialog that had not finished going away.
            var dlg = await AWindowOverAGatedForge(withGate: false);

            await OnClient();
            bool present = dlg.SingleComposer.GetButton("gateButton") != null;
            await OnServer();

            Assert.False(present, "no gate fitted, no button");

            await Input.Press(GlKeys.Escape);
            await Gui.WaitGone<GuiDialogCrucibleForge>(120);
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task ClickingTheButtonWorksTheGate()
        {
            // Driven through the button's own mouse handler rather than by sending the packet
            // directly, so this fails if the button is ever wired to nothing.
            var dlg = await AWindowOverAGatedForge();
            Assert.Equal(GatePosition.Open, Forge.GatePosition, "starts open");

            await OnClient();
            var button = dlg.SingleComposer.GetButton("gateButton");
            int x = (int)(button.Bounds.absX + button.Bounds.OuterWidth / 2);
            int y = (int)(button.Bounds.absY + button.Bounds.OuterHeight / 2);
            button.OnMouseDownOnElement(Capi, new MouseEvent(x, y, EnumMouseButton.Left, 0));
            button.OnMouseUpOnElement(Capi, new MouseEvent(x, y, EnumMouseButton.Left, 0));
            await OnServer();

            await Until(() => Forge.GatePosition == GatePosition.Half, 80, "the gate to move a notch");
            Log($"  after one click the gate is {Forge.GatePosition}");

            await Input.Press(GlKeys.Escape);
            await Gui.WaitGone<GuiDialogCrucibleForge>(120);
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task ThePlateAndTheInletAreActuallyDrawn()
        {
            // The shapes are assets, and a missing or malformed one fails silently: the first cut
            // of this drew nothing at all and the only symptom was a bare forge in a screenshot.
            var be = await AForge(lit: false);
            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Half);
            be.MarkDirty(true);
            await Ticks(30);

            await OnClient();
            var clientBe = (BlockEntityCrucibulumForge)Capi.World.BlockAccessor.GetBlockEntity(ForgePos);
            bool hasGate = clientBe?.HasGate == true;

            var gate = Mesh(clientBe, "GateMesh");
            var vent = Mesh(clientBe, "VentMesh");
            Shape shape = Capi.Assets.TryGet("crucibulum:shapes/block/blastgate.json")?.ToObject<Shape>();
            await OnServer();

            Assert.True(hasGate, "the client knows about the gate - without this nothing can be drawn");
            Assert.NotNull(shape, "the plate's shape is in the zip");

            // Against the shape's own element count rather than a bare "more than nothing". Six
            // faces of four vertices each, so a cuboid that never reached the mesh is the
            // difference between this passing and failing - which is how the handle could be
            // present in the asset, asserted on by the test above, and absent from the screenshots.
            int expected = shape.Elements.Length * 24;

            Log($"  gate mesh {gate?.VerticesCount ?? -1} verts for {shape.Elements.Length} elements"
              + $" (want {expected}), inlet mesh {vent?.VerticesCount ?? -1} verts");

            Assert.NotNull(gate, "the plate tesselated");
            Assert.GreaterOrEqual(gate.VerticesCount, expected, "every element of the plate is drawn, handle included");
            Assert.NotNull(vent, "the inlet tesselated");
            Assert.Greater(vent.VerticesCount, 0, "into actual geometry");
        }

        static MeshData Mesh(BlockEntityCrucibulumForge be, string method) =>
            be?.GetType()
              .GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
              ?.Invoke(be, null) as MeshData;

        // Copper: vanilla makes metal workable at half its melting point, so bits and nuggets are
        // workable from 542 degC and molten at 1084.
        const float CopperWorkable = 542f;
        const float CopperMelts = 1084f;

        static async Task<BlockEntityCrucibulumForge> ACrucibleOfCopperBits(GatePosition? gate)
        {
            var be = await AForge();
            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
            be.AddCharge(new DummySlot(World.Stack("game:nugget-nativecopper", 20)), 20);
            be.FuelSlot.Itemstack = World.Stack("game:coke", 6);   // enough not to burn out mid-run
            if (gate != null) be.FitGateForTesting(World.Stack("game:metalplate-copper"), gate.Value);
            be.MarkDirty(true);

            peakTemp = 0;
            for (int i = 0; i < 60; i++)
            {
                await Hours(0.05);
                await Ticks(6);
                peakTemp = Math.Max(peakTemp, CrucibleTemp(Forge));
            }
            return Forge;
        }

        /// <summary>
        /// The hottest the crucible got, which is not the same as where it ends up: latent heat
        /// holds the temperature almost still while a charge is actually melting, and the fire
        /// burns down afterwards. Reading the final figure to decide whether something melted
        /// reports 933 degC on a crucible that plainly did.
        /// </summary>
        static float peakTemp;

        static float CrucibleTemp(BlockEntityCrucibulumForge be) =>
            be.CrucibleStack.Collectible.GetTemperature(Sapi.World, be.CrucibleStack);

        [VsTest(TimeoutMs = 240000)]
        public async Task ADampedCrucibleHoldsBitsWorkableWithoutMeltingThem()
        {
            // This is the case the whole gate was asked for: Smithing Plus's bit smithing heats
            // bits and native nuggets in a crucible at the workable temperature without letting
            // them melt, and until now the only control over a forge was which fuel you loaded.
            //
            // It runs through a different path from the work item clamp - ApplyGate leaves a
            // crucible alone because CrucibleMaxTemperature already carries the air factor - so it
            // is worth proving rather than assuming the two agree.
            var be = await ACrucibleOfCopperBits(GatePosition.Quarter);

            float temp = CrucibleTemp(be);
            Log($"  quarter-open crucible settled at {temp:0} degC (workable {CopperWorkable:0}, melts {CopperMelts:0})");

            Assert.Greater(temp, CopperWorkable, "hot enough to work the bits");
            Assert.Less(temp, CopperMelts, "but never hot enough to melt them");
            Assert.Less(peakTemp, CopperMelts, "and never was, at any point in the run");
            Assert.False(BlockEntityCrucibulumForge.IsMoltenCrucible(be.CrucibleStack), "still bits, not a pour");
            Assert.Close(0f, be.MeltProgress, 0.001f, "and no melt has begun");
        }

        [VsTest(TimeoutMs = 240000)]
        public async Task TheSameCrucibleMeltsWithTheGateOpen()
        {
            // The control. Without it the test above passes just as well on a forge that could
            // never melt copper in the first place, and proves nothing about the gate.
            var be = await ACrucibleOfCopperBits(GatePosition.Open);

            Log($"  open crucible peaked at {peakTemp:0} degC, melt progress {be.MeltProgress:0.00}");
            Assert.Greater(peakTemp, CopperMelts, "an open forge takes copper past melting");
            Assert.Greater(be.MeltProgress, 0f, "and the charge is going");
        }

        [VsTest]
        public async Task EveryBitSmithingMetalHasANotchThatHoldsIt()
        {
            // Copper, gold and silver are the three Smithing Plus names for bit smithing. Each
            // wants a ceiling at or above half its melting point and below the melting point
            // itself; if no notch lands in that band the feature is useless for that metal.
            var be = await AForge();
            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");

            foreach (var (metal, melts) in new[] { ("copper", 1084f), ("gold", 1063f), ("silver", 961f) })
            {
                float workable = melts / 2;
                string fits = "";

                foreach (var position in new[] { GatePosition.Open, GatePosition.Half, GatePosition.Quarter, GatePosition.Shut })
                {
                    be.FitGateForTesting(World.Stack("game:metalplate-copper"), position);
                    float ceiling = be.CrucibleMaxTemperature();
                    if (ceiling >= workable && ceiling < melts) fits += (fits == "" ? "" : ", ") + position;
                }

                Log($"  {metal}: workable {workable:0}, melts {melts:0} -> {(fits == "" ? "no notch" : fits)}");
                Assert.False(fits == "", $"{metal} bits can be held workable without melting");
            }
            await Task.CompletedTask;
        }


        /// <summary>
        /// The report this suite was extended for: a gate on a forge holding an ingot rather than a
        /// crucible. Every one of these failed against 1.1.0.
        /// </summary>
        static float WorkItemTemp() =>
            Forge.WorkItemStack.Collectible.GetTemperature(Sapi.World, Forge.WorkItemStack);

        static async Task Burn(int rounds)
        {
            for (int i = 0; i < rounds; i++)
            {
                await Hours(0.05);
                await Ticks(10);
            }
        }

        [VsTest(TimeoutMs = 180000)]
        public async Task AColdIngotHeatsUnderAThrottledGate()
        {
            // ApplyGate's shadow only ever fell, so an ingot put into a damped forge was pinned at
            // whatever it went in at and never heated at all - 95 degC against a 680 degC ceiling.
            var be = await AForge();
            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Half);

            var ingot = World.Stack("game:ingot-tinbronze");
            be.WorkItemSlot.Itemstack = ingot;
            ingot.Collectible.SetTemperature(Sapi.World, ingot, 20f);
            be.MarkDirty(true);

            float ceiling = be.MaxTemperature * BlastGate.AirFactor(GatePosition.Half);
            await Burn(40);

            float temp = WorkItemTemp();
            Log($"  a cold ingot reached {temp:0} degC against a half-open ceiling of {ceiling:0}");
            Assert.Greater(temp, ceiling - 60f, "a damped fire still heats the metal, it only stops lower");
            Assert.Less(temp, ceiling + 60f, "and stops where the air puts it");
        }

        [VsTest(TimeoutMs = 240000)]
        public async Task OpeningTheGateAgainLetsTheIngotClimbBack()
        {
            // The other half of the same bug, and the one the report described: with the shadow
            // left stale while the gate was open, any restricted notch wrote a reading from minutes
            // ago straight back over the metal and held it there.
            var be = await AForge();
            var ingot = World.Stack("game:ingot-tinbronze");
            be.WorkItemSlot.Itemstack = ingot;
            ingot.Collectible.SetTemperature(Sapi.World, ingot, 20f);
            be.MarkDirty(true);

            await Burn(20);
            float open = WorkItemTemp();

            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Shut);
            await Burn(30);
            float shut = WorkItemTemp();

            Forge.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Half);
            await Burn(30);
            float half = WorkItemTemp();

            float shutCeiling = be.MaxTemperature * BlastGate.AirFactor(GatePosition.Shut);
            float halfCeiling = be.MaxTemperature * BlastGate.AirFactor(GatePosition.Half);
            Log($"  open {open:0} -> shut {shut:0} (ceiling {shutCeiling:0}) -> half open {half:0} (ceiling {halfCeiling:0})");

            Assert.Less(shut, shutCeiling + 60f, "shutting the gate brings the piece down");
            Assert.Greater(half, shut + 50f, "and opening it a notch lets the fire climb again");
        }

        [VsTest(TimeoutMs = 120000)]
        public async Task TheCeilingQuotedIsTheOneTheContentsAreHeldAt()
        {
            // A crucible is held above the fuel's own ceiling, so quoting the bare fuel figure at
            // someone watching a crucible was 340 degC out on coke at half open - and the window is
            // the only place that figure is ever seen.
            var be = await AForge();
            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Half);
            be.MarkDirty(true);
            await Ticks(2);

            float bare = Forge.GateCeiling;
            Assert.Close(be.MaxTemperature * BlastGate.AirFactor(GatePosition.Half), bare, 1f,
                "an empty forge quotes what the fuel and the air can do");

            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
            be.AddCharge(new DummySlot(World.Stack("game:nugget-nativecopper", 8)), 8);
            be.MarkDirty(true);
            await Ticks(2);

            var tree = new Vintagestory.API.Datastructures.TreeAttribute();
            Forge.SetDialogValues(tree);
            var sb = new StringBuilder();
            Forge.AppendGateInfo(sb);

            Log($"  empty {bare:0} degC, with a crucible {Forge.GateCeiling:0} degC");
            Log($"  block info: {sb.ToString().Trim()}");

            Assert.Close(Forge.CrucibleMaxTemperature(), Forge.GateCeiling, 1f,
                "with a crucible in, the figure is the one the crucible is actually held at");
            Assert.Equal((int)Forge.CrucibleMaxTemperature(), tree.GetInt("gateCeiling"),
                "and that is what the window's button is told");
            Assert.Contains(sb.ToString(), ((int)Forge.CrucibleMaxTemperature()).ToString(),
                "and what the block info says");
        }

        [VsTest]
        public async Task ThePlateIsFoundWhicheverWayTheForgeFaces()
        {
            // The selection box is a plain cuboid in the blocktype and does not turn with the
            // block, while the plate is drawn into a mesh that does - so a hit has to be rotated
            // back before it can be compared with where the plate was drawn.
            var be = await AForge(lit: false);
            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Open);

            // The face the plate is on, in the block's own frame, and the three it is not.
            var front = new Vec3d(0.5, 0.3, 0.94);
            var back = new Vec3d(0.5, 0.3, 0.06);
            var east = new Vec3d(0.94, 0.3, 0.5);
            var west = new Vec3d(0.06, 0.3, 0.5);

            foreach (var (angle, onThePlate) in new[]
            {
                (0f, front), (GameMath.PIHALF, east), (GameMath.PI, back), (3 * GameMath.PIHALF, west),
            })
            {
                be.MeshAngleRad = angle;

                Assert.True(be.IsGateHit(onThePlate), $"at {angle:0.00} rad the plate is where it is drawn");
                foreach (var other in new[] { front, back, east, west })
                {
                    if (other == onThePlate) continue;
                    Assert.False(be.IsGateHit(other), $"at {angle:0.00} rad it is not on the other faces");
                }

                // The hearth is the forge's, whichever way it faces.
                Assert.False(be.IsGateHit(new Vec3d(onThePlate.X, 0.85, onThePlate.Z)),
                    "and a click high on that same face is a click for what is in the fire");
            }

            be.MeshAngleRad = 0;
            await Task.CompletedTask;
        }

        /// <summary>
        /// A forge standing on the floor rather than sunk into it.
        ///
        /// The plot's ground layer is the same layer P(x, 0, z) sits in, so a forge placed there is
        /// flush with the floor around it: its front face - the one the plate is on - is buried
        /// behind the neighbouring block and no ray can reach it. That is a property of the test
        /// plot and not of the game, where a forge is set down on top of the ground, so these tests
        /// build the scene a player would actually be standing at.
        /// </summary>
        static BlockPos StandingForgePos => P(8, 1, 8);

        static BlockEntityCrucibulumForge StandingForge =>
            World.BE<BlockEntityCrucibulumForge>(StandingForgePos);

        static async Task<BlockEntityCrucibulumForge> AForgeOnTheFloor()
        {
            World.SetBlock("game:air", StandingForgePos);
            await Ticks(1);
            World.SetBlock("game:forge", StandingForgePos);
            await Ticks(2);
            return StandingForge;
        }

        static async Task EmptyHand()
        {
            Player.Me.InventoryManager.ActiveHotbarSlot.Itemstack = null;
            Player.Me.InventoryManager.ActiveHotbarSlot.MarkDirty();
            await Ticks(3);
        }

        /// <summary>Stands south of the forge, which is the side the plate is drawn on.</summary>
        static async Task StandInFrontOfTheForge()
        {
            await Player.Teleport(new Vec3d(StandingForgePos.X + 0.5, StandingForgePos.Y, StandingForgePos.Z + 2.5));
            await EmptyHand();
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task AClickOnThePlateWorksTheGate()
        {
            var be = await AForgeOnTheFloor();
            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Open);
            be.WorkItemSlot.Itemstack = World.Stack("game:ingot-tinbronze");
            be.MarkDirty(true);
            await Ticks(20);

            await StandInFrontOfTheForge();
            var sel = await AimAtThePlate();
            Log($"  aimed at {sel?.Position} {sel?.Face} hit y={sel?.HitPosition.Y:0.00} z={sel?.HitPosition.Z:0.00}");

            await Input.Click(EnumMouseButton.Right, 3);
            await Ticks(6);

            Log($"  gate now {StandingForge.GatePosition}, forge still holds {StandingForge.WorkItemStack?.Collectible.Code.Path ?? "nothing"}");
            Assert.Equal(GatePosition.Half, StandingForge.GatePosition, "clicking the plate slides it a notch");
            Assert.NotNull(StandingForge.WorkItemStack, "and leaves the ingot in the fire");
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task AClickOnTheHearthStillHandsTheIngotBack()
        {
            // What the report was actually about: the gate swallowed this click, so an ingot could
            // only be got out of a gated forge by taking the whole plate off first.
            var be = await AForgeOnTheFloor();
            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Open);
            be.WorkItemSlot.Itemstack = World.Stack("game:ingot-tinbronze");
            be.MarkDirty(true);
            await Ticks(20);

            // From the same spot as the test above, so the only difference between them is where on
            // the forge the click lands.
            await StandInFrontOfTheForge();

            await Interact.UseBlock(StandingForgePos, BlockFacing.UP);
            await Ticks(6);

            Log($"  work slot: {StandingForge.WorkItemStack?.Collectible.Code.Path ?? "empty"}, gate {StandingForge.GatePosition}");
            Assert.True(StandingForge.WorkItemSlot.Empty, "a plain click hands the ingot back, as a vanilla forge does");
            Assert.Equal(GatePosition.Open, StandingForge.GatePosition, "and does not touch the gate");
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task TheGateSaysHowItIsWorked()
        {
            // Three lang keys existed for this from the day the gate was written and nothing ever
            // used them, so nothing in the world said a forge would take a plate at all.
            var be = await AForgeOnTheFloor();
            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Open);
            be.MarkDirty(true);
            await Ticks(20);

            await OnClient();
            var block = Capi.World.BlockAccessor.GetBlock(StandingForgePos);
            var sel = new BlockSelection
            {
                Position = StandingForgePos.Copy(),
                Face = BlockFacing.SOUTH,
                HitPosition = new Vec3d(0.5, 0.3, 0.94),
            };
            var help = block.GetPlacedBlockInteractionHelp(Capi.World, sel, Capi.World.Player);
            await OnServer();

            string[] codes = help.Select(h => h.ActionLangCode).ToArray();
            Log("  help offers: " + string.Join(", ", codes));

            Assert.Contains(string.Join(",", codes), "blockhelp-forge-workgate", "how to work the gate");
            Assert.Contains(string.Join(",", codes), "blockhelp-forge-takegate", "and how to take it off");
        }

        /// <summary>
        /// Looks at the plate until the selection actually lands on it. The plate is a band low on
        /// one face, and where a ray aimed at it comes down depends on how tall the player is and
        /// how far away they are standing - so this walks the aim down the face rather than
        /// assuming a single point works from wherever StandNear happened to put someone.
        /// </summary>
        static async Task<BlockSelection> AimAtThePlate()
        {
            BlockSelection sel = null;
            for (double y = 0.45; y > 0.05; y -= 0.1)
            {
                await Interact.LookAt(new Vec3d(
                    StandingForgePos.X + 0.5, StandingForgePos.Y + y, StandingForgePos.Z + 0.94));
                await Ticks(Interact.AimSettleTicks);

                sel = await ClientBlockSelection();
                if (sel != null && sel.Position.Equals(StandingForgePos)
                    && StandingForge.IsGateHit(sel.HitPosition)) return sel;
            }

            throw new AssertionException(
                $"could not aim at the plate; ended up on {sel?.Position} {sel?.Face} at {sel?.HitPosition}");
        }

        static async Task<BlockSelection> ClientBlockSelection()
        {
            await OnClient();
            var sel = Capi.World.Player.CurrentBlockSelection;
            await OnServer();
            return sel;
        }

        [VsTest]
        public async Task TheNotchesComeFromTheConfig()
        {
            // Every other number in this mod is tunable; these were the one set that was not, which
            // makes them the ones a server owner cannot fix when they do not suit their pack.
            var be = await AForge();
            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Quarter);

            float asShipped = be.CrucibleMaxTemperature();
            float original = CrucibulumModSystem.Config.GateAirQuarter;

            try
            {
                CrucibulumModSystem.Config.GateAirQuarter = original / 2;
                float halved = Forge.CrucibleMaxTemperature();
                Log($"  quarter at {original:0.00} gives {asShipped:0} degC; at {original / 2:0.00} gives {halved:0} degC");
                Assert.Less(halved, asShipped, "turning the notch down cools the fire");
            }
            finally
            {
                CrucibulumModSystem.Config.GateAirQuarter = original;
            }
            await Task.CompletedTask;
        }

        [VsTest]
        public async Task ANonsenseConfigCannotMakeAGateAHeater()
        {
            // A gate is a plate over the air inlet. However it is configured it can only restrict,
            // and it cannot be airtight either - zero would take the ceiling to nothing and divide
            // the burn rate away with it.
            var be = await AForge();
            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Shut);

            float open = CrucibulumModSystem.Config.GateAirOpen;
            float shut = CrucibulumModSystem.Config.GateAirShut;

            try
            {
                CrucibulumModSystem.Config.GateAirShut = 5f;
                Assert.Close(1f, Forge.AirFactor, 0.001f, "clamped to full draught, not five times it");

                CrucibulumModSystem.Config.GateAirShut = 0f;
                Assert.Greater(Forge.AirFactor, 0f, "and never to nothing");
                Assert.Greater(Forge.BurnRate, 0f, "so the fire still burns fuel");
            }
            finally
            {
                CrucibulumModSystem.Config.GateAirOpen = open;
                CrucibulumModSystem.Config.GateAirShut = shut;
            }
            await Task.CompletedTask;
        }

        #region What the gate says, and where

        [VsTest]
        public async Task TheGateIsReadableInEveryStateAForgeCanBeIn()
        {
            // The gate line used to be written inside the charge readout, which only a forge holding
            // a crucible with ore in it ever reaches. Those are the minority of states: a bare gated
            // forge, one keeping an ingot warm, one with an empty crucible and one holding a
            // finished melt all said nothing about the gate, and a gate is read at a glance.
            var be = await AForge();
            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Quarter);
            be.MarkDirty(true);
            await Ticks(2);
            Assert.Contains(BlockInfo(), "Blast gate", "a bare gated forge");

            Forge.WorkItemSlot.Itemstack = World.Stack("game:ingot-tinbronze");
            Forge.MarkDirty(true);
            await Ticks(2);
            Assert.Contains(BlockInfo(), "Blast gate", "one keeping an ingot warm");

            Forge.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
            Forge.MarkDirty(true);
            await Ticks(2);
            Assert.Contains(BlockInfo(), "Blast gate", "one holding an empty crucible");

            var molten = World.Stack("game:crucible-brown-smelted");
            ((BlockSmeltedContainer)molten.Collectible).SetContents(molten, World.Stack("game:ingot-copper"), 300);
            Forge.WorkItemSlot.Itemstack = molten;
            Forge.MarkDirty(true);
            await Ticks(2);
            Assert.Contains(BlockInfo(), "Blast gate", "one holding a finished melt");
        }

        [VsTest]
        public async Task AForgeWithNoGateSaysNothingAboutOne()
        {
            var be = await AForge();
            be.WorkItemSlot.Itemstack = World.Stack("game:ingot-tinbronze");
            be.MarkDirty(true);
            await Ticks(2);

            string info = BlockInfo();
            Log("  block info: " + info.Replace("\n", " | ").Trim());
            Assert.False(info.Contains("Blast gate"), "no plate fitted, nothing to say");
        }

        static string BlockInfo()
        {
            var sb = new StringBuilder();
            Forge.GetBlockInfo(null, sb);
            string info = sb.ToString();
            Log("  block info: " + info.Replace("\n", " | ").Trim());
            return info;
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task FittingAPlateAddsTheButtonToAWindowAlreadyOpen()
        {
            // The window lays itself out again when the status line, the melt bar or the gate's own
            // label changes. Fitting a plate changes none of those - a new gate is open, and open is
            // the ceiling the window was already quoting - so the button simply never appeared until
            // the window was closed and reopened.
            var dlg = await AWindowOverAGatedForge(withGate: false);

            await OnClient();
            bool before = dlg.SingleComposer.GetButton("gateButton") != null;
            await OnServer();
            Assert.False(before, "no gate, no button");

            Forge.FitGate(new DummySlot(World.Stack("game:metalplate-copper")), null);
            await Ticks(30);

            await OnClient();
            var button = dlg.SingleComposer.GetButton("gateButton");
            string label = (button as GuiElementTextButton)?.Text;
            await OnServer();

            Log($"  button after the plate went on: {label ?? "(none)"}");
            Assert.NotNull(button, "the button arrives without closing and reopening the window");
            Assert.Contains(label ?? "", "Blast gate", "and says what it is");

            await Input.Press(GlKeys.Escape);
            await Gui.WaitGone<GuiDialogCrucibleForge>(120);
        }

        #endregion

        #region Tongs

        /// <summary>
        /// Puts tongs in the player's off hand, or empties it. Marked dirty because the client keeps
        /// its own copy of the inventory and the tongs rule is read on both sides - a stack written
        /// server-side and never synced leaves the client predicting a refusal the server allows.
        /// </summary>
        static async Task TongsInTheOffHand(bool holding)
        {
            ItemSlot offhand = Player.Me.Entity.LeftHandItemSlot;
            offhand.Itemstack = holding ? World.Stack("game:tongs") : null;
            offhand.MarkDirty();
            await Ticks(3);
        }

        /// <summary>Runs a body with the tongs requirement on, and puts the config back afterwards.</summary>
        static async Task WithTongsRequired(Func<Task> body)
        {
            bool original = CrucibulumModSystem.Config.RequireTongsForGate;
            CrucibulumModSystem.Config.RequireTongsForGate = true;
            try
            {
                await body();
            }
            finally
            {
                CrucibulumModSystem.Config.RequireTongsForGate = original;
                await TongsInTheOffHand(false);
            }
        }

        [VsTest]
        public async Task TongsAreNotAskedForUntilTheOptionIsOn()
        {
            // Default off. The gate was free to work for four releases and turning this on is a
            // choice a server makes, not something an update does to a world behind its back.
            var be = await AForge();
            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Open);
            await TongsInTheOffHand(false);

            Assert.False(be.GateNeedsTongs, "a burning forge asks for nothing by default");
            Assert.True(be.TryCycleGate(Player.Me), "and bare hands work the gate");
            Assert.Equal(GatePosition.Half, Forge.GatePosition, "which moved a notch");
        }

        [VsTest]
        public async Task AHotPlateRefusesBareHandsAndYieldsToTongs()
        {
            var be = await AForge();
            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Open);

            await WithTongsRequired(async () =>
            {
                Assert.True(Forge.IsBurning, "the forge is lit, so the plate is hot");
                Assert.True(Forge.GateNeedsTongs, "which is when the requirement bites");

                await TongsInTheOffHand(false);
                Assert.False(Forge.TryCycleGate(Player.Me), "bare hands are refused");
                Assert.Equal(GatePosition.Open, Forge.GatePosition, "and the plate does not move");

                await TongsInTheOffHand(true);
                Assert.True(Forge.TryCycleGate(Player.Me), "tongs in the off hand are not");
                Assert.Equal(GatePosition.Half, Forge.GatePosition, "and the plate slides a notch");
            });
        }

        [VsTest]
        public async Task AColdForgeNeedsNothingInTheOffHand()
        {
            // There is nothing hot about a plate on an unlit forge, and setting a gate before
            // lighting the fire is the one time this would be friction for its own sake.
            var be = await AForge(lit: false);
            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Open);

            await WithTongsRequired(async () =>
            {
                await TongsInTheOffHand(false);
                Assert.False(Forge.IsBurning, "no fire");
                Assert.False(Forge.GateNeedsTongs, "so no requirement");
                Assert.True(Forge.TryCycleGate(Player.Me), "and the gate can be set by hand");
                Assert.Equal(GatePosition.Half, Forge.GatePosition, "which it was");
            });
        }

        [VsTest]
        public async Task TheHotPlateCannotBePulledOffBareHanded()
        {
            // The hole this closes: if working the gate needs tongs but taking it does not, bare
            // hands pull the whole hot plate free and refit it at whatever notch they like, which
            // is the gesture the requirement exists to stop.
            var be = await AForge();
            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Shut);

            await WithTongsRequired(async () =>
            {
                await TongsInTheOffHand(false);
                Assert.False(Forge.TryTakeGate(Player.Me), "bare hands cannot take hold of it");
                Assert.True(Forge.HasGate, "so the plate stays where it is");

                await TongsInTheOffHand(true);
                Assert.True(Forge.TryTakeGate(Player.Me), "tongs get it off");
                Assert.False(Forge.HasGate, "leaving a plain forge");
            });
        }

        [VsTest]
        public async Task TurningTheGateOffTurnsTheTongsRuleOffWithIt()
        {
            // EnableBlastGate off means the forge behaves as vanilla did. A world that has switched
            // the whole mechanic off should not still be asking anyone to carry tongs for it.
            var be = await AForge();
            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Shut);

            await WithTongsRequired(async () =>
            {
                await TongsInTheOffHand(false);
                CrucibulumModSystem.Config.EnableBlastGate = false;
                try
                {
                    Assert.False(Forge.GateNeedsTongs, "no gate mechanic, no tongs rule");
                    Assert.True(Forge.TryTakeGate(Player.Me), "and the plate still comes back out");
                }
                finally
                {
                    CrucibulumModSystem.Config.EnableBlastGate = true;
                }
            });
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task TheHelpAsksForTongsOnlyWhenTheyAreNeeded()
        {
            // The requirement comes and goes with the fire, and a WorldInteraction's text is fixed
            // when the array is built and cached for the session - so this is the only thing that
            // catches a stale line that names a rule the forge is not under.
            var be = await AForgeOnTheFloor();
            be.FitGateForTesting(World.Stack("game:metalplate-copper"), GatePosition.Open);
            be.MarkDirty(true);
            await Ticks(20);

            await WithTongsRequired(async () =>
            {
                string cold = await OfferedGateHelp();
                Log("  unlit: " + cold);
                Assert.Contains(cold, "blockhelp-forge-workgate,", "a cold plate is worked bare-handed");
                Assert.False(cold.Contains("workgate-tongs"), "and says nothing about tongs");
                Assert.False(cold.Contains("takegate-tongs"), "nor for taking it off");

                StandingForge.FuelSlot.Itemstack = World.Stack("game:coke", 4);
                StandingForge.TryIgnite();
                StandingForge.MarkDirty(true);
                await WaitForTheClientToSeeTheFire();

                string hot = await OfferedGateHelp();
                Log("  lit:   " + hot);
                Assert.Contains(hot, "blockhelp-forge-workgate-tongs,", "a hot one says to bring tongs");
                Assert.Contains(hot, "blockhelp-forge-takegate-tongs,", "and so does taking it off");
                Assert.False(hot.Contains("forge-workgate,"), "and the bare-handed line is gone");
                Assert.False(hot.Contains("forge-takegate,"), "along with its twin");
            });
        }

        /// <summary>
        /// The help is drawn from the client's own copy of the block entity, so the requirement only
        /// appears there once the fire has synced. Polled rather than slept on: Until takes a
        /// synchronous predicate and reading the client's copy means hopping threads.
        /// </summary>
        static async Task WaitForTheClientToSeeTheFire()
        {
            for (int i = 0; i < 20; i++)
            {
                await OnClient();
                bool burning = (Capi.World.BlockAccessor.GetBlockEntity(StandingForgePos)
                    as BlockEntityCrucibulumForge)?.IsBurning == true;
                await OnServer();

                if (burning) return;
                await Ticks(4);
            }

            Assert.Fail("the client never heard that the forge was lit");
        }

        /// <summary>
        /// The gate lines the client would actually draw for a click on the plate: the help array
        /// carries every line the block knows about, and each one's GetMatchingStacks is what says
        /// whether this forge, right now, is offering it. Returned comma-joined with a trailing
        /// comma so a test can name an exact line - "workgate" is a prefix of "workgate-tongs".
        /// </summary>
        static async Task<string> OfferedGateHelp()
        {
            await OnClient();
            var block = Capi.World.BlockAccessor.GetBlock(StandingForgePos);
            var sel = new BlockSelection
            {
                Position = StandingForgePos.Copy(),
                Face = BlockFacing.SOUTH,
                HitPosition = new Vec3d(0.5, 0.3, 0.94),
            };
            var help = block.GetPlacedBlockInteractionHelp(Capi.World, sel, Capi.World.Player)
                .Where(h => h.GetMatchingStacks == null || h.GetMatchingStacks(h, sel, null) != null)
                .Select(h => h.ActionLangCode)
                .ToArray();
            await OnServer();

            return string.Join(",", help) + ",";
        }

        #endregion

        #region The handle

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task ThePlateHasAHandleAndItStaysGrabbable()
        {
            // The handle is there so the plate looks like something tongs could take hold of, which
            // only holds up if the part that looks grabbable is the part that takes the click and
            // never leaves the block as the gate opens. Both are geometry in a shipped asset, so
            // both fail silently.
            var be = await AForgeOnTheFloor();
            await Ticks(5);

            await OnClient();
            Shape shape = Capi.Assets.TryGet("crucibulum:shapes/block/blastgate.json")?.ToObject<Shape>();
            Shape ventShape = Capi.Assets.TryGet("crucibulum:shapes/block/blastvent.json")?.ToObject<Shape>();
            await OnServer();

            Assert.NotNull(shape, "the plate's shape is in the zip");
            Assert.NotNull(ventShape, "and so is the inlet's");

            var plate = shape.Elements.Single(e => e.Name == "gate");
            var handle = shape.Elements.Where(e => e.Name?.StartsWith("handle") == true).ToArray();
            var vent = ventShape.Elements.Single();
            Log("  handle: " + string.Join(", ", handle.Select(e => e.Name)));
            Assert.Greater(handle.Length, 0, "the plate carries a handle");

            foreach (GatePosition pos in Enum.GetValues<GatePosition>())
            {
                StandingForge.FitGateForTesting(World.Stack("game:metalplate-copper"), pos);
                double slide = BlastGate.Slide(pos);

                foreach (var el in handle)
                {
                    foreach (Vec3d corner in Corners(el, slide))
                    {
                        Assert.InRange(corner.X * 16, WallLeft, WallRight,
                            $"{el.Name} stays on the forge's front wall at {pos}");
                        Assert.InRange(corner.Z, 0, 1, $"{el.Name} stays inside the block at {pos}");
                        Assert.True(StandingForge.IsGateHit(AsMeshed(corner, StandingForge.MeshAngleRad)),
                            $"a click on {el.Name} at {pos} counts as a click on the gate");
                    }

                    // The handle leads the plate rather than trailing it, so that it parks against
                    // the wall as the gate opens instead of being drawn back across the uncovered
                    // inlet. A handle sitting in the draught is the one place on the plate you would
                    // least want to put the thing you are meant to take hold of.
                    Assert.GreaterOrEqual(el.From[0] + slide * 16, vent.To[0],
                        $"{el.Name} is clear of the inlet at {pos}");
                }

                double plateLeft = plate.From[0] + slide * 16;
                double plateRight = plate.To[0] + slide * 16;
                if (pos == GatePosition.Shut)
                {
                    Assert.LessOrEqual(plateLeft, vent.From[0], "shut, the plate covers the inlet");
                    Assert.GreaterOrEqual(plateRight, vent.To[0], "all of it");
                }
                else if (pos == GatePosition.Open)
                {
                    Assert.GreaterOrEqual(plateLeft, vent.To[0], "open, the plate is off the inlet entirely");
                }
            }
        }

        /// <summary>
        /// How wide the forge's own front wall is, from the vanilla shape's S element, [1,0,13] to
        /// [15,11,15]. Past either end there is nothing behind the plate but sky, and a handle that
        /// slides out there hangs in the air.
        /// </summary>
        const double WallLeft = 1.0;
        const double WallRight = 15.0;

        /// <summary>Every corner of an element, in block units, at the notch the plate is at.</summary>
        static IEnumerable<Vec3d> Corners(ShapeElement el, double slide)
        {
            foreach (double x in new[] { el.From[0] / 16 + slide, el.To[0] / 16 + slide })
                foreach (double y in new[] { el.From[1] / 16, el.To[1] / 16 })
                    foreach (double z in new[] { el.From[2] / 16, el.To[2] / 16 })
                        yield return new Vec3d(x, y, z);
        }

        /// <summary>
        /// Where a point of the shape ends up once the mesh has been turned to face the way the
        /// forge was placed, which is the form IsGateHit expects and undoes.
        /// </summary>
        static Vec3d AsMeshed(Vec3d point, float angle)
        {
            float c = GameMath.Cos(angle);
            float s = GameMath.Sin(angle);
            double x = point.X - 0.5;
            double z = point.Z - 0.5;
            return new Vec3d(0.5 + c * x + s * z, point.Y, 0.5 - s * x + c * z);
        }

        #endregion
    }
}
