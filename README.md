# TCG Mod Profiles

A BepInEx mod for **TCG Card Shop Simulator**. Interface in English, or in French when the game is in French.

**Different mods for each shop.** Choose which BepInEx mods and Enhanced Prefab Loader packs are enabled in each shop of TCG Save Manager. When you load a shop that needs other mods, the game relaunches itself with the right ones and resumes loading.

Full description, settings and FAQ: [mod page](publish/TCG_Mod_Profiles/README.md) · changes: [CHANGELOG](publish/TCG_Mod_Profiles/CHANGELOG.md).

## Install

**Download:** [Nexus Mods](https://www.nexusmods.com/tcgcardshopsimulator/mods/1929) (Thunderstore / r2modman: coming soon). Requires BepInEx 5 and [TCG Save Manager](https://github.com/dr4z0or/TCG-Save-Manager).

## Building from source (Windows, .NET SDK 8)

1. Clone [TCG-Save-Manager](https://github.com/dr4z0or/TCG-Save-Manager) **next to this repository** (same parent folder): the plugin and the patcher share code with its core library.
2. Copy `local.props.example` to `local.props` and set `GameDir` to your game folder (BepInEx 5 installed).
3. `dotnet build src/TCG.ModProfiles -c Release` and `dotnet build src/TCG.ModProfiles.Preloader -c Release`.
4. Offline tests: `dotnet run --project src/TCG.ModProfiles.Tests`.

Install: `TCG.ModProfiles.dll` in `BepInEx/plugins/TCGModProfiles`, `TCG.ModProfiles.Preloader.dll` in `BepInEx/patchers/TCGModProfiles`.

## Other mods by dr4z0or

- [TCG Save Manager](https://github.com/dr4z0or/TCG-Save-Manager)
- [TCG Custom Game Modes](https://github.com/dr4z0or/TCG-Custom-Game-Modes)
- [TCG Card Translations](https://github.com/dr4z0or/TCG-Card-Translations)
- [Mods Translator](https://github.com/dr4z0or/Mods-Translator)

## License

[MIT](LICENSE). Not affiliated with OPNeon Games.

---

**En français :** Des mods et des packs Enhanced Prefab Loader différents pour chaque boutique ; le jeu se relance avec les bons mods. Nécessite TCG Save Manager. Les joueurs le téléchargent sur [Nexus Mods](https://www.nexusmods.com/tcgcardshopsimulator/mods/1929) (bientôt sur Thunderstore) ; ce dépôt contient le code source.
