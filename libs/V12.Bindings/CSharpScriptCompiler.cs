using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace V12.Bindings
{
    /// <summary>Outcome of compiling a set of C# sources into a loadable assembly.</summary>
    public sealed class CSharpCompileResult
    {
        internal CSharpCompileResult(bool success, string? assemblyPath, IReadOnlyList<string> errors, IReadOnlyList<string> warnings, byte[]? image = null)
        {
            Success = success;
            AssemblyPath = assemblyPath;
            Errors = errors;
            Warnings = warnings;
            Image = image;
        }

        /// <summary>True when the assembly was emitted.</summary>
        public bool Success { get; }

        /// <summary>Absolute path of the emitted DLL, or null on failure.</summary>
        public string? AssemblyPath { get; }

        /// <summary>Raw PE image of the emitted assembly (null on failure).</summary>
        public byte[]? Image { get; }

        /// <summary>Compiler errors, one per line ("file(line,col): error CSxxxx: message").</summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>Compiler warnings in the same format as <see cref="Errors"/>.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>All errors as one newline-separated block (empty when successful).</summary>
        public string ErrorText => Errors.Count == 0 ? "" : string.Join(Environment.NewLine, Errors);
    }

    /// <summary>
    /// Remembers the PE image of assemblies this process compiled at runtime.
    /// Such assemblies are loaded from memory, so they have no
    /// <see cref="Assembly.Location"/>; without the image a later compile could
    /// not reference them (e.g. a pak's C# code using a component type from the
    /// pak it depends on).
    /// </summary>
    public static class CSharpScriptImages
    {
        private static readonly Dictionary<Assembly, byte[]> Images = new();

        public static void Add(Assembly? assembly, byte[]? image)
        {
            if (assembly == null || image == null || image.Length == 0) return;
            lock (Images) Images[assembly] = image;
        }

        public static void Remove(Assembly? assembly)
        {
            if (assembly == null) return;
            lock (Images) Images.Remove(assembly);
        }

        public static void Clear()
        {
            lock (Images) Images.Clear();
        }

        internal static bool TryGet(Assembly assembly, out byte[] image)
        {
            lock (Images) return Images.TryGetValue(assembly, out image!);
        }
    }

    /// <summary>
    /// Compiles a folder (or a set of files) of C# sources into a standalone DLL
    /// with Roslyn, in-process and offline. This is the compile step of the C#
    /// scripting path: <see cref="CSharpScriptHost"/> feeds the emitted assembly
    /// to a collectible <see cref="CSharpScriptLoadContext"/>, and
    /// <see cref="CSharpComponentRegistry"/> discovers the component types in it.
    /// <para>
    /// Every assembly currently loaded in the process (plus any explicit extras,
    /// e.g. a pak's DLLs) is offered to the compiler as a reference, so user code
    /// can use the whole engine surface and anything the host has loaded. A small
    /// set of global usings is injected so a component file needs no boilerplate.
    /// </para>
    /// </summary>
    public static class CSharpScriptCompiler
    {
        /// <summary>Source extension the compiler picks up.</summary>
        public const string Extension = ".cs";

        private const string GlobalUsings = """
            global using System;
            global using System.Collections.Generic;
            global using System.Linq;
            global using System.Numerics;
            global using V12;
            global using V12.Core;
            global using V12.Core.Core.Interfaces;
            global using V12.Components;
            """;

        private static readonly string[] SkippedPathSegments = { "bin", "obj", ".v12", ".git", "node_modules" };

        /// <summary>
        /// Compiles every <c>.cs</c> file under <paramref name="sourceDirectory"/>.
        /// Build-output folders (<c>bin</c>, <c>obj</c>, <c>.v12</c>) are skipped.
        /// </summary>
        public static CSharpCompileResult CompileDirectory(
            string sourceDirectory,
            string assemblyName,
            string? outputPath = null,
            IEnumerable<Assembly>? references = null,
            bool recursive = true)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory))
                return Fail("No source directory was given.");
            if (!Directory.Exists(sourceDirectory))
                return Fail($"Source directory not found: {sourceDirectory}");

            var files = Directory
                .EnumerateFiles(sourceDirectory, "*" + Extension, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(f => !IsBuildOutput(sourceDirectory, f))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
                return Fail($"No {Extension} files found under {sourceDirectory}");

            return CompileFiles(files, assemblyName, outputPath, references);
        }

        /// <summary>Compiles an explicit set of source files into <paramref name="assemblyName"/>.</summary>
        public static CSharpCompileResult CompileFiles(
            IEnumerable<string> sourceFiles,
            string assemblyName,
            string? outputPath = null,
            IEnumerable<Assembly>? references = null)
        {
            var files = (sourceFiles ?? Enumerable.Empty<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
                return Fail("No existing source files to compile.");

            string name = SanitizeAssemblyName(assemblyName);
            var parseOptions = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Regular);

            var trees = new List<SyntaxTree>(files.Count + 1);
            foreach (var file in files)
            {
                string text;
                try { text = File.ReadAllText(file); }
                catch (Exception ex) { return Fail($"Could not read '{file}': {ex.Message}"); }

                trees.Add(CSharpSyntaxTree.ParseText(SourceText.From(text, Encoding.UTF8), parseOptions, path: file));
            }
            trees.Add(CSharpSyntaxTree.ParseText(SourceText.From(GlobalUsings, Encoding.UTF8), parseOptions, path: "__v12_globals.g.cs"));

            var compilation = CSharpCompilation.Create(
                name,
                trees,
                BuildReferences(references),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Debug,
                    allowUnsafe: false,
                    concurrentBuild: false,
                    nullableContextOptions: NullableContextOptions.Enable));

            using var peStream = new MemoryStream();
            var emit = compilation.Emit(peStream);

            var errors = new List<string>();
            var warnings = new List<string>();
            foreach (var diagnostic in emit.Diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error) errors.Add(Format(diagnostic));
                else if (diagnostic.Severity == DiagnosticSeverity.Warning) warnings.Add(Format(diagnostic));
            }

            if (!emit.Success || errors.Count > 0)
                return new CSharpCompileResult(false, null, errors, warnings);

            string path = string.IsNullOrWhiteSpace(outputPath) ? DefaultOutputPath(name) : Path.GetFullPath(outputPath);
            byte[] image;
            try
            {
                image = peStream.ToArray();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, image);
            }
            catch (Exception ex)
            {
                return Fail($"Could not write '{path}': {ex.Message}");
            }

            return new CSharpCompileResult(true, path, errors, warnings, image);
        }

        /// <summary>
        /// Every assembly a compile should see by default: everything already
        /// loaded in the process, minus dynamic modules and Roslyn itself.
        /// </summary>
        public static IReadOnlyList<Assembly> DefaultReferences()
        {
            var list = new List<Assembly>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(Assembly? assembly)
            {
                if (assembly == null || assembly.IsDynamic) return;

                string name = assembly.GetName().Name ?? "";
                if (name.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)) return;
                if (!seen.Add(assembly.FullName ?? name)) return;

                list.Add(assembly);
            }

            Add(typeof(object).Assembly);
            Add(typeof(System.Collections.Generic.List<>).Assembly);
            try { Add(typeof(System.Linq.Enumerable).Assembly); } catch { }
            try { Add(typeof(System.Numerics.Vector3).Assembly); } catch { }
            try { Add(V12LinkedAssemblies.EngineAssembly); } catch { }
            try { Add(V12LinkedAssemblies.BasicAssembly); } catch { }
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                Add(assembly);

            // Assemblies living in a collectible script load context are not
            // returned by AppDomain.GetAssemblies, so pull them from the shared
            // link list (pak DLLs and other runtime-compiled script sets).
            foreach (var assembly in V12LinkedAssemblies.All)
                Add(assembly);

            return list;
        }

        /// <summary>Default output path used when a caller does not supply one.</summary>
        public static string DefaultOutputPath(string assemblyName)
            => Path.Combine(Path.GetTempPath(), "V12CSharp", SanitizeAssemblyName(assemblyName) + ".dll");

        private static IReadOnlyList<MetadataReference> BuildReferences(IEnumerable<Assembly>? extra)
        {
            var byLocation = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(Assembly? assembly)
            {
                if (assembly == null || assembly.IsDynamic) return;

                string name = assembly.GetName().Name ?? "";
                if (name.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)) return;

                string? location = null;
                try { location = assembly.Location; } catch { }

                string key = string.IsNullOrEmpty(location) ? "image:" + assembly.FullName : location;
                if (!seen.Add(key)) return;

                if (!string.IsNullOrEmpty(location))
                {
                    try { byLocation[key] = MetadataReference.CreateFromFile(location); }
                    catch { /* unreadable module: skip it rather than fail the whole compile */ }
                    return;
                }

                // Runtime-compiled assemblies have no file location; reference
                // the PE image we kept when we emitted it.
                if (CSharpScriptImages.TryGet(assembly, out var image))
                {
                    try { byLocation[key] = MetadataReference.CreateFromImage(image); }
                    catch { }
                }
            }

            Add(typeof(object).Assembly);
            try { Add(AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "System.Private.CoreLib")); } catch { }
            try { Add(typeof(object).Module.Assembly); } catch { }
            try { Add(Assembly.Load("System.Runtime")); } catch { }
            try { Add(typeof(System.Collections.Generic.List<>).Assembly); } catch { }
            try { Add(typeof(System.Linq.Enumerable).Assembly); } catch { }
            try { Add(typeof(System.Numerics.Vector3).Assembly); } catch { }
            try { Add(V12LinkedAssemblies.EngineAssembly); } catch { }
            try { Add(V12LinkedAssemblies.BasicAssembly); } catch { }
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string n = assembly.GetName().Name ?? "";
                if (n.StartsWith("V12", StringComparison.OrdinalIgnoreCase))
                    Add(assembly);
            }
            // Also add by type scan in case assembly naming is different
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (a.GetType("V12.Components.ComponentBase") != null || a.GetType("V12.Core.GameRoot") != null)
                            Add(a);
                    }
                    catch { }
                }
            }
            catch { }
            try { Add(Assembly.Load("netstandard")); } catch { }
            // Last resort: try to load common assembly names and from base directory
            string[] names = { "V12", "V12.Basic", "V12.Bindings", "V12.Pak", "V12.Runtime", "V12Engine", "V12Studio", "V12StudioPak" };
            foreach (var n in names)
            {
                try { Add(Assembly.Load(n)); } catch { }
                try { Add(Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, n + ".dll"))); } catch { }
            }
            try
            {
                foreach (var dll in Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.dll"))
                {
                    try
                    {
                        string an = Path.GetFileNameWithoutExtension(dll);
                        if (an.StartsWith("V12", StringComparison.OrdinalIgnoreCase))
                            Add(Assembly.LoadFrom(dll));
                    }
                    catch { }
                }
            }
            catch { }

            return byLocation.Values.ToList();
        }

        private static string Format(Diagnostic diagnostic)
        {
            var span = diagnostic.Location.GetLineSpan();
            string file = span.Path;
            if (string.IsNullOrEmpty(file)) return diagnostic.ToString();

            return $"{Path.GetFileName(file)}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): " +
                   $"{diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.Id}: {diagnostic.GetMessage()}";
        }

        private static string SanitizeAssemblyName(string assemblyName)
        {
            var builder = new StringBuilder();
            foreach (char c in string.IsNullOrWhiteSpace(assemblyName) ? "V12Scripts" : assemblyName)
                builder.Append(char.IsLetterOrDigit(c) || c == '.' || c == '_' ? c : '_');

            string name = builder.ToString().Trim('.', '_');
            return name.Length == 0 ? "V12Scripts" : name;
        }

        private static bool IsBuildOutput(string root, string file)
        {
            string relative = Path.GetRelativePath(root, file);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                foreach (var skipped in SkippedPathSegments)
                    if (string.Equals(parts[i], skipped, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            return false;
        }

        private static CSharpCompileResult Fail(string message)
            => new(false, null, new[] { message }, Array.Empty<string>());
    }
}
