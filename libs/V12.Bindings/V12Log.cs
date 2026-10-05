using Contract.Compiler.StandardLibrary;
using ObjektRT.Core.Attributes;
using V12.Core;

namespace V12.Bindings
{
    /// <summary>
    /// Logging bridge. Callable from Contract as <c>V12.Log.Info("...")</c>.
    /// Routes through the engine's Serilog logger.
    /// </summary>
    [ClassBinding("V12.Log")]
    public static class V12Log
    {
        [MethodBinding]
        public static void Info(string message)
            => GameRoot.Log.Information("{Message}", message);

        [MethodBinding]
        public static void Warn(string message)
            => GameRoot.Log.Warning("{Message}", message);

        [MethodBinding]
        public static void Error(string message)
            => GameRoot.Log.Error("{Message}", message);
    }
}
