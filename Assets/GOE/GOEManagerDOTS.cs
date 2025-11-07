using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace GOE
{
    /// <summary>
    /// DOTS-style manager implementing the full physics design document
    /// Features:
    /// - Force-based physics (F=ma) with two-zone model
    /// - Impulse-based organic movement
    /// - Job dependency chain pipeline
    /// - Parallel transform synchronization with TransformAccessArray
    /// - Axis locking support
    /// </summary>
    public class GOEManagerDOTS : MonoBehaviour
    {
        [Header("System Configuration")]
        public GOESystemConfig systemConfig;
        public int maxEntities = 5000;
        public float cellSize = 10f; // Should be >= largest interaction radius

        [Header("Axis Locking")]
        public bool lockXAxis = false;
        public bool lockYAxis = false;
        public bool lockZAxis = false;

        [Header("Boundaries")]
        public bool enableBoundaries = true;
        public Bounds bounds = new Bounds(Vector3.zero, new Vector3(50, 50, 50));
        public BoundaryBehavior boundaryBehavior = BoundaryBehavior.Reflect;

        [Header("Performance")]
        public int jobBatchSize = 64;
        public float rotationSpeed = 5f;

        // Structure of Arrays (SoA) - Better cache performance
        private NativeArray<float3> positions;
        private NativeArray<float3> velocities;
        private NativeArray<quaternion> rotations;
        private NativeArray<PhysicsProperties> properties;
        private NativeArray<float3> accumulatedForces;

        // Entity metadata
        private NativeArray<int> groupIDs;
        private NativeArray<float> animPhases;
        private NativeArray<float> animSpeeds;
        private NativeArray<float> colorVariations;
        private NativeArray<float> scaleVariations;
        private NativeArray<bool> isActive;

        // Axis constraints
        private NativeArray<bool> constrainX;
        private NativeArray<bool> constrainY;
        private NativeArray<bool> constrainZ;

        // Spatial partitioning
        private NativeArray<int3> cellIndices;
        private NativeMultiHashMap<int3, int> spatialGrid;

        // Contact rules
        private NativeArray<ContactRule> contactRules;
        private int contactRuleCount;

        // Transform synchronization
        private TransformAccessArray transformAccessArray;
        private GameObject[] entityObjects;
        private GOEView[] entityViews;

        private int entityCount = 0;
        private uint randomSeed;

        void Start()
        {
            randomSeed = (uint)System.DateTime.Now.Ticks;
            InitializeArrays();
            LoadContactRules();
        }

        void OnDestroy()
        {
            DisposeArrays();
        }

        void FixedUpdate()
        {
            if (entityCount == 0) return;

            float dt = Time.fixedDeltaTime;

            // === JOB DEPENDENCY CHAIN PIPELINE ===
            // Each job depends on the previous, allowing parallel execution across cores

            // Phase 1: Assign spatial cells
            var assignCellsJob = new GOEForceSystem.AssignCellsJob
            {
                positions = positions,
                cellIndices = cellIndices,
                cellSize = cellSize
            };
            JobHandle assignCellsHandle = assignCellsJob.Schedule(entityCount, jobBatchSize);

            // Phase 2: Build spatial hash
            spatialGrid.Clear();
            var buildHashJob = new GOEForceSystem.BuildSpatialHashJob
            {
                cellIndices = cellIndices,
                spatialGrid = spatialGrid.AsParallelWriter(),
                entityCount = entityCount
            };
            JobHandle buildHashHandle = buildHashJob.Schedule(assignCellsHandle);

            // Phase 3: Calculate forces (heaviest computation)
            var calculateForcesJob = new GOEForceSystem.CalculateForcesJob
            {
                positions = positions,
                properties = properties,
                groupIDs = groupIDs,
                isActive = isActive,
                spatialGrid = spatialGrid,
                cellIndices = cellIndices,
                cellSize = cellSize,
                contactRules = contactRules,
                contactRuleCount = contactRuleCount,
                forces = accumulatedForces
            };
            JobHandle forcesHandle = calculateForcesJob.Schedule(entityCount, jobBatchSize, buildHashHandle);

            // Phase 4: Integrate velocity (forces + impulses + damping)
            var integrateVelocityJob = new GOEIntegrationSystem.IntegrateVelocityJob
            {
                properties = properties,
                forces = accumulatedForces,
                rotations = rotations,
                isActive = isActive,
                velocities = velocities,
                deltaTime = dt,
                randomSeed = randomSeed++
            };
            JobHandle velocityHandle = integrateVelocityJob.Schedule(entityCount, jobBatchSize, forcesHandle);

            // Phase 5: Update rotations to face movement direction
            var updateRotationsJob = new GOEIntegrationSystem.UpdateRotationsJob
            {
                velocities = velocities,
                isActive = isActive,
                rotations = rotations,
                rotationSpeed = rotationSpeed,
                deltaTime = dt
            };
            JobHandle rotationsHandle = updateRotationsJob.Schedule(entityCount, jobBatchSize, velocityHandle);

            // Phase 6: Integrate position
            var integratePositionJob = new GOEIntegrationSystem.IntegratePositionJob
            {
                velocities = velocities,
                isActive = isActive,
                constrainX = constrainX,
                constrainY = constrainY,
                constrainZ = constrainZ,
                positions = positions,
                deltaTime = dt
            };
            JobHandle positionHandle = integratePositionJob.Schedule(entityCount, jobBatchSize, rotationsHandle);

            // Phase 7: Enforce boundaries
            JobHandle boundaryHandle = positionHandle;
            if (enableBoundaries)
            {
                var boundaryJob = new EnforceBoundariesJob
                {
                    positions = positions,
                    velocities = velocities,
                    isActive = isActive,
                    boundsMin = bounds.min,
                    boundsMax = bounds.max,
                    behavior = boundaryBehavior
                };
                boundaryHandle = boundaryJob.Schedule(entityCount, jobBatchSize, positionHandle);
            }

            // Phase 8: Update animations
            var updateAnimJob = new GOEIntegrationSystem.UpdateAnimationJob
            {
                velocities = velocities,
                isActive = isActive,
                animSpeeds = animSpeeds,
                animPhases = animPhases,
                deltaTime = dt
            };
            JobHandle animHandle = updateAnimJob.Schedule(entityCount, jobBatchSize, boundaryHandle);

            // Phase 9: Synchronize transforms (parallel, Burst-compiled)
            var syncTransformJob = new SyncTransformsJob
            {
                positions = positions,
                rotations = rotations,
                scaleVariations = scaleVariations
            };
            JobHandle syncHandle = syncTransformJob.Schedule(transformAccessArray, animHandle);

            // Complete all jobs
            syncHandle.Complete();

            // Phase 10: Update visual properties (main thread - MaterialPropertyBlocks)
            UpdateVisualProperties();
        }

        private void UpdateVisualProperties()
        {
            for (int i = 0; i < entityCount; i++)
            {
                if (entityViews[i] != null && isActive[i])
                {
                    entityViews[i].AnimateWings(animPhases[i]);
                }
            }
        }

        /// <summary>
        /// Burst-compiled transform sync job using TransformAccessArray
        /// Much faster than main thread loop
        /// </summary>
        [BurstCompile]
        struct SyncTransformsJob : IJobParallelForTransform
        {
            [ReadOnly] public NativeArray<float3> positions;
            [ReadOnly] public NativeArray<quaternion> rotations;
            [ReadOnly] public NativeArray<float> scaleVariations;

            public void Execute(int index, TransformAccess transform)
            {
                transform.position = positions[index];
                transform.rotation = rotations[index];
                transform.localScale = Vector3.one * scaleVariations[index];
            }
        }

        /// <summary>
        /// Boundary enforcement job
        /// </summary>
        [BurstCompile]
        struct EnforceBoundariesJob : IJobParallelFor
        {
            public NativeArray<float3> positions;
            public NativeArray<float3> velocities;
            [ReadOnly] public NativeArray<bool> isActive;
            [ReadOnly] public float3 boundsMin;
            [ReadOnly] public float3 boundsMax;
            [ReadOnly] public BoundaryBehavior behavior;

            public void Execute(int index)
            {
                if (!isActive[index]) return;

                float3 pos = positions[index];
                float3 vel = velocities[index];
                bool changed = false;

                switch (behavior)
                {
                    case BoundaryBehavior.Reflect:
                        float restitution = 0.8f;
                        if (pos.x < boundsMin.x) { pos.x = boundsMin.x; vel.x *= -restitution; changed = true; }
                        if (pos.x > boundsMax.x) { pos.x = boundsMax.x; vel.x *= -restitution; changed = true; }
                        if (pos.y < boundsMin.y) { pos.y = boundsMin.y; vel.y *= -restitution; changed = true; }
                        if (pos.y > boundsMax.y) { pos.y = boundsMax.y; vel.y *= -restitution; changed = true; }
                        if (pos.z < boundsMin.z) { pos.z = boundsMin.z; vel.z *= -restitution; changed = true; }
                        if (pos.z > boundsMax.z) { pos.z = boundsMax.z; vel.z *= -restitution; changed = true; }
                        break;

                    case BoundaryBehavior.Wrap:
                        float3 size = boundsMax - boundsMin;
                        if (pos.x < boundsMin.x) pos.x = boundsMax.x;
                        if (pos.x > boundsMax.x) pos.x = boundsMin.x;
                        if (pos.y < boundsMin.y) pos.y = boundsMax.y;
                        if (pos.y > boundsMax.y) pos.y = boundsMin.y;
                        if (pos.z < boundsMin.z) pos.z = boundsMax.z;
                        if (pos.z > boundsMax.z) pos.z = boundsMin.z;
                        changed = true;
                        break;

                    case BoundaryBehavior.Dampen:
                        float dampFactor = 0.9f;
                        float margin = 5f;
                        if (pos.x < boundsMin.x + margin || pos.x > boundsMax.x - margin) vel.x *= dampFactor;
                        if (pos.y < boundsMin.y + margin || pos.y > boundsMax.y - margin) vel.y *= dampFactor;
                        if (pos.z < boundsMin.z + margin || pos.z > boundsMax.z - margin) vel.z *= dampFactor;
                        pos = math.clamp(pos, boundsMin, boundsMax);
                        changed = true;
                        break;
                }

                if (changed)
                {
                    positions[index] = pos;
                    velocities[index] = vel;
                }
            }
        }

        private void InitializeArrays()
        {
            positions = new NativeArray<float3>(maxEntities, Allocator.Persistent);
            velocities = new NativeArray<float3>(maxEntities, Allocator.Persistent);
            rotations = new NativeArray<quaternion>(maxEntities, Allocator.Persistent);
            properties = new NativeArray<PhysicsProperties>(maxEntities, Allocator.Persistent);
            accumulatedForces = new NativeArray<float3>(maxEntities, Allocator.Persistent);

            groupIDs = new NativeArray<int>(maxEntities, Allocator.Persistent);
            animPhases = new NativeArray<float>(maxEntities, Allocator.Persistent);
            animSpeeds = new NativeArray<float>(maxEntities, Allocator.Persistent);
            colorVariations = new NativeArray<float>(maxEntities, Allocator.Persistent);
            scaleVariations = new NativeArray<float>(maxEntities, Allocator.Persistent);
            isActive = new NativeArray<bool>(maxEntities, Allocator.Persistent);

            constrainX = new NativeArray<bool>(maxEntities, Allocator.Persistent);
            constrainY = new NativeArray<bool>(maxEntities, Allocator.Persistent);
            constrainZ = new NativeArray<bool>(maxEntities, Allocator.Persistent);

            cellIndices = new NativeArray<int3>(maxEntities, Allocator.Persistent);
            spatialGrid = new NativeMultiHashMap<int3, int>(maxEntities * 4, Allocator.Persistent);

            entityObjects = new GameObject[maxEntities];
            entityViews = new GOEView[maxEntities];

            transformAccessArray = new TransformAccessArray(0);
        }

        private void DisposeArrays()
        {
            if (positions.IsCreated) positions.Dispose();
            if (velocities.IsCreated) velocities.Dispose();
            if (rotations.IsCreated) rotations.Dispose();
            if (properties.IsCreated) properties.Dispose();
            if (accumulatedForces.IsCreated) accumulatedForces.Dispose();

            if (groupIDs.IsCreated) groupIDs.Dispose();
            if (animPhases.IsCreated) animPhases.Dispose();
            if (animSpeeds.IsCreated) animSpeeds.Dispose();
            if (colorVariations.IsCreated) colorVariations.Dispose();
            if (scaleVariations.IsCreated) scaleVariations.Dispose();
            if (isActive.IsCreated) isActive.Dispose();

            if (constrainX.IsCreated) constrainX.Dispose();
            if (constrainY.IsCreated) constrainY.Dispose();
            if (constrainZ.IsCreated) constrainZ.Dispose();

            if (cellIndices.IsCreated) cellIndices.Dispose();
            if (spatialGrid.IsCreated) spatialGrid.Dispose();

            if (contactRules.IsCreated) contactRules.Dispose();

            if (transformAccessArray.isCreated) transformAccessArray.Dispose();
        }

        private void LoadContactRules()
        {
            if (systemConfig == null || systemConfig.contactRules == null)
            {
                contactRules = new NativeArray<ContactRule>(0, Allocator.Persistent);
                contactRuleCount = 0;
                return;
            }

            contactRuleCount = systemConfig.contactRules.Count;
            contactRules = new NativeArray<ContactRule>(contactRuleCount, Allocator.Persistent);

            for (int i = 0; i < contactRuleCount; i++)
            {
                var rule = systemConfig.contactRules[i];
                contactRules[i] = new ContactRule
                {
                    sourceGroupID = rule.sourceGroup.groupID,
                    targetGroupID = rule.targetGroup.groupID,
                    response = rule.response,
                    responseStrength = rule.responseStrength,
                    activationDistance = rule.activationDistance
                };
            }
        }

        /// <summary>
        /// Spawn entity with full physics properties and axis locking
        /// </summary>
        public int SpawnEntity(Vector3 position, GOEGroupConfig group, Vector3? initialVelocity = null)
        {
            if (entityCount >= maxEntities)
            {
                Debug.LogWarning("Maximum entity count reached!");
                return -1;
            }

            int index = entityCount++;

            // Create GameObject
            GameObject go = Instantiate(group.prefab, position, Quaternion.identity);
            go.name = $"{group.groupName}_{index}";
            entityObjects[index] = go;
            entityViews[index] = go.GetComponent<GOEView>();

            // Add to transform array
            transformAccessArray.Add(go.transform);

            // Initialize transform data
            positions[index] = position;
            velocities[index] = initialVelocity ?? float3.zero;
            rotations[index] = quaternion.identity;

            // Initialize physics properties from DOTS design + impulse movement
            PhysicsProperties physProps = PhysicsProperties.Default();
            physProps.mass = group.mass;
            physProps.damping = group.damping;
            physProps.collisionRadius = group.collisionRadius;
            physProps.collisionStrength = group.collisionStrength;
            physProps.interactionRadius = group.interactionRadius;
            physProps.interactionStrength = group.interactionStrength;
            physProps.maxForce = group.maxForce;
            physProps.impulseStrength = group.impulseStrength;
            physProps.minImpulseInterval = group.minImpulseInterval;
            physProps.maxImpulseInterval = group.maxImpulseInterval;
            physProps.impulseTimer = UnityEngine.Random.Range(physProps.minImpulseInterval, physProps.maxImpulseInterval);
            properties[index] = physProps;

            // Initialize metadata
            groupIDs[index] = group.groupID;
            animPhases[index] = UnityEngine.Random.Range(0f, 2f * Mathf.PI);
            animSpeeds[index] = group.animationSpeed;
            colorVariations[index] = UnityEngine.Random.Range(0f, 1f);
            scaleVariations[index] = UnityEngine.Random.Range(group.minScale, group.maxScale);
            isActive[index] = true;

            // Apply axis locking (global or per-entity)
            constrainX[index] = lockXAxis;
            constrainY[index] = lockYAxis;
            constrainZ[index] = lockZAxis;

            // Reset accumulated forces
            accumulatedForces[index] = float3.zero;

            return index;
        }

        /// <summary>
        /// Set axis constraints for a specific entity
        /// </summary>
        public void SetAxisConstraints(int index, bool lockX, bool lockY, bool lockZ)
        {
            if (index < 0 || index >= entityCount) return;
            constrainX[index] = lockX;
            constrainY[index] = lockY;
            constrainZ[index] = lockZ;
        }

        void OnDrawGizmosSelected()
        {
            if (enableBoundaries)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireCube(bounds.center, bounds.size);
            }
        }
    }

    public enum BoundaryBehavior
    {
        Reflect,
        Wrap,
        Dampen
    }
}
