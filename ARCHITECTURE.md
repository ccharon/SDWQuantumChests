# Architecture

This document explains *why* the code is structured the way it is: the vanilla constraints that force each
Harmony patch to exist, the non-obvious bugs that shaped specific implementation choices, and the reasoning
behind design decisions that might otherwise look arbitrary. Code comments point here for depth; they don't
repeat this history inline.

## Why this needs Harmony instead of just Content Patcher data

A custom big-craftable ID is *not* enough to get real chest behavior. `BigCraftableDataDefinition.CreateItem`
always returns a plain `StardewValley.Object`, and vanilla's `Object.placementAction` decides whether to
instantiate an actual `Chest` (menu, inventory, mutex) via a hardcoded `switch` on exact vanilla
QualifiedItemIds (`"(BC)130"`, `"(BC)BigChest"`, etc.) — an unrecognized ID just falls through to generic
placement with no inventory at all. Several other vanilla methods have this same
hardcoded-vanilla-ID-list pattern, and each needed its own patch:

- `Object.placementAction` → `ObjectPlacementPatcher`: constructs the real `Chest` ourselves for our two item
  IDs, copies `modData` from the item being placed, sets `GlobalInventoryId`, wires color sync.
- `Chest.GetActualCapacity` → `ChestCapacityPatcher`: postfix bumping capacity to 70 for the large tier
  (vanilla only special-cases `SpecialChestType.BigChest`, which we deliberately never set to avoid inheriting
  unrelated BigChest-specific behavior elsewhere).
- `Chest.draw(SpriteBatch,int,int,float)` and the separate `Chest.draw(SpriteBatch,int,int,float,bool local)`
  overload → `ChestColorDrawPatcher` / `ChestColorMenuDrawPatcher`: vanilla only renders `playerChoiceColor`
  for the same hardcoded ID list; anything else silently ignores the color and draws a plain sprite. Both
  overloads need patching separately — one is used for world rendering, the other for menu/preview icons (e.g.
  the color picker's preview swatch).
- `Chest.HandleChestHit` → `ChestHitPreservePairPatcher`: when you axe-hit an *empty* chest, vanilla removes it
  and reconstructs a brand-new item from just its ID string (`new Debris(base.QualifiedItemId, ...)`),
  discarding `modData` — silently stripping the pair ID on every empty-chest pickup. This patch takes over only
  the empty-chest branch and uses the `Debris(Item, ...)` constructor with the real instance instead,
  preserving the pair ID. The "chest still has items → kick it" branch is left untouched (`return true`).
- `CraftingRecipe.createItem` → `CraftingRecipePairIdPatcher`: postfix that stamps a fresh GUID into
  `modData[PairIdKey]` the moment a pair is crafted (recipe yields `Stack = 2`; both physical chests inherit
  the same ID because `Item.getOne()` copies `modData` when the stack is split for placement). Also picks a
  random color from vanilla's own `DiscreteColorPicker.getColorFromSelection` (selections 1-20; 0 is
  `Color.Black`, "no color") and stores it via `PairColorStorage`/`modData[PairColorKey]`.
- `Item.canStackWith` → `ChestPairStackPatcher`: vanilla stacking only compares ID/name/quality, so crafting
  two different pairs back-to-back (or picking up one chest each from two different pairs) would otherwise
  merge them into one stack and corrupt both pair IDs (only one `modData` dictionary survives a merge). This
  patch forces `false` whenever both items are our chest type but have different pair IDs — **and forces
  `true` the other way**, whenever pair IDs *do* match, overriding vanilla's own type/quality/name checks
  entirely. That second half is required because vanilla's `canStackWith` demands both items be the exact same
  .NET type, but a placed-then-collected chest is a real `Chest` instance while a never-placed one is still a
  plain `Object` — without forcing `true`, two halves of the same pair could never re-stack together once one
  had been placed and picked back up.

Private vanilla fields needed by patches (`currentLidFrame` on `Chest`, `chestHit` on `FarmerTeam`) are read via
`HarmonyLib.AccessTools.Field` reflection (wrapped as `RequireField<T>`), resolved inside each patcher's own
`Apply` and cached in a `private static FieldInfo` field.

