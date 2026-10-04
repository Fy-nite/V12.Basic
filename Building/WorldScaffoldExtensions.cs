using System.Collections.Generic;
using System.Numerics;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Basic.Building
{
    /// <summary>Quick level scaffolding: ground, walls, stacks, grids, spawn points, sun.</summary>
    public static class WorldScaffoldExtensions
    {
        /// <summary>A static ground plane (a thin box). The top surface sits at <paramref name="topY"/>.</summary>
        public static Element AddGround(this World world, float size = 40f, float thickness = 1f, float topY = 0f, string? name = null)
        {
            var center = new Vector3(0f, topY - thickness * 0.5f, 0f);
            return world.SpawnBox(at: center, width: size, height: thickness, depth: size, dynamic: false, name: name ?? "Ground");
        }

        /// <summary>Four static walls forming a square boundary of the given half-extent.</summary>
        public static List<Element> AddBoundaryWalls(this World world, float halfExtent = 20f, float height = 3f, float thickness = 0.5f)
        {
            float y = height * 0.5f;
            var walls = new List<Element>
            {
                world.SpawnBox(new Vector3(0f, y, -halfExtent), halfExtent * 2f, height, thickness, false, "Wall_N"),
                world.SpawnBox(new Vector3(0f, y, halfExtent), halfExtent * 2f, height, thickness, false, "Wall_S"),
                world.SpawnBox(new Vector3(halfExtent, y, 0f), thickness, height, halfExtent * 2f, false, "Wall_E"),
                world.SpawnBox(new Vector3(-halfExtent, y, 0f), thickness, height, halfExtent * 2f, false, "Wall_W"),
            };
            return walls;
        }

        /// <summary>
        /// A stack of dynamic boxes. <paramref name="basePosition"/> is the centre of the
        /// bottom box; each box is <paramref name="size"/> per side with an optional gap.
        /// </summary>
        public static List<Element> SpawnBoxStack(this World world, Vector3 basePosition, int count, float size = 1f, float gap = 0f, string? namePrefix = "StackBox")
        {
            var boxes = new List<Element>(count);
            for (int i = 0; i < count; i++)
            {
                float y = basePosition.Y + i * (size + gap);
                boxes.Add(world.SpawnBox(
                    new Vector3(basePosition.X, y, basePosition.Z),
                    size, size, size,
                    dynamic: true,
                    name: $"{namePrefix}_{i}"));
            }
            return boxes;
        }

        /// <summary>A grid of boxes on the XZ plane.</summary>
        public static List<Element> SpawnGrid(this World world, Vector3 origin, int cols, int rows, float spacing = 2f, float size = 1f, bool dynamic = false)
        {
            var boxes = new List<Element>(cols * rows);
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    boxes.Add(world.SpawnBox(
                        new Vector3(origin.X + c * spacing, origin.Y, origin.Z + r * spacing),
                        size, size, size,
                        dynamic: dynamic,
                        name: $"Grid_{c}_{r}"));
                }
            }
            return boxes;
        }

        /// <summary>An empty element marked with <see cref="SpawnPointComponent"/>.</summary>
        public static Element AddSpawnPoint(this World world, Vector3 position, string? name = null)
        {
            var element = new Element(name ?? "SpawnPoint");
            element.SetTransform(position);
            element.AddComponent(new SpawnPointComponent());
            world.AddElement(element);
            return element;
        }

        /// <summary>A directional light oriented by pitch/yaw degrees.</summary>
        public static Element AddSun(this World world, float pitchDegrees = 50f, float yawDegrees = 30f, float r = 1f, float g = 1f, float b = 1f, float energy = 1f, string? name = null)
        {
            var element = new Element(name ?? "Sun");
            element.SetTransform(Vector3.Zero, new Vector3(pitchDegrees, yawDegrees, 0f));
            element.AddDirectionalLight(r, g, b, energy);
            world.AddElement(element);
            return element;
        }
    }
}
