using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using V12;

namespace V12.Basic.Rendering
{
    /// <summary>
    /// Discovers every <see cref="Rendererinfo"/> implementation via reflection and
    /// lets the user choose one to launch.
    ///
    /// <para>
    /// Discovery scans the entry (launcher) assembly and the renderer assemblies it
    /// references directly, so a renderer's info file may live either in the launcher
    /// itself (e.g. the built-in Nova/Pulsar infos) or in its own assembly.
    /// </para>
    /// </summary>
    public class RendererLauncher
    {
        /// <summary>
        /// Features a renderer must declare (in its <see cref="Rendererinfo.Features"/>)
        /// to be considered supported. Empty by default, so any renderer with a name is
        /// accepted.
        /// </summary>
        public IReadOnlyList<Feature> RequiredFeatures { get; }

        /// <summary>Renderers that were discovered and pass <see cref="DoesSupport"/>, ordered by name.</summary>
        public IReadOnlyList<Rendererinfo> Renderers { get; }

        public RendererLauncher() : this(Discover()) { }

        public RendererLauncher(IEnumerable<Rendererinfo> renderers, IEnumerable<Feature>? requiredFeatures = null)
        {
            if (renderers == null) throw new ArgumentNullException(nameof(renderers));

            RequiredFeatures = (requiredFeatures ?? Array.Empty<Feature>()).ToArray();
            Renderers = renderers
                .Where(DoesSupport)
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// True when the renderer advertises a name and declares every feature in
        /// <see cref="RequiredFeatures"/>. Evaluates the renderer's own info contents
        /// (its <see cref="Rendererinfo.Features"/>) rather than just checking for null.
        /// </summary>
        public bool DoesSupport(Rendererinfo renderer)
        {
            if (renderer == null) return false;
            if (string.IsNullOrWhiteSpace(renderer.Name)) return false;
            if (renderer.Features == null) return false;

            foreach (var required in RequiredFeatures)
            {
                if (!renderer.Features.Contains(required))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Instantiate every concrete <see cref="Rendererinfo"/> found in the entry
        /// assembly and its directly-referenced renderer assemblies. Duplicates (same
        /// <see cref="Rendererinfo.Name"/>) keep the first one found.
        /// </summary>
        public static List<Rendererinfo> Discover()
        {
            var found = new Dictionary<string, Rendererinfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var assembly in CandidateAssemblies())
            {
                foreach (var info in DiscoverIn(assembly))
                {
                    if (!found.ContainsKey(info.Name))
                        found[info.Name] = info;
                }
            }

            return found.Values
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>The entry assembly plus each assembly it references directly.</summary>
        private static IEnumerable<Assembly> CandidateAssemblies()
        {
            var entry = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { entry.GetName().Name ?? "" };
            yield return entry;

            foreach (var reference in entry.GetReferencedAssemblies())
            {
                if (!seen.Add(reference.Name ?? "")) continue;

                Assembly? loaded = null;
                try { loaded = Assembly.Load(reference); } catch { /* native or missing — skip */ }
                if (loaded != null) yield return loaded;
            }
        }

        private static IEnumerable<Rendererinfo> DiscoverIn(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray()!;
            }
            catch
            {
                yield break;
            }

            foreach (var type in types)
            {
                if (type == null || type.IsAbstract || type.IsInterface) continue;
                if (!typeof(Rendererinfo).IsAssignableFrom(type)) continue;

                Rendererinfo? instance = null;
                try { instance = (Rendererinfo?)Activator.CreateInstance(type); } catch { }
                if (instance != null) yield return instance;
            }
        }

        /// <summary>Present the discovered renderers on the console and return the user's choice.</summary>
        public Rendererinfo? Choose()
        {
            if (Renderers.Count == 0)
            {
                Console.WriteLine("No renderers were found.");
                return null;
            }

            Console.WriteLine("Available renderers:");
            for (int i = 0; i < Renderers.Count; i++)
            {
                var renderer = Renderers[i];
                Console.WriteLine($"  [{i + 1}] {renderer.Name}");
                if (!string.IsNullOrWhiteSpace(renderer.Description))
                    Console.WriteLine($"        {renderer.Description}");
                var features = renderer.Features is { Length: > 0 }
                    ? string.Join(", ", renderer.Features)
                    : "none";
                Console.WriteLine($"        features: {features}");
            }

            Console.Write($"Select a renderer [1-{Renderers.Count}] (default 1): ");
            var input = Console.ReadLine();

            if (int.TryParse(input, out var choice) && choice >= 1 && choice <= Renderers.Count)
                return Renderers[choice - 1];

            return Renderers[0];
        }

        /// <summary>Boot the chosen renderer with the supplied game pak. Blocks for as long as the renderer runs.</summary>
        public void Launch(Rendererinfo renderer, IV12Gamepack pak)
        {
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            if (pak == null) throw new ArgumentNullException(nameof(pak));

            Console.WriteLine($"Launching renderer '{renderer.Name}' with game pak '{pak.Name}'...");
            renderer.StartRenderer(pak);
        }

        /// <summary>Discover, let the user choose, and launch the supplied game pak.</summary>
        public void Run(IV12Gamepack pak)
        {
            var chosen = Choose();
            if (chosen != null)
                Launch(chosen, pak);
        }
    }
}
