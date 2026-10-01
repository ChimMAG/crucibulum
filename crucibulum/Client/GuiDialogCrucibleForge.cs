// Crucibulum - melt metal in a crucible on the forge, for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option) any
// later version. See COPYING.LESSER, or <https://www.gnu.org/licenses/>.

using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Crucibulum;

/// <summary>
/// The forge's crucible window.
///
/// Laid out like the firepit's, because that is the one every player already knows: the four
/// ingredient slots across the top and the crucible below them. What the firepit does not show,
/// and this does, is the blend — each metal's share of the melt, on the same measure alloy
/// recipes are written in, and the ratio to aim for when the mix is wrong.
///
/// No fuel slot. The forge is fuelled the way a forge has always been fuelled, by shift-clicking
/// coal onto it, and this window is only about what goes in the crucible. Putting fuel here as
/// well meant two different ways to do the one job, and it was the odd one out.
/// </summary>
public class GuiDialogCrucibleForge : GuiDialogBlockEntity
{
    private long lastRedrawMs;
    private EnumPosFlag screenPos;
    private string currentStatus = "";
    private bool wasMelting;
    private bool closing;
    private string currentGate = "";
    private bool composedWithGate;
    private bool sawCrucible;
    private bool leftHeld;
    private bool rightHeld;
    private bool relayoutWanted;
    private bool relayoutQueued;

    private ElementBounds chargeSlotBounds;
    private ElementBounds meltBarBounds;

    protected override double FloatyDialogPosition => 0.6;
    protected override double FloatyDialogAlign => 0.8;
    public override double DrawOrder => 0.2;

    public GuiDialogCrucibleForge(string title, InventoryBase inventory, BlockPos pos, SyncedTreeAttribute tree, ICoreClientAPI capi)
        : base(title, inventory, pos, capi)
    {
        if (IsDuplicate) return;

        tree.OnModified.Add(new TreeModifiedListener { listener = OnAttributesModified });
        Attributes = tree;
    }

    /// <summary>
    /// The window belongs to the crucible. Take the crucible away - out through the slot, by
    /// shift-clicking it off the forge, or by breaking the forge - and there is nothing left for
    /// the window to be about, so it closes rather than sitting open over an empty slot.
    ///
    /// Checked per frame rather than off the slot event, because it has to survive a drag. The
    /// instant a crucible is lifted out of the slot it is riding the mouse cursor and the slot is
    /// already empty; closing right then would take the drag down with it and drop the crucible
    /// wherever a closing window puts things. Waiting until the cursor is empty again means the
    /// window goes once the crucible has actually been put somewhere.
    /// </summary>
    private void CloseIfTheCrucibleHasGone()
    {
        if (closing || !IsOpened() || Inventory == null) return;

        // "The crucible left", not "there is no crucible". The window is opened by the server, and
        // the client's copy of the inventory can arrive a moment later - closing on that gap shuts
        // the window the instant it opens, which it did, intermittently.
        if (!Inventory[0].Empty)
        {
            sawCrucible = true;
            return;
        }

        if (!sawCrucible) return;
        if (capi.World.Player?.InventoryManager?.MouseItemSlot?.Empty == false) return;

        closing = true;
        capi.Event.EnqueueMainThreadTask(() => TryClose(), "closecrucibleforgedlg");
    }

    public override void OnRenderGUI(float deltaTime)
    {
        base.OnRenderGUI(deltaTime);
        if (relayoutWanted && !MouseButtonHeld) RequestRelayout();
        CloseIfTheCrucibleHasGone();
    }

    /// <summary>What the gate is doing, as a thing you press rather than a number you set.</summary>
    private string GateLabel() => Lang.Get("crucibulum:gate-button",
        Lang.Get(Attributes.GetString("gatePositionKey", "crucibulum:gate-open")),
        Attributes.GetInt("gateCeiling"));

    private bool OnGateClicked()
    {
        capi.Network.SendBlockEntityPacket(BlockEntityPosition, BlockEntityCrucibulumForge.CycleGatePacketId, null);
        return true;
    }

