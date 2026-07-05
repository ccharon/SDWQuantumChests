using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Mods;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace QuantumChests
{
    /// <summary>Reads and writes the random color assigned to a pair at craft time (see <see cref="ModConstants.PairColorKey"/>).</summary>
    internal static class PairColorStorage
    {
        /// <summary>Store a color under <see cref="ModConstants.PairColorKey"/>.</summary>
        public static void Store(ModDataDictionary modData, Color color)
        {
            modData[ModConstants.PairColorKey] = color.PackedValue.ToString();
        }

        /// <summary>Try to read the color previously stored via <see cref="Store"/>.</summary>
        public static bool TryGet(ModDataDictionary modData, out Color color)
        {
            if (modData.TryGetValue(ModConstants.PairColorKey, out string? value) && uint.TryParse(value, out uint packed))
            {
                color = new Color { PackedValue = packed };
                return true;
            }

            color = default;
            return false;
        }

        /// <summary>The color to show for a still-unplaced item of a pair (carried, previewed, or sitting in
        /// inventory): an already-placed, already-dyed partner's color if one exists, otherwise the color
        /// randomly assigned to this pair at craft time.</summary>
        public static bool TryGetColorForUnplacedItem(SObject instance, out Color color)
        {
            if (instance.modData.TryGetValue(ModConstants.PairIdKey, out string? pairId) && !string.IsNullOrEmpty(pairId))
            {
                Chest? partner = ModEntry.Service.FindChestByPairId(pairId, excluding: instance as Chest);
                if (partner != null && !partner.playerChoiceColor.Value.Equals(Color.Black))
                {
                    color = partner.playerChoiceColor.Value;
                    return true;
                }

                if (TryGet(instance.modData, out color))
                    return true;
            }

            color = default;
            return false;
        }
    }
}