## Color rendering: four separate paths, one shared technique

A chest can be drawn in four completely different contexts, each via its own vanilla method, none of which
share code with each other:

- **Placed** (`Chest.draw`) → `ChestColorDrawPatcher`.
- **Backpack icon** (`Object.drawInMenu`) → `ChestColorInventoryDrawPatcher`.
- **About-to-place preview** (`Object.drawPlacementBounds` → `this.draw(...)`, the base `Object.draw` overload)
  → `ObjectColorDrawPatcher`. The currently-selected, not-yet-placed item is still a plain `Object`, so this
  dispatches to the base method instead of `Chest.draw`'s override.
- **Carried overhead** (`Game1.drawPlayerHeldObject` → `Object.drawWhenHeld`) → `ObjectHeldColorDrawPatcher`.

Vanilla itself only ever renders `playerChoiceColor` for the *placed* case (and only for its own hardcoded
vanilla chest IDs, hence needing a patch at all). The other three are a deliberate enhancement *beyond* vanilla
parity: without them, a colored chest loses its color the moment it's picked back up, carried, or previewed.
Because a real placed `Chest` always dispatches to its own `Chest.draw` override polymorphically, it never runs
through the base `Object.draw`/`drawWhenHeld` methods at all — so `ObjectColorDrawPatcher` and
`ObjectHeldColorDrawPatcher` only ever fire for a still-unplaced item, which has no `playerChoiceColor` of its
own to read. They resolve the color via `PairColorStorage.TryGetColorForUnplacedItem` instead (see below).

### The layered dye technique, and two ways to get it wrong

Vanilla's actual chest-dye rendering isn't a single tinted sprite. It's three layers, all drawn from the same
resting lid frame:

1. A **base** frame that's a grayscale recolor mask, multiplied by `playerChoiceColor` — the wood panel is
   near-white in the mask so it takes the color strongly; the metal trim is near-black so it stays dark and
   effectively unrecolored.
2. An **open/trim** frame drawn at plain `Color.White` on top — the metal trim/latch's true, unrecolored
   pixels, restoring their neutral color over the recolored base.
3. A **lit** frame (another grayscale mask), tinted again for a highlight glow.

All four patches above now use this exact technique, adapted to each method's own coordinate convention
(tile-position math for `ChestColorDrawPatcher`/`ObjectColorDrawPatcher`, direct screen position for
`ObjectHeldColorDrawPatcher`, centered-anchor/scale for `ChestColorInventoryDrawPatcher`). Two mistakes were
made and fixed while building this out, both worth knowing about before touching this code again:

- **Resting lid frame is `ParentSheetIndex + 1`, not `0`.** A real `Chest`'s lid animates through a range of
  frame indices as it opens/closes; the resting (closed) value is `Chest.startingLidFrame`, which vanilla sets
  to `ParentSheetIndex + 1`. The first attempt at the three-layer technique for the still-unplaced patches
  assumed `0` was the resting frame, which happens to land on a completely unrelated sprite elsewhere in the
  shared `TileSheets/Craftables` sheet (it looked like "a different machine" in-game). This was confirmed by
  unpacking `Craftables.xnb` with `xnbcli` and cropping the actual pixels at both the wrong and correct
  frame indices side by side — see "Verifying vanilla behavior" below for the general technique.
- **Draw order matters, and the natural reading order is wrong.** The layers must be drawn base → lit → trim,
  with trim drawn *last* (on top), matching `ChestColorDrawPatcher`'s `layerDepth` /
  `layerDepth + 1E-05f` / `layerDepth + 2E-05f` values exactly (base, lit, trim in that depth order). Writing
  the three `spriteBatch.Draw` calls in the more "natural" base → trim → lit order puts the tinted lit layer on
  top of the neutral white trim layer, visibly recoloring the metal trim instead of leaving it neutral. When
  copying this technique to a new context, copy the depth values verbatim rather than re-deriving them.

