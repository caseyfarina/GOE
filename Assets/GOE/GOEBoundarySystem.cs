using Unity.Mathematics;
using UnityEngine;

namespace GOE
{
    /// <summary>
    /// Handles boundary enforcement for GOEs to keep them within defined areas.
    /// </summary>
    public static class GOEBoundarySystem
    {
        /// <summary>
        /// Enforce boundaries for all active GOEs
        /// </summary>
        public static void EnforceBounds(GOEData[] data, Bounds bounds, BoundaryBehavior behavior)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (!data[i].isActive) continue;
                
                ref GOEData goe = ref data[i];
                
                switch (behavior)
                {
                    case BoundaryBehavior.Reflect:
                        ReflectAtBounds(ref goe, bounds);
                        break;
                        
                    case BoundaryBehavior.Wrap:
                        WrapAtBounds(ref goe, bounds);
                        break;
                        
                    case BoundaryBehavior.Dampen:
                        DampenNearBounds(ref goe, bounds);
                        break;
                }
            }
        }
        
        /// <summary>
        /// Bounce GOE off boundary walls
        /// </summary>
        private static void ReflectAtBounds(ref GOEData goe, Bounds bounds)
        {
            float3 min = bounds.min;
            float3 max = bounds.max;
            
            // X axis
            if (goe.position.x < min.x)
            {
                goe.position.x = min.x;
                goe.velocity.x = math.abs(goe.velocity.x);
            }
            else if (goe.position.x > max.x)
            {
                goe.position.x = max.x;
                goe.velocity.x = -math.abs(goe.velocity.x);
            }
            
            // Y axis
            if (goe.position.y < min.y)
            {
                goe.position.y = min.y;
                goe.velocity.y = math.abs(goe.velocity.y);
            }
            else if (goe.position.y > max.y)
            {
                goe.position.y = max.y;
                goe.velocity.y = -math.abs(goe.velocity.y);
            }
            
            // Z axis
            if (goe.position.z < min.z)
            {
                goe.position.z = min.z;
                goe.velocity.z = math.abs(goe.velocity.z);
            }
            else if (goe.position.z > max.z)
            {
                goe.position.z = max.z;
                goe.velocity.z = -math.abs(goe.velocity.z);
            }
        }
        
        /// <summary>
        /// Wrap GOE to opposite side when crossing boundary
        /// </summary>
        private static void WrapAtBounds(ref GOEData goe, Bounds bounds)
        {
            float3 min = bounds.min;
            float3 max = bounds.max;
            float3 size = bounds.size;
            
            if (goe.position.x < min.x) goe.position.x += size.x;
            if (goe.position.x > max.x) goe.position.x -= size.x;
            
            if (goe.position.y < min.y) goe.position.y += size.y;
            if (goe.position.y > max.y) goe.position.y -= size.y;
            
            if (goe.position.z < min.z) goe.position.z += size.z;
            if (goe.position.z > max.z) goe.position.z -= size.z;
        }
        
        /// <summary>
        /// Apply extra damping when GOE approaches boundary edges
        /// </summary>
        private static void DampenNearBounds(ref GOEData goe, Bounds bounds, float edgeDistance = 2f)
        {
            float3 min = bounds.min + new Vector3(edgeDistance, edgeDistance, edgeDistance);
            float3 max = bounds.max - new Vector3(edgeDistance, edgeDistance, edgeDistance);
            
            float dampenFactor = 1f;
            
            // Calculate distance to nearest edge
            if (goe.position.x < min.x || goe.position.x > max.x ||
                goe.position.y < min.y || goe.position.y > max.y ||
                goe.position.z < min.z || goe.position.z > max.z)
            {
                dampenFactor = 0.95f; // Extra damping near edges
                goe.velocity *= dampenFactor;
            }
        }
    }
}
