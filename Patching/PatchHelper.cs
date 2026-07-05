using System;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace QuantumChests.Patching
{
    /// <summary>
    /// Locates the vanilla members a patch targets, with a clear error naming exactly what's missing if the game
    /// has changed it - rather than whatever opaque failure Harmony itself would produce from a null target.
    /// </summary>
    internal static class PatchHelper
    {
        /// <summary>Get a method and assert that it was found.</summary>
        /// <typeparam name="TTarget">The type containing the method.</typeparam>
        /// <param name="name">The method name.</param>
        /// <param name="parameters">The method parameter types, or <c>null</c> if it's not overloaded.</param>
        /// <exception cref="InvalidOperationException">The type has no matching method.</exception>
        public static MethodInfo RequireMethod<TTarget>(string name, Type[]? parameters = null)
        {
            return AccessTools.Method(typeof(TTarget), name, parameters)
                ?? throw new InvalidOperationException($"Can't find method {GetMemberString(typeof(TTarget), name, parameters)} to patch.");
        }

        /// <summary>Get a field and assert that it was found.</summary>
        /// <typeparam name="TTarget">The type containing the field.</typeparam>
        /// <param name="name">The field name.</param>
        /// <exception cref="InvalidOperationException">The type has no matching field.</exception>
        public static FieldInfo RequireField<TTarget>(string name)
        {
            return AccessTools.Field(typeof(TTarget), name)
                ?? throw new InvalidOperationException($"Can't find field {GetMemberString(typeof(TTarget), name)} to patch.");
        }

        /// <summary>Get a human-readable representation of a method or field target.</summary>
        private static string GetMemberString(Type type, string name, Type[]? parameters = null)
        {
            StringBuilder str = new();
            str.Append(type.FullName).Append('.').Append(name);

            if (parameters?.Length > 0)
            {
                str.Append('(');
                str.Append(string.Join(", ", parameters.Select(p => p.FullName)));
                str.Append(')');
            }

            return str.ToString();
        }
    }
}
