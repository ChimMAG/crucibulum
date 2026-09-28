# Crucibulum

Set a crucible in the forge and melt metal where you smith it.

Vintage Story 1.22 gave the forge a real rebuild — fuel types that matter, a bellows that
actually drives the temperature. This puts a crucible in it, so the forge earns its keep
between heats instead of sitting idle while you walk back to a firepit.

The crucible climbs the same incandescence range a work item on the forge does — dull red,
through orange, to pale yellow — and the metal itself lights up in the mouth once it is liquid.

## Using it

Same gestures a firepit uses.

| | |
|---|---|
| **Right click** a forge holding a crucible | open the crucible window |
| **Right click** with a crucible | set it in the forge |
| **Shift + right click** with a crucible | set it in the forge |
| **Shift + right click**, empty handed | take the crucible back out, ore and all |

The window belongs to the crucible: take the crucible off the forge and it closes, rather than
being left open over an empty slot. Whatever ore was in the crucible comes back with it, whether
it left by shift-click or was dragged out of the window - it is not left sitting in the forge.

Ore goes in through the window, and fuel goes on the forge the way it always has. Shift is the
crucible itself and nothing else.

### Running the fire cooler

A forge is not set to a temperature, it is given air, and it settles wherever the fuel and the
draught put it — which is how vanilla already models it, since a bellows drives the ceiling up by
a multiplier. A **blast gate** is that same lever below one.

Fit any metal plate to a forge and it becomes the gate. Click the plate to slide it across, and
watch where the fire ends up:

| gate | crucible on coke | ingot on coke | fuel |
|---|---|---|---|
| open | 1200 °C | 800 °C | vanilla rate |
| half open | 1020 °C | 680 °C | slower |
| a quarter open | 840 °C | 560 °C | slower still |
| shut | 660 °C | 440 °C | about half |

Two columns because a crucible is held above the fuel's own ceiling — that is what
`CrucibleTempBonus` is for — and the readout quotes whichever applies to what the forge is
holding.

You never choose a number. You move a plate — visibly, on the front of the forge, so a glance
across the workshop says how a fire is set — and the block info reports the consequence:
`Blast gate: half open — 1020°C`. Less air also means less fuel burnt, so running cool is a trade
rather than a free win. Shift-click with an empty hand takes the plate back, and a forge without
one behaves exactly as it always did.

The plate is what takes the click, not the whole block. A forge holding an ingot still hands it
back on a plain click anywhere else on it, exactly as a vanilla forge does — so smithing at a
gated forge is the same rhythm it always was. On a bare forge, where nothing else wants the click,
anywhere on it works the gate.

Metal already hotter than the damped fire cools down to it, which is the point: it makes a forge
something you can hold a piece *at*, rather than only drive to the fuel's limit.

The plate carries a handle — a short arm off its leading edge and a grip bar standing clear of
that — so there is visibly something to take hold of, and it rides with the plate, which makes the
notch readable from further off than the plate alone did. Leading rather than trailing: on the
trailing edge it would be drawn back across the uncovered inlet as the gate opened, putting the
one part you are meant to grab in the draught. Plate and inlet both sit a voxel and a half left
of centre to leave it room to park against the wall at full open.

#### Tongs

`RequireTongsForGate` asks for tongs in the off hand before a **burning** forge's gate will move,
which is the same rule vanilla applies to anything else too hot to touch. Bare hands get the
in-game error rather than a silent refusal. It is off by default, since it asks a player to carry
a second tool for something that was free before.

Only while the fire is lit: there is nothing hot about a plate on a cold forge, so a gate can
still be set before lighting it. Taking the plate back off is held to the same rule — otherwise
bare hands could pull the whole hot plate free and refit it at whatever notch they liked. The
tongs are not worn by it; anything whose tool is `Tongs` counts, so a modded pair works.

