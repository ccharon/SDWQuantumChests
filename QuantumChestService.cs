using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Netcode;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Inventories;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.SpecialOrders;
using SObject = StardewValley.Object;

namespace QuantumChests

{
    /// <summary>
    /// Tracks entangled chest pairs at runtime: keeps their colors in sync, and collapses a pair - destroying the
    /// surviving chest and its contents - if one half is ever irrecoverably lost.
    /// </summary>
    internal sealed class QuantumChestService
    {
        private readonly IMonitor monitor;
        private readonly IReflectionHelper reflection;
        private readonly IMultiplayerHelper multiplayer;
        private readonly ConditionalWeakTable<Chest, object> wiredForColorSync = new();

        public QuantumChestService(IMonitor monitor, IReflectionHelper reflection, IMultiplayerHelper multiplayer)
        {
            this.monitor = monitor;
            this.reflection = reflection;
            this.multiplayer = multiplayer;
        }

        public void RegisterEvents(IModEvents events)
        {
            events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            events.Player.Warped += this.OnWarped;
            events.World.ObjectListChanged += this.OnObjectListChanged;
            events.Player.InventoryChanged += this.OnInventoryChanged;
        }

        /// <summary>Wire up a freshly-placed or freshly-loaded chest so color changes propagate to its partner.</summary>
        public void EnsureColorSyncWired(Chest chest)
        {
            if (!chest.TryGetPairId(out _))
                return;
            if (this.wiredForColorSync.TryGetValue(chest, out _))
                return;

            this.wiredForColorSync.Add(chest, new object());
            chest.playerChoiceColor.fieldChangeEvent += (NetColor field, Color oldValue, Color newValue) =>
                this.OnChestColorChanged(chest, oldValue, newValue);
        }

        private void OnChestColorChanged(Chest chest, Color oldValue, Color newValue)
        {
            if (!chest.TryGetPairId(out string? pairId))
                return;

            Chest? partner = this.FindChestByPairId(pairId, excluding: chest);
            if (partner != null && partner.playerChoiceColor.Value != newValue)
                partner.playerChoiceColor.Value = newValue;
        }

        /// <summary>Find a placed or carried chest that shares the given pair ID, looking inside any chest-like container as well (not just top-level placement/inventory).</summary>
        public Chest? FindChestByPairId(string pairId, Chest? excluding = null)
        {
            foreach (SObject obj in this.EnumerateAllObjectsIncludingNested())
            {
                if (obj is Chest chest && !ReferenceEquals(chest, excluding) && Matches(chest, pairId))
                    return chest;
            }

            return null;
        }

        /// <summary>Count every existing item that belongs to a pair, wherever it is - placed on a map, carried, stored inside any chest-like container (searched recursively), or briefly sitting on the ground as pickup debris.</summary>
        private int CountPairMembers(string pairId)
        {
            int count = 0;

            foreach (SObject obj in this.EnumerateAllObjectsIncludingNested())
            {
                // count by Stack, not by a flat +1: a pair's two physical halves can be merged into a single
                // Stack=2 item, which is still one object reference representing both members (see ARCHITECTURE.md)
                if (Matches(obj, pairId))
                    count += Math.Max(obj.Stack, 1);
            }

            foreach (GameLocation location in this.GetRelevantLocations())
            {
                foreach (Debris d in location.debris)
                {
                    if (d.item is SObject obj && Matches(obj, pairId))
                        count += Math.Max(obj.Stack, 1);
                }
            }

            return count;
        }

