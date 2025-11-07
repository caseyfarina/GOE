using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace GOE
{
    /// <summary>
    /// Burst-compiled system for calculating soft contact influences between GOEs.
    /// Supports both attraction and repulsion based on group rules.
    /// </summary>
    public static class GOEContactSystem
    {
        /// <summary>
        /// Apply contact influences between all GOEs based on group rules (NativeArray version - minimal copy)
        /// </summary>
        public static void ProcessContacts(
            NativeArray<GOEData> data,
            GOESystemConfig config,
            GOESpatialHash spatialHash,
            float deltaTime)
        {
            int count = data.Length;

            // Only allocate arrays we actually need (influences output, positions for spatial hash)
            NativeArray<float3> influences = new NativeArray<float3>(count, Allocator.TempJob);
            NativeArray<float3> positions = new NativeArray<float3>(count, Allocator.TempJob);

            // Build contact rule lookup
            NativeArray<ContactInfluence> influences_lookup = BuildContactRuleLookup(config, Allocator.TempJob);

            // Calculate maximum activation radius from all rules
            float maxRadius = 0f;
            for (int i = 0; i < influences_lookup.Length; i++)
            {
                if (influences_lookup[i].radius > maxRadius)
                    maxRadius = influences_lookup[i].radius;
            }

            // Extract positions for spatial hash (unavoidable - spatial hash needs positions)
            for (int i = 0; i < count; i++)
            {
                positions[i] = data[i].position;
            }

            // Rebuild spatial hash
            spatialHash.Rebuild(positions);

            // Schedule job - pass persistent data array directly
            var job = new ContactInfluenceJob
            {
                data = data,  // Read-only access, no copy needed!
                influences = influences,
                cellStarts = spatialHash.CellStarts,
                cellCounts = spatialHash.CellCounts,
                entityIndices = spatialHash.EntityIndices,
                contactRules = influences_lookup,
                cellSize = spatialHash.CellSize,
                gridDimensions = spatialHash.GridDimensions,
                deltaTime = deltaTime,
                maxGroupID = config.groups.Length,
                maxActivationRadius = maxRadius  // Dynamic search radius
            };

            JobHandle handle = job.Schedule(count, 32);
            handle.Complete();

            // Apply influences back to data (only thing that needs modification)
            for (int i = 0; i < count; i++)
            {
                if (math.lengthsq(influences[i]) > 0.001f)
                {
                    GOEData temp = data[i];
                    temp.velocity += influences[i];
                    data[i] = temp;
                }
            }

            // Cleanup temporary arrays only
            influences.Dispose();
            influences_lookup.Dispose();
            positions.Dispose();
        }
        
        /// <summary>
        /// Build flat lookup array of contact rules for all group pairs
        /// </summary>
        private static NativeArray<ContactInfluence> BuildContactRuleLookup(
            GOESystemConfig config,
            Allocator allocator)
        {
            int maxGroups = config.groups.Length;
            int totalPairs = maxGroups * maxGroups;
            
            NativeArray<ContactInfluence> lookup = new NativeArray<ContactInfluence>(
                totalPairs,
                allocator
            );
            
            // Initialize all to no influence
            for (int i = 0; i < totalPairs; i++)
            {
                lookup[i] = new ContactInfluence
                {
                    response = ContactResponse.Slow,
                    strength = 0f,
                    radius = 0f
                };
            }
            
            // Fill in actual rules (BIDIRECTIONAL)
            for (int groupIdx = 0; groupIdx < config.groups.Length; groupIdx++)
            {
                GOEGroupConfig group = config.groups[groupIdx];

                if (group.contactRules == null) continue;

                foreach (ContactRule rule in group.contactRules)
                {
                    ContactInfluence influence = new ContactInfluence
                    {
                        response = rule.response,
                        strength = rule.responseStrength,
                        radius = rule.activationDistance
                    };

                    // Forward direction: self → target
                    int forwardIndex = group.groupID * maxGroups + rule.targetGroupID;
                    lookup[forwardIndex] = influence;

                    // Reverse direction: target → self (BIDIRECTIONAL FIX)
                    // This makes "Group A attracts Group B" work in both directions
                    int reverseIndex = rule.targetGroupID * maxGroups + group.groupID;
                    lookup[reverseIndex] = influence;
                }
            }
            
            return lookup;
        }
    }
    
    /// <summary>
    /// Flattened contact influence data for Burst
    /// </summary>
    public struct ContactInfluence
    {
        public ContactResponse response;
        public float strength;
        public float radius;
    }
    
    /// <summary>
    /// Burst-compiled job for calculating contact influences
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct ContactInfluenceJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<GOEData> data;
        [WriteOnly] public NativeArray<float3> influences;

        // Spatial hash data
        [ReadOnly] public NativeArray<int> cellStarts;
        [ReadOnly] public NativeArray<int> cellCounts;
        [ReadOnly] public NativeArray<int> entityIndices;

        [ReadOnly] public NativeArray<ContactInfluence> contactRules;

        public float cellSize;
        public int3 gridDimensions;
        public float deltaTime;
        public int maxGroupID;
        public float maxActivationRadius;  // Maximum activation distance from all rules
        
        public void Execute(int index)
        {
            GOEData self = data[index];
            if (!self.isActive)
            {
                influences[index] = float3.zero;
                return;
            }
            
            float3 totalInfluence = float3.zero;

            // Get cell coordinates
            int3 selfCell = GetCellCoords(self.position);

            // Calculate search radius based on maximum activation distance
            // +1 to ensure we cover the full radius even at cell boundaries
            int cellSearchRadius = (int)math.ceil(maxActivationRadius / cellSize) + 1;

            // Check neighboring cells (dynamic radius based on activation distances)
            for (int z = -cellSearchRadius; z <= cellSearchRadius; z++)
            {
                for (int y = -cellSearchRadius; y <= cellSearchRadius; y++)
                {
                    for (int x = -cellSearchRadius; x <= cellSearchRadius; x++)
                    {
                        int3 neighborCell = selfCell + new int3(x, y, z);
                        
                        // Skip out of bounds
                        if (math.any(neighborCell < int3.zero) || 
                            math.any(neighborCell >= gridDimensions))
                            continue;
                        
                        int cellIndex = GetCellIndex(neighborCell);
                        
                        // Check all entities in this cell
                        int start = cellStarts[cellIndex];
                        int count = cellCounts[cellIndex];
                        
                        for (int i = 0; i < count; i++)
                        {
                            int otherIndex = entityIndices[start + i];
                            
                            // Skip self
                            if (otherIndex == index) continue;
                            
                            GOEData other = data[otherIndex];
                            if (!other.isActive) continue;
                            
                            // Get contact rule
                            int ruleIndex = self.groupID * maxGroupID + other.groupID;
                            ContactInfluence rule = contactRules[ruleIndex];
                            
                            if (rule.response == ContactResponse.Slow) continue;
                            
                            // Calculate distance
                            float3 toOther = other.position - self.position;
                            float distanceSq = math.lengthsq(toOther);
                            float radiusSq = rule.radius * rule.radius;
                            
                            // Only apply influence if within radius
                            if (distanceSq < radiusSq && distanceSq > 0.001f)
                            {
                                float distance = math.sqrt(distanceSq);
                                float3 direction = toOther / distance;
                                
                                // Calculate influence strength (stronger when closer)
                                float normalizedDist = distance / rule.radius;
                                float falloff = 1f - normalizedDist;  // 1.0 at center, 0.0 at edge
                                
                                float3 influence = CalculateInfluence(
                                    rule.response,
                                    direction,
                                    falloff,
                                    rule.strength,
                                    deltaTime
                                );
                                
                                totalInfluence += influence;
                            }
                        }
                    }
                }
            }
            
            influences[index] = totalInfluence;
        }
        
        private float3 CalculateInfluence(
            ContactResponse response,
            float3 direction,
            float falloff,
            float strength,
            float dt)
        {
            switch (response)
            {
                case ContactResponse.Repel:
                    // Push away from other
                    return -direction * strength * falloff * dt;
                    
                case ContactResponse.Attract:
                    // Pull toward other
                    return direction * strength * falloff * dt;
                    
                case ContactResponse.Boost:
                    // Boost in current direction (not toward/away from other)
                    // This gets handled differently - return zero here
                    return float3.zero;
                    
                case ContactResponse.Slow:
                    // Damping is handled in main update
                    return float3.zero;
                    
                default:
                    return float3.zero;
            }
        }
        
        private int3 GetCellCoords(float3 position)
        {
            return new int3(
                (int)math.floor(position.x / cellSize),
                (int)math.floor(position.y / cellSize),
                (int)math.floor(position.z / cellSize)
            );
        }
        
        private int GetCellIndex(int3 cell)
        {
            return cell.x + cell.y * gridDimensions.x + 
                   cell.z * gridDimensions.x * gridDimensions.y;
        }
    }
}
