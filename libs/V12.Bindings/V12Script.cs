using Contract.Compiler.StandardLibrary;

namespace V12.Bindings
{
    /// <summary>
    /// Per-script bindings. Callable from Contract as <c>V12.Script.Owner()</c>.
    /// The owning element id is set by <see cref="ContractScriptRuntime"/> right
    /// before each script hook is invoked, so a .ct element script can address
    /// the element it is attached to without searching by name.
    /// </summary>
    [ClassBinding("V12.Script")]
    public static class V12Script
    {
        [ThreadStatic]
        private static long _ownerId;

        internal static void SetOwner(long elementId) => _ownerId = elementId;

        /// <summary>Id of the element this script is attached to (0 when none).</summary>
        [MethodBinding]
        public static long Owner() => _ownerId;
    }
}
