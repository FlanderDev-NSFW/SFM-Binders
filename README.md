# Binders

Arm and leg binders for SecretFlasherManaka (BepInEx 6 IL2CPP): red leather sleeves that hold her forearms folded against her
upper arms and her shins folded against her thighs, so she can only crawl - on her elbows and knees. One restraint for all four
limbs. Single-player/offline only.

**Needs RestraintHub** (0.1.0+; included in the package): ring entry, number shortcuts, the list of worn restraints, F routing,
input blocking and conflicts are the hub's (`mods/RestraintHub/README.md`).

**Status 0.1.0:** usable, L3 (see Verification). Pick **Arm & Leg Binders** in the common ring (hold 1; the restraint page after
the game's, page 5, first slot) or put it on a number key; hold **F** to struggle free (`Unlock.SuccessChance`, default: the
first attempt).

- **Crawling only:** she is put into the game's crawl (the crouch state, C) and kept there - C does not stand her up while the
  binders are on; after an action that ends the crawl she goes down again. Crawling is slower (`Crawl.SpeedFactor` 0.6 times the
  game's crawl speed, which Z / X still set). No running, nothing that needs her hands, no pose skills, no peeing ("Can't do that
  with the Arm & Leg Binders on."). A pose skill that runs when they go on is ended. Refused while something else holds her arms or
  legs (the game's handcuffs, Yoke, SpreaderBar, the wall restraint) - unless overlap is switched on in the hub (`V`).
- **Pose** (every frame, on top of the crawl animation, `Logic/CrawlPose.cs`):
  - the knees move apart just enough for the two leg sleeves to sit side by side (about 3 cm on the first save);
  - the hips come down until the knees rest on the floor in the sleeves' cups (the crawl leaves them in the air once the shins no
    longer lie on the floor);
  - the chest tilts down until the elbows reach the floor, the head is turned back up most of the way so she looks ahead;
  - the upper arms go down to the elbows; the elbows follow the crawl's hand steps (60 %, at most 45 % of the upper arm);
  - the forearms fold up in front of the upper arms (`Pose.ArmFoldGap`, 12 deg); the wrists bend 75 deg towards the upper arms
    (`Pose.HandBend`), so the hands rest on her shoulders at the sleeves' openings (forearm and hand are longer than her upper arm);
  - the shins fold up behind the thighs (`Pose.LegFoldGap`, 18 deg); her shins are longer than her thighs, so the ankles and
    feet come out of the sleeves' openings behind her bottom.
- **Sleeves shaped to her body** (`BinderFitter`, `Logic/SleeveFit.cs`): each sleeve sits on the drawn bones, its axis halfway
  between the two folded segments, 1.12 times as long as her upper arm / thigh (`Model.ArmLength` / `LegLength`). Which skin
  belongs to which limb is decided once when the binders go on: for one moment (inside one LateUpdate, never drawn) her arms are
  stretched out to the sides and her legs down and apart, her body is baked and each vertex labelled by its nearest limb - so in
  the crawl her breasts, chest and hips never count as arm or leg, however close they come. Twice a second her body is baked
  again and every sleeve is reshaped ring by ring (12 rings along it) around its limb's vertices: each ring is the roundest shape -
  from an oval to a rounded box - that holds the limb from the ring below to the ring above, plus `Model.ClearanceCm` (0.6). The vertices keep their distance from the inner wall, so the straps, buckles and the wall's
  thickness stay as modelled. A sleeve covers its last six fits (3 s), so the crawl's steps do not push skin through it; towards
  the opening a ring never narrows (the limb meets the body there). The elbow / knee rests on the cup's floor. A fit is spread
  over five frames (at most ~3 ms in one). Textured with the model's colour map on the game's toon material.
- **Saved** with the game (lock, unlock chance, number shortcuts; three keys in the slot file); a load with the binders on puts
  them back.

## Model

`assets/binders.bnd` and `assets/binders_color.png` come from `Arm and Leg Binders.blend` (objects `Arm_Binder`, `Leg_Binder`; the
user confirmed they may be shipped), exported by `tools/export_model.py` and embedded in the DLL:

```bash
blender -b --disable-autoexec "D:/Assets/_3D/Arm and Leg Binders.blend" --python mods/Binders/tools/export_model.py -- mods/Binders/assets/binders.bnd mods/Binders/assets/binders_color.png
```

Two skinned meshes in their rest pose (a cage of strap / side bones that is not posed), each a left and a right sleeve: a cup
closed at the bottom and open at the top, the arm sleeves 26 cm long and symmetric front / back, the leg sleeves 33 cm, straight at
the front (thigh) and bulging backwards (shin). Every sleeve is one rigid part; the exporter writes positions, normals and UVs in
Blender axes plus each sleeve's bottom and top centre, and the 2048 px base colour as a 1024 px PNG (the normal / height maps are
not used). At load each sleeve is converted to its own frame for Unity (`BinderPart.ToLocal`: y up the sleeve, z towards the
folded segment, winding flipped for the handedness) and the PNG decoded with `ImageConversion.LoadImage` (falls back to plain red).

