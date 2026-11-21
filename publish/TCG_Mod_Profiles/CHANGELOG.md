# Changelog

## 1.0.1
- Loading a shop ("Continue") and starting the game no longer freeze for several seconds when a very large mod is installed (Collection Tracker's DLL is over 750 MB). Mod Profiles used to read every installed mod in full to identify it; each mod's fingerprint is now remembered and computed again only when the file changes, and mod names and versions are read without loading the whole file.

## 1.0.0 — first public release
- First public version, tested with the published packages (r2modman layout).

## 0.4.1
- Fixed: the game closed at start-up after loading a shop whose Mods screen had never been opened. The choices shown are now saved, and the start-up filter never closes the game: if the choices cannot be read, every mod loads.

## 0.4.0
- Interface in English or French.
- Works with mod managers (preloader found in its own `patchers` subfolder).

## 0.3.0
- Grouped, collapsible list with search, cascading disable, choices kept after a mod update, policy for new mods, copy from another shop.