    /// <summary>
    /// Any left or right button held, anywhere - deliberately broader than "dragging over this
    /// window's slots". A held button may be a sweep, and vanilla's slot grid keeps that sweep's
    /// state in the grid element itself, so laying the window out again would replace the grid and
    /// drop the rest of the sweep; a relayout waits for the release instead. No check of the cursor:
    /// a left sweep drops the whole stack into the first slot and shares it back out from there. The
    /// API's mouse events rather than the dialog's, because they see a release over another window.
    /// </summary>
    private bool MouseButtonHeld => leftHeld || rightHeld;

    private void OnMouseButtonDown(MouseEvent e) => SetHeld(e.Button, true);
    private void OnMouseButtonUp(MouseEvent e) => SetHeld(e.Button, false);

    private void SetHeld(EnumMouseButton button, bool held)
    {
        if (button == EnumMouseButton.Left) leftHeld = held;
        if (button == EnumMouseButton.Right) rightHeld = held;
    }

    private void RequestRelayout()
    {
        relayoutWanted = true;
        if (MouseButtonHeld || relayoutQueued) return;

        relayoutQueued = true;
        capi.Event.EnqueueMainThreadTask(() =>
        {
            relayoutQueued = false;

            // Asked again when it runs, because a press can land between queueing this and the
            // frame that runs it. If one has, it stays wanted and goes on the release.
            if (!IsOpened() || !relayoutWanted || MouseButtonHeld) return;

            relayoutWanted = false;
            SetupDialog();
        }, "setupcrucibleforgedlg");
    }

    private void SetupDialog()
    {
        ItemSlot hoveredSlot = capi.World.Player.InventoryManager.CurrentHoveredSlot;
        if (hoveredSlot != null && hoveredSlot.Inventory?.InventoryID != Inventory?.InventoryID) hoveredSlot = null;

        currentStatus = Attributes.GetString("statusText", "");

        // Top to bottom: what is in the crucible, what will come of it, how hot it is and how far
        // along, and what is burning underneath.
        //
        // No output slot and so no arrow between two of them: the crucible is not a furnace that
        // moves metal from one slot to another, it becomes the molten thing in place. An arrow
        // here would point at nothing, which is what the first cut of this window did.
        const double panelWidth = 300;
        const double rowLabelX = 62;
        const double slotSize = 48;
        const double gap = 12;

        CairoFont statusFont = CairoFont.WhiteDetailText();

        // 24, not 0: the title bar is drawn into the top of the child area, so a slot row placed
        // near the origin butts straight up against it. The firepit starts its first row at 30 for
        // the same reason; this is a little tighter now that there is no heading to sit between.
        chargeSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 24, 4, 1);

        // The status block is measured rather than given a fixed height. One line for a plain melt,
        // three for a mix that will not combine - reserving room for the worst case left a hole
        // under the common one, which is what made the window look padded out with nothing.
        double statusY = chargeSlotBounds.fixedY + chargeSlotBounds.fixedHeight + gap;
        double statusHeight = string.IsNullOrEmpty(currentStatus)
            ? 0
            : new TextDrawUtil().GetMultilineTextHeight(statusFont, currentStatus, panelWidth) / RuntimeEnv.GUIScale;

        ElementBounds statusBounds = ElementBounds.Fixed(0, statusY, panelWidth, Math.Max(statusHeight, 1));

