using System;
using HarmonyLib;
using StardewModdingAPI;

namespace QuantumChests.Patching
{
    /// <summary>
    /// Applies a set of <see cref="IPatcher"/> instances, isolating failures so a single broken patch (e.g. after
    /// a game update changes a method this mod relies on) logs a clear error and disables just that feature,
    /// instead of taking down the whole mod.
    /// </summary>
    internal static class HarmonyPatcher
    {
        /// <summary>Apply the given patchers.</summary>
        /// <param name="mod">The mod applying the patchers.</param>
        /// <param name="patchers">The patchers to apply.</param>
        public static Harmony Apply(Mod mod, params IPatcher[] patchers)
        {
            Harmony harmony = new(mod.ModManifest.UniqueID);

            foreach (IPatcher patcher in patchers)
            {
                try
                {
                    patcher.Apply(harmony, mod.Monitor);
                }
                catch (Exception ex)
                {
                    mod.Monitor.Log($"Failed to apply '{patcher.GetType().Name}' patch; some features may not work correctly. Technical details:\n{ex}", LogLevel.Error);
                }
            }

            return harmony;
        }
    }
}
