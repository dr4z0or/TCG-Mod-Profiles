# TCG Mod Profiles

**Different mods for each shop.** Choose which BepInEx mods and Enhanced Prefab Loader packs are enabled in each shop of TCG Save Manager. When you load a shop that needs other mods, the game relaunches itself with the right ones and resumes loading.

Requires **TCG Save Manager**. *Français plus bas.*

## Features

- A **Mods** button on each shop card of *My shops*.
- Grouped, collapsible list with search: *Mods*, *Libraries*, *Packs* (Enhanced Prefab Loader content, by folder), *Essential*, *Missing mods*.
- **Dependencies handled:** enabling a mod enables what it needs; disabling a mod offers to disable what depends on it. Incompatibilities and minimum versions are checked.
- **New mods:** choose whether mods a shop sees for the first time are *to decide*, *enabled* or *disabled* (for all shops). Updated mods keep their choice and are listed under *To review*.
- **Copy from…** another shop.
- **Apply and relaunch:** if the shop needs a different set of mods than the one loaded, the profile is saved, the game closes, reopens and loads the shop. With the same set, switching is immediate.
- Mods are filtered in memory at start-up: **no DLL is moved or renamed**.
- Interface in English or French (setting `Interface.Language` of TCG Save Manager).

## Installation

**r2modman / Thunderstore Mod Manager:** install *TCG Mod Profiles* (TCG Save Manager is installed with it). Launch the game from the manager. The automatic relaunch reuses the game's launch arguments; if the game ever reopens without your mods, start it again from the manager.

**Manual:** install TCG Save Manager first, then extract the archive's `BepInEx` folder into the game folder. It contains:

```
BepInEx/plugins/TCGModProfiles/TCG.ModProfiles.dll
BepInEx/patchers/TCGModProfiles/TCG.ModProfiles.Preloader.dll
```

The preloader is what disables mods before they load; without it, choices are saved but not applied.

## Good to know

- TCG Save Manager, TCG Mod Profiles and TCG Custom Game Modes are always enabled.
- A mod needed by a shop's saved data (for example Furniture Overhaul) stays required for that shop. Removing a mod does not convert the data it saved.
- The automatic relaunch uses Windows PowerShell and waits at most 60 seconds for the game to close. The resume request expires after 10 minutes.
- Profiles are stored in `%LOCALAPPDATA%\TCGSM\campaigns`.

---

## Français

Des mods différents par boutique : bouton **Mods** sur chaque boutique, dépendances gérées, nouveaux mods à décider/actifs/désactivés, copie depuis une autre boutique. **Appliquer et relancer** rouvre le jeu avec les bons mods et reprend le chargement. Aucune DLL n'est déplacée. Nécessite TCG Save Manager ; installer aussi le préchargeur (`BepInEx/patchers`).
