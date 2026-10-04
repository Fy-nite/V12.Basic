using System;
using System.Numerics;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Basic.Building
{
    /// <summary>
    /// Fluent builder for elements — sugar over the extension helpers so there is a single
    /// source of truth. Example:
    /// <code>
    /// new ElementBuilder("Crate")
    ///     .Box(1, 1, 1)
    ///     .Dynamic()
    ///     .Material(0.8f, 0.2f, 0.2f)
    ///     .At(-3, 0.5f, 0)
    ///     .Build(world);
    /// </code>
    /// </summary>
    public sealed class ElementBuilder
    {
        private readonly string? _name;

        private MeshShape? _shape;
        private float _width = 1f, _height = 1f, _depth = 1f;
        private bool _addMesh = true;
        private bool _addCollider = true;
        private bool _addPhysics = true;
        private bool _kinematic = true;
        private float _gravityScale = 1f;

        private Vector3 _position = Vector3.Zero;
        private Vector3? _rotationDegrees;
        private float? _uniformScale;

        private (float r, float g, float b, float a, float metallic, float roughness)? _material;
        private (float r, float g, float b, float range, float energy)? _pointLight;
        private (float r, float g, float b, float range, float energy, float angle, float softness)? _spotLight;
        private (float r, float g, float b, float energy)? _directionalLight;
        private Action<IWorldElement>? _configure;

        public ElementBuilder(string? name = null) => _name = name;

        // ── Shape (mesh + default collider dimensions) ───────────────────────
        public ElementBuilder Box(float width = 1f, float height = 1f, float depth = 1f) => SetShape(MeshShape.Box, width, height, depth);

        /// <summary>Sphere; <paramref name="radius"/> is the true radius.</summary>
        public ElementBuilder Sphere(float radius = 0.5f) => SetShape(MeshShape.Sphere, radius * 2f, radius * 2f, radius * 2f);

        public ElementBuilder Capsule(float radius = 0.3f, float height = 1.8f) => SetShape(MeshShape.Capsule, radius * 2f, height, radius * 2f);

        public ElementBuilder Cylinder(float radius = 0.5f, float height = 1f) => SetShape(MeshShape.Cylinder, radius * 2f, height, radius * 2f);

        public ElementBuilder Plane(float width = 1f, float depth = 1f) => SetShape(MeshShape.Plane, width, 0.1f, depth);

        public ElementBuilder SetShape(MeshShape shape, float width, float height, float depth)
        {
            _shape = shape;
            _width = width;
            _height = height;
            _depth = depth;
            return this;
        }

        // ── Toggles ──────────────────────────────────────────────────────────
        public ElementBuilder NoMesh() { _addMesh = false; return this; }
        public ElementBuilder NoCollider() { _addCollider = false; return this; }
        public ElementBuilder NoPhysics() { _addPhysics = false; return this; }

        public ElementBuilder Dynamic(float gravityScale = 1f) { _kinematic = false; _gravityScale = gravityScale; _addPhysics = true; return this; }
        public ElementBuilder Kinematic() { _kinematic = true; _addPhysics = true; return this; }

        // ── Appearance ───────────────────────────────────────────────────────
        public ElementBuilder Material(float r = 1f, float g = 1f, float b = 1f, float a = 1f, float metallic = 0f, float roughness = 0.5f)
        {
            _material = (r, g, b, a, metallic, roughness);
            return this;
        }

        public ElementBuilder PointLight(float r = 1f, float g = 1f, float b = 1f, float range = 10f, float energy = 1f)
        {
            _pointLight = (r, g, b, range, energy);
            return this;
        }

        public ElementBuilder SpotLight(float r = 1f, float g = 1f, float b = 1f, float range = 15f, float energy = 1f, float angle = 45f, float softness = 0.5f)
        {
            _spotLight = (r, g, b, range, energy, angle, softness);
            return this;
        }

        public ElementBuilder DirectionalLight(float r = 1f, float g = 1f, float b = 1f, float energy = 1f)
        {
            _directionalLight = (r, g, b, energy);
            return this;
        }

        // ── Transform ────────────────────────────────────────────────────────
        public ElementBuilder At(float x, float y, float z) { _position = new Vector3(x, y, z); return this; }
        public ElementBuilder At(Vector3 position) { _position = position; return this; }
        public ElementBuilder Rotated(float xDegrees, float yDegrees, float zDegrees) { _rotationDegrees = new Vector3(xDegrees, yDegrees, zDegrees); return this; }
        public ElementBuilder UniformScale(float scale) { _uniformScale = scale; return this; }

        /// <summary>Escape hatch to configure the element directly before it is added.</summary>
        public ElementBuilder Tap(Action<IWorldElement> configure) { _configure = configure; return this; }

        // ── Build ────────────────────────────────────────────────────────────
        public Element Build(World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            var element = new Element(_name);
            element.SetTransform(_position, _rotationDegrees);

            if (_shape.HasValue)
            {
                if (_addCollider) element.AddCollider(_shape.Value, _width, _height, _depth);
                if (_addMesh) element.AddMesh(_shape.Value, _width, _height, _depth);
                if (_addPhysics && _addCollider) element.AddPhysics(_kinematic, _gravityScale);
            }

            if (_material is { } material)
                element.AddMaterial(material.r, material.g, material.b, material.a, material.metallic, material.roughness);

            if (_pointLight is { } point)
                element.AddPointLight(point.r, point.g, point.b, point.range, point.energy);
            else if (_spotLight is { } spot)
                element.AddSpotLight(spot.r, spot.g, spot.b, spot.range, spot.energy, spot.angle, spot.softness);
            else if (_directionalLight is { } sun)
                element.AddDirectionalLight(sun.r, sun.g, sun.b, sun.energy);

            if (_uniformScale.HasValue)
                element.AddComponent(new ScaleComponent(_uniformScale.Value));

            _configure?.Invoke(element);
            world.AddElement(element);
            return element;
        }

        public Element BuildSelected(GameRoot root)
        {
            var world = (root ?? throw new ArgumentNullException(nameof(root))).SelectedWorld
                ?? throw new InvalidOperationException("GameRoot has no SelectedWorld to build into.");
            return Build(world);
        }
    }
}
