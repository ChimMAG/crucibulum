// Crucibulum - melt metal in a crucible on the forge, for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option) any
// later version. See COPYING.LESSER, or <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Crucibulum;

/// <summary>
/// Draws the crucible sitting in the forge, and the pool of metal in its mouth.
///
/// The vanilla ForgeContentsRenderer would happily draw the crucible if the block were flagged
/// <c>forgable</c> -- but its glow is the one it uses for a lump of iron being worked:
/// <c>extraGlow = (temp - 550) / 2</c>, which saturates to a featureless white lump by about
/// 1050 C. A crucible spends its whole working life above that, so "hot" and "ready to pour"
/// would look exactly alike and nothing inside the mouth would be visible at all.
///
/// Owning the draw instead costs one mesh and buys a gentler curve on the body, so the silhouette
/// survives to the top of the range, and lets the metal be the brightest thing in the frame.
/// </summary>
public class CrucibleRenderer : IRenderer, ITexPositionSource
{
    private readonly BlockEntityCrucibulumForge be;
    private readonly ICoreClientAPI capi;
    private readonly Matrixf modelMat = new();

    private MultiTextureMeshRef bodyRef;
    private MultiTextureMeshRef meltRef;

    private ITexPositionSource ingotTextures;
    private string metalName = "copper";

    // Collectibles are singletons, so identity is a reference compare and costs nothing. Comparing
    // Code.ToShortString() here meant two string allocations every frame for every visible forge.
    private CollectibleObject renderedCrucible;
    private CollectibleObject renderedMetal;
    private int renderedUnits = -1;
    private float renderedMeshAngle = float.NaN;

    // HasSolidifed allocates a DummySlot and looks up a melting point. Temperature does not move
    // fast enough to be worth asking sixty times a second.
    //
    // The "never asked yet" case is its own flag rather than a sentinel timestamp. It was
    // long.MinValue, and `now - long.MinValue` overflows to a negative number for every real value
    // of now, so the check was never due: moltenCached stayed false for the renderer's whole life
    // and liquid metal was drawn on the solidified branch - which below 550 degC is no glow at all,
    // so a crucible of molten tin sat there looking like a cold disc.
    private const long MoltenCheckMs = 250;
    private long moltenCheckedAtMs;
    private bool moltenChecked;
    private bool moltenCached;

    /// <summary>Units of metal that fill the crucible to the brim, for the purpose of pool height.</summary>
    private const float FullUnits = 500f;

    /// <summary>
    /// The hue comes off vanilla's incandescence ramp at the crucible's real temperature, on the
    /// forge's own schedule from ForgeContentsRenderer: nothing below 550 degC, then red through
    /// orange to pale yellow. Matching that matters because the crucible is drawn beside an ingot
    /// heating on the same coals and beside its own icon in the window, and both of those use this
    /// ramp - anything else reads as the wrong colour rather than as a choice.
    ///
    /// Do not hold the hue back while the charge is solid to keep somewhere brighter to go: 900 degC
    /// is exactly where the ramp is pure red with no green in it, so that pins a whole heat-up at
    /// flat salmon beside an icon that is orange-gold.
    ///
    /// How *far* that colour is mixed over the texture is capped, though, and that is not vanilla.
    /// An ingot may sensibly turn into a featureless bright blob, because at that heat it is one; a
    /// crucible doing it just looks like a rendering fault, and the window's own icon does not do it
    /// either. Capping the mix keeps the shape while the hue does the talking.
    ///
    /// The cap applies to the mix alone. Bloom - ExtraGlow - runs the full vanilla ramp, because the
    /// two answer different questions: how much of the crucible you still see, versus how much light
    /// it is throwing. Capping both left a molten crucible looking cooler than the coals underneath
    /// it, which is backwards.
    /// </summary>
    public static float GlowStartTemp = 550f;
    public static float GlowDivisor = 2f;
    public static int GlowCeiling = 150;

    public double RenderOrder => 0.5;
    public int RenderRange => 24;
    public Size2i AtlasSize => capi.BlockTextureAtlas.Size;
    public TextureAtlasPosition this[string textureCode] => ingotTextures[metalName];

    public CrucibleRenderer(BlockEntityCrucibulumForge be, ICoreClientAPI capi)
    {
        this.be = be;
        this.capi = capi;
    }

    public void OnContentsChanged()
    {
        renderedUnits = -1;
        moltenChecked = false;   // different contents, so the cached answer is about something else
    }

    private KeyValuePair<ItemStack, int> GetMelt(ItemStack crucible)
    {
        if (crucible?.Collectible is not BlockSmeltedContainer smelted) return default;
        return smelted.GetContents(capi.World, crucible);
    }