It works on a seated crucible too, which is what makes bit smithing possible at a forge. Vanilla
counts metal as workable at half its melting point, so copper bits are workable from 542 °C and
molten at 1084 — a quarter-open gate settles a crucible of them at about 830 °C, hot enough to
work and never near melting. Copper, gold and silver all have at least two notches inside their
band.

### Adding it to an existing world

Forges already standing when the mod is installed are brought up to date as their chunks load,
keeping whatever was in them. Nothing to do by hand — and in particular nothing to break and
replace, which is what an earlier version required: patching the blocktype changes what gets built
from then on, but a saved chunk records the class its block entity was written with, so an existing
forge came back as the vanilla one under a block that was already ours. It offered the crucible in
its interaction help and then did nothing.

### Other mods' forges

QPTech's [ChiselTools](https://mods.vintagestory.at/chiseltools) decorative forge melts metal too,
and keeps its chiselled cover — see [docs/compat.md](docs/compat.md) for why that works and what
would break it. The support is a patch guarded with `dependsOn`, so it costs nothing when
ChiselTools is not installed.

### Smithing is untouched

A forge with no crucible in it behaves exactly as it always has, and there is a suite of tests
that exists purely to keep it that way:

- an ingot burns fuel at **vanilla's own rate** — the faster burn is for melting only
- an ingot heats as it always did, by the vanilla forge's own tick
- the 1200 °C crucible ceiling is **not** a ceiling on smithing; a blown forge still drives an
  ingot past it
- a plain right click on a forge holding an ingot hands it over, as ever
- a plain right click with something in hand on a bare forge still does nothing, rather than
  putting a window in the smith's face
- ingots still stack four to the slot, still drop when the forge is broken, and the block info
  says nothing about crucibles

Fuel, ignition and work items are untouched — the forge still does everything it did.

### The crucible window

Right click a forge with a crucible in it and the window opens: the four ingredient slots across
the top, then what they will make, then the crucible with its temperature and a melt bar, then
the fuel.

There is no output slot and so no arrow between two of them — a crucible is not a furnace moving
metal from one slot to another, it *becomes* the molten thing in place. The melt bar only appears
once something is actually melting, rather than sitting empty and looking stuck.

What the window adds over the firepit's is the blend. The firepit shows you slots and then says *nothing at all* when a mix
matches no alloy, which looks identical to a mix that does; here each metal's share is shown, on
the same measure alloy recipes are written in — units of metal, not lumps, so twenty nuggets of
native copper count as 100:

```
Copper 90% · Tin 10%
Will create 100 units of Tin bronze
```

and when the ratio is off it says so, and says what it wants:

```
Copper 40% · Tin 60%
These will not combine
Tin bronze needs Tin 8-12%, Copper 88-92%
```

If the metals make no alloy at all, it says they will not combine and quotes no ratio, rather
than sending you after a recipe that does not exist.

The slots filter what they will take, so the window cannot be used as a chest — and they take
exactly what a firepit's crucible takes. That means ore and nuggets, and **not** ingots, work items
or broken tool heads: a firepit refuses those by size, not by anything to do with smelting, since
the crucible declares a mouth a nugget fits and an ingot does not. Versions before 1.4.0 missed
that limit at the forge, and a tool worn to nothing came back as a whole ingot. `MeltIngots` and
`MeltBrokenToolHeads` put either back, one class each, for a world that wants it.

The slots also want a fired crucible actually sitting in the forge: 1.4.0 through 1.4.2 measured the
mouth of whatever was in the work slot, which let an ingot in while the crucible was on the cursor
or had already run molten, whatever the two switches said.

There is no fuel slot: the forge is fuelled the way a forge has always been fuelled, by
shift-clicking coal onto it, and this window is only about what goes in the crucible.

### And the same thing in the block info

