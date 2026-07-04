using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Extensions;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Network.ChestHit;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace QuantumChests
{
    [HarmonyPatch(typeof(SObject), nameof(SObject.placementAction))]
    internal static class ObjectPlacementPatch
    {
        private static bool Prefix(SObject __instance, GameLocation location, int x, int y, Farmer? who, ref bool __result)
        {
            if (__instance.QualifiedItemId != ModConstants.QualifiedChestId && __instance.QualifiedItemId != ModConstants.QualifiedLargeChestId)
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
            chest.playerChoiceColor.Value = existingPartner?.playerChoiceColor.Value ?? ModConstants.DefaultTint;

            chest.lightSource = new LightSource(
                chest.GenerateLightSourceId(tile),
                4,
                new Vector2(tile.X * 64f + 32f, tile.Y * 64f),
                1f,
                ModConstants.GlowColor,
                LightSource.LightContext.None,
                0L,
                location.NameOrUniqueName);
            location.sharedLights.AddLight(chest.lightSource);

            location.objects.Add(tile, chest);
            location.playSound("axe");

            ModEntry.Service.EnsureColorSyncWired(chest);

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Chest), nameof(Chest.GetActualCapacity))]
    internal static class ChestCapacityPatch
    {
        private static void Postfix(Chest __instance, ref int __result)
        {
            if (__instance.ItemId == ModConstants.LargeChestId)
                __result = ModConstants.LargeChestCapacity;
        }
    }

    /// <summary>Vanilla only renders <see cref="Chest.playerChoiceColor"/> for a hardcoded list of vanilla chest IDs; everything else is drawn with a plain, uncolored sprite. This replicates that same colored-rendering logic for our chest IDs.</summary>
    [HarmonyPatch(typeof(Chest), nameof(Chest.draw), new[] { typeof(SpriteBatch), typeof(int), typeof(int), typeof(float) })]
    internal static class ChestColorDrawPatch
    {
        private static readonly FieldInfo CurrentLidFrameField = AccessTools.Field(typeof(Chest), "currentLidFrame");

        private static bool Prefix(Chest __instance, SpriteBatch spriteBatch, int x, int y, float alpha)
        {
            string qualifiedId = __instance.QualifiedItemId;
            bool isLarge = qualifiedId == ModConstants.QualifiedLargeChestId;
            if (!isLarge && qualifiedId != ModConstants.QualifiedChestId)
                return true;
            if (!__instance.playerChest.Value)
                return true;

            int currentLidFrame = (int)CurrentLidFrameField.GetValue(__instance)!;
            int shakeOffset = __instance.shakeTimer > 0 ? Game1.random.Next(-1, 2) : 0;

            float posX = x;
            float posY = y;
            float layerDepth = Math.Max(0f, ((posY + 1f) * 64f - 24f) / 10000f) + posX * 1E-05f;

            ParsedItemData data = ItemRegistry.GetDataOrErrorItem(qualifiedId);
            Texture2D texture = data.GetTexture();

            if (__instance.playerChoiceColor.Value.Equals(Color.Black))
            {
                spriteBatch.Draw(texture, Game1.GlobalToLocal(Game1.viewport, new Vector2(posX * 64f + shakeOffset, (posY - 1f) * 64f)), data.GetSourceRect(), __instance.Tint * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth);
                spriteBatch.Draw(texture, Game1.GlobalToLocal(Game1.viewport, new Vector2(posX * 64f + shakeOffset, (posY - 1f) * 64f)), data.GetSourceRect(0, currentLidFrame), __instance.Tint * alpha * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth + 1E-05f);
                return false;
            }

            int baseIndex = isLarge ? 312 : 168;
            int openIndex = isLarge ? currentLidFrame + 16 : currentLidFrame + 46;
            int litIndex = isLarge ? currentLidFrame + 8 : currentLidFrame + 38;

            Rectangle baseRect = data.GetSourceRect(0, baseIndex);
            Rectangle openRect = data.GetSourceRect(0, openIndex);
            Rectangle litRect = data.GetSourceRect(0, litIndex);

            spriteBatch.Draw(texture, Game1.GlobalToLocal(Game1.viewport, new Vector2(posX * 64f, (posY - 1f) * 64f + shakeOffset)), baseRect, __instance.playerChoiceColor.Value * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth);
            spriteBatch.Draw(texture, Game1.GlobalToLocal(Game1.viewport, new Vector2(posX * 64f, posY * 64f + 20f)), new Rectangle(0, baseIndex / 8 * 32 + 53, 16, 11), Color.White * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth + 2E-05f);
            spriteBatch.Draw(texture, Game1.GlobalToLocal(Game1.viewport, new Vector2(posX * 64f, (posY - 1f) * 64f + shakeOffset)), openRect, Color.White * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth + 2E-05f);
            spriteBatch.Draw(texture, Game1.GlobalToLocal(Game1.viewport, new Vector2(posX * 64f, (posY - 1f) * 64f + shakeOffset)), litRect, __instance.playerChoiceColor.Value * alpha * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth + 1E-05f);

            return false;
        }
    }

    /// <summary>Same problem as <see cref="ChestColorDrawPatch"/>, but for the second draw overload vanilla uses for menu/preview rendering (e.g. the color picker's preview icon) - it also hardcodes sprite offsets per vanilla ID and falls back to nonsensical offsets for anything else.</summary>
    [HarmonyPatch(typeof(Chest), nameof(Chest.draw), new[] { typeof(SpriteBatch), typeof(int), typeof(int), typeof(float), typeof(bool) })]
    internal static class ChestColorMenuDrawPatch
    {
        private static readonly FieldInfo CurrentLidFrameField = AccessTools.Field(typeof(Chest), "currentLidFrame");

        private static bool Prefix(Chest __instance, SpriteBatch spriteBatch, int x, int y, float alpha, bool local)
        {
            string qualifiedId = __instance.QualifiedItemId;
            bool isLarge = qualifiedId == ModConstants.QualifiedLargeChestId;
            if (!isLarge && qualifiedId != ModConstants.QualifiedChestId)
                return true;
            if (!__instance.playerChest.Value)
                return true;

            int currentLidFrame = (int)CurrentLidFrameField.GetValue(__instance)!;
            int shakeOffset = __instance.shakeTimer > 0 ? Game1.random.Next(-1, 2) : 0;

            ParsedItemData data = ItemRegistry.GetDataOrErrorItem(qualifiedId);
            Texture2D texture = data.GetTexture();

            if (__instance.playerChoiceColor.Value.Equals(Color.Black))
            {
                spriteBatch.Draw(texture, local ? new Vector2(x, y - 64) : Game1.GlobalToLocal(Game1.viewport, new Vector2(x * 64 + shakeOffset, (y - 1) * 64)), data.GetSourceRect(), __instance.Tint * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, local ? 0.89f : ((float)(y * 64 + 4) / 10000f));
                return false;
            }

            int baseIndex = isLarge ? 312 : 168;
            int openIndex = isLarge ? currentLidFrame + 16 : currentLidFrame + 46;
            int litIndex = isLarge ? currentLidFrame + 8 : currentLidFrame + 38;

            Rectangle baseRect = data.GetSourceRect(0, baseIndex);
            Rectangle openRect = data.GetSourceRect(0, openIndex);
            Rectangle litRect = data.GetSourceRect(0, litIndex);

            spriteBatch.Draw(texture, local ? new Vector2(x, y - 64) : Game1.GlobalToLocal(Game1.viewport, new Vector2(x * 64, (y - 1) * 64 + shakeOffset)), baseRect, __instance.playerChoiceColor.Value * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, local ? 0.9f : ((float)(y * 64 + 4) / 10000f));
            spriteBatch.Draw(texture, local ? new Vector2(x, y - 64) : Game1.GlobalToLocal(Game1.viewport, new Vector2(x * 64, (y - 1) * 64 + shakeOffset)), litRect, __instance.playerChoiceColor.Value * alpha * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, local ? 0.9f : ((float)(y * 64 + 5) / 10000f));
            spriteBatch.Draw(texture, local ? new Vector2(x, y + 20) : Game1.GlobalToLocal(Game1.viewport, new Vector2(x * 64, y * 64 + 20)), new Rectangle(0, baseIndex / 8 * 32 + 53, 16, 11), Color.White * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, local ? 0.91f : ((float)(y * 64 + 6) / 10000f));
            spriteBatch.Draw(texture, local ? new Vector2(x, y - 64) : Game1.GlobalToLocal(Game1.viewport, new Vector2(x * 64, (y - 1) * 64 + shakeOffset)), openRect, Color.White * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, local ? 0.91f : ((float)(y * 64 + 6) / 10000f));

            return false;
        }
    }

    /// <summary>Vanilla's inventory-slot icon rendering (<see cref="SObject.drawInMenu"/>) draws a single plain sprite and never looks at <see cref="Chest.playerChoiceColor"/> at all - even a placed, dyed chest loses its color the moment it's picked back up. This shows the pair's chosen color on the icon instead, so entangled partners still sitting in your inventory can be told apart by color before you place them.</summary>
    [HarmonyPatch(typeof(SObject), nameof(SObject.drawInMenu), new[] { typeof(SpriteBatch), typeof(Vector2), typeof(float), typeof(float), typeof(float), typeof(StackDrawType), typeof(Color), typeof(bool) })]
    internal static class ChestColorInventoryDrawPatch
    {
        private static bool Prefix(SObject __instance, SpriteBatch spriteBatch, Vector2 location, float scaleSize, float transparency, float layerDepth, StackDrawType drawStackNumber, Color color, bool drawShadow)
        {
            string qualifiedId = __instance.QualifiedItemId;
            if (qualifiedId != ModConstants.QualifiedChestId && qualifiedId != ModConstants.QualifiedLargeChestId)
                return true;

            Color? chosenColor = GetChosenColor(__instance);
            if (chosenColor is not Color tint)
                return true; // neither this item nor its entangled partner has been colored yet - draw the plain default sprite

            __instance.AdjustMenuDrawForRecipes(ref transparency, ref scaleSize);
            // bigCraftables never draw a menu shadow in vanilla's own drawInMenu either, so drawShadow is intentionally not handled here

            ParsedItemData data = ItemRegistry.GetDataOrErrorItem(qualifiedId);
            float scale = scaleSize > 0.2f ? scaleSize / 2f : scaleSize; // bigCraftables are drawn at half the requested icon scale, same as vanilla

            // reuse the exact same source rect vanilla's own drawInMenu uses for the plain icon (rather
            // than the separate "recolorable" frames the world/menu-preview draw patches use, which are
            // sized/positioned for a taller world-tile composite, not a single icon-shaped crop) and just
            // tint it - this guarantees the icon's position and size always matches vanilla exactly.
            Rectangle sourceRect = data.GetSourceRect(0, __instance.ParentSheetIndex);
            Color blended = new Color(tint.R * color.R / 255, tint.G * color.G / 255, tint.B * color.B / 255, tint.A * color.A / 255);
            spriteBatch.Draw(data.GetTexture(), location + new Vector2(32f, 32f), sourceRect, blended * transparency, 0f, new Vector2(sourceRect.Width / 2f, sourceRect.Height / 2f), 4f * scale, SpriteEffects.None, layerDepth);

            __instance.DrawMenuIcons(spriteBatch, location, scaleSize, transparency, layerDepth, drawStackNumber, color);

            return false;
        }

        /// <summary>The color to show for this chest icon: its own chosen color if it's a placed-then-picked-up <see cref="Chest"/> instance, or its entangled partner's color if this is still a plain, never-placed item.</summary>
        private static Color? GetChosenColor(SObject instance)
        {
            if (instance is Chest chest && chest.playerChoiceColor.Value != Color.Black)
                return chest.playerChoiceColor.Value;

            if (!instance.modData.TryGetValue(ModConstants.PairIdKey, out string? pairId) || string.IsNullOrEmpty(pairId))
                return null;

            Chest? partner = ModEntry.Service.FindChestByPairId(pairId, excluding: instance as Chest);
            if (partner != null && partner.playerChoiceColor.Value != Color.Black)
                return partner.playerChoiceColor.Value;

            return null;
        }
    }

    /// <summary>Vanilla picks up an emptied chest by discarding the actual item instance and reconstructing a brand new one from just its item ID - which silently strips our pair ID (and, by extension, breaks pairing) the moment a quantum chest is emptied and retrieved. Preserve identity by handing off the real instance instead.</summary>
    [HarmonyPatch(typeof(Chest), nameof(Chest.HandleChestHit))]
    internal static class ChestHitPreservePairPatch
    {
        private static readonly FieldInfo ChestHitField = AccessTools.Field(typeof(FarmerTeam), "chestHit");

        private static bool Prefix(Chest __instance, ChestHitArgs args)
        {
            string qualifiedId = __instance.QualifiedItemId;
            if (qualifiedId != ModConstants.QualifiedChestId && qualifiedId != ModConstants.QualifiedLargeChestId)
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

                var chestHit = (ChestHitSynchronizer)ChestHitField.GetValue(Game1.player.team)!;
                chestHit.SignalDelete(location, args.ChestTile.X, args.ChestTile.Y);

                __instance.GetMutex().ReleaseLock();
            });

            return false;
        }
    }

    [HarmonyPatch(typeof(CraftingRecipe), nameof(CraftingRecipe.createItem))]
    internal static class CraftingRecipePairIdPatch
    {
        private static void Postfix(Item __result)
        {
            if (__result == null)
                return;
            if (__result.QualifiedItemId != ModConstants.QualifiedChestId && __result.QualifiedItemId != ModConstants.QualifiedLargeChestId)
                return;
            if (__result.modData.ContainsKey(ModConstants.PairIdKey))
                return;

            __result.modData[ModConstants.PairIdKey] = Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>Vanilla stacking only compares item ID/name/quality, so two different pairs would otherwise merge into one stack the moment they share an item ID - corrupting both pairs. Only let our chest items stack when they share the exact same pair ID.</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.canStackWith))]
    internal static class ChestPairStackPatch
    {
        private static void Postfix(Item __instance, ISalable other, ref bool __result)
        {
            if (!__result)
                return;
            if (__instance.QualifiedItemId != ModConstants.QualifiedChestId && __instance.QualifiedItemId != ModConstants.QualifiedLargeChestId)
                return;
            if (other is not Item otherItem)
                return;

            __instance.modData.TryGetValue(ModConstants.PairIdKey, out string? pairIdA);
            otherItem.modData.TryGetValue(ModConstants.PairIdKey, out string? pairIdB);

            if (pairIdA != pairIdB)
                __result = false;
        }
    }
}