## Config (`BepInEx/config/com.local.binders.cfg`)

| Key | Default | Meaning |
|---|---|---|
| General.RingLabel | Arm & Leg Binders | ring menu / list text |
| Unlock.AttemptSeconds | 4.5 | seconds of F per attempt |
| Unlock.SuccessChance | 1 | chance per full attempt (1 = first try; failures add 0.02) |
| Crawl.SpeedFactor | 0.6 | her crawling speed in the binders, times the game's (applies live) |
| Pose.ArmFoldGap | 12 | angle between upper arm and folded forearm (deg) |
| Pose.LegFoldGap | 18 | angle between thigh and folded shin (deg) |
| Pose.HandBend | 75 | wrist bend towards the upper arm (deg; 0 = straight) |
| Model.ArmLength / LegLength | 1.12 / 1.12 | sleeve length relative to her upper arm / thigh (applies live) |
| Model.ClearanceCm | 0.6 | room between her limbs and the sleeves' inner walls (cm; -1 .. 5, applies live) |
| Debug.Disable | 0 | bit mask (live): 1 no pose, 2 do not keep her crawling, 4 crawl at the game's speed, 8 hide the sleeves, 16 sleeves as modelled (not shaped to her) |

Diagnostics: `game get Binders.BinderController::Status` (knee spread, hips drop, chest tilt, elbow / knee heights, sleeve
scales), `game call Binders.BinderController Probe` (her bones as animated, posed and drawn, cm, logged), `LockHere`, `Release`,
`game set Binders.BinderController::DebugView 'right 0.6'` (the main camera at a fixed side of her - right, left, front, back,
top - with its own light, for screenshots where the game's camera cannot go; `'front 0.4 feet'` aims at her ankles; `''` off). Outside the flat's walls (Apart,
teleport `0,6.2`) nothing blocks the view. `game set Binders.BinderController::DebugGhost true`: the body bake the sleeves
are fitted to, drawn over her (white where it lies outside her drawn body; hide `Selestia_body` to see it alone).
`game get Binders.BinderController::FitStatus`: points per sleeve, cup depth, opening and middle ring, the longest fit step.

## Verification

- L1: `./tools/dev.ps1 test Binders` (43): model format and errors, sleeves' local frames (y range, winding vs normals, fold
  sides, half widths), the inner wall found on all four sleeves, rings fitted to outlines (clearance, minimum, growth limit,
  missing rings, openings never narrowing, a square outline squared off, oval outlines kept round), deformation (wall on the ring,
  thickness kept, squarer rings, the real leg sleeve within 3 mm), unions of fits, limb points (nearest limb, window, one pass), shoulders lowered about the spine (height, length, never raised, out of reach), elbow steps, folds, sleeve frames.
