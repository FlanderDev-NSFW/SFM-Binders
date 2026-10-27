# Binders 1.0.0

Arm and leg binders for **Secret Flasher Manaka**: leather sleeves hold her forearms folded against her upper arms and her
shins against her thighs, so she can only crawl, on her elbows and knees.

## Requirements
- Secret Flasher Manaka (tested with v1.1.3)
- BepInEx 6 for IL2CPP (tested with build 738)
- **RestraintHub 1.0.0** or newer

## Install
Extract the zip into the game folder (the folder with `SecretFlasherManaka.exe`). You get `BepInEx/plugins/Binders/`.

## How to use
Hold a ring key (e.g. `1`), page with `Z` / `X` to the restraint page and click **Arm & Leg Binders**. She goes down into a
crawl and stays there. With the binders selected in the worn list (`B` selects), hold `F` to struggle free.

She crawls slower than normal. Running, standing up, pose skills, peeing and everything that needs her hands are refused.
The sleeves are shaped to her body.

## Settings
`BepInEx/config/FlanderDev.SFM.Binders.cfg` (created on the first start). Most useful:

| Setting | Default | |
|---|---|---|
| Crawl.SpeedFactor | 0.6 | crawling speed compared to normal |
| Model.ClearanceCm | 0.6 | room inside the sleeves |
| Unlock.SuccessChance | 1 | chance that struggling frees her (1 = first try) |

## Good to know
- Whether the binders are on is saved with the game.
- Clothes on her arms or legs (stockings, gloves) can show through the sleeves.
- After a big change of pose the sleeves can look loose for a moment.
- Actions that stand her up (interactions, events) play out folded.

Single-player / offline only. Code: MIT licence (`LICENSE.txt`). Models: from "Prop Pack 4" by biancaboo1, CC BY-NC-ND 4.0 (smutba.se; details in `LICENSE-MODELS.txt`).

## Building from source
- .NET SDK 8, and a copy of the game with BepInEx 6 IL2CPP started once: copy `Directory.Build.local.props.example` to
  `Directory.Build.local.props` and set `GameDir`. Then `dotnet build src/Binders/Binders.csproj -c Release`.
- It compiles against RestraintHub: the [SFM-RestraintHub](https://github.com/FlanderDev-NSFW/SFM-RestraintHub) repository next to this one, `lib/RestraintHub/RestraintHub.dll`, or the hub installed in the game.
- The source of the mod is `src/Binders` and its tests (`tests/`); `lib/SfmKit` is a copy of the shared [SfmKit](https://github.com/FlanderDev-NSFW/SfmKit)
  library; `docs/` holds the developer notes.
- Every push to `main` builds, tests and releases it (`.github/workflows/release.yml`, `build/ci.py`).
