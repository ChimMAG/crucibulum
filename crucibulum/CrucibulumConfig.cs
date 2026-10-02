// Crucibulum - melt metal in a crucible on the forge, for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option) any
// later version. See COPYING.LESSER, or <https://www.gnu.org/licenses/>.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Vintagestory.API.Common;

namespace Crucibulum;

/// <summary>
/// Written to ModConfig/crucibulum.json on first run.
/// </summary>
public class CrucibulumConfig
{
    /// <summary>
    /// Degrees added to the forge's own fuel-derived ceiling (700 + the fuel's inForge.tempGainDeg)
    /// when a crucible is sitting in it. 400 puts coke at 1200 C, charcoal at 1150, bituminous coal
    /// at 1100, lignite at 1000 and contaminated coal at 900 -- so fuel choice still decides which
    /// metals you can melt, but copper no longer needs a bellows.
    /// </summary>
    [Category("Heat")]
    [Description("Degrees a crucible adds to the forge's fuel ceiling.")]
    [Range(0, 1200)]
    public int CrucibleTempBonus = 400;

    /// <summary>
    /// Hard ceiling on crucible temperature. 0 means "use the crucible's own maxHeatableTemp
    /// attribute", which vanilla ships as 1200 C. This is what keeps iron (1500 C) out of reach
    /// even with a bellows running.
    /// </summary>
    [Category("Heat")]
    [Description("Hard ceiling in degrees; 0 uses the crucible's own maxHeatableTemp.")]
    [Range(0, 2000)]
    public int MaxCrucibleTemperature = 0;

    /// <summary>
    /// Multiplier on how fast the charge melts once it is up to temperature. 1 matches the firepit.
    /// </summary>
    [Category("Melting")]
    [Description("How fast a charge melts once hot enough; 1 matches the firepit.")]
    [Range(0.1, 10)]
    public float MeltSpeedMultiplier = 1f;

    /// <summary>
    /// Whether an ingot may go into the crucible.
    ///
    /// Off by default because a firepit's crucible refuses one: the fired crucible declares a mouth
    /// of 0.125 x 0.25 x 0.125 and an ingot is the collectible default of 0.5, so the inventory
    /// turns it away by size before smelting is ever asked. Versions before 1.4.0 did not apply that
    /// limit at the forge and took ingots; this is for a world that liked it that way. It admits
    /// ingots and nothing else - a work item is still refused.
    ///
    /// Read live, so flipping it takes effect at once and needs no restart.
    /// </summary>
    [Category("Melting")]
    [Description("Let ingots into the crucible, which a firepit refuses by size.")]
    public bool MeltIngots = false;

    /// <summary>
    /// Whether a broken tool head may go into the crucible, to come back as a whole ingot.
    ///
    /// Smithing Plus hands a head back when a tool breaks, as a plain work item marked with a
    /// brokenCount, and means for it to be chiselled into bits worth what is left of it. Melting
    /// it is refused at a firepit by the same size rule an ingot meets, and is refused here for the
    /// same reason - a 100-unit ingot out of a tool that has been worn to nothing is what the
    /// mod's balance rests on not happening. On, exactly that class of item is admitted: an
    /// unfinished work item straight off the anvil still is not.
    ///
    /// Read live.
    /// </summary>
    [Category("Melting")]
    [Description("Let broken tool heads into the crucible, for a whole ingot back; a firepit refuses them.")]
    public bool MeltBrokenToolHeads = false;

    /// <summary>
    /// Whether the sparks a melting charge throws can set light to what they land on.
    ///
    /// A spark comes down somewhere within a couple of blocks of the forge every so often while the
    /// metal is melting - not once it is molten - and where it lands next to something that burns,
    /// it starts a fire the way vanilla's own spreading fire does; a pile of firewood or coal it
    /// lands on is lit directly. Off by default, since it turns the forge into a hazard for
    /// anyone who built a workshop out of wood. The world's own fire spread has to be on as well.
    ///
    /// The spark itself is held back by solid blocks between the crucible and where it would land,
    /// and by land claims: it neither lands on nor burns fuel in a claim the forge is not inside. A
    /// fire it does start is ordinary vanilla fire from then on, and spreads as one - which, like
    /// any fire whose starter the game cannot name, pays no attention to claims.
    ///
    /// Read live.
    /// </summary>
    [Category("Sparks")]
    [Description("Let a melting charge's sparks start fires around the forge, if the world allows fire spread.")]
    public bool SparksSpreadFire = false;

