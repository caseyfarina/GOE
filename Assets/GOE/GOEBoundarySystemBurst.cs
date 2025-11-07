using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace GOE
{
    /// <summary>
    /// Burst-compiled boundary enforcement
    /// </summary>
    public static class GOEBoundarySystemBurst
    {
        /// <summary>
        /// Enforce boundaries using Burst compilation (NativeArray version - zero copy)
        /// </summary>
        public static void EnforceBoundsBurst(
            NativeArray<GOEData> data,
            Bounds bounds,
            BoundaryBehavior behavior)
        {
            // Schedule job directly on persistent NativeArray - no copying needed!
            var job = new BoundaryEnforcementJob
            {
                data = data,
                boundsMin = bounds.min,
                boundsMax = bounds.max,
                boundsSize = bounds.size,
                behavior = behavior
            };

            JobHandle handle = job.Schedule(data.Length, 32);
            handle.Complete();
        }
    }
    
    /// <summary>
    /// Burst-compiled boundary enforcement job
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct BoundaryEnforcementJob : IJobParallelFor
    {
        public NativeArray<GOEData> data;
        public float3 boundsMin;
        public float3 boundsMax;
        public float3 boundsSize;
        public BoundaryBehavior behavior;
        
        public void Execute(int index)
        {
            if (!data[index].isActive) return;
            
            GOEData goe = data[index];
            
            switch (behavior)
            {
                case BoundaryBehavior.Reflect:
                    ReflectAtBounds(ref goe);
                    break;
                    
                case BoundaryBehavior.Wrap:
                    WrapAtBounds(ref goe);
                    break;
                    
                case BoundaryBehavior.Dampen:
                    DampenNearBounds(ref goe);
                    break;
            }
            
            data[index] = goe;
        }
        
        private void ReflectAtBounds(ref GOEData goe)
        {
            // X axis (only enforce if not constrained)
            if (!goe.constrainX)
            {
                if (goe.position.x < boundsMin.x)
                {
                    goe.position.x = boundsMin.x;
                    goe.velocity.x = math.abs(goe.velocity.x);
                }
                else if (goe.position.x > boundsMax.x)
                {
                    goe.position.x = boundsMax.x;
                    goe.velocity.x = -math.abs(goe.velocity.x);
                }
            }

            // Y axis (only enforce if not constrained)
            if (!goe.constrainY)
            {
                if (goe.position.y < boundsMin.y)
                {
                    goe.position.y = boundsMin.y;
                    goe.velocity.y = math.abs(goe.velocity.y);
                }
                else if (goe.position.y > boundsMax.y)
                {
                    goe.position.y = boundsMax.y;
                    goe.velocity.y = -math.abs(goe.velocity.y);
                }
            }

            // Z axis (only enforce if not constrained)
            if (!goe.constrainZ)
            {
                if (goe.position.z < boundsMin.z)
                {
                    goe.position.z = boundsMin.z;
                    goe.velocity.z = math.abs(goe.velocity.z);
                }
                else if (goe.position.z > boundsMax.z)
                {
                    goe.position.z = boundsMax.z;
                    goe.velocity.z = -math.abs(goe.velocity.z);
                }
            }
        }
        
        private void WrapAtBounds(ref GOEData goe)
        {
            // Only wrap on non-constrained axes
            if (!goe.constrainX)
            {
                if (goe.position.x < boundsMin.x) goe.position.x += boundsSize.x;
                if (goe.position.x > boundsMax.x) goe.position.x -= boundsSize.x;
            }

            if (!goe.constrainY)
            {
                if (goe.position.y < boundsMin.y) goe.position.y += boundsSize.y;
                if (goe.position.y > boundsMax.y) goe.position.y -= boundsSize.y;
            }

            if (!goe.constrainZ)
            {
                if (goe.position.z < boundsMin.z) goe.position.z += boundsSize.z;
                if (goe.position.z > boundsMax.z) goe.position.z -= boundsSize.z;
            }
        }
        
        private void DampenNearBounds(ref GOEData goe)
        {
            const float edgeDistance = 2f;
            float3 min = boundsMin + edgeDistance;
            float3 max = boundsMax - edgeDistance;

            // Check if near any edge (only on non-constrained axes)
            bool nearEdge = false;

            if (!goe.constrainX && (goe.position.x < min.x || goe.position.x > max.x))
                nearEdge = true;
            if (!goe.constrainY && (goe.position.y < min.y || goe.position.y > max.y))
                nearEdge = true;
            if (!goe.constrainZ && (goe.position.z < min.z || goe.position.z > max.z))
                nearEdge = true;

            if (nearEdge)
            {
                const float dampenFactor = 0.95f;
                goe.velocity *= dampenFactor;
            }
        }
    }
}
