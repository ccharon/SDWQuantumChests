using HarmonyLib;
using StardewModdingAPI;

namespace QuantumChests
{
    internal sealed class ModEntry : Mod
    {
        internal static QuantumChestService Service { get; private set; } = null!;

        public override void Entry(IModHelper helper)
        {
            Service = new QuantumChestService(this.Monitor, helper.Translation, helper.Reflection);
            Service.RegisterEvents(helper.Events);

            new ContentProvider(helper).RegisterEvents(helper.Events);

            Harmony harmony = new Harmony(this.ModManifest.UniqueID);
            harmony.PatchAll(typeof(ModEntry).Assembly);

            this.Monitor.Log("Quantum Chests loaded.", LogLevel.Debug);
        }
    }
}
