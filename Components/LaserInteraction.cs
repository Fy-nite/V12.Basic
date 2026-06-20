using System;
using V12.Components;
using V12.Core.Core.Interfaces;

namespace V12.Basic.Components
{
    /// <summary>
    /// Core interaction helper for laser/grab/select. Glue code should call into
    /// this to update pointing/select/grab state; on grab end this will persist
    /// transforms to the authoritative TransformComponent for the element.
    /// </summary>
    public class LaserInteraction
    {
        public bool IsPointing { get; private set; }
        public bool IsSelecting { get; private set; }
        public long? HitEntityId { get; private set; }
        public float HitX { get; private set; }
        public float HitY { get; private set; }
        public float HitZ { get; private set; }
        public float HitNX { get; private set; }
        public float HitNY { get; private set; }
        public float HitNZ { get; private set; }

        public void UpdatePointing(long? hitEntityId, float x, float y, float z, float nx, float ny, float nz)
        {
            HitEntityId = hitEntityId;
            HitX = x; HitY = y; HitZ = z;
            HitNX = nx; HitNY = ny; HitNZ = nz;
            IsPointing = hitEntityId.HasValue;
        }

        public void ClearPointing()
        {
            HitEntityId = null;
            IsPointing = false;
        }

        public void BeginSelect() => IsSelecting = true;
        public void EndSelect() => IsSelecting = false;

        /// <summary>
        /// Called by glue when a grab is released. This will write the provided world
        /// position/rotation back into the authoritative TransformComponent for the
        /// element with id <paramref name="entityId"/> if a resolver has been
        /// configured and the element is found.
        /// </summary>
        public static Func<long, IWorldElement> ElementResolver;

        public void EndGrabAndPersist(long entityId, float x, float y, float z, float yawDegrees)
        {
            try
            {
                if (ElementResolver == null) return;
                var elem = ElementResolver(entityId);
                if (elem == null) return;
                var tc = elem.GetComponent("Transform") as TransformComponent;
                if (tc == null) return;
                tc.SetPosition(x, y, z);
                tc.Rotation = yawDegrees;
            }
            catch { }
        }
    }
}
