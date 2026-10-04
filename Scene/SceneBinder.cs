using System;
using System.Collections.Generic;
using System.Linq;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Basic.Scene
{
    /// <summary>
    /// Binds C# code to elements discovered in a scene (e.g. a WorldML file authored in the
    /// V12 editor or loaded from a <c>.v12pak</c>). Matches by name, child path, tag, or a
    /// predicate; handlers run immediately for elements already present and, when
    /// <see cref="Auto"/> is on, again whenever matching content is added.
    ///
    /// <code>
    /// world.Bind()
    ///      .On("SpawnBoxButton", e =&gt; e.GetComponent&lt;ButtonComponent&gt;().OnPressed = Spawn)
    ///      .On&lt;HealthComponent&gt;("Boss", h =&gt; h.MaxHealth = 500)
    ///      .OnTag("enemy", e =&gt; e.AddComponent(new HealthComponent(50)))
    ///      .Auto();
    /// </code>
    /// </summary>
    public sealed class SceneBinder : IDisposable
    {
        private sealed class Rule
        {
            public Func<IWorldElement, bool> Predicate = _ => false;
            public Action<IWorldElement> Apply = _ => { };
            public string Label = "";
            public bool Multi;
            public bool Once;
            public bool Warn;
            public bool Warned;
            public readonly HashSet<long> Applied = new();
        }

        private readonly Func<IEnumerable<World>> _worlds;
        private readonly List<Rule> _rules = new();
        private readonly List<World> _subscribed = new();

        private bool _auto;
        private bool _throwOnUnmatched;
        private bool _disposed;

        public SceneBinder(Func<IEnumerable<World>> worlds)
        {
            _worlds = worlds ?? throw new ArgumentNullException(nameof(worlds));
        }

        /// <summary>Binds to the first element with this name.</summary>
        public SceneBinder On(string name, Action<IWorldElement> apply, bool once = false)
            => AddRule(e => NameIs(e, name), apply, $"On(\"{name}\")", multi: false, once: once, warn: true);

        /// <summary>Binds to every element with this name.</summary>
        public SceneBinder OnAll(string name, Action<IWorldElement> apply, bool once = false)
            => AddRule(e => NameIs(e, name), apply, $"OnAll(\"{name}\")", multi: true, once: once, warn: true);

        /// <summary>Binds to an element by child path, e.g. <c>"Player/CameraPitch/Camera3D"</c>.</summary>
        public SceneBinder OnPath(string path, Action<IWorldElement> apply, bool once = false)
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return AddRule(e => AncestryMatches(e, parts), apply, $"OnPath(\"{path}\")", multi: false, once: once, warn: true);
        }

        /// <summary>Binds to every element tagged with <paramref name="tag"/>.</summary>
        public SceneBinder OnTag(string tag, Action<IWorldElement> apply, bool once = false)
            => AddRule(e => e.GetComponent<TagComponent>()?.HasTag(tag) == true, apply, $"OnTag(\"{tag}\")", multi: true, once: once, warn: false);

        /// <summary>Binds to every element matching a predicate.</summary>
        public SceneBinder OnWhere(Func<IWorldElement, bool> predicate, Action<IWorldElement> apply, bool once = false)
            => AddRule(predicate, apply, "OnWhere(...)", multi: true, once: once, warn: false);

        /// <summary>Binds to a component on the first element with this name (optionally adding it).</summary>
        public SceneBinder On<TComponent>(string name, Action<TComponent> apply, bool addIfMissing = false, bool once = false)
            where TComponent : IComponent, new()
            => AddRule(e => NameIs(e, name), e =>
            {
                var component = e.GetComponent<TComponent>();
                if (component == null && addIfMissing)
                {
                    component = new TComponent();
                    e.AddComponent(component);
                }
                if (component != null) apply(component);
            }, $"On<{typeof(TComponent).Name}>(\"{name}\")", multi: false, once: once, warn: true);

        /// <summary>
        /// Resolve now and again whenever matching content is added. Call this last in the
        /// chain (it runs <see cref="Apply"/> immediately).
        /// </summary>
        public SceneBinder Auto(bool enabled = true)
        {
            _auto = enabled;
            if (!enabled) return this;
            Subscribe();
            Apply();
            return this;
        }

        /// <summary>Throw instead of logging when a name/path handler matches nothing.</summary>
        public SceneBinder ThrowOnUnmatched(bool enabled = true)
        {
            _throwOnUnmatched = enabled;
            return this;
        }

        /// <summary>Scan the bound worlds now and invoke every matching handler.</summary>
        public void Apply()
        {
            foreach (var world in ActiveWorlds())
                foreach (var element in world.Enumerate())
                    foreach (var rule in _rules)
                        TryApply(rule, element);
            WarnUnmatched();
        }

        /// <summary>Re-scan (e.g. after elements were added via <c>AddChild</c> at runtime).</summary>
        public void Refresh() => Apply();

        public void Clear()
        {
            Unsubscribe();
            _rules.Clear();
        }

        public void Dispose()
        {
            _disposed = true;
            Unsubscribe();
        }

        private SceneBinder AddRule(Func<IWorldElement, bool> predicate, Action<IWorldElement> apply, string label, bool multi, bool once, bool warn)
        {
            _rules.Add(new Rule { Predicate = predicate, Apply = apply, Label = label, Multi = multi, Once = once, Warn = warn });
            return this;
        }

        private static void TryApply(Rule rule, IWorldElement element)
        {
            if (rule.Applied.Contains(element.Id)) return;
            if (!rule.Multi && rule.Applied.Count > 0) return;
            if (rule.Once && rule.Applied.Count > 0) return;
            if (!rule.Predicate(element)) return;

            rule.Apply(element);
            rule.Applied.Add(element.Id);
        }

        private void Subscribe()
        {
            if (_disposed) return;
            foreach (var world in ActiveWorlds())
            {
                if (world == null || _subscribed.Contains(world)) continue;
                world.ElementAdded += OnElementAdded;
                _subscribed.Add(world);
            }
        }

        private void Unsubscribe()
        {
            foreach (var world in _subscribed)
                world.ElementAdded -= OnElementAdded;
            _subscribed.Clear();
        }

        private void OnElementAdded(IWorldElement element)
        {
            // A parsed subtree exists in full before it is added, so scanning it catches
            // everything under the new root.
            foreach (var candidate in Walk(element))
                foreach (var rule in _rules)
                    TryApply(rule, candidate);
        }

        private IEnumerable<World> ActiveWorlds() => _worlds().Where(w => w != null);

        private void WarnUnmatched()
        {
            foreach (var rule in _rules)
            {
                if (!rule.Warn || rule.Warned || rule.Applied.Count != 0) continue;
                rule.Warned = true;
                var message = $"[SceneBinder] No element matched {rule.Label}.";
                if (_throwOnUnmatched) throw new InvalidOperationException(message);
                Console.WriteLine(message);
            }
        }

        private static bool NameIs(IWorldElement element, string name)
            => string.Equals(element.Name, name, StringComparison.OrdinalIgnoreCase);

        private static bool AncestryMatches(IWorldElement element, string[] parts)
        {
            if (parts.Length == 0) return false;
            var node = element;
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                if (node == null || !NameIs(node, parts[i])) return false;
                node = node.Parent;
            }
            return true;
        }

        private static IEnumerable<IWorldElement> Walk(IWorldElement element)
        {
            yield return element;
            foreach (var child in element.Children.ToArray())
                foreach (var descendant in Walk(child))
                    yield return descendant;
        }
    }

    /// <summary>Entry points for <see cref="SceneBinder"/>.</summary>
    public static class SceneBindingExtensions
    {
        /// <summary>Start binding code to this world's elements.</summary>
        public static SceneBinder Bind(this World world)
            => new SceneBinder(() => new[] { world });

        /// <summary>Start binding code across every active world (persistent + selected).</summary>
        public static SceneBinder Bind(this GameRoot root)
            => new SceneBinder(() => root.ActiveWorlds);
    }
}
