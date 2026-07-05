using Microsoft.Xna.Framework;

namespace QuantumChests
{
    /// <summary>Shared item IDs, mod-data keys, and visual constants used across the mod.</summary>
    internal static class ModConstants
    {
        public const string ModId = "Nukulartechniker.QuantumChests";

        public const string ChestId = ModId + "_Chest";
        public const string LargeChestId = ModId + "_BigChest";

        public const string QualifiedChestId = "(BC)" + ChestId;
        public const string QualifiedLargeChestId = "(BC)" + LargeChestId;

        public const string PairIdKey = ModId + "/PairId";

        /// <summary>Mod-data key storing the random predefined color assigned to a pair at craft time, so both
        /// halves already agree on a color before either one is ever placed.</summary>
        public const string PairColorKey = ModId + "/PairColor";

        public const int LargeChestCapacity = 70;

        /// <summary>
        /// Default player-choice color for a freshly placed chest, so it's visibly distinct from a plain wood chest
        /// even before the owner picks a custom color.
        /// </summary>
        public static readonly Color DefaultTint = new Color(147, 112, 219);

        /// <summary>Check whether a qualified item ID is one of our two chest tiers.</summary>
        /// <param name="qualifiedItemId">The qualified item ID to check.</param>
        /// <param name="isLarge">Whether it's the large tier, if it's one of ours.</param>
        public static bool TryGetTier(string qualifiedItemId, out bool isLarge)
        {
            if (qualifiedItemId == QualifiedChestId)
            {
                isLarge = false;
                return true;
            }

            if (qualifiedItemId == QualifiedLargeChestId)
            {
                isLarge = true;
                return true;
            }

            isLarge = false;
            return false;
        }
    }
}
