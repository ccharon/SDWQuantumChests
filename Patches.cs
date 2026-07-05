using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using QuantumChests.Patching;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Extensions;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;
using StardewValley.Network.ChestHit;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace QuantumChests
{
    /// <summary>
    /// Vanilla's placement logic only builds a real, working <see cref="Chest"/> (inventory, mutex, menu) for a
    /// hardcoded list of vanilla chest IDs; anything else falls through to a plain, non-functional placed object.
    /// This builds the real <see cref="Chest"/> ourselves for our two item IDs instead.
    /// </summary>
    internal sealed class ObjectPlacementPatcher : BasePatcher
    {
        public override void Apply(Harmony harmony, IMonitor monitor)
        {
            harmony.Patch(
                original: RequireMethod<SObject>(nameof(SObject.placementAction)),
                prefix: this.GetHarmonyMethod(nameof(Prefix))
            );
        }

        private static bool Prefix(SObject __instance, GameLocation location, int x, int y, Farmer? who, ref bool __result)
        {
            if (!ModConstants.TryGetTier(__instance.QualifiedItemId, out _))
                return true;

            Vector2 tile = new Vector2(x / 64, y / 64);
            if (location.objects.ContainsKey(tile))
            {
                __result = false;
                return false;
            }

            Chest chest = new Chest(playerChest: true, tile, __instance.ItemId)
            {
                name = __instance.Name,
                shakeTimer = 50
            };

            foreach (string key in __instance.modData.Keys)
                chest.modData[key] = __instance.modData[key];

            if (chest.modData.TryGetValue(ModConstants.PairIdKey, out string? pairId) && !string.IsNullOrEmpty(pairId))
                chest.GlobalInventoryId = pairId;

            Chest? existingPartner = !string.IsNullOrEmpty(pairId) ? ModEntry.Service.FindChestByPairId(pairId) : null;
            Color fallbackColor = PairColorStorage.TryGet(chest.modData, out Color storedColor) ? storedColor : ModConstants.DefaultTint;
            chest.playerChoiceColor.Value = existingPartner?.playerChoiceColor.Value ?? fallbackColor;

            location.objects.Add(tile, chest);
            location.playSound("axe");

            ModEntry.Service.EnsureColorSyncWired(chest);

            __result = true;
            return false;
        }
    }

    /// <summary>
    /// Vanilla only grants the larger 70-slot capacity to chests flagged
    /// <see cref="Chest.SpecialChestTypes.BigChest"/>, which we deliberately never set (to avoid inheriting
    /// unrelated BigChest behavior elsewhere) - so this grants the large tier's capacity directly by item ID instead.
    /// </summary>
    internal sealed class ChestCapacityPatcher : BasePatcher
    {
        public override void Apply(Harmony harmony, IMonitor monitor)
        {
            harmony.Patch(
                original: RequireMethod<Chest>(nameof(Chest.GetActualCapacity)),
                postfix: this.GetHarmonyMethod(nameof(Postfix))
            );
        }

        private static void Postfix(Chest __instance, ref int __result)
        {
            if (__instance.ItemId == ModConstants.LargeChestId)
                __result = ModConstants.LargeChestCapacity;
        }
    }

    /// <summary>
    /// Vanilla only renders <see cref="Chest.playerChoiceColor"/> for a hardcoded list of vanilla chest IDs;
    /// everything else is drawn with a plain, uncolored sprite. This replicates that colored-rendering logic
    /// for our chest IDs. Covers world rendering; <see cref="ChestColorMenuDrawPatcher"/> covers the separate
    /// menu/preview draw overload.
    /// </summary>
    internal sealed class ChestColorDrawPatcher : BasePatcher
    {
        private static FieldInfo currentLidFrameField = null!;

        public override void Apply(Harmony harmony, IMonitor monitor)
        {
            currentLidFrameField = RequireField<Chest>("currentLidFrame");

            harmony.Patch(
                original: RequireMethod<Chest>(nameof(Chest.draw), new[] { typeof(SpriteBatch), typeof(int), typeof(int), typeof(float) }),
                prefix: this.GetHarmonyMethod(nameof(Prefix))
            );
        }

        private static bool Prefix(Chest __instance, SpriteBatch spriteBatch, int x, int y, float alpha)
        {
            if (!ModConstants.TryGetTier(__instance.QualifiedItemId, out bool isLarge))
                return true;
            if (!__instance.playerChest.Value)
                return true;

            int currentLidFrame = (int)currentLidFrameField.GetValue(__instance)!;
            int shakeOffset = __instance.shakeTimer > 0 ? Game1.random.Next(-1, 2) : 0;

            float posX = x;
            float posY = y;
            float layerDepth = Math.Max(0f, ((posY + 1f) * 64f - 24f) / 10000f) + posX * 1E-05f;

            ParsedItemData data = ItemRegistry.GetDataOrErrorItem(__instance.QualifiedItemId);
            Texture2D texture = data.GetTexture();

            if (__instance.playerChoiceColor.Value.Equals(Color.Black))
            {
                spriteBatch.Draw(texture, Game1.GlobalToLocal(Game1.viewport, new Vector2(posX * 64f + shakeOffset, (posY - 1f) * 64f)), data.GetSourceRect(), __instance.Tint * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth);
                spriteBatch.Draw(texture, Game1.GlobalToLocal(Game1.viewport, new Vector2(posX * 64f + shakeOffset, (posY - 1f) * 64f)), data.GetSourceRect(0, currentLidFrame), __instance.Tint * alpha * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth + 1E-05f);
                return false;
            }

            ChestSpriteFrames frames = ChestSpriteFrames.Get(data, isLarge, currentLidFrame);

            spriteBatch.Draw(texture, Game1.GlobalToLocal(Game1.viewport, new Vector2(posX * 64f, (posY - 1f) * 64f + shakeOffset)), frames.Base, __instance.playerChoiceColor.Value * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth);
            spriteBatch.Draw(texture, Game1.GlobalToLocal(Game1.viewport, new Vector2(posX * 64f, posY * 64f + 20f)), frames.Latch, Color.White * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth + 2E-05f);
            spriteBatch.Draw(texture, Game1.GlobalToLocal(Game1.viewport, new Vector2(posX * 64f, (posY - 1f) * 64f + shakeOffset)), frames.Open, Color.White * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth + 2E-05f);
            spriteBatch.Draw(texture, Game1.GlobalToLocal(Game1.viewport, new Vector2(posX * 64f, (posY - 1f) * 64f + shakeOffset)), frames.Lit, __instance.playerChoiceColor.Value * alpha * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth + 1E-05f);

            return false;
        }
    }

    /// <summary>
    /// Vanilla shows a translucent "about to place" preview of the currently-selected, not-yet-placed item via
    /// <c>Object.drawPlacementBounds</c> → <c>this.draw(...)</c>. Since it's still a plain <see cref="SObject"/>
    /// at that point, this dispatches to the base <see cref="SObject"/> draw override, not <c>Chest.draw</c> -
    /// a distinct rendering path <see cref="ChestColorDrawPatcher"/> (placed), <see cref="ObjectHeldColorDrawPatcher"/>
    /// (carried overhead), and <see cref="ChestColorInventoryDrawPatcher"/> (backpack icon) don't cover. A real
    /// placed <see cref="Chest"/> never reaches this method (its own override takes over polymorphically), so
    /// there's no overlap with those.
    /// </summary>
    internal sealed class ObjectColorDrawPatcher : BasePatcher
    {
        public override void Apply(Harmony harmony, IMonitor monitor)
        {
            harmony.Patch(
                original: RequireMethod<SObject>(nameof(SObject.draw), new[] { typeof(SpriteBatch), typeof(int), typeof(int), typeof(float) }),
                prefix: this.GetHarmonyMethod(nameof(Prefix))
            );
        }

        private static bool Prefix(SObject __instance, SpriteBatch spriteBatch, int x, int y, float alpha)
        {
            if (!ModConstants.TryGetTier(__instance.QualifiedItemId, out bool isLarge))
                return true;

            // this is always a still-unplaced plain Object (never a real Chest - see remarks above), so it
            // never has a playerChoiceColor of its own to read
            if (!PairColorStorage.TryGetColorForUnplacedItem(__instance, out Color tint))
                return true; // no color to show yet - draw the plain default sprite

            ParsedItemData data = ItemRegistry.GetDataOrErrorItem(__instance.QualifiedItemId);
            Texture2D texture = data.GetTexture();
            ChestSpriteFrames frames = ChestSpriteFrames.Get(data, isLarge, __instance.ParentSheetIndex + 1);

            float layerDepth = Math.Max(0f, ((y + 1) * 64f - 24f) / 10000f) + x * 1E-05f;

            spriteBatch.Draw(texture, Game1.GlobalToLocal(Game1.viewport, new Vector2(x * 64f, (y - 1f) * 64f)), frames.Base, tint * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth);
            spriteBatch.Draw(texture, Game1.GlobalToLocal(Game1.viewport, new Vector2(x * 64f, y * 64f + 20f)), frames.Latch, Color.White * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth + 2E-05f);
            spriteBatch.Draw(texture, Game1.GlobalToLocal(Game1.viewport, new Vector2(x * 64f, (y - 1f) * 64f)), frames.Open, Color.White * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth + 2E-05f);
            spriteBatch.Draw(texture, Game1.GlobalToLocal(Game1.viewport, new Vector2(x * 64f, (y - 1f) * 64f)), frames.Lit, tint * alpha * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth + 1E-05f);

            return false;
        }
    }

    /// <summary>
    /// While a placeable item is selected in the toolbar, vanilla shows it carried above the farmer's head via
    /// <c>Game1.drawPlayerHeldObject</c> → <see cref="SObject.drawWhenHeld"/> - a fourth rendering path, distinct
    /// from placement (<see cref="ObjectColorDrawPatcher"/>), placed (<see cref="ChestColorDrawPatcher"/>), and
    /// backpack icon (<see cref="ChestColorInventoryDrawPatcher"/>), that also never looks at any color.
    /// </summary>
    internal sealed class ObjectHeldColorDrawPatcher : BasePatcher
    {
        public override void Apply(Harmony harmony, IMonitor monitor)
        {
            harmony.Patch(
                original: RequireMethod<SObject>(nameof(SObject.drawWhenHeld)),
                prefix: this.GetHarmonyMethod(nameof(Prefix))
            );
        }

        private static bool Prefix(SObject __instance, SpriteBatch spriteBatch, Vector2 objectPosition, Farmer f)
        {
            if (!ModConstants.TryGetTier(__instance.QualifiedItemId, out bool isLarge))
                return true;

            if (!PairColorStorage.TryGetColorForUnplacedItem(__instance, out Color tint))
                return true; // no color to show yet - draw the plain default sprite

            ParsedItemData data = ItemRegistry.GetDataOrErrorItem(__instance.QualifiedItemId);
            Texture2D texture = data.GetTexture();
            ChestSpriteFrames frames = ChestSpriteFrames.Get(data, isLarge, __instance.ParentSheetIndex + 1);
            float layerDepth = Math.Max(0f, (f.StandingPixel.Y + 3) / 10000f);

            spriteBatch.Draw(texture, objectPosition, frames.Base, tint, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth);
            spriteBatch.Draw(texture, objectPosition, frames.Lit, tint, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth + 1E-05f);
            spriteBatch.Draw(texture, objectPosition, frames.Open, Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth + 2E-05f);

            return false;
        }
    }

    /// <summary>
    /// Same problem as <see cref="ChestColorDrawPatcher"/>, but for the second draw overload vanilla uses for
    /// menu/preview rendering (e.g. the color picker's preview icon) - it also hardcodes sprite offsets per
    /// vanilla ID and falls back to nonsensical offsets for anything else.
    /// </summary>
    internal sealed class ChestColorMenuDrawPatcher : BasePatcher
    {
        private static FieldInfo currentLidFrameField = null!;

        public override void Apply(Harmony harmony, IMonitor monitor)
        {
            currentLidFrameField = RequireField<Chest>("currentLidFrame");

            harmony.Patch(
                original: RequireMethod<Chest>(nameof(Chest.draw), new[] { typeof(SpriteBatch), typeof(int), typeof(int), typeof(float), typeof(bool) }),
                prefix: this.GetHarmonyMethod(nameof(Prefix))
            );
        }

        private static bool Prefix(Chest __instance, SpriteBatch spriteBatch, int x, int y, float alpha, bool local)
        {
            if (!ModConstants.TryGetTier(__instance.QualifiedItemId, out bool isLarge))
                return true;
            if (!__instance.playerChest.Value)
                return true;

            int currentLidFrame = (int)currentLidFrameField.GetValue(__instance)!;
            int shakeOffset = __instance.shakeTimer > 0 ? Game1.random.Next(-1, 2) : 0;

            ParsedItemData data = ItemRegistry.GetDataOrErrorItem(__instance.QualifiedItemId);
            Texture2D texture = data.GetTexture();

            if (__instance.playerChoiceColor.Value.Equals(Color.Black))
            {
                spriteBatch.Draw(texture, local ? new Vector2(x, y - 64) : Game1.GlobalToLocal(Game1.viewport, new Vector2(x * 64 + shakeOffset, (y - 1) * 64)), data.GetSourceRect(), __instance.Tint * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, local ? 0.89f : ((float)(y * 64 + 4) / 10000f));
                return false;
            }

            ChestSpriteFrames frames = ChestSpriteFrames.Get(data, isLarge, currentLidFrame);

            spriteBatch.Draw(texture, local ? new Vector2(x, y - 64) : Game1.GlobalToLocal(Game1.viewport, new Vector2(x * 64, (y - 1) * 64 + shakeOffset)), frames.Base, __instance.playerChoiceColor.Value * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, local ? 0.9f : ((float)(y * 64 + 4) / 10000f));
            spriteBatch.Draw(texture, local ? new Vector2(x, y - 64) : Game1.GlobalToLocal(Game1.viewport, new Vector2(x * 64, (y - 1) * 64 + shakeOffset)), frames.Lit, __instance.playerChoiceColor.Value * alpha * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, local ? 0.9f : ((float)(y * 64 + 5) / 10000f));
            spriteBatch.Draw(texture, local ? new Vector2(x, y + 20) : Game1.GlobalToLocal(Game1.viewport, new Vector2(x * 64, y * 64 + 20)), frames.Latch, Color.White * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, local ? 0.91f : ((float)(y * 64 + 6) / 10000f));
            spriteBatch.Draw(texture, local ? new Vector2(x, y - 64) : Game1.GlobalToLocal(Game1.viewport, new Vector2(x * 64, (y - 1) * 64 + shakeOffset)), frames.Open, Color.White * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, local ? 0.91f : ((float)(y * 64 + 6) / 10000f));

            return false;
        }
    }

    /// <summary>
    /// Vanilla's inventory-slot icon rendering draws a single plain sprite and never looks at
    /// <see cref="Chest.playerChoiceColor"/> at all - even a placed, dyed chest loses its color the moment
    /// it's picked back up. This shows the pair's chosen color on the icon instead, so entangled partners
    /// still sitting in your inventory can be told apart by color before you place them.
    /// </summary>
    internal sealed class ChestColorInventoryDrawPatcher : BasePatcher
    {
        public override void Apply(Harmony harmony, IMonitor monitor)
        {
            harmony.Patch(
                original: RequireMethod<SObject>(nameof(SObject.drawInMenu), new[] { typeof(SpriteBatch), typeof(Vector2), typeof(float), typeof(float), typeof(float), typeof(StackDrawType), typeof(Color), typeof(bool) }),
                prefix: this.GetHarmonyMethod(nameof(Prefix))
            );
        }

        private static bool Prefix(SObject __instance, SpriteBatch spriteBatch, Vector2 location, float scaleSize, float transparency, float layerDepth, StackDrawType drawStackNumber, Color color, bool drawShadow)
        {
            if (!ModConstants.TryGetTier(__instance.QualifiedItemId, out bool isLarge))
                return true;

            Color? chosenColor = GetChosenColor(__instance);
            if (chosenColor is not Color tint)
                return true; // neither this item nor its entangled partner has been colored yet - draw the plain default sprite

            __instance.AdjustMenuDrawForRecipes(ref transparency, ref scaleSize);
            // bigCraftables never draw a menu shadow in vanilla's own drawInMenu either, so drawShadow is intentionally not handled here

            ParsedItemData data = ItemRegistry.GetDataOrErrorItem(__instance.QualifiedItemId);
            Texture2D texture = data.GetTexture();
            float scale = scaleSize > 0.2f ? scaleSize / 2f : scaleSize; // bigCraftables are drawn at half the requested icon scale, same as vanilla

            // same layered recolor technique as ChestColorDrawPatcher, adapted to drawInMenu's centered anchor/scale
            // convention (see ARCHITECTURE.md for why this technique is used instead of a flat tint)
            ChestSpriteFrames frames = ChestSpriteFrames.Get(data, isLarge, __instance.ParentSheetIndex + 1);
            Vector2 origin = new Vector2(frames.Base.Width / 2f, frames.Base.Height / 2f);
            Vector2 position = location + new Vector2(32f, 32f);

            Color blendedTint = new Color(tint.R * color.R / 255, tint.G * color.G / 255, tint.B * color.B / 255, tint.A * color.A / 255);

            spriteBatch.Draw(texture, position, frames.Base, blendedTint * transparency, 0f, origin, 4f * scale, SpriteEffects.None, layerDepth);
            spriteBatch.Draw(texture, position, frames.Lit, blendedTint * transparency, 0f, origin, 4f * scale, SpriteEffects.None, layerDepth + 1E-05f);
            spriteBatch.Draw(texture, position, frames.Open, color * transparency, 0f, origin, 4f * scale, SpriteEffects.None, layerDepth + 2E-05f);

            __instance.DrawMenuIcons(spriteBatch, location, scaleSize, transparency, layerDepth, drawStackNumber, color);

            return false;
        }

        /// <summary>
        /// The color to show for this chest icon: its own chosen color if it's a placed-then-picked-up
        /// <see cref="Chest"/> instance, its entangled partner's color if that partner is placed and dyed, or
        /// otherwise the color randomly assigned to this pair at craft time.
        /// </summary>
        private static Color? GetChosenColor(SObject instance)
        {
            if (instance is Chest chest && chest.playerChoiceColor.Value != Color.Black)
                return chest.playerChoiceColor.Value;

            if (PairColorStorage.TryGetColorForUnplacedItem(instance, out Color color))
                return color;

            return null;
        }
    }

    /// <summary>
    /// Vanilla picks up an emptied chest by discarding the actual item instance and reconstructing a brand new
    /// one from just its item ID - silently stripping our pair ID the moment a quantum chest is emptied and
    /// retrieved. Preserves identity by handing off the real instance instead.
    /// </summary>
    internal sealed class ChestHitPreservePairPatcher : BasePatcher
    {
        private static FieldInfo chestHitField = null!;

        public override void Apply(Harmony harmony, IMonitor monitor)
        {
            chestHitField = RequireField<FarmerTeam>("chestHit");

            harmony.Patch(
                original: RequireMethod<Chest>(nameof(Chest.HandleChestHit)),
                prefix: this.GetHarmonyMethod(nameof(Prefix))
            );
        }

        private static bool Prefix(Chest __instance, ChestHitArgs args)
        {
            if (!ModConstants.TryGetTier(__instance.QualifiedItemId, out _))
                return true;
            if (!__instance.isEmpty())
                return true; // not empty: let vanilla's normal "kick the chest" behavior run unmodified

            __instance.GetMutex().RequestLock(delegate
            {
                __instance.clearNulls();
                if (!__instance.isEmpty())
                    return; // became non-empty while waiting for the lock

                GameLocation location = args.Location;
                Vector2 tile = Utility.PointToVector2(args.ChestTile);

                __instance.performRemoveAction();
                if (location.objects.Remove(tile) && __instance.Type == "Crafting" && __instance.Fragility != 2)
                {
                    location.debris.Add(new Debris(__instance, args.ToolPosition, Utility.PointToVector2(args.StandingPixel)));
                }

                var chestHit = (ChestHitSynchronizer)chestHitField.GetValue(Game1.player.team)!;
                chestHit.SignalDelete(location, args.ChestTile.X, args.ChestTile.Y);

                __instance.GetMutex().ReleaseLock();
            });

            return false;
        }
    }

    /// <summary>
    /// Stamps a fresh, shared pair ID into <c>modData</c> the moment a chest pair is crafted, so the two physical
    /// chests produced by one recipe craft are recognized as entangled partners. Also assigns a random color
    /// from vanilla's own predefined chest palette, so a freshly-crafted pair is already visually distinct from
    /// other pairs before either half is ever placed or dyed.
    /// </summary>
    internal sealed class CraftingRecipePairIdPatcher : BasePatcher
    {
        public override void Apply(Harmony harmony, IMonitor monitor)
        {
            harmony.Patch(
                original: RequireMethod<CraftingRecipe>(nameof(CraftingRecipe.createItem)),
                postfix: this.GetHarmonyMethod(nameof(Postfix))
            );
        }

        private static void Postfix(Item __result)
        {
            if (__result == null)
                return;
            if (!ModConstants.TryGetTier(__result.QualifiedItemId, out _))
                return;
            if (__result.modData.ContainsKey(ModConstants.PairIdKey))
                return;

            __result.modData[ModConstants.PairIdKey] = Guid.NewGuid().ToString("N");

            // selection 0 is Color.Black ("no color"), so only pick among the 20 real predefined swatches
            int selection = Game1.random.Next(1, DiscreteColorPicker.totalColors);
            PairColorStorage.Store(__result.modData, DiscreteColorPicker.getColorFromSelection(selection));
        }
    }

    /// <summary>
    /// Vanilla stacking only compares item ID/name/quality, so two different pairs would otherwise merge into
    /// one stack the moment they share an item ID - corrupting both pairs. Forces our chest items to stack
    /// only when they share the exact same pair ID - and forces them <em>to</em> stack when they do, even if
    /// vanilla's own check disagrees: vanilla requires both items to be the exact same .NET type, but a
    /// placed-then-collected chest is a real <see cref="Chest"/> instance while a never-placed one is still a
    /// plain <see cref="SObject"/>. See ARCHITECTURE.md for why both directions are necessary.
    /// </summary>
    internal sealed class ChestPairStackPatcher : BasePatcher
    {
        public override void Apply(Harmony harmony, IMonitor monitor)
        {
            harmony.Patch(
                original: RequireMethod<Item>(nameof(Item.canStackWith)),
                postfix: this.GetHarmonyMethod(nameof(Postfix))
            );
        }

        private static void Postfix(Item __instance, ISalable other, ref bool __result)
        {
            if (!ModConstants.TryGetTier(__instance.QualifiedItemId, out _))
                return;
            if (other is not Item otherItem || otherItem.QualifiedItemId != __instance.QualifiedItemId)
                return;

            __instance.modData.TryGetValue(ModConstants.PairIdKey, out string? pairIdA);
            otherItem.modData.TryGetValue(ModConstants.PairIdKey, out string? pairIdB);

            __result = !string.IsNullOrEmpty(pairIdA) && pairIdA == pairIdB;
        }
    }

    /// <summary>
    /// Every inventory menu's trash can (<c>MenuWithInventory</c>, <c>InventoryPage</c>, <c>CraftingPage</c>,
    /// <c>JunimoNoteMenu</c>) discards its held item by calling <see cref="Utility.trashItem"/> directly - which
    /// never touches <see cref="Farmer.Items"/> or <see cref="GameLocation.objects"/>, so neither event
    /// <see cref="QuantumChestService"/> listens for (<c>Player.InventoryChanged</c>, <c>World.ObjectListChanged</c>)
    /// ever fires. Without this patch, trashing one half of a pair leaves its partner behind until some unrelated
    /// event (e.g. picking the partner up) happens to trigger a rescan.
    /// </summary>
    internal sealed class TrashCanDestructionPatcher : BasePatcher
    {
        public override void Apply(Harmony harmony, IMonitor monitor)
        {
            harmony.Patch(
                original: RequireMethod<Utility>(nameof(Utility.trashItem)),
                postfix: this.GetHarmonyMethod(nameof(Postfix))
            );
        }

        private static void Postfix(Item item)
        {
            if (item.modData.TryGetValue(ModConstants.PairIdKey, out string? pairId) && !string.IsNullOrEmpty(pairId))
                ModEntry.Service.HandlePotentialDestruction(pairId);
        }
    }
}
