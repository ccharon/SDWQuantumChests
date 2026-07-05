using Microsoft.Xna.Framework;
using StardewValley.ItemTypeDefinitions;

namespace QuantumChests
{
    /// <summary>The four sprite-sheet frames needed to draw a chest with vanilla's layered dye technique (see ARCHITECTURE.md): a recolored base, a neutral trim overlay, a tinted highlight, and a small latch highlight.</summary>
    internal readonly struct ChestSpriteFrames
    {
        public Rectangle Base { get; }
        public Rectangle Open { get; }
        public Rectangle Lit { get; }
        public Rectangle Latch { get; }

        private ChestSpriteFrames(Rectangle @base, Rectangle open, Rectangle lit, Rectangle latch)
        {
            this.Base = @base;
            this.Open = open;
            this.Lit = lit;
            this.Latch = latch;
        }

        /// <summary>Get the frames for a chest tier at a given lid frame.</summary>
        /// <param name="data">The chest's sprite data.</param>
        /// <param name="isLarge">Whether this is the large tier.</param>
        /// <param name="lidFrame">The current lid animation frame: a real, placed <see cref="StardewValley.Objects.Chest"/>'s own <c>currentLidFrame</c> field, or <c>ParentSheetIndex + 1</c> - see <see cref="StardewValley.Objects.Chest.startingLidFrame"/> - for a still-unplaced item with no animation state of its own.</param>
        public static ChestSpriteFrames Get(ParsedItemData data, bool isLarge, int lidFrame)
        {
            int baseIndex = isLarge ? 312 : 168;
            int openIndex = isLarge ? lidFrame + 16 : lidFrame + 46;
            int litIndex = isLarge ? lidFrame + 8 : lidFrame + 38;

            return new ChestSpriteFrames(
                @base: data.GetSourceRect(0, baseIndex),
                open: data.GetSourceRect(0, openIndex),
                lit: data.GetSourceRect(0, litIndex),
                latch: new Rectangle(0, baseIndex / 8 * 32 + 53, 16, 11)
            );
        }
    }
}
