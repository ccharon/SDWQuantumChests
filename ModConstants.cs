using Microsoft.Xna.Framework;

namespace QuantumChests
{
    internal static class ModConstants
    {
        public const string ModId = "Nukulartechniker.QuantumChests";

        public const string ChestId = ModId + "_Chest";
        public const string LargeChestId = ModId + "_BigChest";

        public const string QualifiedChestId = "(BC)" + ChestId;
        public const string QualifiedLargeChestId = "(BC)" + LargeChestId;

        public const string PairIdKey = ModId + "/PairId";

        public const int LargeChestCapacity = 70;

        public static readonly Color GlowColor = new Color(140, 90, 220) * 0.85f;

        /// <summary>Default player-choice color for a freshly placed chest, so it's visibly distinct from a plain wood chest even before the owner picks a custom color.</summary>
        public static readonly Color DefaultTint = new Color(147, 112, 219);
    }
}