        /// <summary>Every object placed on any map, carried by any farmer, stored in any of the game's out-of-the-way item stores (see <see cref="EnumerateAuxiliaryItemLists"/>), or nested inside any container (recursively). Deduplicated by reference, since two entangled chests share one underlying inventory and would otherwise double-count its contents. Streamed lazily so first-match searches like <see cref="FindChestByPairId"/> stop scanning as soon as they find what they're after. The coverage list mirrors vanilla's own ForEachItemHelper walk - see ARCHITECTURE.md before trimming or extending it.</summary>
        private IEnumerable<SObject> EnumerateAllObjectsIncludingNested()
        {
            var visited = new HashSet<SObject>(ReferenceEqualityComparer.Instance);

            foreach (GameLocation location in this.GetRelevantLocations())
            {
                foreach (SObject obj in location.objects.Values)
                {
                    foreach (SObject found in WalkObject(obj, visited))
                        yield return found;
                }

                // item stores the game keeps outside location.objects: furniture (dresser storage,
                // items placed on tables), the farmhouse/island fridge (a Chest that lives in its own
                // field, not in objects), and building-owned chests (Junimo hut output, mill input/output)
                foreach (Furniture furniture in location.furniture)
                {
                    foreach (SObject found in WalkObject(furniture, visited))
                        yield return found;
                }

                if (location.GetFridge(onlyUnlocked: false) is Chest fridge)
                {
                    foreach (SObject found in WalkObject(fridge, visited))
                        yield return found;
                }

                foreach (Building building in location.buildings)
                {
                    foreach (Chest chest in building.buildingChests)
                    {
                        foreach (SObject found in WalkObject(chest, visited))
                            yield return found;
                    }
                }
            }

            foreach (Farmer farmer in Game1.getAllFarmers())
            {
                foreach (SObject found in WalkList(farmer.Items, visited))
                    yield return found;

                // an item briefly held on the mouse cursor mid-drag (e.g. picking one chest up to
                // swap it with another in the inventory menu) isn't in farmer.Items, but it isn't
                // destroyed either - count it, or the momentary gap looks identical to a real loss.
                if (farmer.CursorSlotItem is SObject cursorObj)
                {
                    foreach (SObject found in WalkObject(cursorObj, visited))
                        yield return found;
                }

                // an item queued for Marlon's item-recovery service after dying
                if (farmer.recoveredItem is SObject recoveredObj)
                {
                    foreach (SObject found in WalkObject(recoveredObj, visited))
                        yield return found;
                }
            }

            // some menus (e.g. the crafting tab) hold their in-progress drag item in their own
            // "heldItem" field instead of Game1.player.CursorSlotItem - swapping a freshly-crafted
            // chest onto a slot there displaces the old occupant into that field, invisible to the
            // checks above, which otherwise looks identical to the chest being destroyed.
            if (this.GetActiveMenuHeldItem() is SObject menuHeldObj)
            {
                foreach (SObject found in WalkObject(menuHeldObj, visited))
                    yield return found;
            }

            foreach (IList<Item> list in this.EnumerateAuxiliaryItemLists())
            {
                foreach (SObject found in WalkList(list, visited))
                    yield return found;
            }
        }

        /// <summary>Every plain item list the game (or another mod) keeps outside placed objects and farmer backpacks. An item sitting in any of these is stored, not destroyed - if the presence count can't see one of them, moving a chest there falsely collapses its pair (see ARCHITECTURE.md for the full inventory of these stores and how it was derived).</summary>
        private IEnumerable<IList<Item>> EnumerateAuxiliaryItemLists()
        {
            foreach (GameLocation location in this.GetRelevantLocations())
            {
                if (location is Farm farm)
                {
                    // one shared bin, or one per farmer with separate wallets; getShippingBin picks.
                    // Duplicates are fine - the enumeration dedups items by reference anyway.
                    foreach (Farmer farmer in Game1.getAllFarmers())
                        yield return farm.getShippingBin(farmer);
                }

                if (location is ShopLocation shop)
                {
                    yield return shop.itemsFromPlayerToSell;
                    yield return shop.itemsToStartSellingTomorrow;
                }
            }

            // items dropped on death, recoverable via Marlon - losing them fires InventoryChanged,
            // so without this list a death while carrying a chest falsely collapses its pair
            foreach (Farmer farmer in Game1.getAllFarmers())
                yield return farmer.itemsLostLastDeath;

            var team = Game1.player.team;

            // any global-inventory-backed storage: vanilla's own (e.g. Junimo chests) or another
            // mod's GetOrCreateGlobalInventory storage with no placed chest to recurse into
            foreach (Inventory inventory in team.globalInventories.Values)
                yield return inventory;

            yield return team.returnedDonations; // the Lost & Found box
            yield return team.luauIngredients;
            yield return team.grangeDisplay; // Stardew Valley Fair display - items come back after
            foreach (SpecialOrder order in team.specialOrders)
                yield return order.donatedItems;
        }

