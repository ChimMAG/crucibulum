#!/usr/bin/env bash
#
# Regenerate the ModDB screenshot set and the mod icon.
#
#   bash scripts/make-shots.sh [user@host]      (default: dizzyd@vsclient.home)
#
# Shots need a bigger window and particles, which vstestkit's client template turns off for
# deterministic visual baselines. The template is swapped for the boot and put straight back;
# it lives in the slot's own checkout, so no other tenant on the box sees it, and it is only
# read at boot anyway.
#
# Shadows stay OFF. Asking for them on this headless GL setup crashes the client at startup
# with "FBO ShadowmapFar: One or more attachment points are not framebuffer attachment
# complete", and a swallowed crash here is invisible: run.sh simply boots its own session with
# the restored template and you get small, particle-free shots that look like success.

set -euo pipefail

HOST="${1:-dizzyd@vsclient.home}"
SLOT=crucibulum
TREE="vstestkit-$SLOT"
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$REPO/docs/screenshots"
RAW=/tmp/cru-shots

# Before syncing, not after: sync-linux refuses to push into a slot with a live session, and a
# session left over from poking at the box by hand is the usual reason this stops dead.
ssh "$HOST" "cd $TREE && bash scripts/stop.sh >/dev/null 2>&1 || true"

echo "==> syncing mod and shots to $HOST"
( cd "$REPO/../vstestkit" && bash scripts/sync-linux.sh "$HOST" --mod "$REPO/crucibulum" >/dev/null )
# Into the tree sync-linux.sh maintains, not a second copy beside it. These two used to point
# at different places - the mod was synced to ~/$TREE/mods/$SLOT and the shots were booted
# against ~/mods/$SLOT - so the scenes photographed whatever build happened to be sitting in
# the other tree. It fails silently and looks exactly like a successful run.
rsync -a --delete "$REPO/shots/" "$HOST:$TREE/mods/$SLOT/shots/" --exclude bin --exclude obj

echo "==> booting a client at 1920x1080 with particles"
ssh "$HOST" bash -s "$SLOT" "$TREE" <<'REMOTE'
set -euo pipefail
SLOT="$1"; TREE="$2"
cd "$HOME/$TREE"

cp templates/clientsettings.json /tmp/cru-clientsettings.bak
trap 'cp /tmp/cru-clientsettings.bak templates/clientsettings.json' EXIT

python3 - <<'PY'
import json
p = "templates/clientsettings.json"
d = json.load(open(p))
d["intSettings"].update({
    "screenWidth": 1920, "screenHeight": 1080,
    "viewDistance": 192, "mipmapLevel": 3,
    "shadowMapQuality": 0,          # see the note at the top of make-shots.sh
})
d["boolSettings"].update({
    "renderParticles": True, "ambientParticles": True, "wavingStuff": True,
})
json.dump(d, open(p, "w"), indent=2)
PY

