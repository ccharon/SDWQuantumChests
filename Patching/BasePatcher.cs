using System;
using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;

namespace QuantumChests.Patching
{
    /// <summary>
    /// Base implementation for a Harmony patcher, with helpers for resolving patch targets and this patcher's
    /// own prefix/postfix methods.
    /// </summary>
    internal abstract class BasePatcher : IPatcher
    {
        /// <inheritdoc />
        public abstract void Apply(Harmony harmony, IMonitor monitor);

        /// <summary>Get a method and assert that it was found.</summary>
        /// <typeparam name="TTarget">The type containing the method.</typeparam>
        /// <param name="name">The method name.</param>
        /// <param name="parameters">The method parameter types, or <c>null</c> if it's not overloaded.</param>
        protected static MethodInfo RequireMethod<TTarget>(string name, Type[]? parameters = null)
            => PatchHelper.RequireMethod<TTarget>(name, parameters);

        /// <summary>Get a field and assert that it was found.</summary>
        /// <typeparam name="TTarget">The type containing the field.</typeparam>
        /// <param name="name">The field name.</param>
        protected static FieldInfo RequireField<TTarget>(string name)
            => PatchHelper.RequireField<TTarget>(name);

        /// <summary>Get a Harmony patch method (a prefix/postfix/transpiler) defined on this patcher instance's own class.</summary>
        /// <param name="name">The method name.</param>
        protected HarmonyMethod GetHarmonyMethod(string name)
        {
            return new HarmonyMethod(
                AccessTools.Method(this.GetType(), name)
                ?? throw new InvalidOperationException($"Can't find patcher method {this.GetType().FullName}.{name}.")
            );
        }
    }
}