    /// <summary>
    /// How many sparks a melting charge throws, as a multiple of the default - which builds from
    /// about seven a second as a melt starts to about twenty-five as it nears liquid. 0 turns them
    /// off; the burst when a melt finishes has a setting of its own.
    ///
    /// Only how it looks: what can start a fire is <see cref="SparkLandingSeconds"/>. Read live.
    /// </summary>
    [Category("Sparks")]
    [Description("How many sparks a melting charge throws, as a multiple of the default; 0 for none.")]
    [Range(0, 5)]
    public float MeltSparkRate = 1f;

    /// <summary>
    /// How big the burst of sparks is when a melt finishes, as a multiple of the default. 0 turns
    /// it off. Read live.
    /// </summary>
    [Category("Sparks")]
    [Description("The burst of sparks when a melt finishes, as a multiple of the default; 0 for none.")]
    [Range(0, 5)]
    public float MeltDoneSparkBurst = 1f;

    /// <summary>
    /// With <see cref="SparksSpreadFire"/> on, how many seconds go by on average between sparks
    /// coming down somewhere around a melting forge. Each one lands at random within two blocks, and
    /// most of a workshop floor will not burn, so this is the rate of chances rather than of fires.
    /// Read live.
    /// </summary>
    [Category("Sparks")]
    [Description("With sparks spreading fire, average seconds between sparks landing around a melting forge.")]
    [Range(1, 600)]
    public float SparkLandingSeconds = 10f;

    /// <summary>
    /// How fast the forge burns while working a crucible, against the firepit's rate per second.
    ///
    /// The forge burns on the calendar and the firepit on the real clock, which left melting over
    /// a forge six times cheaper for the same charge. This closes that gap.
    ///
    /// Note it is a rate, not a bill: what a job actually costs also depends on how long it takes,
    /// and the forge spends longer bringing a charge up to temperature than a firepit does. Over a
    /// whole job - heating and melting together - the default 0.6 lands a forge melt at roughly
    /// 0.65 of a firepit's fuel for a small charge and 0.8 for a brim-full crucible, so the forge
    /// stays the better place to melt without making fuel meaningless.
    ///
    /// Set it to 0 to go back to the vanilla forge burn rate and the six-fold discount.
    /// </summary>
    [Category("Fuel")]
    [Description("Burn rate while working, against a firepit's per-second rate; 0 disables the correction.")]
    [Range(0, 4)]
    public float CrucibleFuelUseVsFirepit = 0.6f;

    /// <summary>
    /// What a melt costs to keep liquid, as a fraction of what it cost to make.
    ///
    /// Once the metal is molten the fire is only replacing what the crucible loses to the air, so
    /// holding is cheap: the default 0.35 leaves a molten crucible costing about a third of what
    /// the melt did - one lump of coke lasts 190s against melting's 67s - and barely more than an
    /// empty forge's 240s.
    /// </summary>
    [Category("Fuel")]
    [Description("What holding a melt costs as a fraction of making one.")]
    [Range(0.05, 2)]
    public float MoltenHoldFuelShare = 0.35f;

    /// <summary>
    /// The thermal mass of the crucible itself, in ingot-equivalents.
    ///
    /// A charge heats the crucible in proportion to how much metal is in it against how much the
    /// clay itself soaks up, so this sets how much the charge size matters. At the default 3, an
    /// empty crucible climbs at full speed, a single ingot's worth of ore takes about a third
    /// longer, and a brim-full seven-ingot crucible takes over three times as long to reach
    /// temperature. Raise it to flatten the difference out.
    /// </summary>
    [Category("Heat")]
    [Description("The clay's own heat capacity in ingot-equivalents; sets how much charge size matters.")]
    [Range(0.1, 20)]
    public float CrucibleThermalMass = 3f;

    /// <summary>
    /// How briskly the crucible climbs towards the temperature the fuel can hold it at.
    ///
    /// The climb is proportional to how far there is left to go, so it is quick while the crucible
    /// is cold and eases in over the last stretch. 0.5 brings a cold crucible up in a coke forge in
    /// about two minutes; raise it to heat faster.
    /// </summary>
    [Category("Heat")]
    [Description("How briskly the crucible climbs towards the ceiling.")]
    [Range(0.05, 5)]
    public float HeatRate = 0.5f;

