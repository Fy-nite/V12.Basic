using V12.Components;
using V12.Core.Core.Interfaces;

namespace V12.Bindings
{
    /// <summary>
    /// A live instance of a Contract (<c>.ct</c>) component type, wrapped as a
    /// normal V12 component. The type is compiled once by
    /// <see cref="ContractComponentRegistry"/>; this component owns one VM object
    /// handle and drives the type's <c>on_attach</c>/<c>on_update</c>/<c>on_detach</c>
    /// hooks. Inside those hooks the owning element is available as
    /// <c>V12.Script.Owner()</c>, and instance fields are just <c>this.&lt;field&gt;</c>.
    ///
    /// Attach it with <see cref="ContractComponentRegistry.Attach"/> or by adding
    /// it from a script via <c>V12.Components.AddContractComponent(id, type)</c>.
    /// </summary>
    public sealed class ContractComponent : ComponentBase
    {
        private readonly ContractComponentRegistry.TypeEntry _entry;
        private object? _handle;

        internal ContractComponent(ContractComponentRegistry.TypeEntry entry, object? handle)
        {
            _entry = entry;
            _handle = handle;
        }

        /// <summary>The <c>.ct</c> component type name.</summary>
        public string TypeName => _entry.TypeName;

        /// <inheritdoc />
        public override string? Name
        {
            get => _entry.TypeName;
            set { /* identity is the Contract type name */ }
        }

        /// <summary>The VM object handle for this instance (null until allocated).</summary>
        internal object? Handle => _handle;

        public override void OnAttach(IWorldElement worldElement)
        {
            base.OnAttach(worldElement);
            _handle ??= _entry.Allocate();
            if (_handle == null) return;

            V12Script.SetOwner(worldElement.Id);
            _entry.Invoke(_entry.OnAttach, _handle, worldElement.Id);
        }

        public override void Update(float deltaTime)
        {
            if (_handle == null || _entry.OnUpdate == null) return;

            V12Script.SetOwner(Owner?.Id ?? 0);
            _entry.Invoke(_entry.OnUpdate, _handle, deltaTime);
        }

        public override void OnDetach(IWorldElement worldElement)
        {
            if (_handle != null)
            {
                V12Script.SetOwner(worldElement.Id);
                _entry.Invoke(_entry.OnDetach, _handle, worldElement.Id);
            }
            base.OnDetach(worldElement);
        }

        // ── Field access (C# side) ─────────────────────────────────────

        /// <summary>Reads an instance field by name, or null.</summary>
        public object? GetField(string fieldName)
            => _handle == null ? null : _entry.Runtime.Inner.GetField($"{TypeName}.{fieldName}", _handle);

        /// <summary>Writes an instance field by name.</summary>
        public void SetField(string fieldName, object? value)
        {
            if (_handle == null) return;
            _entry.Runtime.Inner.SetField($"{TypeName}.{fieldName}", value, _handle);
        }

        public float GetFloat(string fieldName, float fallback = 0f)
            => ToSingle(GetField(fieldName), fallback);

        public void SetFloat(string fieldName, float value) => SetField(fieldName, value);

        public int GetInt(string fieldName, int fallback = 0)
            => (int)ToDouble(GetField(fieldName), fallback);

        public void SetInt(string fieldName, int value) => SetField(fieldName, value);

        public bool GetBool(string fieldName, bool fallback = false)
            => GetField(fieldName) is bool b ? b : fallback;

        public void SetBool(string fieldName, bool value) => SetField(fieldName, value);

        public string GetString(string fieldName, string fallback = "")
            => GetField(fieldName) as string ?? fallback;

        public void SetString(string fieldName, string value) => SetField(fieldName, value);

        private static float ToSingle(object? value, float fallback)
        {
            try { return value == null ? fallback : Convert.ToSingle(value); }
            catch { return fallback; }
        }

        private static double ToDouble(object? value, double fallback)
        {
            try { return value == null ? fallback : Convert.ToDouble(value); }
            catch { return fallback; }
        }

        public override IWorldElement BuildUI()
            => Owner ?? V12.Core.Procedurals.GenBox("ContractComponentUI", System.Numerics.Vector3.One);
    }
}
