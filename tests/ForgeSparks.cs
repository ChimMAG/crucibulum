using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Crucibulum;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using Vintagestory.Server;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Crucibulum.Tests
{
    /// <summary>
    /// A melting charge sparks and a molten one does not, and - only when the config and the world
    /// both say so - a spark that lands beside something that burns starts a fire.
    ///
    /// Where a spark lands is random, so the landing itself is driven directly at a chosen spot;
    /// what is under test is what a landing does, and when it is allowed to.
    /// </summary>
    public class ForgeSparks
    {
        static BlockPos ForgePos => P(8, 0, 8);
        static BlockEntityCrucibulumForge Forge => World.BE<BlockEntityCrucibulumForge>(ForgePos);

        // An open space two blocks out, one up from the ground the forge is set into, with a log
        // beyond it. The line from the crucible's mouth to here runs through open air.
        static BlockPos Landing => P(10, 1, 8);
        static BlockPos Log => P(11, 1, 8);

        const string LogCode = "game:log-placed-oak-ud";

        sealed class Settings
        {
            bool sparksSpreadFire, allowFireSpread;
            float meltSparkRate, meltDoneSparkBurst, sparkLandingSeconds;

            public static Settings Take() => new()
            {
                sparksSpreadFire = CrucibulumModSystem.Config.SparksSpreadFire,
                meltSparkRate = CrucibulumModSystem.Config.MeltSparkRate,
                meltDoneSparkBurst = CrucibulumModSystem.Config.MeltDoneSparkBurst,
                sparkLandingSeconds = CrucibulumModSystem.Config.SparkLandingSeconds,
                allowFireSpread = Sapi.World.Config.GetBool("allowFireSpread"),
            };

            public void Restore()
            {
                CrucibulumModSystem.Config.SparksSpreadFire = sparksSpreadFire;
                CrucibulumModSystem.Config.MeltSparkRate = meltSparkRate;
                CrucibulumModSystem.Config.MeltDoneSparkBurst = meltDoneSparkBurst;
                CrucibulumModSystem.Config.SparkLandingSeconds = sparkLandingSeconds;
                Sapi.World.Config.SetBool("allowFireSpread", allowFireSpread);
            }
        }

        Settings before;

        [BeforeEach]
        public async Task TakeTheSettingsAndKeepThePlayerAlive()
        {
            before = Settings.Take();   // before any await, so nothing has had a chance to move them
            await TestLife.Alive();
        }

        [AfterEach]
        public void PutTheSettingsBack() => before?.Restore();

        static void Allow(bool config, bool world)
        {
            CrucibulumModSystem.Config.SparksSpreadFire = config;
            Sapi.World.Config.SetBool("allowFireSpread", world);
        }

        /// <summary>A cold forge, with something to burn beyond the landing spot if asked for.</summary>
        static async Task AForge(bool withLog = true)
        {
            World.SetBlock("game:forge", ForgePos);
            if (withLog) World.SetBlock(LogCode, Log);
            await Ticks(2);

            Assert.True(Sapi.World.BlockAccessor.GetBlock(Landing).Replaceable >= 6000, "the landing spot is open");
            if (withLog) Assert.True(Sapi.World.BlockAccessor.GetBlock(Log).CombustibleProps?.BurnDuration > 0, $"{LogCode} burns");
        }

        /// <summary>
        /// A forge standing on the ground rather than set into it, so a landing at its own height is
        /// open air, with the log beyond.
        /// </summary>
        static BlockPos RaisedForgePos => P(8, 1, 8);

        static async Task RaisedForge(int coke)
        {
            World.SetBlock("game:forge", RaisedForgePos);
            World.SetBlock(LogCode, Log);
            await Ticks(2);

            var be = World.BE<BlockEntityCrucibulumForge>(RaisedForgePos);
            be.FuelSlot.Itemstack = World.Stack("game:coke", coke);
            be.MarkDirty(true);
            Assert.True(Sapi.World.BlockAccessor.GetBlock(Landing).Replaceable >= 6000, "the landing spot is open");
        }

        /// <summary>A lit forge with a crucible of copper, not yet hot.</summary>
        static async Task ChargedForge(int copper = 4)
        {
            World.SetBlock("game:forge", ForgePos);
            await Ticks(2);

            var be = Forge;
            be.FuelSlot.Itemstack = World.Stack("game:coke", 8);
            be.WorkItemSlot.Itemstack = World.Stack("game:crucible-brown-fired");
            be.ChargeSlots[0].Itemstack = World.Stack("game:nugget-nativecopper", copper);
            be.TryIgnite();
            be.MarkDirty(true);
        }

        /// <summary>A charged forge brought up to where its copper is melting.</summary>
        static async Task MeltingForge(int copper = 4)
        {
            await ChargedForge(copper);
            await Heat();
            await Until(() => Forge.IsMelting, 200, "the charge to start melting");
        }

        /// <summary>The forge heats on calendar deltas: a baseline, an hour, and a tick that spends it.</summary>
        static async Task Heat()
        {
            await World.TickNow(ForgePos);
            await Hours(1);
            await World.TickNow(ForgePos);
        }

        static async Task UntilMolten() =>
            await Until(() => BlockEntityCrucibulumForge.IsMoltenCrucible(Forge.WorkItemStack), 900, "the crucible turning molten");

        static bool LandSpark(BlockPos at) => LandSparkFrom(ForgePos, at);

        static bool LandSparkFrom(BlockPos forge, BlockPos at) => (bool)typeof(BlockEntityCrucibulumForge)
            .GetMethod("LandSpark", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(World.BE<BlockEntityCrucibulumForge>(forge), new object[] { at });

        static string BlockAt(BlockPos pos) => Sapi.World.BlockAccessor.GetBlock(pos).Code?.Path ?? "air";

        static LandClaim Claim(string owner, BlockPos from, BlockPos to)
        {
            var claim = new LandClaim { OwnedByPlayerUid = owner, LastKnownOwnerName = owner, ProtectionLevel = 1 };
            claim.Areas.Add(new Cuboidi(from.X, from.Y, from.Z, to.X + 1, to.Y + 1, to.Z + 1));
            Sapi.World.Claims.Add(claim);
            return claim;
        }

        [VsTest]
        public async Task SparksStartNoFiresByDefault()
        {
            await AForge();
            CrucibulumModSystem.Config.SparksSpreadFire = new CrucibulumConfig().SparksSpreadFire;
            Sapi.World.Config.SetBool("allowFireSpread", true);

            Assert.False(CrucibulumModSystem.Config.SparksSpreadFire, "off out of the box");
            Assert.False(LandSpark(Landing), "a spark beside a log, with the option at its default");
            Assert.Equal("air", BlockAt(Landing), "and no fire");
        }

        [VsTest]
        public async Task ASparkBesideSomethingThatBurnsStartsAFire()
        {
            await AForge();
            Allow(config: true, world: true);

            Assert.True(LandSpark(Landing), "the spark caught");
            Assert.Equal("fire", BlockAt(Landing), "vanilla's fire, where it landed");

            var burning = Sapi.World.BlockAccessor.GetBlockEntity(Landing)?.GetBehavior<BEBehaviorBurning>();
            Assert.True(burning?.IsBurning == true, "and it is actually burning");
            Assert.Equal(Log, burning.FuelPos, "on the log");
        }

        [VsTest]
        public async Task TheWorldsOwnFireSpreadRuleStillApplies()
        {
            await AForge();
            Allow(config: true, world: false);

            Assert.False(LandSpark(Landing), "a world with fire spread off");
            Assert.Equal("air", BlockAt(Landing), "and no fire");
        }

        [VsTest]
        public async Task ASparkWithNothingToBurnGoesOut()
        {
            await AForge(withLog: false);
            Allow(config: true, world: true);

            Assert.False(LandSpark(Landing), "open ground and soil");
            Assert.Equal("air", BlockAt(Landing), "and no fire");
        }

        [VsTest]
        public async Task AWallBetweenTheForgeAndTheLandingStopsASpark()
        {
            await AForge();
            Allow(config: true, world: true);
            World.SetBlock("game:rock-granite", P(9, 1, 8));
            await Ticks(1);

            Assert.False(LandSpark(Landing), "a spark with a block of granite in its path");
            Assert.Equal("air", BlockAt(Landing), "and no fire");
        }

        [VsTest]
        public async Task ABlockRightOnTopOfTheForgeStopsASpark()
        {
            // With fuel in, the crucible's mouth sits just above the top of the forge, in the cell
            // above it - so a block set there is the first thing a spark meets, and a line dropping
            // to a landing at the forge's own height leaves it within a few hundredths of a block.
            await RaisedForge(coke: 2);
            Allow(config: true, world: true);
            World.SetBlock("game:rock-granite", RaisedForgePos.UpCopy());
            await Ticks(1);

            Assert.False(LandSparkFrom(RaisedForgePos, Landing), "a spark from under a block of granite");
            Assert.Equal("air", BlockAt(Landing), "and no fire");
        }

        [VsTest]
        public async Task ASparkPassesUnderAnUpperSlab()
        {
            // The line runs about a third of the way up the cell in front of the forge. An upper slab
            // there fills only the top half, so the spark goes under it; a lower one is in the way.
            await AForge();
            Allow(config: true, world: true);

            World.SetBlock("game:glassslab-quartz-up-free", P(9, 1, 8));
            await Ticks(1);
            Assert.Equal("glassslab-quartz-up-free", BlockAt(P(9, 1, 8)), "the upper slab is in place");
            Assert.True(LandSpark(Landing), "a spark under an upper slab");

            World.SetBlock("game:log-placed-oak-ud", Log);   // it is burning now; put a fresh one
            Sapi.World.BlockAccessor.SetBlock(0, Landing);
            World.SetBlock("game:glassslab-quartz-down-free", P(9, 1, 8));
            await Ticks(1);
            Assert.Equal("glassslab-quartz-down-free", BlockAt(P(9, 1, 8)), "the lower slab is in place");
            Assert.False(LandSpark(Landing), "a spark into a lower slab");
        }

        [VsTest]
        public async Task ASparkLightsNothingInSomeoneElsesClaim()
        {
            await AForge();
            Allow(config: true, world: true);

            var claim = Claim("someone-else", Landing, Log);
            try
            {
                Assert.False(LandSpark(Landing), "a spark into a claim the forge is not part of");
                Assert.Equal("air", BlockAt(Landing), "and no fire");
            }
            finally
            {
                Sapi.World.Claims.Remove(claim);
            }
        }

        [VsTest]
        public async Task ASparkBurnsNoFuelInSomeoneElsesClaim()
        {
            // The landing spot is open land; only the log beside it is claimed. Fire stands in the
            // open space but eats the fuel, so the fuel is what the claim has to protect.
            await AForge();
            Allow(config: true, world: true);

            var claim = Claim("someone-else", Log, Log);
            try
            {
                Assert.Equal(0, Sapi.World.Claims.Get(Landing)?.Length ?? 0, "the landing spot is unclaimed");
                Assert.Greater(Sapi.World.Claims.Get(Log)?.Length ?? 0, 0, "the log is claimed");

                Assert.False(LandSpark(Landing), "a spark whose only fuel is in someone else's claim");
                Assert.Equal("air", BlockAt(Landing), "and no fire");
            }
            finally
            {
                Sapi.World.Claims.Remove(claim);
            }
        }

        [VsTest]
        public async Task ASparkMayLandInTheForgesOwnClaim()
        {
            await AForge();
            Allow(config: true, world: true);

            var claim = Claim("the-smith", ForgePos.AddCopy(-1, -1, -1), Log);
            try
            {
                Assert.True(LandSpark(Landing), "a spark inside the claim the forge sits in");
                Assert.Equal("fire", BlockAt(Landing), "and the fire it started");
            }
            finally
            {
                Sapi.World.Claims.Remove(claim);
            }
        }

        [VsTest]
        public async Task ASparkLightsACoalPile()
        {
            await AForge(withLog: false);
            Allow(config: true, world: true);

            World.SetBlock("game:coalpile", Landing);
            await Ticks(1);
            var pile = (BlockEntityCoalPile)Sapi.World.BlockAccessor.GetBlockEntity(Landing);
            pile.inventory[0].Itemstack = World.Stack("game:charcoal", 8);
            pile.MarkDirty(true);
            Assert.True(pile.CanIgnite && !pile.IsBurning, "an unlit pile of charcoal");

            Assert.True(LandSpark(Landing), "a spark on the pile");
            Assert.True(pile.IsBurning, "and it is alight");
        }

        [VsTest]
        public async Task ASparkLightsAPileOfFirewood()
        {
            await AForge(withLog: false);
            Allow(config: true, world: true);

            World.SetBlock("game:groundstorage", Landing);
            await Ticks(1);
            var pile = (BlockEntityGroundStorage)Sapi.World.BlockAccessor.GetBlockEntity(Landing);
            // Filled directly, so what vanilla does once a pile's contents are in - at load, and after
            // a player adds to it - has to be done here: settle how the pile is stored, and work out
            // how long its contents burn. The second is private, and without it CanIgnite is false.
            pile.Inventory[0].Itemstack = World.Stack("game:firewood", 8);
            pile.DetermineStorageProperties(null);
            typeof(BlockEntityGroundStorage)
                .GetMethod("UpdateIgnitable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(pile, null);
            pile.MarkDirty(true);
            Assert.True(pile.CanIgnite && !pile.IsBurning, "an unlit pile of firewood");

            Assert.True(LandSpark(Landing), "a spark on the pile");
            Assert.True(pile.IsBurning, "and it is alight");
        }

        [VsTest]
        public async Task TheLandingRateFollowsTheConfig()
        {
            // Per crucible tick, so the configured average gap is what a player actually sees.
            CrucibulumModSystem.Config.SparkLandingSeconds = 10;
            Assert.Close(BlockEntityCrucibulumForge.SparkLandingChance(0.2f), 0.02, 1e-6, "one every ten seconds, at 200ms a tick");

            CrucibulumModSystem.Config.SparkLandingSeconds = 1;
            Assert.Close(BlockEntityCrucibulumForge.SparkLandingChance(0.2f), 0.2, 1e-6, "one a second");

            CrucibulumModSystem.Config.SparkLandingSeconds = 0;
            Assert.Close(BlockEntityCrucibulumForge.SparkLandingChance(0.2f), 0.2, 1e-6, "a zero from a hand-edited file is held at one a second");
            await Task.CompletedTask;
        }

        [VsTest(TimeoutMs = 90000)]
        public async Task ItIsMeltingWhileItMeltsAndNotOnceItIsMolten()
        {
            await ChargedForge();
            Assert.False(Forge.IsMelting, "cold ore is not melting");

            await Heat();
            await Until(() => Forge.IsMelting, 200, "the charge to start melting");
            await UntilMolten();
            Assert.False(Forge.IsMelting, "a molten crucible is not melting any more");
        }

        [VsTest(TimeoutMs = 120000)]
        public async Task TheBurstScalesAndCanBeTurnedOff()
        {
            // Thrown on the server as the melt completes.
            using var particles = new ParticleWatch(ForgePos);

            CrucibulumModSystem.Config.MeltDoneSparkBurst = 0;
            await MeltingForge();
            await UntilMolten();
            Assert.Equal(0, particles.Sparks(onServer: true).Count, "no burst at 0");

            CrucibulumModSystem.Config.MeltDoneSparkBurst = 2;
            await MeltingForge();
            await UntilMolten();
            var bursts = particles.Sparks(onServer: true);
            Assert.Equal(1, bursts.Count, "one burst at 2");
            Assert.Equal(24f, bursts[0].MinQuantity, "twice the default's twelve");
            Assert.Equal(16f, bursts[0].AddQuantity, "and twice its eight more at random");
        }

        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task TheClientSparksWhileMeltingAndOnlySmokesOnceMolten()
        {
            await MeltingForge();
            using var particles = new ParticleWatch(ForgePos);

            await OnClient();
            BlockEntityCrucibulumForge ClientForge() => (BlockEntityCrucibulumForge)Capi.World.BlockAccessor.GetBlockEntity(ForgePos);
            await Until(() => ClientForge()?.IsMelting == true, 40, "the client to hear the charge is melting");
            await Until(() => particles.Sparks(onServer: false).Count > 0, 40, "a spark thrown on the client while it melts");

            await Until(() => BlockEntityCrucibulumForge.IsMoltenCrucible(ClientForge()?.WorkItemStack), 900, "the client to see it molten");
            await Until(() => ClientForge().IsMelting == false, 40, "and that it has stopped melting");

            particles.Clear();
            await Ticks(40);
            Assert.Equal(0, particles.Sparks(onServer: false).Count, "no sparks from a molten crucible");
            Assert.Greater(particles.Smoke(onServer: false).Count, 0, "only the crucible's smoke - which also shows the watch sees this forge");
        }

        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task TheMeltSparkRateSetsHowManyAreThrown()
        {
            await MeltingForge();
            using var particles = new ParticleWatch(ForgePos);

            await OnClient();
            BlockEntityCrucibulumForge ClientForge() => (BlockEntityCrucibulumForge)Capi.World.BlockAccessor.GetBlockEntity(ForgePos);
            await Until(() => ClientForge()?.IsMelting == true, 40, "the client to hear the charge is melting");

            // At 5, between five a tick as a melt starts and twenty as it ends - every tick, with
            // nothing below the whole number left to chance.
            CrucibulumModSystem.Config.MeltSparkRate = 5;
            particles.Clear();
            await Ticks(20);
            var thrown = particles.Sparks(onServer: false);
            Assert.Greater(thrown.Count, 0, "sparks at 5");
            Assert.True(thrown.All(t => t.MinQuantity >= 5 && t.MinQuantity <= 21 && t.AddQuantity == 0),
                "five to twenty at a time at 5: " + string.Join(", ", thrown.Select(t => $"{t.MinQuantity}+{t.AddQuantity}")));

            CrucibulumModSystem.Config.MeltSparkRate = 0;
            particles.Clear();
            await Ticks(40);
            Assert.True(ClientForge().IsMelting, "still melting");
            Assert.Equal(0, particles.Sparks(onServer: false).Count, "none at 0");
        }

        [VsTest]
        public async Task TheSparksBuildFromATrickleToAPour()
        {
            Assert.Close(BlockEntityCrucibulumForge.MeltSparksPerTick(0), 1, 1e-6, "one a tick as a melt starts");
            Assert.Close(BlockEntityCrucibulumForge.MeltSparksPerTick(1), 4, 1e-6, "four as it nears liquid");
            Assert.Close(BlockEntityCrucibulumForge.MeltSparkVigour(0), 0.35, 1e-6, "thrown at a third of a pour's force to start");
            Assert.Close(BlockEntityCrucibulumForge.MeltSparkVigour(1), 1, 1e-6, "and as hard as a pour by the end");
            Assert.Close(BlockEntityCrucibulumForge.MeltSparksPerTick(7), 4, 1e-6, "held at the end past it");
            await Task.CompletedTask;
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task ALateMeltThrowsMoreAndHarderThanAnEarlyOne()
        {
            // Eight nuggets is twelve seconds of melt: long enough to compare its first quarter with
            // its last over plenty of ticks, so the chance in each tick's count averages out.
            await MeltingForge(copper: 8);
            using var particles = new ParticleWatch(ForgePos);

            await OnClient();
            BlockEntityCrucibulumForge ClientForge() => (BlockEntityCrucibulumForge)Capi.World.BlockAccessor.GetBlockEntity(ForgePos);
            await Until(() => BlockEntityCrucibulumForge.IsMoltenCrucible(ClientForge()?.WorkItemStack), 900, "the client to see it molten");

            var thrown = particles.Sparks(onServer: false);
            int quarter = thrown.Count / 4;
            Assert.Greater(quarter, 5, $"enough of a melt to compare ({thrown.Count} throws)");

            var early = thrown.Take(quarter).ToList();
            var late = thrown.Skip(thrown.Count - quarter).ToList();
            double earlyCount = early.Average(t => t.MinQuantity), lateCount = late.Average(t => t.MinQuantity);
            double earlyLift = early.Average(t => t.AddVelocityY), lateLift = late.Average(t => t.AddVelocityY);
            Log($"  first quarter: {earlyCount:0.00} a throw, lift {earlyLift:0.00}; last: {lateCount:0.00}, lift {lateLift:0.00}");

            Assert.Greater(lateCount, earlyCount + 1, "more sparks a throw near the end");
            Assert.Greater(lateLift, earlyLift * 1.5, "thrown harder near the end");
        }

        [VsTest(TimeoutMs = 90000), RequiresClient]
        public async Task AMeltThatStopsBetweenSyncsStillTellsTheClient()
        {
            // Tin added to a melting crucible of copper makes a half-and-half mix that is no alloy,
            // so the melt stops - but the crucible is as hot as ever, so nothing else about the forge
            // changes and no later tick has anything new to say. Syncs are held to two a second; the
            // last one is set just now, so the stop lands inside that window, as it does half the time.
            await MeltingForge(copper: 20);

            // Settled first: the fire moving from heating to melting is itself sent at once, and the
            // melt starts within a tick of it - a stop landing on that tick would ride along with it.
            // Two crucible ticks: one to send the change of job, one to find nothing more to say. Then
            // a couple of server ticks, because a block entity marked dirty is only written out when
            // the server gets round to sending it - one still waiting would carry the stop with it.
            await Until(() => Forge.WorkState == CrucibleWork.Melting, 100, "the fire to settle into melting");
            await World.TickNow(ForgePos);
            await World.TickNow(ForgePos);
            await Ticks(2);
            Assert.True(Forge.IsMelting, "still melting before the tin goes in");

            var be = Forge;
            float temp = be.WorkItemStack.Collectible.GetTemperature(Sapi.World, be.WorkItemStack);
            var tin = World.Stack("game:nugget-cassiterite", 20);
            tin.Collectible.SetTemperature(Sapi.World, tin, temp);
            be.ChargeSlots[1].Itemstack = tin;

            typeof(BlockEntityCrucibulumForge).GetField("lastSyncMs", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(be, Sapi.World.ElapsedMilliseconds);
            await World.TickNow(ForgePos);
            Assert.False(Forge.IsMelting, "the mix stopped melting on the server");

            // Heard at once, not on whatever sync happens to come next: a crucible creeping by a
            // fraction of a degree is enough to send one after the half-second hold, and that would
            // hide the stop going unsent. Eight client ticks is well inside the hold.
            await OnClient();
            await Until(() => ((BlockEntityCrucibulumForge)Capi.World.BlockAccessor.GetBlockEntity(ForgePos))?.IsMelting == false, 8,
                "the client to hear at once that the melt has stopped");
        }

        /// <summary>
        /// The crucible's own particles at the forge's block, on either side, from the game's own
        /// SpawnParticles. Test-only: a Harmony prefix for as long as this is held.
        ///
        /// The vanilla forge throws sparks and smoke of its own from the same block, so position is
        /// not enough. The crucible's are told apart by colour: they are clones of vanilla's
        /// big-metal-spark and held-smoke templates, which the forge's own do not use.
        /// </summary>
        sealed class ParticleWatch : IDisposable
        {
            public sealed record Spawn(bool OnServer, bool IsSpark, float MinQuantity, float AddQuantity, float AddVelocityY);

            static readonly object gate = new();
            static readonly List<Spawn> seen = new();
            static BlockPos watching;

            readonly Harmony harmony = new("crucibulum.tests.particlewatch");

            public ParticleWatch(BlockPos pos)
            {
                lock (gate) { seen.Clear(); watching = pos.Copy(); }
                var prefix = new HarmonyMethod(typeof(ParticleWatch).GetMethod(nameof(Saw), BindingFlags.NonPublic | BindingFlags.Static));
                harmony.Patch(AccessTools.Method(typeof(ClientMain), nameof(ClientMain.SpawnParticles), new[] { typeof(IParticlePropertiesProvider), typeof(IPlayer) }), prefix);
                harmony.Patch(AccessTools.Method(typeof(ServerMain), nameof(ServerMain.SpawnParticles), new[] { typeof(IParticlePropertiesProvider), typeof(IPlayer) }), prefix);
            }

            static void Saw(object __instance, IParticlePropertiesProvider __0)
            {
                if (__0 is not SimpleParticleProperties p) return;

                bool spark = p.Color == BlockSmeltedContainer.bigMetalSparks.Color;
                bool smoke = p.Color == BlockSmeltedContainer.smokeHeld.Color;
                if (!spark && !smoke) return;

                lock (gate)
                {
                    if (watching == null) return;
                    if (Math.Floor(p.MinPos.X) != watching.X || Math.Floor(p.MinPos.Z) != watching.Z) return;
                    seen.Add(new Spawn(__instance is ServerMain, spark, p.MinQuantity, p.AddQuantity, p.AddVelocity.Y));
                }
            }

            public List<Spawn> Sparks(bool onServer) { lock (gate) return seen.Where(s => s.OnServer == onServer && s.IsSpark).ToList(); }
            public List<Spawn> Smoke(bool onServer) { lock (gate) return seen.Where(s => s.OnServer == onServer && !s.IsSpark).ToList(); }
            public void Clear() { lock (gate) seen.Clear(); }

            public void Dispose()
            {
                harmony.UnpatchAll(harmony.Id);
                lock (gate) { watching = null; seen.Clear(); }
            }
        }
    }
}
