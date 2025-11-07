using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace GOE
{
    /// <summary>
    /// DOTS-style force calculation system with two-zone physics model
    /// Zone 1: Collision (inverse-square repulsion)
    /// Zone 2: Interaction (linear falloff attraction/repulsion)
    /// </summary>
    public static class GOEForceSystem
    {
        /// <summary>
        /// Calculate forces between all entities using spatial hash
        /// Implements two-zone force model from DOTS Physics Design
        /// </summary>
        [BurstCompile]
        public struct CalculateForcesJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> positions;
            [ReadOnly] public NativeArray<PhysicsProperties> properties;
            [ReadOnly] public NativeArray<int> groupIDs;
            [ReadOnly] public NativeArray<bool> isActive;

            // Spatial hash for optimization
            [ReadOnly] public NativeMultiHashMap<int3, int> spatialGrid;
            [ReadOnly] public NativeArray<int3> cellIndices;
            [ReadOnly] public float cellSize;

            // Contact rules (from original GOE system)
            [ReadOnly] public NativeArray<ContactRule> contactRules;
            [ReadOnly] public int contactRuleCount;

            [WriteOnly] public NativeArray<float3> forces;

            public void Execute(int index)
            {
                // Skip inactive entities
                if (!isActive[index])
                {
                    forces[index] = float3.zero;
                    return;
                }

                float3 position = positions[index];
                PhysicsProperties props = properties[index];
                int groupID = groupIDs[index];
                int3 cellIndex = cellIndices[index];

                float3 totalForce = float3.zero;

                // Check 27 neighboring cells (including own cell)
                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int z = -1; z <= 1; z++)
                        {
                            int3 neighborCell = cellIndex + new int3(x, y, z);

                            // Iterate through all entities in this cell
                            if (spatialGrid.TryGetFirstValue(neighborCell, out int otherIndex, out var iterator))
                            {
                                do
                                {
                                    if (otherIndex == index || !isActive[otherIndex])
                                        continue;

                                    float3 otherPos = positions[otherIndex];
                                    PhysicsProperties otherProps = properties[otherIndex];
                                    int otherGroupID = groupIDs[otherIndex];

                                    // Calculate physics force (always applies)
                                    totalForce += CalculatePhysicsForce(
                                        position, otherPos,
                                        props, otherProps
                                    );

                                    // Calculate contact rule forces (group-based)
                                    totalForce += CalculateContactForce(
                                        position, otherPos,
                                        groupID, otherGroupID,
                                        props, otherProps
                                    );

                                } while (spatialGrid.TryGetNextValue(out otherIndex, ref iterator));
                            }
                        }
                    }
                }

                // Clamp force to prevent instability
                float magnitude = math.length(totalForce);
                if (magnitude > props.maxForce)
                {
                    totalForce = math.normalize(totalForce) * props.maxForce;
                }

                forces[index] = totalForce;
            }

            /// <summary>
            /// Calculate two-zone physics force (collision + interaction)
            /// Always applies between entities regardless of group
            /// </summary>
            private float3 CalculatePhysicsForce(
                float3 position,
                float3 otherPosition,
                PhysicsProperties properties,
                PhysicsProperties otherProperties)
            {
                float3 delta = position - otherPosition;
                float distance = math.length(delta);

                // Early exit for same position (avoid division by zero)
                if (distance < 0.0001f)
                    return float3.zero;

                float3 direction = delta / distance;
                float3 force = float3.zero;

                // Use larger collision radius from either entity (symmetry)
                float effectiveCollisionRadius = math.max(
                    properties.collisionRadius,
                    otherProperties.collisionRadius
                );

                // === ZONE 1: COLLISION AVOIDANCE (Short Range, Always Repulsive) ===
                if (distance < effectiveCollisionRadius)
                {
                    // Average the strengths for symmetric force
                    float strength = (properties.collisionStrength + otherProperties.collisionStrength) * 0.5f;
                    float normalizedDistance = distance / effectiveCollisionRadius;

                    // Inverse square law - very strong when close, weakens quickly
                    // The +0.01f prevents division by zero and adds stability
                    float collisionMagnitude = strength / (normalizedDistance * normalizedDistance + 0.01f);

                    force += direction * collisionMagnitude;
                }

                // === ZONE 2: INTERACTION (Long Range, Attraction or Repulsion) ===
                // Only apply if distance is beyond collision zone
                float effectiveInteractionRadius = math.max(
                    properties.interactionRadius,
                    otherProperties.interactionRadius
                );

                if (distance < effectiveInteractionRadius && distance >= effectiveCollisionRadius)
                {
                    // Average the strengths - note: can be positive (attract) or negative (repel)
                    float strength = (properties.interactionStrength + otherProperties.interactionStrength) * 0.5f;

                    // Distance from collision boundary to interaction boundary
                    float interactionZoneSize = effectiveInteractionRadius - effectiveCollisionRadius;
                    float distanceIntoZone = distance - effectiveCollisionRadius;

                    // Linear falloff from full strength at collision boundary to zero at interaction boundary
                    // This makes force weaken with distance
                    float falloff = 1f - (distanceIntoZone / interactionZoneSize);

                    // Apply force in appropriate direction
                    // Positive strength = attraction (pull toward other entity = negative direction)
                    // Negative strength = repulsion (push away = positive direction)
                    force += -direction * strength * falloff;
                }

                return force;
            }

            /// <summary>
            /// Calculate group-based contact forces using rules
            /// Only applies between specific group pairs
            /// </summary>
            private float3 CalculateContactForce(
                float3 position,
                float3 otherPosition,
                int groupID,
                int otherGroupID,
                PhysicsProperties properties,
                PhysicsProperties otherProperties)
            {
                float3 delta = position - otherPosition;
                float distance = math.length(delta);

                if (distance < 0.0001f)
                    return float3.zero;

                float3 direction = delta / distance;
                float3 force = float3.zero;

                // Check contact rules for this group pair
                for (int i = 0; i < contactRuleCount; i++)
                {
                    ContactRule rule = contactRules[i];

                    // Check if this rule applies to this entity pair
                    if (rule.sourceGroupID == groupID && rule.targetGroupID == otherGroupID)
                    {
                        // Only apply if within activation distance
                        if (distance < rule.activationDistance)
                        {
                            // Linear distance falloff
                            float falloff = 1f - (distance / rule.activationDistance);

                            switch (rule.response)
                            {
                                case ContactResponse.Attract:
                                    force += -direction * rule.responseStrength * falloff;
                                    break;

                                case ContactResponse.Repel:
                                    force += direction * rule.responseStrength * falloff;
                                    break;

                                // Boost and Slow are velocity-based, handled in integration
                            }
                        }
                    }
                }

                return force;
            }
        }

        /// <summary>
        /// Assign spatial cells for entities (first phase of spatial hash)
        /// </summary>
        [BurstCompile]
        public struct AssignCellsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> positions;
            public NativeArray<int3> cellIndices;
            public float cellSize;

            public void Execute(int index)
            {
                float3 pos = positions[index];
                cellIndices[index] = new int3(
                    (int)math.floor(pos.x / cellSize),
                    (int)math.floor(pos.y / cellSize),
                    (int)math.floor(pos.z / cellSize)
                );
            }
        }

        /// <summary>
        /// Build spatial hash map (second phase of spatial hash)
        /// </summary>
        [BurstCompile]
        public struct BuildSpatialHashJob : IJob
        {
            [ReadOnly] public NativeArray<int3> cellIndices;
            public NativeMultiHashMap<int3, int>.ParallelWriter spatialGrid;
            public int entityCount;

            public void Execute()
            {
                for (int i = 0; i < entityCount; i++)
                {
                    spatialGrid.Add(cellIndices[i], i);
                }
            }
        }
    }

    /// <summary>
    /// Contact rule structure (compatible with original GOE system)
    /// </summary>
    public struct ContactRule
    {
        public int sourceGroupID;
        public int targetGroupID;
        public ContactResponse response;
        public float responseStrength;
        public float activationDistance;
    }

    /// <summary>
    /// Contact response types
    /// </summary>
    public enum ContactResponse
    {
        Attract,
        Repel,
        Boost,  // Handled in velocity integration
        Slow    // Handled in velocity integration
    }
}