        /// <summary>The item currently held mid-drag by the active menu, if any - checked by field name via reflection since different menus (e.g. <see cref="CraftingPage"/>) keep their own separate "heldItem" field rather than going through <see cref="Farmer.CursorSlotItem"/>.</summary>
        private Item? GetActiveMenuHeldItem()
        {
            IClickableMenu? menu = Game1.activeClickableMenu;
            if (menu is GameMenu gameMenu)
                menu = gameMenu.GetCurrentPage();
            if (menu == null)
                return null;

            return this.reflection.GetField<Item>(menu, "heldItem", required: false)?.GetValue();
        }

        /// <summary>Every location relevant to this player: the same as <see cref="Utility.ForEachLocation"/> for the host, but <see cref="IMultiplayerHelper.GetActiveLocations"/> for a non-host farmhand (see ARCHITECTURE.md for why <see cref="Game1.locations"/> alone isn't safe to scan for a farmhand). The location refs are collected eagerly (vanilla's walk is callback-only, and locations number in the dozens); the expensive part - object/item enumeration - stays lazy downstream.</summary>
        private IEnumerable<GameLocation> GetRelevantLocations()
        {
            var locations = new List<GameLocation>();

            if (Context.IsMainPlayer)
            {
                Utility.ForEachLocation(location =>
                {
                    locations.Add(location);
                    return true;
                }, includeInteriors: true, includeGenerated: true);
                return locations;
            }

            foreach (GameLocation location in this.multiplayer.GetActiveLocations())
            {
                locations.Add(location);
                location.ForEachInstancedInterior(interior =>
                {
                    locations.Add(interior);
                    return true;
                });
            }

            locations.AddRange(MineShaft.activeMines);
            locations.AddRange(VolcanoDungeon.activeLevels);

            return locations;
        }

        private static IEnumerable<SObject> WalkObject(SObject obj, HashSet<SObject> visited)
        {
            if (!visited.Add(obj))
                yield break;
            yield return obj;

            if (obj is Chest chest)
            {
                foreach (SObject found in WalkList(chest.GetItemsForPlayer(), visited))
                    yield return found;
            }
            else if (obj is StorageFurniture storage)
            {
                foreach (SObject found in WalkList(storage.heldItems, visited))
                    yield return found;
            }

            // deliberately NOT Sign.displayItem: a sign shows a getOne() copy (pair ID and all)
            // without consuming the real item, so counting it would inflate the pair count and
            // mask a real loss (see ARCHITECTURE.md)

            // machines, auto-grabbers, and tables hold their content in heldObject (an auto-grabber's
            // whole storage is a Chest sitting in heldObject)
            if (obj.heldObject.Value is SObject held)
            {
                foreach (SObject found in WalkObject(held, visited))
                    yield return found;
            }
        }

        private static IEnumerable<SObject> WalkList(IEnumerable<Item?> items, HashSet<SObject> visited)
        {
            foreach (Item? item in items)
            {
                if (item is SObject obj)
                {
                    foreach (SObject found in WalkObject(obj, visited))
                        yield return found;
                }
            }
        }

        private static bool Matches(SObject obj, string pairId)
        {
            return obj.TryGetPairId(out string? otherPairId) && otherPairId == pairId;
        }

        private void OnObjectListChanged(object? sender, StardewModdingAPI.Events.ObjectListChangedEventArgs e)
        {
            foreach (var pair in e.Added)
            {
                if (pair.Value is Chest chest)
                    this.EnsureColorSyncWired(chest);
            }

            foreach (var pair in e.Removed)
            {
                if (pair.Value.TryGetPairId(out string? pairId))
                    this.HandlePotentialDestruction(pairId);
            }
        }

        private void OnInventoryChanged(object? sender, StardewModdingAPI.Events.InventoryChangedEventArgs e)
        {
            foreach (Item item in e.Removed)
            {
                if (item is SObject obj && obj.TryGetPairId(out string? pairId))
                    this.HandlePotentialDestruction(pairId);
            }
        }

        private void OnSaveLoaded(object? sender, StardewModdingAPI.Events.SaveLoadedEventArgs e)
        {
            this.GrantRecipesIfNeeded();
            this.WireAllPlacedChests();
        }

        private void OnWarped(object? sender, StardewModdingAPI.Events.WarpedEventArgs e)
        {
            foreach (SObject obj in e.NewLocation.objects.Values)
            {
                if (obj is Chest chest)
                    this.EnsureColorSyncWired(chest);
            }
        }

