# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A SMAPI (Stardew Modding API) mod for Stardew Valley 1.6 targeting SMAPI 4.x. It adds "Quantum Chests": craftable in entangled pairs, sharing one inventory no matter where each half is placed, with color sync between partners and a "collapse" mechanic if one half is irrecoverably lost.

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

## Architecture

### Why this needs Harmony instead of just Content Patcher data

A custom big-craftable ID is *not* enough to get real chest behavior. `BigCraftableDataDefinition.CreateItem` always returns a plain `StardewValley.Object`, and vanilla's `Object.placementAction` decides whether to instantiate an actual `Chest` (menu, inventory, mutex) via a hardcoded `switch` on exact vanilla QualifiedItemIds (`"(BC)130"`, `"(BC)BigChest"`, etc.) — an unrecognized ID just falls through to generic placement with no inventory at all. Several other vanilla methods have this same hardcoded-vanilla-ID-list pattern and each needed its own patch:

- `Object.placementAction` → `ObjectPlacementPatch`: constructs the real `Chest` ourselves for our two item IDs, copies `modData` from the item being placed, sets `GlobalInventoryId`, attaches the glow light source, wires color sync.
- `Chest.GetActualCapacity` → `ChestCapacityPatch`: postfix bumping capacity to 70 for the large tier (vanilla only special-cases `SpecialChestType.BigChest`, which we deliberately never set to avoid inheriting unrelated BigChest-specific behavior elsewhere).
- `Chest.draw(SpriteBatch,int,int,float)` and the separate `Chest.draw(SpriteBatch,int,int,float,bool local)` overload → `ChestColorDrawPatch` / `ChestColorMenuDrawPatch`: vanilla only renders `playerChoiceColor` for the same hardcoded ID list; anything else silently ignores the color and draws a plain sprite. **Both overloads needed patching separately** — one is used for world rendering, the other for menu/preview icons (e.g. the color picker's preview swatch) — missing either one reproduces a real bug we hit (color not appearing / garbage sprite offsets in the preview).
- `Chest.HandleChestHit` → `ChestHitPreservePairPatch`: when you axe-hit an *empty* chest, vanilla removes it and reconstructs a brand-new item from just its ID string (`new Debris(base.QualifiedItemId, ...)`), discarding `modData` — silently stripping the pair ID on every empty-chest pickup. This patch takes over only the empty-chest branch and uses the `Debris(Item, ...)` constructor with the real instance instead, preserving the pair ID. The "chest still has items → kick it" branch is left untouched (`return true`).
- `CraftingRecipe.createItem` → `CraftingRecipePairIdPatch`: postfix that stamps a fresh GUID into `modData[PairIdKey]` the moment a pair is crafted (recipe yields `Stack = 2`; both physical chests inherit the same ID because `Item.getOne()` copies `modData` when the stack is split for placement).
- `Item.canStackWith` → `ChestPairStackPatch`: vanilla stacking only compares ID/name/quality, so crafting two different pairs back-to-back (or later picking up one chest each from two different pairs) would otherwise merge them into one stack and corrupt both pair IDs (only one `modData` dictionary survives a merge). Postfix forces `false` whenever both items are our chest type but have different pair IDs.

Private vanilla fields needed by patches (`currentLidFrame` on `Chest`, `chestHit` on `FarmerTeam`) are read via `HarmonyLib.AccessTools.Field` reflection, cached as `static readonly FieldInfo`.

`Object.drawInMenu` → `ChestColorInventoryDrawPatch` is a deliberate enhancement *beyond* vanilla parity, not a restore-vanilla-behavior patch like the others above: vanilla never renders `playerChoiceColor` on the inventory-slot icon for *any* chest, dyed or not, so a colored chest picked back up loses its color in the backpack. This patch shows the color anyway, and — because an un-placed, freshly-crafted chest is still a plain `Object` with no `playerChoiceColor` field of its own — it looks up the color from the entangled partner (via `QuantumChestService.FindChestByPairId`) when the item itself doesn't have one, so the still-carried half of a pair matches the color of its already-placed, already-dyed partner. This is what lets a player tell multiple carried pairs apart at a glance. It deliberately draws the same source rect vanilla's own `drawInMenu` uses (`GetSourceRect(0, ParentSheetIndex)`), just multiplicatively tinted, rather than reusing the separate "recolorable" sprite frames (168+/312+) that `ChestColorDrawPatch`/`ChestColorMenuDrawPatch` draw — those frames are sized/positioned for a taller world-tile composite, not a single icon-shaped crop, and using them here produced garbled icons (a real bug we hit).

### Pairing and shared-inventory mechanism

No custom sync code was written for the shared inventory — it reuses the same mechanism vanilla uses for Junimo Chests: `Chest.GlobalInventoryId` (a string key) makes `Chest.Items`/`GetMutex()` transparently redirect to `Game1.player.team.GetOrCreateGlobalInventory(id)` / `GetOrCreateGlobalInventoryMutex(id)`, which is already fully multiplayer-networked. Our twist is scoping it to exactly 2 members instead of "every Junimo Chest," by keying on a per-pair GUID instead of the vanilla constant.

The pair ID lives in `Item.modData[ModConstants.PairIdKey]` and is the single source of truth for "these two chests belong together." It's assigned once at craft time and copied forward at every placement; nothing ever reassigns it later.

### Destruction / collapse detection (`QuantumChestService`)

Deliberate design decision (see conversation history, not to be "fixed" without re-confirming with the user): regular Stardew chests can't actually be destroyed by bombs (`GameLocation.destroyObject` explicitly excludes `is Chest`) and have `Fragility` 0, so there is intentionally **no new way to destroy a chest** added by this mod. Instead, `QuantumChestService` watches for a pair member disappearing for *any* reason (picked up, trashed, or something unanticipated like a temporary/regenerated location) and reacts based on how many members still exist anywhere:

- `EnumerateAllObjectsIncludingNested` is the core search: every location's placed objects + every farmer's top-level inventory, recursing into any `Chest`-like container's stored items (a quantum chest can be stored inside a regular chest, or even inside its own partner — both are valid states, not bugs). Deduplicated by reference (`ReferenceEqualityComparer`) because two entangled chests share one underlying `Inventory` object and would otherwise double-count its contents when both are scanned.
- `CountPairMembers` additionally scans `location.debris` (`Debris.item`) to account for the brief window between an emptied chest being removed from the map and the player walking over the resulting pickup-debris to actually collect it — without this, that window looks identical to "destroyed" and falsely triggers a collapse (this was a real bug; see git history).
- If a pair drops to exactly 1 remaining member, `CollapsePair` removes the survivor too (wherever it actually is — map, top-level inventory, or nested in a container, via `RemoveObjectFromWherever`) and discards the shared inventory entirely; contents are **not** preserved. This is intentional flavor ("the danger of quantum mechanics"), not a bug to fix by making it safer.
- Color sync (`EnsureColorSyncWired`) hooks `Chest.playerChoiceColor.fieldChangeEvent` per chest, guarded by a `ConditionalWeakTable<Chest, object>` so re-wiring on save load / warp / placement is idempotent and doesn't leak memory for chests that no longer exist.

### Content (`ContentProvider`)

Adds two `Data/BigCraftables` entries and two `Data/CraftingRecipes` entries via `Content.AssetRequested`. Both chest tiers intentionally reuse vanilla's own `TileSheets/Craftables` sprites (`Texture = null`, `SpriteIndex` 130 / 304 — the same sprites as vanilla Chest / Big Chest) rather than custom art; the purple glow + default tint are the only visual differentiator. Recipes are granted directly via `Game1.player.craftingRecipes.TryAdd` on `SaveLoaded` rather than through the vanilla unlock-condition field (deliberately not level/quest-gated, just materials-gated).

Known vanilla item/data IDs relied on (verified against the installed game via `ilspycmd`, not assumed from memory): Iridium Bar = `337`, Battery Pack = `787`, vanilla Chest = `130`, vanilla Big Chest = `BigChest`.

`i18n/default.json` is translated into every other officially-supported Stardew Valley language (`de`, `es`, `pt` [Brazilian], `ru`, `ja`, `zh`, `it`, `fr`, `ko`, `tr`, `hu`) as separate files in the same folder. Whenever a string in `default.json` is added, changed, or removed, update all of those files to match in the same change — don't let them silently drift out of sync.

### Verifying vanilla behavior before patching

This mod was built by decompiling the installed game (`ilspycmd` against `Stardew Valley.dll` / `StardewValley.GameData.dll`, found at `~/.local/share/Steam/steamapps/common/Stardew Valley/`) rather than assuming vanilla behavior from memory — several assumptions made from general recollection turned out wrong (e.g. whether picking up a chest preserves contents; it only does if the chest is empty). When adding a new patch or relying on undocumented vanilla behavior, check the decompiled source first rather than guessing.