A flat single-layer tint (multiply the whole plain closed-icon sprite by one color) was tried first for the
three still-unplaced patches, specifically to avoid the frame-index risk above. It looked plausible but was
itself wrong in a different way: a flat multiply hits the brightest pixels hardest, so it visibly recolored the
metal trim (the brightest part of the base sprite) while barely affecting the darker wood grain — the *opposite*
of how vanilla's own dye rendering allocates color. Once the resting-frame bug above was understood and fixed,
there was no reason not to use the real three-layer technique everywhere, so all four paths were unified on it.

## Random craft-time color (`PairColorStorage`)

`ModConstants.PairColorKey` stores a color (packed via `Color.PackedValue`) in `modData`, assigned once by
`CraftingRecipePairIdPatcher` at craft time from vanilla's own 20-color chest palette. `PairColorStorage.TryGetColorForUnplacedItem`
is the single shared policy used by all three still-unplaced-item patches: prefer an already-placed,
already-dyed partner's color, falling back to this stored craft-time color if no partner is placed yet.
`ObjectPlacementPatcher` uses the same stored color as its fallback when placing the *first* chest of a pair.
Once either chest is actually placed and colored, the real `Chest.playerChoiceColor`/color-sync mechanism takes
over as the source of truth — the stored value only matters before that first placement.

## Harmony patch registration (`Patching/`, `ModEntry`)

Each patch is its own class implementing `Patching.IPatcher` (via `BasePatcher`), with an explicit
`Apply(Harmony, IMonitor)` method that resolves its own target via `RequireMethod<T>`/`RequireField<T>` (thin
wrappers over `AccessTools` that throw a clear `"Can't find method/field X.Y to patch"` instead of a null
target silently failing later) and registers its prefix/postfix by name via `GetHarmonyMethod`. `ModEntry`
applies all of them through `HarmonyPatcher.Apply(this, new ObjectPlacementPatcher(), ...)`, which wraps *each*
patcher's `Apply` call in its own try/catch and logs a per-patcher error on failure.

This is deliberately not `harmony.PatchAll(assembly)`: with `PatchAll`, one patch failing to apply (e.g. after a
game update changes a method signature) can abort the whole batch, taking the entire mod down instead of just
the one broken feature. The pattern is borrowed from how `Pathoschild.Stardew.Common.Patching` structures
patches across Pathoschild's own mod suite (`Automate`, `ChestsAnywhere`, etc.).

## Pairing and shared-inventory mechanism

No custom sync code was written for the shared inventory — it reuses the same mechanism vanilla uses for
Junimo Chests: `Chest.GlobalInventoryId` (a string key) makes `Chest.Items`/`GetMutex()` transparently redirect
to `Game1.player.team.GetOrCreateGlobalInventory(id)` / `GetOrCreateGlobalInventoryMutex(id)`, which is already
fully multiplayer-networked. The twist is scoping it to exactly 2 members instead of "every Junimo Chest," by
keying on a per-pair GUID instead of the vanilla constant.

The pair ID lives in `Item.modData[ModConstants.PairIdKey]` and is the single source of truth for "these two
chests belong together." It's assigned once at craft time and copied forward at every placement; nothing ever
reassigns it later.

## Destruction / collapse detection (`QuantumChestService`)

Deliberate design decision, not to be "fixed" without re-confirming with the maintainer: regular Stardew chests
can't actually be destroyed by bombs (`GameLocation.destroyObject` explicitly excludes `is Chest`) and have
`Fragility` 0, so there is intentionally **no new way to destroy a chest** added by this mod. Instead,
`QuantumChestService` watches for a pair member disappearing for *any* reason (picked up, trashed, or something
unanticipated like a temporary/regenerated location) and reacts based on how many members still exist anywhere.

A pair's two physical halves can be merged into a single `Stack = 2` item — fresh from crafting, or after two
collected (placed-then-picked-up) chests re-stack via `ChestPairStackPatcher` — and that's still *one* object
reference representing *both* members. `CountPairMembers` therefore counts by `.Stack` quantity
(`Math.Max(obj.Stack, 1)`), not by a flat `+1` per matched object reference; counting a merged stack as 1 made
it look like "only one member left" the instant it became the sole representation of the pair (e.g. briefly
held on the cursor mid-inventory-swap), triggering a false collapse.