        private void WireAllPlacedChests()
        {
            foreach (GameLocation location in this.GetRelevantLocations())
            {
                foreach (SObject obj in location.objects.Values)
                {
                    if (obj is Chest chest)
                        this.EnsureColorSyncWired(chest);
                }
            }
        }

        private void GrantRecipesIfNeeded()
        {
            // Game1.player is safe in split-screen: SMAPI raises SaveLoaded once per screen with
            // Game1.player scoped to that screen's player, so every split-screen player is granted
            // the recipes too (remote farmhands get them from their own game instance's SaveLoaded)
            Game1.player.craftingRecipes.TryAdd(ModConstants.ChestId, 0);
            Game1.player.craftingRecipes.TryAdd(ModConstants.LargeChestId, 0);
        }

        /// <summary>A pair member was removed from a location or inventory. If only one (or zero) member is left anywhere, react accordingly.</summary>
        public void HandlePotentialDestruction(string pairId)
        {
            int remaining = this.CountPairMembers(pairId);

            if (remaining == 0)
            {
                Game1.player.team.globalInventories.Remove(pairId);
                Game1.player.team.globalInventoryMutexes.Remove(pairId);
            }
            else if (remaining == 1)
            {
                this.CollapsePair(pairId);
            }
            // remaining >= 2: both members still exist somewhere (e.g. one was just relocated/placed) - nothing to do
        }

        /// <summary>One member of a pair is irrecoverably gone. Make the other vanish too, taking the shared contents with it.</summary>
        private void CollapsePair(string pairId)
        {
            Chest? survivor = this.FindChestByPairId(pairId);
            if (survivor != null)
            {
                if (survivor.Location != null && survivor.lightSource != null)
                    survivor.Location.removeLightSource(survivor.lightSource.Id);

                this.RemoveObjectFromWherever(survivor);

                Game1.addHUDMessage(new HUDMessage(I18n.Collapse_Message(), 3));
            }

            // the shared contents are not preserved anywhere - they vanish along with both chests
            Game1.player.team.globalInventories.Remove(pairId);
            Game1.player.team.globalInventoryMutexes.Remove(pairId);

            this.monitor.Log($"A quantum chest pair collapsed: its partner was lost, so the survivor and their shared contents vanished (pair {pairId}).", LogLevel.Info);
        }

        /// <summary>Remove an object wherever it actually is - placed on a map, held directly in a farmer's inventory, nested inside any container found by <see cref="EnumerateAllObjectsIncludingNested"/>, or sitting in any of the auxiliary item stores from <see cref="EnumerateAuxiliaryItemLists"/>.</summary>
        /// <remarks>Mutating mid-enumeration is safe here only because every removal immediately returns - the lazy enumerators are never advanced past a mutation.</remarks>
        private bool RemoveObjectFromWherever(SObject target)
        {
            foreach (GameLocation location in this.GetRelevantLocations())
            {
                foreach (KeyValuePair<Vector2, SObject> pair in location.objects.Pairs)
                {
                    if (ReferenceEquals(pair.Value, target))
                    {
                        location.objects.Remove(pair.Key);
                        return true;
                    }
                }
            }

            foreach (Farmer farmer in Game1.getAllFarmers())
            {
                if (farmer.Items.Contains(target))
                {
                    farmer.removeItemFromInventory(target);
                    return true;
                }

                if (ReferenceEquals(farmer.recoveredItem, target))
                {
                    farmer.recoveredItem = null;
                    return true;
                }
            }

            foreach (SObject obj in this.EnumerateAllObjectsIncludingNested())
            {
                bool removed = obj switch
                {
                    Chest container => RemoveFromList(container.GetItemsForPlayer(), target),
                    StorageFurniture storage => RemoveFromList(storage.heldItems, target),
                    _ => false,
                };
                if (removed)
                    return true;

                if (ReferenceEquals(obj.heldObject.Value, target))
                {
                    obj.heldObject.Value = null;
                    return true;
                }
            }

            foreach (IList<Item> list in this.EnumerateAuxiliaryItemLists())
            {
                if (RemoveFromList(list, target))
                    return true;
            }

            return false;
        }

        private static bool RemoveFromList(IList<Item> items, SObject target)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i], target))
                {
                    items.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }
    }
}
