using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Crucibulum;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// Produces the screenshot set for the ModDB listing. Not part of the test suite - run it with
/// scripts/make-shots.sh, which boots a client with particles on and a 1080p window first.
/// </summary>
public class ModDbShots
{
    const string Forge = "game:forge";
    const string Fired = "game:crucible-brown-fired";
    const string Smelted = "game:crucible-brown-smelted";
    const string Copper = "game:nugget-nativecopper";
    const string Tin = "game:nugget-cassiterite";

    static string Out(string name) => System.IO.Path.Combine(
        Environment.GetEnvironmentVariable("VSTK_SHOT_DIR") ?? "/tmp", name);

    /// <summary>
    /// Empties a generous box and lays fresh turf. Plots sit close enough together that the
    /// previous scene's blocks wander into frame otherwise.
    /// </summary>
    static void ClearAround(BlockPos centre, int r = 30)
    {
        for (int dx = -r; dx <= r; dx++)
        for (int dz = -r; dz <= r; dz++)
        {
            for (int dy = 0; dy <= 12; dy++) World.SetBlock("game:air", centre.AddCopy(dx, dy, dz));
            World.SetBlock("game:soil-medium-normal", centre.AddCopy(dx, -1, dz));
        }
    }

    /// <summary>A forge with fuel in it, lit or not, and whatever crucible the scene wants.</summary>
    static BlockEntityCrucibulumForge Forge_(BlockPos pos, bool lit = true, string fuel = "game:coke")
    {
        World.SetBlock(Forge, pos);
        var be = World.BE<BlockEntityCrucibulumForge>(pos);
        be.FuelSlot.Itemstack = World.Stack(fuel, 4);
        if (lit) be.TryIgnite();
        be.MarkDirty(true);
        return be;
    }

    static void PutFiredCrucible(BlockEntityCrucibulumForge be, float temp, params (string code, int qty)[] charge)
    {
        be.WorkItemSlot.Itemstack = World.Stack(Fired);
        // Straight into the slots. The block entity has no charge-loading helper of its own - the
        // window loads a crucible through the ordinary inventory machinery - and a scene only needs
        // the ore to be there, not the thermodynamics of putting it there.
        int chargeSlot = 0;
        foreach (var (code, qty) in charge) be.ChargeSlots[chargeSlot++].Itemstack = World.Stack(code, qty);

        be.WorkItemStack.Collectible.SetTemperature(Sapi.World, be.WorkItemStack, temp);
        foreach (var slot in be.ChargeSlots)
            slot.Itemstack?.Collectible.SetTemperature(Sapi.World, slot.Itemstack, temp);

        be.MarkDirty(true);
    }

    static void PutMoltenCrucible(BlockEntityCrucibulumForge be, string metal, int units, float temp)
    {
        var smelted = (BlockSmeltedContainer)Sapi.World.GetBlock(new AssetLocation(Smelted));
        var stack = new ItemStack(smelted);
        smelted.SetContents(stack, World.Stack(metal), units);
        stack.Collectible.SetTemperature(Sapi.World, stack, temp);

        be.WorkItemSlot.Itemstack = stack;
        be.MarkDirty(true);
    }

    /// <summary>
    /// Stands somewhere and looks at something.
    ///
    /// <paramref name="feet"/> is where the player stands, not where the camera is: the eye sits
    /// about 1.7 above it. Treating it as the camera puts the viewpoint three blocks up looking
    /// steeply down, which is how the first cut of these shots ended up photographing grass.
    ///
    /// The aim point is pulled a little below the subject on purpose. LookAt centres the target in
    /// the 1080-tall frame, but the frame is then cropped bottom-heavy to cut the hotbar, so
    /// anything centred before the crop sits low after it.
    /// </summary>
    static async Task Aim(Vec3d feet, Vec3d target, int settle = 60)
    {
        await Player.Teleport(feet);
        await Ticks(10);
        await Interact.LookAt(target);
        await Frames.Wait(settle);
    }

    /// <summary>The crucible's own middle, which is what every scene is actually about.</summary>
    /// <summary>
    /// The crucible's own middle, dropped by <paramref name="drop"/>.
    ///
    /// Aiming below the subject lifts it in frame, which is needed twice over: the camera looks
    /// down from eye height, and the frame is then cropped bottom-heavy to cut the hotbar. The
    /// further away the scene, the more drop it takes to move the subject the same distance on
    /// screen, so wide shots want roughly twice what close ones do.
    /// </summary>
    static Vec3d Crucible(BlockPos forge, double drop) =>
        new Vec3d(forge.X + 0.5, forge.Y + 1.15 - drop, forge.Z + 0.5);

