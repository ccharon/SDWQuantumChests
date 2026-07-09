using System.Diagnostics.CodeAnalysis;
using StardewValley;

namespace QuantumChests
{
    /// <summary>Extension methods for reading this mod's data from game items.</summary>
    internal static class ItemExtensions
    {
        /// <summary>Get the entangled-pair ID from an item's mod data, if it has a non-empty one.</summary>
        /// <param name="item">The item to check.</param>
        /// <param name="pairId">The pair ID shared by both halves of the entangled pair, or <c>null</c> if the item isn't half of a pair.</param>
        public static bool TryGetPairId(this Item item, [NotNullWhen(true)] out string? pairId)
        {
            if (item.modData.TryGetValue(ModConstants.PairIdKey, out pairId) && !string.IsNullOrEmpty(pairId))
                return true;

            pairId = null;
            return false;
        }
    }
}
