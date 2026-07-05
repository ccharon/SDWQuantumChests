# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A SMAPI (Stardew Modding API) mod for Stardew Valley 1.6 targeting SMAPI 4.x. It adds "Quantum Chests": craftable in entangled pairs, sharing one inventory no matter where each half is placed, with color sync between partners and a "collapse" mechanic if one half is irrecoverably lost.

See `ARCHITECTURE.md` for how it's built and why: which vanilla methods needed patching and what breaks without each patch, the chest color-rendering system (four separate draw paths, one shared technique, and the mistakes made getting there), the pairing/shared-inventory/collapse-detection mechanism, and other non-obvious design decisions. Code comments intentionally stay short and point there rather than repeating that history inline — read it before making non-trivial changes to any of those areas, and update it when a change alters the reasoning it documents.

## Commands

Build and auto-deploy to the live game's Mods folder:
```
dotnet build
```
`Pathoschild.Stardew.ModBuildConfig` (referenced in the `.csproj`) auto-detects the Stardew Valley install and copies the built DLL + `manifest.json` + `i18n/` into `<game>/Mods/QuantumChests` on every build. **The source in this directory must stay outside the live `Mods` folder** — if it's moved inside, the deploy step tries to copy files onto themselves and the build fails (this happened once; see git history / conversation for the exact error).

Smoke-test that the mod loads without errors (no save needed, checks Harmony patch application and asset registration):
```
timeout 25 "/home/christian/.local/share/Steam/steamapps/common/Stardew Valley/StardewModdingAPI"
```
Grep the output for `error`/`exception`/`quantum`. This launches the real game process, so **never run it while the user has their own game session open** — check `ps aux | grep -i stardew` first. There is no automated test suite; anything beyond "does it load" (crafting, placement, menus, visuals) requires a human playing the game.

There is no linter configured beyond the C# compiler's own warnings — treat any new build warning as something to fix, not ignore (e.g. the `AvoidNetField` analyzer warning means use the public property wrapper instead of the raw `NetField`, like `.Tint` instead of `.tint.Value`).

## i18n

`i18n/default.json` is translated into every other officially-supported Stardew Valley language (`de`, `es`, `pt` [Brazilian], `ru`, `ja`, `zh`, `it`, `fr`, `ko`, `tr`, `hu`) as separate files in the same folder. Whenever a string in `default.json` is added, changed, or removed, update all of those files to match in the same change — don't let them silently drift out of sync.

## Verifying vanilla behavior before patching

This mod was built by decompiling the installed game (`ilspycmd` against `Stardew Valley.dll` / `StardewValley.GameData.dll`, found at `~/.local/share/Steam/steamapps/common/Stardew Valley/`) rather than assuming vanilla behavior from memory — several assumptions made from general recollection turned out wrong. When adding a new patch or relying on undocumented vanilla behavior, check the decompiled source first rather than guessing. The same applies to sprite/texture assumptions: `xnbcli` (found locally under `~/Dokumente/SDWFarmMap/` and `~/Downloads/`) can unpack a `.xnb` file to a plain `.png` for direct pixel inspection — see `ARCHITECTURE.md` for an example of a bug this caught.