You do not have to open anything for the common case. Point at the forge and the contents,
shares, yield, `Melts at 1084°C`, `Melting: 45%`, and finally
`Molten: 200 units of Copper — ready to pour` are all in the block info. If the fuel cannot get
there it says so instead: `Needs 1084°C; this fuel tops out at 1000°C`.

The window carries the same target on its temperature line — `900°C · melts at 1084°C` — so
there is no guessing what a crucible is waiting for. It drops off once the metal is going, when
the bar underneath has taken over the question.

### What you can melt

The forge's own ceiling is `700 + the fuel's tempGainDeg`. A crucible adds 400 to that, the
bellows multiplier applies as it always does, and the result is clamped by the crucible's own
`maxHeatableTemp` — an attribute the game already ships (1200) and never reads.

Unaided, that makes fuel choice the thing that decides what you can melt:

| fuel | ceiling | highest-melting metal in reach |
|---|---|---|
| coke | 1200 °C | cupronickel (1171) |
| charcoal | 1150 °C | copper (1084) |
| bituminous coal / anthracite | 1100 °C | copper (1084) |
| lignite | 1000 °C | silver (961) |
| contaminated coal | 900 °C | bismuth bronze (850) |

Anything below a row's ceiling melts on that fuel, so coke does the whole list. Charcoal buys
headroom over plain coal rather than a new metal — nothing in the game melts between 1084 and
1171 — but that headroom is what stops a copper melt stalling when the fire dips. Only coke
does cupronickel.

Run a bellows into it and, as with any forge, the ceiling is multiplied — which means the
1200 °C clamp is reached on any fuel. The clamp itself does not move, so **iron (1482 °C) and
nickel (1325 °C) stay out of reach** however hard you blow. Iron is still bloomery work, as it
should be.

Every alloy recipe in the game works, because the smelting itself is vanilla's: the four
ingredient slots stand in for a firepit's cooking slots and `BlockSmeltingContainer` does
the rest.

### The shape of a melt

Three phases, and they cost differently, because physically they are different work.

**Coming up to temperature** depends on how much is in it, because the same fire poured into
more metal raises it more slowly. The climb is also proportional to how far there is left to go —
brisk while the crucible is cold, easing in over the last stretch — which is the curve the firepit
already uses rather than a straight ramp that slams into the ceiling and stops.

Vanilla does *not* do this for crucibles: the firepit damps by the container's stack size, which
for a crucible is always one, so a firepit heats twenty nuggets and a brim-full crucible at
exactly the same rate.

Tipping cold ore into a hot crucible cools the whole lot, weighted against what is already in
there — so topping up a nearly-molten crucible genuinely sets you back.

**Melting** takes 30 seconds per ingot's worth of ore, the same as a firepit. While it is
actually melting the temperature all but stops climbing — that is latent heat, the fire's energy
going into changing the metal's state rather than raising its temperature.

| charge | heat | melt | total | coke *(firepit)* |
|---|---|---|---|---|
| 20 nuggets (100 units) | 61s | 30s | 91s | 1.4 *(2.1)* |
| 60 nuggets (300 units) | 92s | 90s | 182s | 2.7 *(3.6)* |
| 140 nuggets (700 units) | 153s | 210s | 363s | 5.4 *(6.6)* |

**Holding it liquid** is cheap. Once the metal is molten the fire is only replacing what the
crucible loses to the air, so a molten crucible burns about a third of what melting burned,
barely more than an empty forge. Let it go solid and it is a full melt to do over again.

| what the fire is doing | one coke lasts |
|---|---|
| nothing, or an empty crucible | 240s *(vanilla, untouched)* |
| heating a charge | 67s |
| melting | 67s |
| **holding a melt liquid** | **190s** |

### What a melt costs

A forge and a firepit burn on different clocks. A firepit spends a fuel item's `BurnDuration`
in **real** seconds; the forge spends `1/BurnRate` in **in-game hours**. At the shipped calendar
an in-game hour is 120 real seconds, so one lump of coke is 40 seconds in a firepit and 240 in a
forge — and melt time is counted in real seconds on both. Melting over a forge therefore came
out **six times cheaper**, and a single lump saw off a full 700-unit crucible.

Fuel now goes faster while there is metal in the crucible, scaled so that a melt costs
`CrucibleFuelUseVsFirepit` (default **0.8**) of what the same melt costs in a firepit:

Over a whole job — heating and melting together — a forge melt lands at about 0.65 of a firepit's
fuel for a small charge and 0.8 for a brim-full crucible. Before this it was a flat sixth.

The multiplier is derived from the calendar and from each fuel's own firepit burn duration
rather than hardcoded, so it stays honest on a server that has changed day length, and it never
drops below the vanilla rate — putting a crucible in can't end up *saving* fuel.

**An empty crucible costs nothing extra**, and neither does smithing: the faster burn applies
only while there is ore to melt or metal to keep liquid.

### Where the ore lives

In the forge, not in the crucible — exactly as a firepit holds it. Pull a crucible out
mid-melt and the ore stays behind in the coals; right click empty handed to get it back.
Break the forge and it drops.

That is deliberate. A crucible carried off with ore inside it would be invisible to a
firepit, which reads its ingredients from whatever heat source is holding it.

## Configuration

`ModConfig/crucibulum.json`, written on first run:

| | |
|---|---|
| `CrucibleTempBonus` | degrees a crucible adds to the forge's fuel ceiling (400) |
| `MaxCrucibleTemperature` | hard ceiling; 0 means use the crucible's `maxHeatableTemp` |
| `CrucibleFuelUseVsFirepit` | burn rate while working, against a firepit's per-second rate (0.6); 0 disables the correction |
| `CrucibleThermalMass` | the clay's own heat capacity in ingot-equivalents; sets how much charge size matters |
| `MoltenHoldFuelShare` | what holding a melt costs as a fraction of making one (0.35) |
| `HeatRate` | how briskly the crucible climbs; the curve eases in near the ceiling |
| `MeltSpeedMultiplier` | 1 matches the firepit |
| `EnableBlastGate` | whether a forge can be fitted with a gate at all |
| `RequireTongsForGate` | tongs in the off hand to work a burning forge's gate (off) |
| `CrucibleOnlyInForge` | refuse crucibles at the firepit, so metal only melts in the forge (off) |
| `MeltIngots` | let ingots into the crucible, which a firepit refuses by size (off) |
| `MeltBrokenToolHeads` | let a broken tool head in, for a whole ingot back; a firepit refuses it (off) |
| `GateAirOpen`, `GateAirHalf`, `GateAirQuarter`, `GateAirShut` | what each notch does to the fire, as a share of full draught (1.0 / 0.85 / 0.7 / 0.55) |

With [ConfigLib](https://mods.vintagestory.at/configlib) installed these appear on its settings
screen, and — the part that matters more — the server's values sync to every client. Without it
each side reads its own file, and since clients *display* numbers derived from this config (the
block info, and the window's `Blast gate: half open — 1020°C`), a retuned server otherwise has
everyone quoting ceilings that are not true there. ConfigLib is optional and is not required to
build; nothing of it ships in the zip.

The gate factors are the same lever a bellows works from the other side, so 1 is full draught and
less is throttled. They are clamped to at most 1 — a plate over the air inlet cannot make a fire
hotter than an open one — and held above zero, since shut is a banked fire rather than an airtight
one. Moving them moves which metals can be held workable without melting.

### Melting only in the forge

`CrucibleOnlyInForge` is for a world where the firepit cannot take a crucible at all, so every
melt - and every reheat of a molten one - happens at a forge. It is off by default; vanilla's
firepit keeps working and the forge is simply a second place to melt.

Turned on, the firepit refuses a crucible on every way in: a click on the block, a drag into its
window, a shift-click from the inventory. The firepit window opens instead, as it does for any
other item that does not go in, and the crucible stays in hand. Only the crucible is refused - ore
still lands in the input slot, where it sits exactly as it did before, since vanilla never smelted
it without a container either way. Taking a crucible *out* is untouched, so one already sitting in
a firepit when the switch is thrown comes out as it always did. The forge does not read the
setting at all.

It is read live: flip it in the config screen and it takes effect at once, with no restart. It is
also the one thing in the mod that needs a Harmony patch - see below for why.

## How it hooks in

Two JSON patches, and one Harmony patch that is inert unless asked for. The forge blocktype's
`class` and `entityClass` are repointed at subclasses of `BlockForge` and `BlockEntityForge`,
which is enough because `OnBlockInteractStart` is virtual and the vanilla forge already heats
whatever is in its work item slot.

The Harmony patch is `CrucibleOnlyInForge`, and it is Harmony because nothing else reaches the
firepit: it accepts a crucible by C# class - `BlockFirepit.OnBlockInteractStart` tests
`is BlockSmeltingContainer or BlockSmeltedContainer`, and `InventorySmelting.CanContain` says yes
to its first three slots unconditionally - so there is no attribute a JSON patch could turn off.
(Removing `cookingContainerSlots` from the crucible makes it *inert* in a firepit, but it still
goes in, and a molten one still reheats there.) What every way in does share is the firepit
inventory's accept methods, so three postfixes - `ItemSlotInput.CanTakeFrom`, `ItemSlotInput.CanHold`
and `InventorySmelting.CanContain` - each `&&` in a refusal when the switch is on and the stack is
a crucible. Removal goes through `CanTake`, which is left alone. Both sides are patched, since the
client predicts a drag against its own copy of the inventory and a server-only patch would show the
crucible landing and then snap back; the postfix is idempotent and reads the config on every call,
so it is installed once for the process rather than once per side, which in singleplayer would be
twice.

The forge is a `BlockEntityContainer`, not a `BlockEntityOpenableContainer`, so none of the
open/close/sync plumbing a window needs comes for free — it is written out in the block entity,
deliberately close to `BEOpenableContainer`'s so it behaves like every other container. The
forge's two slots and the crucible's four live in **one** inventory (0 work item, 1 fuel, 2-5
charge), because a dialog syncs exactly one. Widening it is safe for forges saved before this
mod: slot loading walks the new slot count and finds nothing at the indices that were not there.

The crucible is deliberately **not** flagged `forgable`. It could be — 1.22 added a generic
forgable path that would accept and draw it for free — but that path draws contents with the
glow curve meant for a lump of iron under the hammer, which saturates a crucible to a
featureless white lump well below its working temperature, and `BEForge`'s merge branch is
guarded by `!forgable` and would stack four crucibles into a slot that only ever smelts one.
Owning the draw costs one mesh and buys a crucible you can still recognise at 1200 °C.

## Building

```bash
export VINTAGE_STORY="$(ls -d ~/.cairn/games/*.app | sort -V | tail -1)"
./build.sh
```

The zip lands in `Releases/`.

## Playing with it

A Cairn pack with its own data directory, so it does not touch your real install or worlds:

```bash
CAIRN=~/src/cairn/artifacts/osx-arm64/cairn-cli
$CAIRN launch crucibulum
```

To rebuild and refresh the zip in that pack after a change:

```bash
bash scripts/install-to-pack.sh          # builds, then copies into ~/.cairn/packs/crucibulum/Mods
```

The zip is loaded through `--addModPath` rather than being a ModDB-managed mod, so
`cairn-cli list` will keep reporting the pack as **0 mods**. That is expected.

Config lands in `~/.cairn/packs/crucibulum/data/ModConfig/crucibulum.json` on first run.

### A quick creative-mode setup

```
/giveblock forge 1
/giveblock crucible-brown-fired 4
/giveitem coke 32
/giveitem nugget-nativecopper 40
/giveitem nugget-cassiterite 8
/giveblock ingotmold-brown-fired 2
```

Place the forge, shift-click coke in, light it, right click a crucible in, then right click the
forge to open the window and drag nuggets across. 18 copper to 2 tin is valid tin bronze; 8 to 12
is not, and the window will tell you what it wants instead.

## Performance

Two paths run per frame and were measured rather than assumed (headless, so GC counters mean
something):

| | before | after |
|---|---|---|
| block info text, once a frame while looked at | 4.6 us, 9.6 KB | cached, rebuilt 5x/sec |
| renderer's per-frame check, per visible forge | 0.13 us, 128 B | 0.06 us, **0 B** |

The block info was the bad one: at 60fps it was close to a megabyte a second of garbage from a
single forge, most of it from walking every alloy in the game to work out what a wrong mix ought
to have been. It is now built a few times a second and handed back in between, and thrown away
at once when the contents change. Only the *text* is cached — what the fire is doing drives the
fuel rate and the melt, and a stale answer there would be a bug rather than a stale sentence.

The renderer compared `Code.ToShortString()` between frames, which allocated two strings per
frame per visible forge, and asked whether the metal had solidified every frame, which allocates
a slot each time. It now compares collectible references, which is free, and asks about
solidifying four times a second.

Everything else is small and measured: `WorkState` 0.17 us / 96 B and `BurnRate` 0.44 us / 240 B,
each called about five times a second per forge. The server tick only syncs to clients when
something actually changed, and throttles a drifting temperature or melt bar to twice a second
rather than five times — a sync serialises all six slots to every client in range.

For reference, `base.GetBlockInfo` — vanilla's own, resolving the crucible's display name — costs
7.5 us of the 8 us total. The mod's share is the remaining half microsecond.

## Screenshots

`docs/screenshots/` holds the ModDB set and `crucibulum/modicon.png` the icon. Both are
generated rather than hand-taken, and **neither is tracked** — only the mechanism is. Reproduce
them with:

```bash
bash scripts/make-shots.sh
```

The icon is a build input, so a zip built from a fresh clone carries no icon until this has been
run once; the build itself is unaffected.

See `docs/screenshots/README.md` for what each shot shows and the handful of things a shot needs
that a test run does not.

## Testing

155 in-game tests under `tests/`, run against a real game with
[vstestkit](../vstestkit):

```bash
cd ../vstestkit
bash scripts/run.sh ../crucibulum/tests --mod ../crucibulum/crucibulum            # headless
bash scripts/run.sh ../crucibulum/tests --mod ../crucibulum/crucibulum --client   # + a client
```

### What is and is not covered

Server side: the heat curve, thermal mass, latent heat, dousing, fuel rates per work state, the
melt itself including alloys, the readout's every branch, persistence across a reload, charge
handling, and a suite guarding plain smithing.

Client side, driven through real clicks and a real window: every interaction gesture, the dialog
opening and closing (including that the server actually opens the inventory), the slot grid's
packet reaching the server, the slot filters, and that the renderer builds its meshes without
throwing.

Also checked: every lang key the code asks for resolves, the config loads with sane defaults, and
the block info text is not rebuilt every frame. `CrucibleOnlyInForge` is driven both ways on every
path into a firepit - click, window drag, shift-click, fuel slot, a molten crucible back for
reheating - plus the way out, the forge, ore in hand, a live flip, and that its Harmony postfixes
are installed exactly once.

**Not covered.** The renderer is smoke-tested, not pixel-tested — there are no visual baselines,
so colours and placement were verified by eye. Two players opening the same window at once is
untested. A bellows is simulated by setting the oxygen rate rather than by placing one.

## License

GNU Lesser General Public License v3 or later — see [COPYING.LESSER](COPYING.LESSER) and
[COPYING](COPYING), with the summary in [NOTICE](NOTICE).
