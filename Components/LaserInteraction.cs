using System;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;
using System.Numerics;

namespace V12.Basic.Components
{
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

        public static Func<long, IWorldElement> ElementResolver;

        public void EndGrabAndPersist(long entityId, float x, float y, float z, float yawDegrees)
        {
            try
            {
                if (ElementResolver == null) return;
                var elem = ElementResolver(entityId);
                if (elem == null) return;
                var lt = elem.LocalTransform;
                lt.Position = new Vector3(x, y, z);
                lt.Rotation = Quaternion.CreateFromYawPitchRoll(yawDegrees * (MathF.PI / 180f), 0, 0);
                elem.LocalTransform = lt;
            }
            catch { }
        }
    }
}
