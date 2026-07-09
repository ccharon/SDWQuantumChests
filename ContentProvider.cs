using System.Collections.Generic;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley.GameData.BigCraftables;

namespace QuantumChests
{
    /// <summary>
    /// Adds the two chest tiers' <c>Data/BigCraftables</c> and <c>Data/CraftingRecipes</c> entries, and
    /// re-applies them whenever the game's language changes so their translated text stays in sync.
    /// </summary>
    internal sealed class ContentProvider
    {
        private readonly IModHelper helper;

        public ContentProvider(IModHelper helper)
        {
            this.helper = helper;
        }

        public void RegisterEvents(IModEvents events)
        {
            events.Content.AssetRequested += this.OnAssetRequested;
            events.Content.LocaleChanged += this.OnLocaleChanged;
        }

        /// <summary>Force the translated text baked into <see cref="OnAssetRequested"/> to be regenerated for the new language (see ARCHITECTURE.md for why this doesn't happen on its own).</summary>
        private void OnLocaleChanged(object? sender, LocaleChangedEventArgs e)
        {
            this.helper.GameContent.InvalidateCache("Data/BigCraftables");
            this.helper.GameContent.InvalidateCache("Data/CraftingRecipes");
        }

        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.IsEquivalentTo("Data/BigCraftables"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, BigCraftableData>().Data;

                    data[ModConstants.ChestId] = this.CreateChestData(ModConstants.ChestId, I18n.Chest_Name(), I18n.Chest_Description(), price: 2000, spriteIndex: 130);
                    data[ModConstants.LargeChestId] = this.CreateChestData(ModConstants.LargeChestId, I18n.Bigchest_Name(), I18n.Bigchest_Description(), price: 4000, spriteIndex: 304);
                });
            }
            else if (e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes"))
            {
                e.Edit(asset =>
                {
                    IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;

                    // "(BC)130" must stay qualified: unqualified "130" would resolve to the Object type first (Tuna),
                    // never reaching the BigCraftable Chest sharing that same numeric ID
                    data[ModConstants.ChestId] = this.CreateRecipeData(ModConstants.ChestId, "337 2 787 2 (BC)130 2", I18n.Chest_Name());
                    data[ModConstants.LargeChestId] = this.CreateRecipeData(ModConstants.LargeChestId, "337 4 787 2 BigChest 2", I18n.Bigchest_Name());
                });
            }
        }

        /// <summary>Build a <c>Data/BigCraftables</c> entry for one of our chest tiers.</summary>
        /// <param name="itemId">The unqualified item ID.</param>
        /// <param name="displayName">The translated display name.</param>
        /// <param name="description">The translated description.</param>
        /// <param name="price">The purchase/sell price.</param>
        /// <param name="spriteIndex">The sprite index into vanilla's <c>TileSheets/Craftables</c>.</param>
        private BigCraftableData CreateChestData(string itemId, string displayName, string description, int price, int spriteIndex)
        {
            return new BigCraftableData
            {
                Name = itemId,
                DisplayName = displayName,
                Description = description,
                Price = price,
                Fragility = 0,
                CanBePlacedOutdoors = true,
                CanBePlacedIndoors = true,
                IsLamp = false,
                Texture = null, // vanilla TileSheets/Craftables - looks identical to the vanilla chest tier it shares a sprite with
                SpriteIndex = spriteIndex,
                ContextTags = new List<string> { "automate_storage" }, // Automate only auto-tags its own hardcoded vanilla chest IDs, not ours
            };
        }

        /// <summary>Build a <c>Data/CraftingRecipes</c> entry for one of our chest tiers.</summary>
        /// <param name="itemId">The unqualified item ID being crafted.</param>
        /// <param name="ingredients">The recipe's ingredient list, in vanilla's <c>"id count id count..."</c> format.</param>
        /// <param name="displayName">The translated display name for the recipe.</param>
        private string CreateRecipeData(string itemId, string ingredients, string displayName)
        {
            return $"{ingredients}/Home/{itemId} 2/true/null/{displayName}";
        }
    }
}