    /// <summary>
    /// Whether a crucible is refused by the firepit, so the forge is the only place to melt.
    ///
    /// Off by default: vanilla's firepit keeps taking crucibles and this mod only adds the forge
    /// as a second option. Turned on, every way of getting a crucible into a firepit - a click on
    /// the block, a drag into its window, a shift-click from the inventory - is refused, and the
    /// firepit window opens instead as it would for any other item. Taking one back out is not
    /// touched, so a crucible already sitting in a firepit when this is switched on comes out as
    /// it always did. Ore in hand is unaffected; it never smelted without a crucible anyway.
    ///
    /// Read live, so flipping it takes effect at once and needs no restart.
    /// </summary>
    [Category("Firepit")]
    [Description("Refuse crucibles at the firepit, so metal only melts in the forge.")]
    public bool CrucibleOnlyInForge = false;

    /// <summary>
    /// Whether a forge can be fitted with a blast gate at all. Turning this off leaves every forge
    /// on full draught, which is vanilla's behaviour; gates already fitted stop throttling and can
    /// still be taken back out.
    /// </summary>
    [Category("Blast gate")]
    [Description("Whether a forge can be fitted with a blast gate at all.")]
    public bool EnableBlastGate = true;

    /// <summary>
    /// Whether the gate on a burning forge may only be worked with tongs in the off hand.
    ///
    /// Off by default, because it asks the player to carry a second tool to use a mechanic that was
    /// free before. Turned on, a lit forge's plate is treated the way vanilla treats any metal over
    /// <see cref="Vintagestory.API.Config.GlobalConstants.TooHotToTouchTemperature"/>: bare hands
    /// are refused, with an in-game error saying why, and tongs - anything whose tool is
    /// <see cref="EnumTool.Tongs"/>, so a modded pair counts - let it through.
    ///
    /// Only while the forge is burning. There is nothing hot about the plate on a cold forge, so
    /// setting a gate before lighting the fire needs nothing in the off hand; and it is the only
    /// time the setting would otherwise be pure friction, since a cold forge's gate does not matter
    /// until it is lit. Taking the plate back off a burning forge is held to the same rule as
    /// working it - otherwise bare hands could pull the whole hot plate free and refit it wherever
    /// they liked, which is the gesture the requirement is meant to stop.
    ///
    /// Read live, so flipping it takes effect at once and needs no restart.
    /// </summary>
    [Category("Blast gate")]
    [Description("Require tongs in the off hand to work a burning forge's gate.")]
    public bool RequireTongsForGate = false;

    /// <summary>
    /// What each notch of the blast gate does to the fire, as a share of full draught. These are
    /// the same lever vanilla's bellows works from the other side, where the ceiling is
    /// <c>MaxTemperature * (1 + extraOxygenRate)</c> and a bellows takes the multiplier up to about
    /// 1.64 - so one is an open gate and anything less is a throttled one.
    ///
    /// A gate only ever restricts, so these are clamped to at most one however they are set here;
    /// a plate over the air inlet cannot make a fire burn hotter than an open one. They are also
    /// held above zero, since shut is a banked fire rather than an airtight one.
    ///
    /// Shut at 0.55 puts a crucible on coke at about 660 degC, which sits inside the workable band
    /// of most metals - vanilla counts metal workable at half its melting point. Moving these moves
    /// which metals can be held workable without melting.
    /// </summary>
    [Category("Blast gate")]
    [Description("Share of full draught with the gate open.")]
    [Range(0.05, 1)]
    public float GateAirOpen = 1.00f;
    [Category("Blast gate")]
    [Description("Share of full draught with the gate half open.")]
    [Range(0.05, 1)]
    public float GateAirHalf = 0.85f;
    [Category("Blast gate")]
    [Description("Share of full draught with the gate a quarter open.")]
    [Range(0.05, 1)]
    public float GateAirQuarter = 0.70f;
    [Category("Blast gate")]
    [Description("Share of full draught with the gate shut.")]
    [Range(0.05, 1)]
    public float GateAirShut = 0.55f;
}
