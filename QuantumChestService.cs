using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Netcode;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Objects;
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
        private readonly ITranslationHelper translation;
        private readonly IReflectionHelper reflection;
        private readonly IMultiplayerHelper multiplayer;
        private readonly ConditionalWeakTable<Chest, object> wiredForColorSync = new();

        public QuantumChestService(IMonitor monitor, ITranslationHelper translation, IReflectionHelper reflection, IMultiplayerHelper multiplayer)
        {
            this.monitor = monitor;
            this.translation = translation;
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
            if (!chest.modData.ContainsKey(ModConstants.PairIdKey))
                return;
            if (this.wiredForColorSync.TryGetValue(chest, out _))
                return;

            this.wiredForColorSync.Add(chest, new object());
            chest.playerChoiceColor.fieldChangeEvent += (NetColor field, Color oldValue, Color newValue) =>
                this.OnChestColorChanged(chest, oldValue, newValue);
        }

        private void OnChestColorChanged(Chest chest, Color oldValue, Color newValue)
        {
            if (!chest.modData.TryGetValue(ModConstants.PairIdKey, out string? pairId) || string.IsNullOrEmpty(pairId))
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

            this.ForEachRelevantLocation(location =>
            {
                foreach (Debris d in location.debris)
                {
                    if (d.item is SObject obj && Matches(obj, pairId))
                        count += Math.Max(obj.Stack, 1);
                }
                return true;
            });

            return count;
        }

        /// <summary>Every object placed on any map, carried by any farmer, or nested inside any chest-like container's storage (recursively). Deduplicated by reference, since two entangled chests share one underlying inventory and would otherwise double-count its contents.</summary>
        private IEnumerable<SObject> EnumerateAllObjectsIncludingNested()
        {
            var results = new List<SObject>();
            var visited = new HashSet<SObject>(ReferenceEqualityComparer.Instance);

            this.ForEachRelevantLocation(location =>
            {
                foreach (SObject obj in location.objects.Values)
                    CollectRecursively(obj, results, visited);
                return true;
            });

            foreach (Farmer farmer in Game1.getAllFarmers())
            {
                foreach (Item? item in farmer.Items)
                {
                    if (item is SObject obj)
                        CollectRecursively(obj, results, visited);
                }

                // an item briefly held on the mouse cursor mid-drag (e.g. picking one chest up to
                // swap it with another in the inventory menu) isn't in farmer.Items, but it isn't
                // destroyed either - count it, or the momentary gap looks identical to a real loss.
                if (farmer.CursorSlotItem is SObject cursorObj)
                    CollectRecursively(cursorObj, results, visited);
            }

            // some menus (e.g. the crafting tab) hold their in-progress drag item in their own
            // "heldItem" field instead of Game1.player.CursorSlotItem - swapping a freshly-crafted
            // chest onto a slot there displaces the old occupant into that field, invisible to the
            // checks above, which otherwise looks identical to the chest being destroyed.
            if (this.GetActiveMenuHeldItem() is SObject menuHeldObj)
                CollectRecursively(menuHeldObj, results, visited);

            return results;
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

        /// <summary>Visit every location relevant to this player: the same as <see cref="Utility.ForEachLocation"/> for the host, but <see cref="IMultiplayerHelper.GetActiveLocations"/> for a non-host farmhand (see ARCHITECTURE.md for why <see cref="Game1.locations"/> alone isn't safe to scan for a farmhand).</summary>
        private void ForEachRelevantLocation(Func<GameLocation, bool> action)
        {
            if (Context.IsMainPlayer)
            {
                Utility.ForEachLocation(action, includeInteriors: true, includeGenerated: true);
                return;
            }

            foreach (GameLocation location in this.multiplayer.GetActiveLocations())
            {
                if (!action(location))
                    return;

                bool shouldContinue = true;
                location.ForEachInstancedInterior(interior =>
                {
                    if (action(interior))
                        return true;
                    shouldContinue = false;
                    return false;
                });
                if (!shouldContinue)
                    return;
            }

            foreach (MineShaft mine in MineShaft.activeMines)
            {
                if (!action(mine))
                    return;
            }

            foreach (VolcanoDungeon volcano in VolcanoDungeon.activeLevels)
            {
                if (!action(volcano))
                    return;
            }
        }

        private static void CollectRecursively(SObject obj, List<SObject> results, HashSet<SObject> visited)
        {
            if (!visited.Add(obj))
                return;
            results.Add(obj);

            if (obj is Chest chest)
            {
                foreach (Item? stored in chest.GetItemsForPlayer())
                {
                    if (stored is SObject storedObj)
                        CollectRecursively(storedObj, results, visited);
                }
            }
        }

        private static bool Matches(SObject obj, string pairId)
        {
            return obj.modData.TryGetValue(ModConstants.PairIdKey, out string? otherPairId) && otherPairId == pairId;
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
                if (pair.Value.modData.TryGetValue(ModConstants.PairIdKey, out string? pairId) && !string.IsNullOrEmpty(pairId))
                    this.HandlePotentialDestruction(pairId);
            }
        }

        private void OnInventoryChanged(object? sender, StardewModdingAPI.Events.InventoryChangedEventArgs e)
        {
            foreach (Item item in e.Removed)
            {
                if (item is SObject obj && obj.modData.TryGetValue(ModConstants.PairIdKey, out string? pairId) && !string.IsNullOrEmpty(pairId))
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
            this.ForEachRelevantLocation(location =>
            {
                foreach (SObject obj in location.objects.Values)
                {
                    if (obj is Chest chest)
                        this.EnsureColorSyncWired(chest);
                }
                return true;
            });
        }

        private void GrantRecipesIfNeeded()
        {
            Game1.player.craftingRecipes.TryAdd(ModConstants.ChestId, 0);
            Game1.player.craftingRecipes.TryAdd(ModConstants.LargeChestId, 0);
        }

        /// <summary>A pair member was removed from a location or inventory. If only one (or zero) member is left anywhere, react accordingly.</summary>
        private void HandlePotentialDestruction(string pairId)
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

                Game1.addHUDMessage(new HUDMessage(this.translation.Get("collapse.message"), 3));
            }

            // the shared contents are not preserved anywhere - they vanish along with both chests
            Game1.player.team.globalInventories.Remove(pairId);
            Game1.player.team.globalInventoryMutexes.Remove(pairId);

            this.monitor.Log($"A quantum chest pair collapsed: its partner was lost, so the survivor and their shared contents vanished (pair {pairId}).", LogLevel.Info);
        }

        /// <summary>Remove an object wherever it actually is - placed on a map, held directly in a farmer's inventory, or nested inside some other chest-like container's storage (searched recursively).</summary>
        private bool RemoveObjectFromWherever(SObject target)
        {
            bool removedFromMap = false;
            this.ForEachRelevantLocation(location =>
            {
                foreach (KeyValuePair<Vector2, SObject> pair in location.objects.Pairs)
                {
                    if (ReferenceEquals(pair.Value, target))
                    {
                        location.objects.Remove(pair.Key);
                        removedFromMap = true;
                        return false;
                    }
                }
                return true;
            });
            if (removedFromMap)
                return true;

            foreach (Farmer farmer in Game1.getAllFarmers())
            {
                if (farmer.Items.Contains(target))
                {
                    farmer.removeItemFromInventory(target);
                    return true;
                }
            }

            foreach (SObject obj in this.EnumerateAllObjectsIncludingNested())
            {
                if (obj is Chest container && RemoveFromContainer(container, target))
                    return true;
            }

            return false;
        }

        private static bool RemoveFromContainer(Chest container, SObject target)
        {
            IInventory items = container.GetItemsForPlayer();
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