        double crucibleY = statusY + statusHeight + (statusHeight > 0 ? gap + 4 : 4);
        ElementBounds crucibleBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, crucibleY, 1, 1);

        // Melting adds a bar under the temperature, so the row is taller only while it is there.
        bool melting = Attributes.GetFloat("maxMeltTime", 0) > 0 && Attributes.GetFloat("meltProgress", 0) > 0;
        ElementBounds crucibleTempBounds = ElementBounds.Fixed(
            rowLabelX, crucibleY + (melting ? 6 : (slotSize - 24) / 2), panelWidth - rowLabelX, 24);

        meltBarBounds = ElementBounds.Fixed(rowLabelX, crucibleY + 32, panelWidth - rowLabelX, 10);

        // The gate row, only for a forge that has one. A forge holding a crucible opens this window
        // on a click, so the click that works the gate on a bare forge cannot be used here - this
        // is the way to reach it without taking the crucible out first.
        bool haveGate = Attributes.GetInt("haveGate") > 0;
        composedWithGate = haveGate;
        double gateY = crucibleY + slotSize + gap;
        ElementBounds gateBounds = ElementBounds.Fixed(0, gateY, panelWidth, 28);

        ElementBounds panelBounds = ElementBounds.Fixed(0, 0, panelWidth,
            haveGate ? gateY + 28 : crucibleY + slotSize);

        ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        bgBounds.WithChildren(panelBounds);

        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithFixedAlignmentOffset(IsRight(screenPos) ? -GuiStyle.DialogToScreenPadding : GuiStyle.DialogToScreenPadding, 0)
            .WithAlignment(IsRight(screenPos) ? EnumDialogArea.RightMiddle : EnumDialogArea.LeftMiddle);

        if (!capi.Settings.Bool["immersiveMouseMode"])
        {
            dialogBounds.fixedOffsetY += (panelBounds.fixedHeight + 65) * YOffsetMul(screenPos);
            dialogBounds.fixedOffsetX += (panelBounds.fixedWidth + 10) * XOffsetMul(screenPos);
        }

        int[] chargeSlotIds = new int[BlockEntityCrucibulumForge.ChargeSlotCount];
        for (int i = 0; i < chargeSlotIds.Length; i++) chargeSlotIds[i] = BlockEntityCrucibulumForge.FirstChargeSlot + i;

        SingleComposer = capi.Gui
            .CreateCompo("crucibulumforge" + BlockEntityPosition, dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar(DialogTitle, () => TryClose())
            .BeginChildElements(bgBounds)
                .AddDynamicCustomDraw(panelBounds, OnBgDraw, "symbolDrawer")
                .AddItemSlotGrid(Inventory, DoSendPacket, 4, chargeSlotIds, chargeSlotBounds, "chargeSlots")

                .AddDynamicText("", statusFont, statusBounds, "statusText")

                .AddItemSlotGrid(Inventory, DoSendPacket, 1, new[] { 0 }, crucibleBounds, "crucibleSlot")
                .AddDynamicText("", CairoFont.WhiteDetailText(), crucibleTempBounds, "crucibleTemp")

                .AddIf(haveGate)
                    .AddSmallButton(GateLabel(), OnGateClicked, gateBounds, EnumButtonStyle.Normal, "gateButton")
                .EndIf()
            .EndChildElements()
            .Compose();

        lastRedrawMs = capi.ElapsedMilliseconds;

        if (hoveredSlot != null) SingleComposer.OnMouseMove(new MouseEvent(capi.Input.MouseX, capi.Input.MouseY));

        SingleComposer.GetDynamicText("statusText").SetNewText(currentStatus, true);
        OnAttributesModified();
    }

    private void OnAttributesModified()
    {
        if (!IsOpened() || SingleComposer == null) return;

        float temp = Attributes.GetFloat("crucibleTemp");
        bool haveCrucible = Attributes.GetInt("haveCrucible") > 0;

        string tempText = !haveCrucible ? ""
            : temp <= 20 ? Lang.Get("Cold")
            : $"{temp:#}°C";

        // What it is climbing towards. Only while it is still below: once the metal is going, the
        // bar underneath says how far along it is and the threshold has stopped mattering.
        float meltingPoint = Attributes.GetFloat("meltingPoint");
        if (haveCrucible && meltingPoint > 0 && temp < meltingPoint)
        {
            tempText = Lang.Get("crucibulum:dlg-meltsat", tempText, (int)meltingPoint);
        }

        SingleComposer.GetDynamicText("crucibleTemp").SetNewText(tempText);


        // The status text and the melt bar both change the height of the window, so a change in
        // either has to lay it out again rather than just re-letter it in place.
        string status = Attributes.GetString("statusText", "");
        bool melting = Attributes.GetFloat("maxMeltTime", 0) > 0 && Attributes.GetFloat("meltProgress", 0) > 0;

        // The gate's label is drawn into the button's texture when it is composed, so moving the
        // gate means laying the window out again rather than re-lettering it in place.
        string gate = GateLabel();

        // Whether there is a gate at all, and not only what it says. A plate fitted to a forge
        // someone already has the window open on leaves every other line identical - a new gate is
        // open, which is the ceiling the window was already quoting - so nothing else here notices
        // it, and the button simply never appeared until the window was closed and reopened.
        bool haveGate = Attributes.GetInt("haveGate") > 0;

        if (status != currentStatus || melting != wasMelting || gate != currentGate || haveGate != composedWithGate)
        {
            currentStatus = status;
            wasMelting = melting;
            currentGate = gate;
            RequestRelayout();
            return;
        }

        if (capi.ElapsedMilliseconds - lastRedrawMs > 250)
        {
            SingleComposer.GetCustomDraw("symbolDrawer")?.Redraw();
            lastRedrawMs = capi.ElapsedMilliseconds;
        }
    }

    /// <summary>
    /// The melt bar under the crucible's temperature. Drawn rather than composed so it can simply
    /// not be there when there is nothing melting, instead of sitting empty and looking stuck.
    /// </summary>
    private void OnBgDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        float maxMelt = Attributes.GetFloat("maxMeltTime", 0);
        float progress = Attributes.GetFloat("meltProgress", 0);

        // Nothing melting, no bar. An empty trough sitting there permanently reads as a thing that
        // is stuck rather than a thing that has not started.
        if (maxMelt <= 0 || progress <= 0 || meltBarBounds == null) return;

        double rel = GameMath.Clamp(progress / maxMelt, 0, 1);

        double x = GuiElement.scaled(meltBarBounds.fixedX);
        double y = GuiElement.scaled(meltBarBounds.fixedY);
        double w = GuiElement.scaled(meltBarBounds.fixedWidth);
        double h = GuiElement.scaled(meltBarBounds.fixedHeight);

        ctx.Save();

        // Trough.
        GuiElement.RoundRectangle(ctx, x, y, w, h, 2);
        ctx.SetSourceRGBA(0, 0, 0, 0.4);
        ctx.Fill();

        {
            GuiElement.RoundRectangle(ctx, x, y, Math.Max(GuiElement.scaled(3), w * rel), h, 2);
            LinearGradient molten = new LinearGradient(x, 0, x + w, 0);
            molten.AddColorStop(0, new Color(0.65, 0.18, 0.02, 1));
            molten.AddColorStop(1, new Color(1, 0.66, 0.15, 1));
            ctx.SetSource(molten);
            ctx.Fill();
            molten.Dispose();
        }

        ctx.Restore();
    }

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        closing = false;
        sawCrucible = false;
        leftHeld = rightHeld = false;
        relayoutWanted = relayoutQueued = false;
        capi.Event.MouseDown += OnMouseButtonDown;
        capi.Event.MouseUp += OnMouseButtonUp;

        screenPos = GetFreePos("smallblockgui");
        OccupyPos("smallblockgui", screenPos);
        SetupDialog();
    }

    public override void OnGuiClosed()
    {
        capi.Event.MouseDown -= OnMouseButtonDown;
        capi.Event.MouseUp -= OnMouseButtonUp;

        SingleComposer.GetSlotGrid("chargeSlots")?.OnGuiClosed(capi);
        SingleComposer.GetSlotGrid("crucibleSlot")?.OnGuiClosed(capi);

        FreePos("smallblockgui", screenPos);
        base.OnGuiClosed();
    }
}
