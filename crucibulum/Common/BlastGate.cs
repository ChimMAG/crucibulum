// Crucibulum - melt metal in a crucible on the forge, for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option) any
// later version. See COPYING.LESSER, or <https://www.gnu.org/licenses/>.

using Vintagestory.API.MathTools;

namespace Crucibulum;

/// <summary>
/// How far the forge's air inlet is open.
///
/// A forge is not set to a temperature, it is given air, and it settles wherever the fuel and the
/// draught put it. Vanilla already models that: the ceiling is
/// <c>MaxTemperature * (1 + extraOxygenRate)</c>, and a bellows drives the multiplier up to about
/// 1.64. A gate is the same lever below one, so the whole mechanic is vanilla's own, extended
/// downwards rather than invented.
///
/// The player never picks a temperature. They move a plate, and the readout says where the fire
/// ends up.
/// </summary>
public enum GatePosition
{
    Open = 0,
    Half = 1,
    Quarter = 2,
    Shut = 3,
}

public static class BlastGate
{
    /// <summary>
    /// How far the plate slides aside, in voxels, to uncover the inlet completely. Five, which is
    /// four and a half to clear the inlet plus half a voxel so the two do not sit flush. What sets
    /// the ceiling is no longer the plate but the handle leading it: plate and handle together run
    /// x 3.5 to 10, so five takes the pair to 15 - which is where the forge's own front wall ends,
    /// and any further out the handle hangs in the air against the background.
    /// </summary>
    public const float FullSlide = 5f;

    public const int Positions = 4;

    /// <summary>
    /// What each position does to the fire's ceiling. Shut is not airtight - a banked forge still
    /// burns, it just cannot get near forging heat - so the range stops well short of zero.
    /// </summary>
    public static float AirFactor(GatePosition position)
    {
        CrucibulumConfig cfg = CrucibulumModSystem.Config;

        float factor = position switch
        {
            GatePosition.Open => cfg.GateAirOpen,
            GatePosition.Half => cfg.GateAirHalf,
            GatePosition.Quarter => cfg.GateAirQuarter,
            _ => cfg.GateAirShut,
        };

        // Clamped rather than trusted. A gate is a plate over the air inlet: it can only ever
        // restrict, so nothing above full draught, and shut is a banked fire rather than an
        // airtight one, so nothing at zero either - which would also divide the burn rate away.
        return GameMath.Clamp(factor, 0.05f, 1f);
    }

    /// <summary>
    /// How far the plate has been drawn back, in blocks. This is the whole readout: shut covers the
    /// inlet, open leaves it black and open, and you can see which from across the workshop.
    /// </summary>
    public static float Slide(GatePosition position) =>
        FullSlide / 16f * ((Positions - 1 - (int)position) / (float)(Positions - 1));

    public static GatePosition Next(GatePosition position) =>
        (GatePosition)(((int)position + 1) % Positions);

    public static string LangKey(GatePosition position) => position switch
    {
        GatePosition.Open => "crucibulum:gate-open",
        GatePosition.Half => "crucibulum:gate-half",
        GatePosition.Quarter => "crucibulum:gate-quarter",
        _ => "crucibulum:gate-shut",
    };
}
