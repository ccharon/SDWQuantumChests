using System.Collections.Generic;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley.GameData.BigCraftables;

namespace QuantumChests
{
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

        /// <summary>The translated text baked into <see cref="OnAssetRequested"/> is resolved once, whenever the asset is first loaded - switching languages afterward doesn't get SMAPI to re-run that edit on its own (it only unloads its own vanilla asset cache), so the data would otherwise stay stuck in whatever language was active at that first load. Force it to be regenerated for the new language.</summary>
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

                    data[ModConstants.ChestId] = new BigCraftableData
                    {
                        Name = ModConstants.ChestId,
                        DisplayName = this.helper.Translation.Get("chest.name"),
                        Description = this.helper.Translation.Get("chest.description"),
                        Price = 2000,
                        Fragility = 0,
                        CanBePlacedOutdoors = true,
                        CanBePlacedIndoors = true,
                        IsLamp = false,
                        Texture = null, // vanilla TileSheets/Craftables - looks identical to a regular chest; the glow marks it as quantum
                        SpriteIndex = 130, // same sprite as the vanilla Chest
                        ContextTags = new List<string> { "automate_storage" }, // Automate only auto-tags its own hardcoded vanilla chest IDs, not ours
                    };

                    data[ModConstants.LargeChestId] = new BigCraftableData
                    {
                        Name = ModConstants.LargeChestId,
                        DisplayName = this.helper.Translation.Get("bigchest.name"),
                        Description = this.helper.Translation.Get("bigchest.description"),
                        Price = 4000,
                        Fragility = 0,
                        CanBePlacedOutdoors = true,
                        CanBePlacedIndoors = true,
                        IsLamp = false,
                        Texture = null, // vanilla TileSheets/Craftables - looks identical to a regular Big Chest
                        SpriteIndex = 304, // same sprite as the vanilla Big Chest
                        ContextTags = new List<string> { "automate_storage" }, // Automate only auto-tags its own hardcoded vanilla chest IDs, not ours
                    };
                });
            }
            else if (e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes"))
            {
                e.Edit(asset =>
                {
                    IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;

                    data[ModConstants.ChestId] =
                        $"337 2 787 2/Home/{ModConstants.ChestId} 2/true/null/{this.helper.Translation.Get("chest.name")}";

                    data[ModConstants.LargeChestId] =
                        $"337 4 787 3/Home/{ModConstants.LargeChestId} 2/true/null/{this.helper.Translation.Get("bigchest.name")}";
                });
            }
        }
    }
}
