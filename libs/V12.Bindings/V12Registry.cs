using System.Globalization;
using Contract.Compiler.StandardLibrary;
using V12.Core;

namespace V12.Bindings
{
    /// <summary>
    /// Service registry bindings. Callable from Contract as
    /// <c>V12.Registry.Has("InputService")</c> etc.
    /// <c>Register</c>/<c>RegisterString</c> overwrite an existing service of the
    /// same name, so Contract code can replace engine services at runtime.
    /// </summary>
    [ClassBinding("V12.Registry")]
    public static class V12Registry
    {
        /// <summary>Whether a service is registered under the given name.</summary>
        [MethodBinding]
        public static bool Has(string name)
            => GameRoot.Instance?.Registry.Get(name) != null;

        /// <summary>Register (or replace) a numeric service value.</summary>
        [MethodBinding]
        public static bool Register(string name, long value)
        {
            var root = GameRoot.Instance;
            if (root == null) return false;
            root.Registry.RegisterOrReplace(name, value);
            return true;
        }

        /// <summary>Register (or replace) a double service value.</summary>
        [MethodBinding]
        public static bool RegisterDouble(string name, double value)
        {
            var root = GameRoot.Instance;
            if (root == null) return false;
            root.Registry.RegisterOrReplace(name, value);
            return true;
        }

        /// <summary>Register (or replace) a string service value.</summary>
        [MethodBinding]
        public static bool RegisterString(string name, string value)
        {
            var root = GameRoot.Instance;
            if (root == null) return false;
            root.Registry.RegisterOrReplace(name, value);
            return true;
        }

        /// <summary>Read a numeric service value as a long (0 when missing).</summary>
        [MethodBinding]
        public static long GetInt(string name)
        {
            var service = GameRoot.Instance?.Registry.Get(name);
            return service?.ServiceInstance switch
            {
                long l => l,
                int i => i,
                double d => (long)d,
                float f => (long)f,
                _ => 0,
            };
        }

        /// <summary>Read a string service value ("" when missing).</summary>
        [MethodBinding]
        public static string GetString(string name)
            => GameRoot.Instance?.Registry.Get(name)?.ServiceInstance?.ToString() ?? "";

        /// <summary>Remove a registered service. Returns true when one was removed.</summary>
        [MethodBinding]
        public static bool Remove(string name)
            => GameRoot.Instance?.Registry.Unregister(name) ?? false;

        /// <summary>Newline-separated list of registered service names.</summary>
        [MethodBinding]
        public static string List()
        {
            var root = GameRoot.Instance;
            if (root == null) return "";
            return string.Join("\n", root.Registry.GetServices().Select(s => s.name));
        }
    }
}