    /// <summary>
    /// Closes the HUD for a clean frame. ICoreClientAPI.HideGuis is read-only, but the hotbar,
    /// stat bars, minimap and block tooltip are all dialogs and dialogs close on request.
    /// <paramref name="keep"/> holds back any whose type name contains one of the given strings,
    /// which is how the block-info shot keeps the very thing it is meant to show.
    /// </summary>
    static async Task HideHud(params string[] keep)
    {
        var hand = Player.Me?.InventoryManager?.ActiveHotbarSlot;
        if (hand != null) { hand.Itemstack = null; hand.MarkDirty(); }
        await Ticks(4);

        await OnClient();
        Capi.Settings.Int["cloudRenderMode"] = 0;
        // Ordinary first person floats the seraph's hand in the corner of every frame; immersive
        // mode renders the body from the eyes instead, which keeps it out of shot.
        Capi.Settings.Bool["immersiveFpMode"] = true;

        // Distance haze washes the horizon out. It comes from the ambient manager rather than a
        // render setting, so it is overridden at full weight.
        Capi.Ambient.CurrentModifiers["crucibulumshot"] = new AmbientModifier
        {
            FogDensity = new WeightedFloat(0f, 1f),
            FogMin = new WeightedFloat(0f, 1f),
        }.EnsurePopulated();

        var open = new List<GuiDialog>(Capi.Gui.OpenedGuis);
        foreach (var d in open)
        {
            string n = d.GetType().Name;
            bool held = false;
            foreach (string k in keep) if (n.Contains(k)) held = true;
            if (held) continue;

            if (n.StartsWith("Hud") || n == "GuiDialogWorldMap") d.TryClose();
        }

        await OnServer();
        await Frames.Wait(10);
    }

    // ------------------------------------------------------------------

    [VsTest(TimeoutMs = 300000), RequiresClient, PlotSize(48, 32)]
    public async Task Shot01_MoltenCrucibleInTheForge()
    {
        await World.SetCalendarTo(500 * 24 + 17);           // late afternoon: lit, but the glow still reads
        BlockPos f = P(24, 1, 20);
        ClearAround(f);

        var be = Forge_(f);
        PutMoltenCrucible(be, "game:ingot-copper", 300, 1150);
        await Ticks(40);                                    // let the smoke build

        await Aim(new Vec3d(f.X + 1.7, f.Y, f.Z + 2.4), Crucible(f, 0.7));
        await HideHud();
        Log("01 -> " + await Shot.Take(Out("01-molten-crucible.png")));
    }

    [VsTest(TimeoutMs = 300000), RequiresClient, PlotSize(48, 32)]
    public async Task Shot02_TheCrucibleWindow()
    {
        await World.SetCalendarTo(500 * 24 + 11);
        BlockPos f = P(24, 1, 20);
        ClearAround(f);

        var be = Forge_(f);
        PutFiredCrucible(be, 1180, (Copper, 18), (Tin, 2));    // 90:10 - valid tin bronze
        await Ticks(20);

        await Aim(new Vec3d(f.X + 1.9, f.Y, f.Z + 2.6), Crucible(f, 0.7), 30);
        await Interact.UseBlock(f, BlockFacing.UP);            // a plain click opens it
        await Frames.Wait(40);

        await HideHud("CrucibleForge");
        Log("02 -> " + await Shot.Take(Out("02-crucible-window.png")));
    }

    [VsTest(TimeoutMs = 300000), RequiresClient, PlotSize(48, 32)]
    public async Task Shot03_TheRatioItWants()
    {
        await World.SetCalendarTo(500 * 24 + 11);
        BlockPos f = P(24, 1, 20);
        ClearAround(f);

        var be = Forge_(f);
        PutFiredCrucible(be, 900, (Copper, 8), (Tin, 12));     // 40:60 - no alloy matches
        await Ticks(20);

        await Aim(new Vec3d(f.X + 1.9, f.Y, f.Z + 2.6), Crucible(f, 0.7), 30);
        await Interact.UseBlock(f, BlockFacing.UP);
        await Frames.Wait(40);

        await HideHud("CrucibleForge");
        Log("03 -> " + await Shot.Take(Out("03-wrong-ratio.png")));
    }

    [VsTest(TimeoutMs = 300000), RequiresClient, PlotSize(48, 32)]
    public async Task Shot04_TheColourItGoes()
    {
        // Cold, hot, and ready, side by side. The hue is the whole readout at a distance.
        await World.SetCalendarTo(500 * 24 + 18);             // early dusk: dim enough for the glow, light enough to see
        BlockPos centre = P(24, 1, 20);
        ClearAround(centre);

        PutFiredCrucible(Forge_(centre.AddCopy(-2, 0, 0), lit: false), 20);
        PutFiredCrucible(Forge_(centre), 950, (Copper, 20));
        PutMoltenCrucible(Forge_(centre.AddCopy(2, 0, 0)), "game:ingot-copper", 300, 1150);
        await Ticks(40);

        await Aim(new Vec3d(centre.X + 0.5, centre.Y, centre.Z + 5.0), Crucible(centre, 1.15));
        await HideHud();
        Log("04 -> " + await Shot.Take(Out("04-heat-states.png")));
    }