mkdir -p /tmp/cru-shots && rm -f /tmp/cru-shots/*.png

# VSTK_SHOT_DIR is read inside the game process, so it has to be exported for the *boot*.
# Putting it on run.sh does nothing: run.sh reuses the live session, and the game that is
# already up never saw it - the shots then land in the fallback /tmp and the fetch finds
# nothing.
VSTK_SHOT_DIR=/tmp/cru-shots \
VSTK_EXTRA_MODS="$HOME/$TREE/mods/$SLOT/crucibulum/bin/Debug/Mods" \
VSTK_EXTRA_ORIGINS="$HOME/$TREE/mods/$SLOT/crucibulum/assets" \
  bash scripts/boot.sh --client

# Loud, not silent: if the window did not come up at the size we asked for, the shots would be
# taken against whatever booted instead.
SIZE=$(bash scripts/vstk eval --side client 'return capi.Render.FrameWidth + "x" + capi.Render.FrameHeight;' \
       | python3 -c 'import sys,json; print(json.load(sys.stdin)["result"]["value"])')
echo "    client window: $SIZE"
[ "$SIZE" = "1920x1080" ] || { echo "expected 1920x1080" >&2; exit 1; }
REMOTE

echo "==> taking the shots"
# Deliberately tolerant: one scene failing should still let the others be collected, and the
# summary line below says plainly whether any did.
SHOTS_OK=1
ssh "$HOST" "cd \$HOME/$TREE && bash scripts/run.sh mods/$SLOT/shots \
    --mod mods/$SLOT/crucibulum --client --keep" > /tmp/cru-shotrun.log 2>&1 || SHOTS_OK=0
grep -E "^ok|^FAIL|^ERR|passed," /tmp/cru-shotrun.log || true

ssh "$HOST" "cd \$HOME/$TREE && bash scripts/stop.sh" >/dev/null 2>&1 || true

echo "==> fetching and cropping"
mkdir -p "$OUT" "$RAW"
# Clear both ends. A scene that has been renamed or dropped otherwise lingers here from a previous
# run and gets cropped into the set again, looking for all the world like it was just taken.
rm -f "$RAW"/*.png "$OUT"/*.png
rsync -a "$HOST:$RAW/*.png" "$RAW/"

# Pillow does the cropping, and it is not part of a stock macOS python. Rather than make this
# script depend on someone having pip-installed it, uv can supply one for the length of a single
# command and install nothing; a machine that already has PIL just uses it.
PY_IMG=(python3)
if ! python3 -c "import PIL" 2>/dev/null; then
  command -v uv >/dev/null 2>&1 \
    || { echo "this step needs Pillow (pip install pillow) or uv on PATH" >&2; exit 1; }
  PY_IMG=(uv run --quiet --with pillow python3)
fi

"${PY_IMG[@]}" - "$RAW" "$OUT" "$REPO/crucibulum/modicon.png" <<'PY'
import sys, pathlib
from PIL import Image

raw, out, icon = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]), pathlib.Path(sys.argv[3])

# The hotbar and stat bars reopen themselves and cannot be closed for good, so they are cropped
# off: about 153px of a 1080 frame. 1690x909 out of 1920x1080 matches the house style.
CROP = (115, 18, 1805, 927)

# ...except where the crucible window is in shot. GetFreePos anchors it to a screen edge, so
# taking 115px off the sides clips it mid-sentence. Those keep full width.
WIDE = (0, 18, 1920, 927)

for src in sorted(raw.glob("*.png")):
    if src.name.startswith("07-"):
        continue
    box = WIDE if ("window" in src.name or "ratio" in src.name) else CROP
    im = Image.open(src).crop(box)
    im.save(out / src.name)
    print(f"    {src.name}  ->  {im.width}x{im.height}")

src = raw / "07-icon-source.png"
if src.exists():
    # A fixed box measured against Shot07's composition, which is deliberately the same camera as
    # the window shot. Centring a square on the frame instead puts the forge in a corner: LookAt
    # aims below the subject to fight the bottom-heavy crop, so the forge is not where the eye is.
    ICON = (700, 410, 1200, 910)
    im = Image.open(src).crop(ICON).resize((480, 480), Image.LANCZOS)
    # A screenshot-derived PNG is nearly 300KB at full colour and half that on a 256-colour
    # palette, with no difference anyone will see at icon size. It rides in every download.
    im.convert("P", palette=Image.ADAPTIVE, colors=256).save(icon, optimize=True)
    print(f"    07-icon-source.png  ->  {icon.name}  480x480")
PY

ls -la "$OUT"/*.png "$REPO/crucibulum/modicon.png" 2>/dev/null

# The icon is a build input, and a zip built before it existed silently has none.
echo
echo "the icon is picked up at build time - rebuild to get it into the zip:"
echo "  bash scripts/install-to-pack.sh"

[ "$SHOTS_OK" = 1 ] || { echo; echo "NOTE: at least one scene failed - see /tmp/cru-shotrun.log"; exit 1; }
