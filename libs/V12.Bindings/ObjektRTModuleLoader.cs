using Contract.Runtime;
using ObjektRT.Core.Model;

namespace V12.Bindings
{
    /// <summary>
    /// Neutral loader for precompiled ObjektRT script modules
    /// (<c>.orbt</c>/<c>.oil</c>/<c>.oir</c>). No Contract compilation is
    /// involved: the module is read from disk, statically linked when it
    /// still carries an import table (sibling modules resolved against the
    /// file's directory), and prepared so its <c>ClrImport</c>/
    /// <c>AssemblyRef</c>/<c>DllImport</c> metadata registers with the host.
    /// Contract (<c>.ct</c>) scripts compile-on-load instead; this is the
    /// load-as-module path for anything already compiled to ObjektRT.
    /// </summary>
    public static class ObjektRTModuleLoader
    {
        /// <summary>True when the path names a compiled script module (.orbt/.oil/.oir).</summary>
        public static bool IsCompiledScript(string? path)
        {
            var ext = Path.GetExtension(path ?? "").ToLowerInvariant();
            return ext == ".orbt" || ext == ".oil" || ext == ".oir";
        }

        /// <summary>
        /// Reads the compiled module at <paramref name="path"/> into
        /// <paramref name="host"/>'s pipeline: read → static-link imports
        /// (when present) → prepare metadata. The caller loads the returned
        /// module into the VM (<c>host.Inner.LoadModule</c>).
        /// </summary>
        public static ORBTModule Load(ContractRuntime host, string path)
        {
            var module = host.LoadModuleFileAuto(path);
            if (module.Imports.Count > 0)
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
                module = Contract.Compiler.StaticLinker.Link(module, dir);
            }
            host.PrepareModule(module);
            return module;
        }
    }
}
