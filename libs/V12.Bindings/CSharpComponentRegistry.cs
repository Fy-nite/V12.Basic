using System.Reflection;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Bindings
{
    /// <summary>
    /// Registry of components compiled from C# source at runtime. Unlike
    /// <see cref="ContractComponentRegistry"/> (which hosts Contract VM types),
    /// this registry holds real CLR types: user classes that derive from
    /// <see cref="ComponentBase"/>/<see cref="ScriptableComponent"/> and were
    /// emitted by <see cref="CSharpScriptCompiler"/> into a loadable assembly.
    /// <para>
    /// Instances are ordinary <see cref="IComponent"/>s, so the engine's
    /// <see cref="World"/> updates them every frame and the inspector/serializer
    /// treat them like any built-in component. Attach them by name
    /// (<see cref="Attach"/>) from C# or from Contract via
    /// <c>V12.Components.AddCSharpComponent</c>.
    /// </para>
    /// </summary>
    public sealed class CSharpComponentRegistry
    {
        /// <summary>Registry service key (retrieve with <c>Registry.Get&lt;CSharpComponentRegistry&gt;(ServiceName)</c>).</summary>
        public const string ServiceName = "CSharpComponents";

        /// <summary>A registered component CLR type plus its editor metadata.</summary>
        public sealed class TypeEntry
        {
            internal TypeEntry(Type type, string? sourcePath)
            {
                Type = type;
                TypeName = type.Name;
                QualifiedName = type.FullName ?? type.Name;
                SourcePath = sourcePath;

                var entry = type.GetCustomAttribute<ComponentEntryAttribute>();
                Category = entry?.Category ?? "GAMEPLAY";
                DisplayName = entry?.DisplayName ?? Humanize(type.Name);
                SingleInstance = entry?.SingleInstance ?? true;
                SortOrder = entry?.SortOrder ?? 0;
                HasEntryAttribute = entry != null;
            }

            /// <summary>The CLR type implementing <see cref="IComponent"/>.</summary>
            public Type Type { get; }

            /// <summary>Simple type name (e.g. <c>SpinComponent</c>).</summary>
            public string TypeName { get; }

            /// <summary>Namespace-qualified type name.</summary>
            public string QualifiedName { get; }

            /// <summary>Assembly/source the type came from, for diagnostics and the editor.</summary>
            public string? SourcePath { get; }

            /// <summary>Editor category (from <see cref="ComponentEntryAttribute"/> or GAMEPLAY).</summary>
            public string Category { get; }

            /// <summary>Editor display name (from <see cref="ComponentEntryAttribute"/> or the type name).</summary>
            public string DisplayName { get; }

            /// <summary>Whether the add menu should allow only one instance.</summary>
            public bool SingleInstance { get; }

            /// <summary>Sort order within the category.</summary>
            public int SortOrder { get; }

            /// <summary>True when the type carried a <see cref="ComponentEntryAttribute"/>.</summary>
            public bool HasEntryAttribute { get; }

            private static string Humanize(string name)
            {
                if (name.EndsWith("Component", StringComparison.OrdinalIgnoreCase) && name.Length > "Component".Length)
                    name = name[..^"Component".Length];

                var builder = new System.Text.StringBuilder(name.Length + 4);
                for (int i = 0; i < name.Length; i++)
                {
                    if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) builder.Append(' ');
                    builder.Append(name[i]);
                }
                return builder.ToString();
            }
        }

        private readonly Dictionary<string, TypeEntry> _bySimpleName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TypeEntry> _byQualifiedName = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Registry shared through the game root (also reachable via <see cref="Current"/>).</summary>
        public static CSharpComponentRegistry? Current { get; private set; }

        /// <summary>Names of every registered component type.</summary>
        public IReadOnlyCollection<string> TypeNames => _bySimpleName.Keys;

        /// <summary>Every registered component type.</summary>
        public IReadOnlyCollection<TypeEntry> Entries => _bySimpleName.Values;

        /// <summary>Whether a component type with this name (or alias) is registered.</summary>
        public bool Contains(string typeName) => TryGet(typeName, out _);

        /// <summary>Looks up a registered component type by simple or qualified name.</summary>
        public bool TryGet(string typeName, out TypeEntry entry)
        {
            entry = null!;
            if (string.IsNullOrWhiteSpace(typeName)) return false;

            string key = typeName.Trim();
            if (_bySimpleName.TryGetValue(key, out entry!)) return true;
            if (_byQualifiedName.TryGetValue(key, out entry!)) return true;

            // Accept "PakName.TypeName" and "Namespace.TypeName" aliases.
            int dot = key.LastIndexOf('.');
            if (dot >= 0 && dot < key.Length - 1)
                return _bySimpleName.TryGetValue(key[(dot + 1)..], out entry!);

            // Accept a missing/extra "Component" suffix.
            if (!key.EndsWith("Component", StringComparison.OrdinalIgnoreCase))
                return _bySimpleName.TryGetValue(key + "Component", out entry!);

            return false;
        }

        /// <summary>
        /// Discovers every concrete <see cref="IComponent"/> in
        /// <paramref name="assembly"/> with a public parameterless constructor and
        /// registers it. Existing registrations with the same type name are
        /// replaced, so recompiling a script set picks up edits. Returns the
        /// number of types registered.
        /// </summary>
        public int RegisterAssembly(Assembly assembly, string? sourcePath = null)
        {
            if (assembly == null) return 0;

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
            catch (Exception ex)
            {
                GameRoot.Log.Warning(ex, "[CSharpComponents] Could not read types from {Assembly}", assembly.GetName().Name);
                return 0;
            }

            int count = 0;
            foreach (var type in types)
            {
                if (type == null) continue;
                if (!RegisterType(type, sourcePath ?? assembly.GetName().Name)) continue;
                count++;
            }

            if (count == 0)
                GameRoot.Log.Warning("[CSharpComponents] No IComponent types found in {Assembly}", assembly.GetName().Name);

            return count;
        }

        /// <summary>Registers one component type. Returns false when the type is not a usable component.</summary>
        public bool RegisterType(Type type, string? sourcePath = null)
        {
            if (type == null || type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition) return false;
            if (!typeof(IComponent).IsAssignableFrom(type)) return false;
            if (type.GetConstructor(Type.EmptyTypes) == null) return false;

            var entry = new TypeEntry(type, sourcePath);
            _bySimpleName[entry.TypeName] = entry;
            _byQualifiedName[entry.QualifiedName] = entry;

            GameRoot.Log.Information("[CSharpComponents] Registered '{Type}' from {Source}",
                entry.QualifiedName, entry.SourcePath ?? "<runtime>");
            return true;
        }

        /// <summary>
        /// Removes every type registered from <paramref name="sourcePath"/>. Call
        /// this before re-registering a recompiled script set so renamed or
        /// deleted classes do not linger (and so the old collectible load context
        /// can actually be reclaimed).
        /// </summary>
        public int RemoveSource(string? sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) return 0;

            var stale = _bySimpleName.Values
                .Where(e => string.Equals(e.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();

            foreach (var entry in stale)
            {
                _bySimpleName.Remove(entry.TypeName);
                _byQualifiedName.Remove(entry.QualifiedName);
            }
            return stale.Count;
        }

        /// <summary>Creates a new instance of a registered component type, or null when unknown.</summary>
        public IComponent? Create(string typeName)
        {
            if (!TryGet(typeName, out var entry))
            {
                GameRoot.Log.Warning("[CSharpComponents] No component type named '{Type}'", typeName);
                return null;
            }

            try
            {
                if (Activator.CreateInstance(entry.Type) is IComponent component) return component;
                GameRoot.Log.Warning("[CSharpComponents] '{Type}' does not implement IComponent", entry.QualifiedName);
            }
            catch (Exception ex)
            {
                GameRoot.Log.Error(ex, "[CSharpComponents] Failed to create '{Type}'", entry.QualifiedName);
            }
            return null;
        }

        /// <summary>Creates a component of <paramref name="typeName"/> and attaches it to the element.</summary>
        public bool Attach(IWorldElement element, string typeName)
        {
            if (element == null) return false;
            var component = Create(typeName);
            if (component == null) return false;
            element.AddComponent(component);
            return true;
        }

        /// <summary>Installs this registry as a game-root service.</summary>
        public void Register(GameRoot? gameRoot)
        {
            Current = this;
            if (gameRoot != null && gameRoot.Registry.Get<CSharpComponentRegistry>(ServiceName) == null)
                gameRoot.Registry.Register(ServiceName, this);
        }

        /// <summary>Convenience: the registry installed on the current game root (or <see cref="Current"/>).</summary>
        public static CSharpComponentRegistry? FromGameRoot()
            => GameRoot.Instance?.Registry.Get<CSharpComponentRegistry>(ServiceName) ?? Current;

        /// <summary>
        /// Returns the registry installed on the current game root, creating and
        /// registering one when none exists yet (e.g. before <c>Initialize</c>).
        /// </summary>
        public static CSharpComponentRegistry GetOrCreate()
        {
            var root = GameRoot.Instance;
            if (root != null)
            {
                var installed = root.Registry.Get<CSharpComponentRegistry>(ServiceName);
                if (installed != null) return installed;

                var created = new CSharpComponentRegistry();
                created.Register(root);
                return created;
            }

            return Current ??= new CSharpComponentRegistry();
        }

        /// <summary>
        /// Builds a registry from <c>scripts/csharp</c> (falling back to
        /// <c>scripts/components</c>) under the working directory or the
        /// executable directory, installs it as a service, and returns it.
        /// Loading only the first directory avoids registering types twice.
        /// </summary>
        public static CSharpComponentRegistry CreateAndRegister(GameRoot gameRoot)
        {
            var registry = new CSharpComponentRegistry();
            registry.Register(gameRoot);
            return registry;
        }
    }
}