    [VsTest(TimeoutMs = 300000), RequiresClient, PlotSize(48, 32)]
    public async Task Shot05_MeltWhereYouSmith()
    {
        // The point of the mod in one frame: the crucible and the anvil at the same fire.
        await World.SetCalendarTo(500 * 24 + 16);
        BlockPos f = P(24, 1, 20);
        ClearAround(f);

        var be = Forge_(f);
        PutMoltenCrucible(be, "game:ingot-tinbronze", 400, 1150);

        World.SetBlock("game:anvil-tinbronze", f.AddCopy(2, 0, 0));
        World.SetBlock("game:ingotmold-brown-fired", f.AddCopy(-2, 0, 0));
        await Ticks(40);

        await Aim(new Vec3d(f.X + 0.5, f.Y, f.Z + 4.4), Crucible(f, 1.0));
        await HideHud();
        Log("05 -> " + await Shot.Take(Out("05-melt-where-you-smith.png")));
    }

    [VsTest(TimeoutMs = 300000), RequiresClient, PlotSize(48, 32)]
    public async Task Shot07_ModIcon()
    {
        // Square-cropped to 480x480 for the mod icon, so the forge wants to fill the frame.
        // Heating rather than molten: the orange reads better small, and it is the state you
        // actually stand and watch.
        //
        // Two things have to be kept out of an icon that are fine in a gallery shot. The crosshair
        // is drawn by the client and is not a dialog that can be closed - but it goes away while a
        // GUI has the mouse ungrabbed, so the crucible window is opened and left open, off to the
        // side of the square. The block selection outline is the other: it appears on whatever the
        // player is looking at, so after opening the window the aim is moved off the forge.
        await World.SetCalendarTo(500 * 24 + 15);
        BlockPos f = P(24, 1, 20);
        ClearAround(f);

        var be = Forge_(f);
        PutFiredCrucible(be, 1000, (Copper, 20));
        await Ticks(40);

        // Framed exactly like the window shot, because the crop box below was measured against
        // that composition. The window is opened and left open: the crosshair is drawn by the
        // client rather than by a dialog and cannot be closed, but it is not drawn at all while a
        // GUI has the mouse ungrabbed. It sits far to the right, outside the square.
        await Aim(new Vec3d(f.X + 1.9, f.Y, f.Z + 2.6), Crucible(f, 0.7), 30);
        await Interact.UseBlock(f, BlockFacing.UP);
        await Frames.Wait(40);

        await HideHud("CrucibleForge");
        Log("07 -> " + await Shot.Take(Out("07-icon-source.png")));
    }

    [VsTest(TimeoutMs = 180000), RequiresClient]
    public async Task Shot06_TheBlastGate()
    {
        await World.SetCalendarTo(500 * 24 + 11);

        // Four forges, one per gate position, so the whole range reads left to right.
        //
        // Shoulder to shoulder rather than spaced out. The subject is the plate and the handle on
        // it, which are a few voxels across: a row seven blocks wide had to be shot from far enough
        // back that the outer two fell off the frame and the hardware was too small to read.
        var positions = new[] { GatePosition.Shut, GatePosition.Quarter, GatePosition.Half, GatePosition.Open };
        BlockPos first = P(16, 1, 20);
        ClearAround(first, 40);

        for (int i = 0; i < positions.Length; i++)
        {
            BlockPos f = first.AddCopy(i, 0, 0);
            var be = Forge_(f);
            be.MeshAngleRad = 0;                     // all square on, so the four read as one row
            be.FitGate(new DummySlot(World.Stack("game:metalplate-copper")), null);
            while (be.GatePosition != positions[i]) be.CycleGate(null);
            be.FuelSlot.Itemstack = World.Stack("game:coke", 4);
            be.TryIgnite();
            be.MarkDirty(true);
        }
        await Ticks(20);

        // Shot04's camera, which frames a row of forges the way the set expects. Centred on the
        // seam between the middle two so the crosshair lands on a forge rather than picking out a
        // square of grass in the middle of the frame.
        await Aim(new Vec3d(first.X + 2.0, first.Y, first.Z + 4.0), new Vec3d(first.X + 2.0, first.Y + 0.30, first.Z + 0.5), 30);
        await HideHud();
        Log("06 -> " + await Shot.Take(Out("06-blast-gate.png")));
    }
}
