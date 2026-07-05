using HarmonyLib;
using StardewModdingAPI;

namespace QuantumChests.Patching
{
    /// <summary>
    /// A Harmony patch (or group of related patches) that can be applied independently of the others, so a
    /// single broken patch doesn't take down the rest of the mod.
    /// </summary>
    internal interface IPatcher
    {
        /// <summary>Apply this patcher's Harmony patches.</summary>
        /// <param name="harmony">The Harmony instance to patch with.</param>
        /// <param name="monitor">Writes messages to the console and log file.</param>
        void Apply(Harmony harmony, IMonitor monitor);
    }
}