- L3 0.1.0 (2026-10-25, dev copy, first save, Apart):
  - the game's crawl measured with `Probe`: hips 42 cm, shoulders 46 cm up, hands under the shoulders, shins flat behind the knees;
    upper arm 26, forearm 25, thigh 40 (32 as drawn), shin 42 cm.
  - `LockHere` standing -> crawling within a second; Status: hips down 8 cm, chest 14 deg, elbows 3.6 cm and knees 4.7 cm over the
    floor (the sleeves' cups); texture 1024 x 1024 loaded. Screenshots: arm sleeves upright under the shoulders with the forearms
    and (after the hand curl) the hands inside, leg sleeves around thighs and folded shins, feet behind her bottom; before the
    drawn-length fix the knees floated 13 cm up and the leg sleeves lay along the floor.
  - crawled 2.7 m with `game walk`; moving: crawl speed 0.234 m/s = 0.39 x 0.6.
  - C keeps her crawling; control: Debug.Disable 2 -> the same C stands her up; back to 0 -> down again.
  - F held -> "release: struggled free", sleeves hidden, crawl speed 0.6 again, C stands her up (screenshot: standing normally).
  - through the real ring (hold 1, page 5, first slot `restrainthub_Binders`, click, release): "ring: binders put on", locked; key 1
    still Wall Restraint (screenshot of the shortcut panel).
  - with the hub's blocking on, key 4 (Spreader Bar) -> "refused: SpreaderBar conflicts with Arm & Leg Binders (Legs)"; with the
    player's overlap setting (blocking off) the bar went on as designed.
  - fit (after the player saw the wrists bent nearly 180 deg and her arms and especially legs through the sleeves), with
    `DebugView` from all sides: at width 1.0 the calves bulged out of the leg sleeves' backs and the sleeves reached halfway up the
    thighs; at 1.2 / 1.35 the two leg sleeves cut into each other between the knees - fixed by moving the knees apart; final
    1.12 long, 1.15 / 1.1 wide, legs folded 18 deg, wrists 75 deg: no skin through the sleeves' walls standing or crawling, the
    hands resting on the shoulders, ankles and feet out of the leg sleeves' openings.
  - shaped to her (after the player found the fixed sleeves loose and open to the camera; 2026-10-25): the body bake has to be
    scaled by her body object's lossy scale (1.3125) - without it the fitted sleeves were far too small and off her limbs; with
    it a mesh of the bake drawn over her coincides with her drawn body (DebugGhost). Rings through the outline's box let the
    calves show through the leg sleeves' backs (corners) - fixed with enclosing rings, then squared-off rings (the arm sleeves had
    grown half as wide again as the arms). From six sides standing and crawling (A held): no skin through the walls at clearance
    0.6 (at 0.4 one fleck on an arm sleeve while crawling), the leg openings filled by her thighs. Fit time 3.0 ms at most per
    frame (7.5 ms in one frame before it was spread; the first fit ~11 ms with the JIT).
  - limbs labelled in a spread pose (after the player saw the sleeves grow around her breasts and the sides of her chest / hips):
    "labels: arms 519 / 520, legs 772 / 790 of 18601 vertices"; arm sleeves' middle 5.2 x 8.8 -> 4.2 x 5.5 cm half, leg openings
    11.5 x 12.1 -> 10.0 x 10.2 cm. A spot of skin on each arm sleeve's front (rings that held only their own half-band, the blend
    between two rings cut it) gone with rings that hold the limb up to their neighbours. Six sides standing and crawling: no skin
    through the walls; at most 2.1 ms per frame.
  - not run in game: the save round trip (it would write the player's slot; the hooks are Yoke's / the template's, L3 there).

## Known limits

- Only her body mesh (`Selestia_body`) is measured: clothes on her arms or legs (stockings, gloves) can show through the sleeves.
- The limb labels come from nearest bones in a spread pose; where a limb meets the body (shoulder, hip) the cut follows that.
- The sleeves are reshaped twice a second and cover the last 3 s, so after a big change of pose they can stay roomy for a moment.
- The sleeves' length comes from bone lengths (`Model.ArmLength` / `LegLength`).
- The elbows follow the crawl's hand steps and the knees its knee steps, so she crawls with the game's rhythm; the feet are not
  animated.
- Other actions that stand her up (interactions, events) play out folded until she is crawling again.