    /// <summary>The crucible body, plus the pool of metal if there is any.</summary>
    private void RegenMesh(ItemStack crucible, ItemStack metalStack, int units)
    {
        bodyRef?.Dispose(); bodyRef = null;
        meltRef?.Dispose(); meltRef = null;

        renderedCrucible = crucible?.Collectible;
        renderedMetal = metalStack?.Collectible;
        renderedUnits = units;
        renderedMeshAngle = be.MeshAngleRad;
        moltenChecked = false;

        if (crucible?.Block == null) return;

        ModelTransform tf = crucible.Collectible.Attributes?["inForgeTransform"].AsObject<ModelTransform>();
        tf?.EnsureDefaultValues();

        MeshData body = capi.TesselatorManager.GetDefaultBlockMesh(crucible.Block)?.Clone();
        if (body != null)
        {
            if (tf != null) body.ModelTransform(tf);
            body.Rotate(new Vec3f(0.5f, 0, 0.5f), 0, be.MeshAngleRad, 0);
            bodyRef = capi.Render.UploadMultiTextureMesh(body);
        }

        if (metalStack == null || units <= 0) return;

        Shape shape = Shape.TryGet(capi, "crucibulum:shapes/block/meltsurface.json");
        if (shape == null)
        {
            capi.Logger.Warning("[crucibulum] melt surface shape not found");
            return;
        }

        ingotTextures = capi.Tesselator.GetTextureSource(
            capi.World.GetBlock(new AssetLocation("ingotpile")), returnNullWhenMissing: true);

        metalName = metalStack.Collectible.Variant.TryGetValue("metal", out string variant)
            ? variant
            : metalStack.Collectible.LastCodePart();
        if (ingotTextures == null || ingotTextures[metalName] == null) metalName = "copper";

        capi.Tesselator.TesselateShape("crucibulum-melt", shape, out MeshData melt, this);
        if (melt == null) return;

        // Fill the mouth in proportion to how much metal is in there, growing up from the floor.
        melt.Scale(new Vec3f(0.5f, 1 / 16f, 0.5f), 1, GameMath.Clamp(units / FullUnits, 0.2f, 1f), 1);

        if (tf != null) melt.ModelTransform(tf);
        melt.Rotate(new Vec3f(0.5f, 0, 0.5f), 0, be.MeshAngleRad, 0);

        meltRef = capi.Render.UploadMultiTextureMesh(melt);
    }

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        ItemStack crucible = be.CrucibleStack;
        KeyValuePair<ItemStack, int> melt = GetMelt(crucible);

        if (!ReferenceEquals(crucible?.Collectible, renderedCrucible)
            || !ReferenceEquals(melt.Key?.Collectible, renderedMetal)
            || melt.Value != renderedUnits
            || be.MeshAngleRad != renderedMeshAngle)
        {
            RegenMesh(crucible, melt.Key, melt.Value);
        }

        if (crucible == null || bodyRef == null) return;

        int temp = (int)crucible.Collectible.GetTemperature(capi.World, crucible);
        bool molten = IsMolten(crucible, melt.Key);

        float[] bodyColor = ColorUtil.GetIncandescenceColorAsColor4f(temp);
        float[] meltColor = bodyColor;
        int bodyGlow = GameMath.Clamp((int)((temp - GlowStartTemp) / GlowDivisor), 0, 255);
        int bodyMix = Math.Min(bodyGlow, GlowCeiling);

        IRenderAPI rpi = capi.Render;
        Vec3d camPos = capi.World.Player.Entity.CameraPos;
        BlockPos pos = be.Pos;

        rpi.GlDisableCullFace();

        IStandardShaderProgram prog = rpi.StandardShader;
        prog.Use();
        prog.RgbaAmbientIn = rpi.AmbientColor;
        prog.RgbaFogIn = rpi.FogColor;
        prog.FogMinIn = rpi.FogMin;
        prog.FogDensityIn = rpi.FogDensity;
        prog.RgbaTint = ColorUtil.WhiteArgbVec;
        prog.DontWarpVertices = 0;
        prog.AddRenderFlags = 0;
        prog.ExtraGodray = 0;
        prog.OverlayOpacity = 0;
        prog.NormalShaded = 1;
        prog.TempGlowMode = 0;
        prog.RgbaLightIn = capi.World.BlockAccessor.GetLightRGBs(pos.X, pos.Y, pos.Z);
        prog.ViewMatrix = rpi.CameraMatrixOriginf;
        prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;

        // The forge renders its contents on top of the coal bed, which itself rises with the fuel.
        prog.ModelMatrix = modelMat
            .Identity()
            .Translate(pos.X - camPos.X, pos.Y - camPos.Y + 11 / 16f + (be.FuelLevel - 1) / 16f / 4f, pos.Z - camPos.Z)
            .Values;

        prog.RgbaGlowIn = new Vec4f(bodyColor[0], bodyColor[1], bodyColor[2], bodyMix / 255f);
        prog.ExtraGlow = bodyGlow;
        rpi.RenderMultiTextureMesh(bodyRef, "tex");

        if (meltRef != null && melt.Key != null)
        {
            // Liquid metal is lit from within; a solidified charge is just a dull disc of metal
            // sitting in the bottom, which is exactly what it should look like.
            int meltGlow = molten ? 255 : bodyGlow;

            prog.RgbaGlowIn = new Vec4f(meltColor[0], meltColor[1], meltColor[2], meltGlow / 255f);
            prog.ExtraGlow = meltGlow;
            rpi.RenderMultiTextureMesh(meltRef, "tex");
        }

        prog.Stop();
        rpi.GlEnableCullFace();
    }

    /// <summary>Whether the charge is liquid, asked a few times a second rather than every frame.</summary>
    private bool IsMolten(ItemStack crucible, ItemStack metal)
    {
        if (metal == null) return false;

        long now = capi.ElapsedMilliseconds;
        if (!moltenChecked || now - moltenCheckedAtMs >= MoltenCheckMs)
        {
            moltenChecked = true;
            moltenCheckedAtMs = now;
            moltenCached = !((BlockSmeltedContainer)crucible.Collectible).HasSolidifed(crucible, metal, capi.World);
        }

        return moltenCached;
    }

    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        bodyRef?.Dispose(); bodyRef = null;
        meltRef?.Dispose(); meltRef = null;
    }
}
