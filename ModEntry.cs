using System.Linq;
using QuantumChests.Patching;
using StardewModdingAPI;

namespace QuantumChests
{
    /// <summary>Entry point: wires up the chest-tracking service, content edits, and Harmony patches.</summary>
    internal sealed class ModEntry : Mod
    {
        internal static QuantumChestService Service { get; private set; } = null!;

        public override void Entry(IModHelper helper)
        {
            I18n.Init(helper.Translation);

            Service = new QuantumChestService(this.Monitor, helper.Reflection, helper.Multiplayer);
            Service.RegisterEvents(helper.Events);

            new ContentProvider(helper).RegisterEvents(helper.Events);

            // each patcher is applied independently - if one fails (e.g. a future game update changes
            // a method it targets), that one feature logs an error and degrades gracefully instead of
            // taking down every other patch (and thus the whole mod) with it
            HarmonyPatcher.Apply(this,
                new ObjectPlacementPatcher(),
                new ChestCapacityPatcher(),
                new ChestColorDrawPatcher(),
                new ObjectColorDrawPatcher(),
                new ObjectHeldColorDrawPatcher(),
                new ChestColorMenuDrawPatcher(),
                new ChestColorInventoryDrawPatcher(),
                new ChestHitPreservePairPatcher(),
                new CraftingRecipePairIdPatcher(),
                new ChestPairStackPatcher(),
                new TrashCanDestructionPatcher()
            );

            if (!helper.Translation.GetTranslations().Any())
                this.Monitor.Log("The translation files in this mod's i18n folder seem to be missing. The mod will still work, but you'll see 'missing translation' messages. Try reinstalling the mod to fix this.", LogLevel.Warn);

            this.Monitor.Log("Quantum Chests loaded.", LogLevel.Debug);
        }
    }
}