Several other transient states needed accounting for too, all discovered by actually triggering them during
testing rather than by inspection alone:

- `EnumerateAllObjectsIncludingNested` is the core search: every location's placed objects + every farmer's
  top-level inventory, recursing into any `Chest`-like container's stored items (a quantum chest can be stored
  inside a regular chest, or even inside its own partner — both are valid states, not bugs). Deduplicated by
  reference (`ReferenceEqualityComparer`) because two entangled chests share one underlying `Inventory` object
  and would otherwise double-count its contents when both are scanned. It's a lazy iterator chain (`yield
  return` all the way down, modeled on how `Pathoschild.Stardew.ChestsAnywhere` streams its chest search), so
  first-match consumers stop scanning at the first hit — that matters most for `FindChestByPairId`, which runs
  on every `playerChoiceColor` change and usually finds the partner long before the walk would reach the
  auxiliary stores. `CountPairMembers` still consumes the full sequence; only evaluation timing changed, not
  coverage or order. `RemoveObjectFromWherever` also iterates lazily, which is safe only because every removal
  immediately `return`s — the enumerators are never advanced past a mutation.
- `CountPairMembers` also scans `location.debris` (`Debris.item`) to account for the brief window between an
  emptied chest being removed from the map and the player walking over the resulting pickup-debris to actually
  collect it — without this, that window looks identical to "destroyed."
- An item briefly held on the mouse cursor mid-drag (`Farmer.CursorSlotItem`) — e.g. picking one chest up to
  swap it with another in the inventory menu — isn't in `farmer.Items`, but it isn't destroyed either, so it's
  counted too.
- Some menus (e.g. the crafting tab, `CraftingPage`) hold their in-progress drag item in their own `heldItem`
  field instead of `Game1.player.CursorSlotItem` — swapping a freshly-crafted chest onto a slot there displaces
  the old occupant into that field, invisible to the checks above unless specifically read via reflection
  (`GetActiveMenuHeldItem`).
- The general failure mode behind several of these: **any storage the presence-scan doesn't know about is
  indistinguishable from destruction**. Moving a chest into an unscanned store removes it from `farmer.Items`
  or `location.objects`, fires `Player.InventoryChanged`/`World.ObjectListChanged`, the recount can't find it,
  and a perfectly intact pair falsely collapses — deleting its shared contents. This surfaced first via
  another mod's `GetOrCreateGlobalInventory`-backed storage (a courier-style mod temporarily holding
  handed-over items in its own global inventory), and turned out to apply to a whole list of vanilla stores
  too. See "Where an item can live" below for the full coverage list and how it was derived.
- Every inventory menu's trash can (`MenuWithInventory`, `InventoryPage`, `CraftingPage`, `JunimoNoteMenu`)
  discards its held item via `Utility.trashItem` directly — this never touches `Farmer.Items` or
  `GameLocation.objects`, so neither `Player.InventoryChanged` nor `World.ObjectListChanged` fires for it.
  Without `TrashCanDestructionPatcher` (a postfix on `Utility.trashItem`), trashing one half of a pair leaves
  its partner behind in the world until some unrelated event happens to trigger a rescan (found by trashing a
  chest and observing its partner only vanish once later picked up, rather than immediately).

If a pair drops to exactly 1 remaining member, `CollapsePair` removes the survivor too (wherever it actually is
— map, top-level inventory, nested in a container, or in any auxiliary store, via `RemoveObjectFromWherever`)
and discards the shared inventory entirely; contents are **not** preserved. This is intentional flavor ("the
danger of quantum mechanics"), not a bug to fix by making it safer.

### Where an item can live: the full presence-scan coverage list

The authoritative checklist for "every place the game can keep an `Item` instance" is vanilla's own
`StardewValley.Internal.ForEachItemHelper.ForEachItemInWorld` / `ForEachItemInLocation` (decompile it — it's
what the game itself uses for save migrations that must touch every item). `EnumerateAllObjectsIncludingNested`
plus `EnumerateAuxiliaryItemLists` deliberately mirror its coverage, with a few additions and exclusions listed
below. **If a game update adds a new item store, re-diff against `ForEachItemHelper` and extend the scan** —
any store the scan can't see falsely collapses a pair moved into it.

Covered, mirroring vanilla's walk (each verified against the decompiled 1.6 source):

- `location.objects` recursively — with `Object.heldObject` followed at every level (matching
  `Object.ForEachItem`): machine contents, and notably the **auto-grabber**, whose entire storage is a `Chest`
  sitting in `heldObject`.
- `location.furniture` — `StorageFurniture.heldItems` (dressers) and items placed on tables (again
  `heldObject`).
- `location.GetFridge(onlyUnlocked: false)` — the farmhouse/island fridge is a `Chest` in its own field on
  `FarmHouse`/`IslandFarmHouse`, **not** in `location.objects`; without this, putting a chest in the fridge
  collapsed its pair.
- `building.buildingChests` for every building — Junimo hut output, mill input/output.
- `Farm.getShippingBin(farmer)` per farmer — `sharedShippingBin` or per-farmer `personalShippingBin` depending
  on `useSeparateWallets`; iterated per farmer with duplicates handled by the reference-dedup. (Vanilla's
  `ForEachItemInWorld` itself skips the bin; we cover it anyway.)
- `ShopLocation.itemsFromPlayerToSell` / `itemsToStartSellingTomorrow` — items sold to a shop for resale.
- `farmer.itemsLostLastDeath` and `farmer.recoveredItem` — death loss / Marlon's item-recovery service. Dying
  fires `InventoryChanged` for the lost items, so before this was scanned, **dying while carrying a chest
  falsely collapsed its pair** even though the item was recoverable.
- `team.globalInventories.Values` — every global inventory: vanilla's (Junimo chests) or **any other mod's**
  `GetOrCreateGlobalInventory` storage, which has no placed chest anywhere to recurse into. Iterate the
  *values* (item lists); the keys are just IDs, including each pair's own shared-inventory ID. The
  reference-dedup keeps a pair's own shared inventory from double-counting when it's reachable both here and
  via a placed chest's `GetItemsForPlayer()`.
- `team.returnedDonations` (the Lost & Found box) and `specialOrders[*].donatedItems`.
- `team.luauIngredients` and `team.grangeDisplay` (fair display items are returned to the player afterward) —
  these two are real item stores that even vanilla's `ForEachItemInWorld` doesn't walk.

Deliberately **excluded**, don't "fix" these without re-checking the reasoning:

- `Sign.displayItem`: a sign stores `currentItem.getOne()` — a *copy* (with `modData`, so it carries the pair
  ID) — **without consuming the shown item** (verified in the decompiled `Sign.checkForAction`). Counting it
  would inflate the pair count and mask a real loss.
- Equipment slots (`shirtItem`/`pantsItem`/`hat`/`boots`/rings), `toolBeingUpgraded`, and NPC hats
  (pet/horse/child): typed slots that can never hold a big-craftable `Object`.

`RemoveObjectFromWherever` walks the same coverage (chest contents, `StorageFurniture.heldItems`,
`heldObject`, and every auxiliary list) so a *genuine* collapse can remove the survivor from any of these
places, not just map/backpack/chest.

Color sync (`EnsureColorSyncWired`) hooks `Chest.playerChoiceColor.fieldChangeEvent` per chest, guarded by a
`ConditionalWeakTable<Chest, object>` so re-wiring on save load / warp / placement is idempotent and doesn't
leak memory for chests that no longer exist.

### Multiplayer: `GetRelevantLocations`

`GetRelevantLocations` is what every scan above actually walks, instead of calling `Utility.ForEachLocation`
directly. For the host it's the same thing, but for a non-host farmhand, `Game1.locations` can hold stale,
no-longer-synced snapshots of locations that client isn't actively tracking (e.g. another farmhand's cabin
they've never entered) — scanning those could under- or over-count a pair and collapse it based on outdated
data. `IMultiplayerHelper.GetActiveLocations()` (checked via `Context.IsMainPlayer`) is scoped to only what's
actively kept in sync for the current client, so that's used instead when not the host. This distinction was
found by comparing against how `Pathoschild.Stardew.ChestsAnywhere` handles the same host/farmhand split in its
own location-walking code.

It returns an eagerly collected `List<GameLocation>` rather than a lazy iterator: vanilla's
`Utility.ForEachLocation` is callback-only (no enumerable form to wrap), and a save has only dozens of
locations, so collecting the refs is cheap. The laziness that actually pays off — enumerating each location's
objects and nested items — happens downstream in `EnumerateAllObjectsIncludingNested`.

## Content (`ContentProvider`)

Adds two `Data/BigCraftables` entries and two `Data/CraftingRecipes` entries via `Content.AssetRequested`. Both
chest tiers intentionally reuse vanilla's own `TileSheets/Craftables` sprites (`Texture = null`, `SpriteIndex`
130 / 304 — the same sprites as vanilla Chest / Big Chest) rather than custom art; the randomly-assigned dye
color is the only visual differentiator. Recipes are granted directly via `Game1.player.craftingRecipes.TryAdd`
on `SaveLoaded` rather than through the vanilla unlock-condition field (deliberately not level/quest-gated,
just materials-gated).

The translated text baked into the `Data/BigCraftables`/`Data/CraftingRecipes` edits is resolved once, whenever
the asset is first loaded. Switching languages afterward doesn't get SMAPI to re-run that edit on its own (it
only unloads its own vanilla asset cache), so `ContentProvider` listens for `Content.LocaleChanged` and
explicitly invalidates both assets to force them to regenerate in the new language.

**No light source is attached to placed chests.** There was one originally — a purple glow — kept as a visual
signature from before the random-color feature existed. A `LightSource` additively tints nearby pixels during
the game's lighting pass, so it was shifting a *placed* chest's rendered hue away from the exact same
`playerChoiceColor` shown on the backpack icon / carried-overhead / placement-preview renders (which have no
light shining on them) — visibly different colors for the same stored value, confirmed by comparing a
screenshot of a placed vs. carried chest of the same pair. It was removed once the random-color feature made
this a real, reported bug rather than just an aesthetic choice. Don't re-add a light source to the chest itself
without re-solving that color-shift problem first.

Known vanilla item/data IDs relied on (verified against the installed game via `ilspycmd`, not assumed from
memory): Iridium Bar = `337`, Battery Pack = `787`, vanilla Chest = `130`, vanilla Big Chest = `BigChest`.

`i18n/default.json` is translated into every other officially-supported Stardew Valley language (`de`, `es`,
`pt` [Brazilian], `ru`, `ja`, `zh`, `it`, `fr`, `ko`, `tr`, `hu`) as separate files in the same folder. Whenever
a string in `default.json` is added, changed, or removed, update all of those files to match in the same change
— don't let them silently drift out of sync.

## Verifying vanilla behavior before patching

This mod was built by decompiling the installed game (`ilspycmd` against `Stardew Valley.dll` /
`StardewValley.GameData.dll`, found at `~/.local/share/Steam/steamapps/common/Stardew Valley/`) rather than
assuming vanilla behavior from memory — several assumptions made from general recollection turned out wrong
(e.g. whether picking up a chest preserves contents; it only does if the chest is empty). When adding a new
patch or relying on undocumented vanilla behavior, check the decompiled source first rather than guessing.

The same applies to sprite/texture assumptions, not just code: when a patch draws specific frames from a shared
vanilla spritesheet (e.g. `TileSheets/Craftables`), verify the actual frame contents before trusting an assumed
index or offset. `xnbcli` (found locally under `~/Dokumente/SDWFarmMap/` and `~/Downloads/`) can unpack a
`.xnb` file (e.g. `Content/TileSheets/Craftables.xnb`) to a plain `.png` for direct inspection — this is exactly
how the wrong "resting lid frame" assumption above was caught and fixed, by cropping and viewing the actual
pixels at both the wrong and correct frame indices rather than continuing to guess from the "garbled sprite"
symptom alone.
