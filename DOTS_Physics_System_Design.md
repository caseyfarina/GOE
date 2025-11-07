# Data-Oriented Physics System: Soft-Body Repulsion and Attraction

## Overview

This document outlines best practices for implementing a scalable, data-oriented physics system for entities that exert distance-based repulsion and optional attraction forces on each other. The implementation leverages Unity's Job System and Burst compiler for high-performance parallel processing, without requiring the full ECS/DOTS stack.

## System Requirements

- **Primary Behavior**: Short-range collision avoidance - entities strongly repel each other within a small radius to prevent overlap
- **Secondary Behavior**: Long-range interaction - optional attraction OR repulsion at a larger radius, with force weakening by distance
- **Performance Goal**: Handle thousands of entities efficiently using parallel processing
- **Collision Type**: Soft forces only (no rigid body collision resolution)

## Architecture Components

### 1. Component Data Structures

```csharp
using Unity.Collections;
using Unity.Mathematics;

// Pure data structures - Burst compatible
public struct PhysicsProperties
{
    public float Mass;
    
    // Small radius: collision avoidance (always repulsive)
    public float CollisionRadius;      
    public float CollisionStrength;
    
    // Large radius: interaction zone (attraction OR repulsion)
    public float InteractionRadius;    
    public float InteractionStrength;  // Positive = attraction, Negative = repulsion
    
    public float MaxForce;             // Force clamping for stability
    public float Damping;              // Velocity damping coefficient
}

public struct PhysicsVelocity
{
    public float3 Linear;
}

public struct PhysicsForce
{
    public float3 Accumulated;  // Reset each frame
}

// For spatial partitioning
public struct SpatialCell
{
    public int3 CellIndex;
}
```

**Radius Relationship**:
- `CollisionRadius` should be small (e.g., 0.5 - 2.0 units) - prevents entities from overlapping
- `InteractionRadius` should be larger (e.g., 5.0 - 20.0 units) - defines influence range
- **Requirement**: `InteractionRadius > CollisionRadius` (enforced in your initialization code)

**Design Principles**:
- Use `float3` instead of `Vector3` for Burst compatibility
- Keep structs blittable (no managed references)
- Store in `NativeArray<T>` for job access
- Separate data from behavior (MonoBehaviour holds game logic, not in job structs)

### 2. Spatial Partitioning Strategy

**Critical for Performance**: With N entities, naive O(N²) checks become prohibitively expensive beyond a few hundred entities.

#### Recommended Approach: Spatial Hash Grid with NativeMultiHashMap

```csharp
using Unity.Collections;
using Unity.Mathematics;

public class SpatialHashGrid
{
    public float CellSize;
    public NativeMultiHashMap<int3, int> Grid;  // Cell coordinates -> Entity indices
    
    public SpatialHashGrid(float cellSize, int initialCapacity)
    {
        CellSize = cellSize;
        Grid = new NativeMultiHashMap<int3, int>(initialCapacity, Allocator.Persistent);
    }
    
    public void Dispose()
    {
        if (Grid.IsCreated)
            Grid.Dispose();
    }
    
    public int3 GetCellIndex(float3 position)
    {
        return new int3(
            (int)math.floor(position.x / CellSize),
            (int)math.floor(position.y / CellSize),
            (int)math.floor(position.z / CellSize)
        );
    }
}
```

**Benefits**:
- `NativeMultiHashMap` is Burst-compatible and thread-safe for parallel reads
- Reduces checks from O(N²) to approximately O(N × k) where k is average entities per cell
- Can be passed into jobs with `[ReadOnly]` attribute
- No GC allocations

**Implementation Pattern**:
1. Divide world space into uniform grid cells
2. Each frame, clear and repopulate the hash map
3. Only check entities in the same cell and adjacent cells (27 cells in 3D, 9 in 2D)

**NativeMultiHashMap Notes**:
- Allows multiple values (entity indices) per key (cell coordinate)
- Use `TryGetFirstValue` and `TryGetNextValue` to iterate through entities in a cell
- Thread-safe for parallel writes if using different keys, parallel reads always safe

#### Alternative: Multi-Box Pruning or BVH

For highly non-uniform distributions, consider bounding volume hierarchies, but these add complexity and may not parallelize as cleanly.

### 3. System Processing Pipeline with Unity Jobs

#### Data Layout in MonoBehaviour

```csharp
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;

public class PhysicsManager : MonoBehaviour
{
    // Persistent NativeArrays - allocated once, reused each frame
    private NativeArray<float3> positions;
    private NativeArray<PhysicsProperties> properties;
    private NativeArray<PhysicsVelocity> velocities;
    private NativeArray<PhysicsForce> forces;
    private NativeArray<SpatialCell> cells;
    
    private NativeMultiHashMap<int3, int> spatialGrid;
    
    private int entityCount;
    private float cellSize;
    
    void Start()
    {
        int maxEntities = 10000;
        cellSize = 5f; // Should be >= largest InteractionRadius
        
        // Allocate persistent arrays
        positions = new NativeArray<float3>(maxEntities, Allocator.Persistent);
        properties = new NativeArray<PhysicsProperties>(maxEntities, Allocator.Persistent);
        velocities = new NativeArray<PhysicsVelocity>(maxEntities, Allocator.Persistent);
        forces = new NativeArray<PhysicsForce>(maxEntities, Allocator.Persistent);
        cells = new NativeArray<SpatialCell>(maxEntities, Allocator.Persistent);
        
        spatialGrid = new NativeMultiHashMap<int3, int>(maxEntities * 4, Allocator.Persistent);
    }
    
    void OnDestroy()
    {
        // Clean up NativeArrays
        if (positions.IsCreated) positions.Dispose();
        if (properties.IsCreated) properties.Dispose();
        if (velocities.IsCreated) velocities.Dispose();
        if (forces.IsCreated) forces.Dispose();
        if (cells.IsCreated) cells.Dispose();
        if (spatialGrid.IsCreated) spatialGrid.Dispose();
    }
}
```

**Why NativeArrays**:
- Direct memory access, no managed overhead
- Can be safely passed to jobs
- Burst compiler can optimize operations
- Allocator.Persistent keeps them alive between frames (no per-frame allocation)

#### Phase 1: Assign Spatial Cells

```csharp
[BurstCompile]
struct AssignCellsJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> Positions;
    public NativeArray<int3> CellIndices;
    public float CellSize;
    
    public void Execute(int index)
    {
        float3 pos = Positions[index];
        CellIndices[index] = new int3(
            (int)math.floor(pos.x / CellSize),
            (int)math.floor(pos.y / CellSize),
            (int)math.floor(pos.z / CellSize)
        );
    }
}

// In Update or FixedUpdate
JobHandle assignCellsHandle = new AssignCellsJob
{
    Positions = positions,
    CellIndices = cellIndices,
    CellSize = cellSize
}.Schedule(entityCount, 64); // 64 = batch size
```

**Burst Optimization**:
- `[BurstCompile]` attribute enables aggressive optimization
- Use `math` functions instead of `Mathf` for better performance
- Keep job structs simple with only blittable types

#### Phase 2: Build Spatial Hash

```csharp
[BurstCompile]
struct BuildSpatialHashJob : IJob
{
    [ReadOnly] public NativeArray<int3> CellIndices;
    public NativeMultiHashMap<int3, int>.ParallelWriter SpatialGrid;
    public int EntityCount;
    
    public void Execute()
    {
        for (int i = 0; i < EntityCount; i++)
        {
            SpatialGrid.Add(CellIndices[i], i);
        }
    }
}

// Before scheduling, clear the grid
spatialGrid.Clear();

// Schedule the build job
JobHandle buildHashHandle = new BuildSpatialHashJob
{
    CellIndices = cellIndices,
    SpatialGrid = spatialGrid.AsParallelWriter(),
    EntityCount = entityCount
}.Schedule(assignCellsHandle);
```

**Why IJob instead of IJobParallelFor**:
- Building the hash map requires synchronization
- However, you can use `ParallelWriter` for thread-safe adds
- Alternative: Use IJobParallelFor if you partition work by cell range

#### Phase 3: Calculate Forces (The Heavy Computation)

```csharp
[BurstCompile]
struct CalculateForcesJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> Positions;
    [ReadOnly] public NativeArray<PhysicsProperties> Properties;
    [ReadOnly] public NativeMultiHashMap<int3, int> SpatialGrid;
    [ReadOnly] public NativeArray<int3> CellIndices;
    
    [WriteOnly] public NativeArray<float3> Forces;
    
    public void Execute(int index)
    {
        float3 position = Positions[index];
        PhysicsProperties props = Properties[index];
        int3 cellIndex = CellIndices[index];
        
        float3 totalForce = float3.zero;
        
        // Check 27 neighboring cells (including own cell)
        for (int x = -1; x <= 1; x++)
        for (int y = -1; y <= 1; y++)
        for (int z = -1; z <= 1; z++)
        {
            int3 neighborCell = cellIndex + new int3(x, y, z);
            
            // Iterate through all entities in this cell
            if (SpatialGrid.TryGetFirstValue(neighborCell, out int otherIndex, out var iterator))
            {
                do
                {
                    if (otherIndex == index) continue;
                    
                    float3 otherPos = Positions[otherIndex];
                    PhysicsProperties otherProps = Properties[otherIndex];
                    
                    totalForce += CalculateForce(position, otherPos, props, otherProps);
                    
                } while (SpatialGrid.TryGetNextValue(out otherIndex, ref iterator));
            }
        }
        
        // Clamp force to prevent instability
        float magnitude = math.length(totalForce);
        if (magnitude > props.MaxForce)
        {
            totalForce = math.normalize(totalForce) * props.MaxForce;
        }
        
        Forces[index] = totalForce;
    }
    
    // Force calculation helper (inlined by Burst)
    private static float3 CalculateForce(
        float3 position,
        float3 otherPosition,
        PhysicsProperties properties,
        PhysicsProperties otherProperties)
    {
        float3 delta = position - otherPosition;
        float distance = math.length(delta);
        
        if (distance < 0.0001f) return float3.zero;
        
        float3 direction = delta / distance;
        
        float effectiveCollisionRadius = math.max(
            properties.CollisionRadius,
            otherProperties.CollisionRadius
        );
        
        float3 force = float3.zero;
        
        // Collision zone - always repulsive
        if (distance < effectiveCollisionRadius)
        {
            float strength = (properties.CollisionStrength + otherProperties.CollisionStrength) * 0.5f;
            float normalizedDistance = distance / effectiveCollisionRadius;
            float collisionMagnitude = strength / (normalizedDistance * normalizedDistance + 0.01f);
            
            force += direction * collisionMagnitude;
        }
        
        // Interaction zone - attraction or repulsion with distance falloff
        float effectiveInteractionRadius = math.max(
            properties.InteractionRadius,
            otherProperties.InteractionRadius
        );
        
        if (distance < effectiveInteractionRadius && distance >= effectiveCollisionRadius)
        {
            float strength = (properties.InteractionStrength + otherProperties.InteractionStrength) * 0.5f;
            float interactionZoneSize = effectiveInteractionRadius - effectiveCollisionRadius;
            float distanceIntoZone = distance - effectiveCollisionRadius;
            float falloff = 1f - (distanceIntoZone / interactionZoneSize);
            
            force += -direction * strength * falloff;
        }
        
        return force;
    }
}

// Schedule
JobHandle forcesHandle = new CalculateForcesJob
{
    Positions = positions,
    Properties = properties,
    SpatialGrid = spatialGrid,
    CellIndices = cellIndices,
    Forces = forces
}.Schedule(entityCount, 64, buildHashHandle);
```

**Key Burst Optimizations**:
- Use `math.length`, `math.normalize`, `math.max` instead of Vector3 methods
- Helper methods are automatically inlined
- `[ReadOnly]` helps Burst understand data flow
- Batch size of 64 is good default (tune based on profiling)

#### Phase 4: Integrate Velocity

```csharp
[BurstCompile]
struct IntegrateVelocityJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<PhysicsProperties> Properties;
    [ReadOnly] public NativeArray<float3> Forces;
    public NativeArray<float3> Velocities;
    public float DeltaTime;
    
    public void Execute(int index)
    {
        PhysicsProperties props = Properties[index];
        float3 acceleration = Forces[index] / props.Mass;
        
        // Update velocity
        float3 velocity = Velocities[index];
        velocity += acceleration * DeltaTime;
        
        // Apply damping
        velocity *= (1f - props.Damping * DeltaTime);
        
        Velocities[index] = velocity;
    }
}

// Schedule
JobHandle velocityHandle = new IntegrateVelocityJob
{
    Properties = properties,
    Forces = forces,
    Velocities = velocities,
    DeltaTime = Time.fixedDeltaTime
}.Schedule(entityCount, 64, forcesHandle);
```

#### Phase 5: Integrate Position

```csharp
[BurstCompile]
struct IntegratePositionJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> Velocities;
    public NativeArray<float3> Positions;
    public float DeltaTime;
    
    public void Execute(int index)
    {
        Positions[index] += Velocities[index] * DeltaTime;
    }
}

// Schedule
JobHandle positionHandle = new IntegratePositionJob
{
    Velocities = velocities,
    Positions = positions,
    DeltaTime = Time.fixedDeltaTime
}.Schedule(entityCount, 64, velocityHandle);

// Complete all jobs
positionHandle.Complete();
```

#### Complete Update Loop

```csharp
void FixedUpdate()
{
    // Chain all jobs with dependencies
    JobHandle assignCells = new AssignCellsJob { /* ... */ }
        .Schedule(entityCount, 64);
    
    spatialGrid.Clear();
    
    JobHandle buildHash = new BuildSpatialHashJob { /* ... */ }
        .Schedule(assignCells);
    
    JobHandle calculateForces = new CalculateForcesJob { /* ... */ }
        .Schedule(entityCount, 64, buildHash);
    
    JobHandle integrateVelocity = new IntegrateVelocityJob { /* ... */ }
        .Schedule(entityCount, 64, calculateForces);
    
    JobHandle integratePosition = new IntegratePositionJob { /* ... */ }
        .Schedule(entityCount, 64, integrateVelocity);
    
    // Complete the chain
    integratePosition.Complete();
    
    // Now safe to read positions and sync with GameObjects
    SyncTransforms();
}
```

**Job Dependency Management**:
- Each job depends on the previous completing
- Jobs automatically execute on worker threads
- `.Complete()` blocks until all work finishes
- Never access NativeArrays from main thread while jobs are running

### 4. Force Calculation Best Practices

The force calculation function is called millions of times per frame, so Burst optimization is critical.

```csharp
// This is shown inline in the job above, but isolated here for clarity
[BurstCompile]
private static float3 CalculateForce(
    float3 position,
    float3 otherPosition,
    PhysicsProperties properties,
    PhysicsProperties otherProperties)
{
    float3 delta = position - otherPosition;
    float distance = math.length(delta);
    
    // Early exit for same position (avoid division by zero)
    if (distance < 0.0001f) return float3.zero;
    
    float3 direction = delta / distance;  // Normalized direction vector
    float3 force = float3.zero;
    
    // Use larger collision radius from either entity (symmetry)
    float effectiveCollisionRadius = math.max(
        properties.CollisionRadius,
        otherProperties.CollisionRadius
    );
    
    // === ZONE 1: COLLISION AVOIDANCE (Short Range, Always Repulsive) ===
    if (distance < effectiveCollisionRadius)
    {
        // Average the strengths for symmetric force
        float strength = (properties.CollisionStrength + otherProperties.CollisionStrength) * 0.5f;
        float normalizedDistance = distance / effectiveCollisionRadius;
        
        // Inverse square law - very strong when close, weakens quickly
        // The +0.01f prevents division by zero and adds stability
        float collisionMagnitude = strength / (normalizedDistance * normalizedDistance + 0.01f);
        
        force += direction * collisionMagnitude;
    }
    
    // === ZONE 2: INTERACTION (Long Range, Attraction or Repulsion) ===
    // Only apply if distance is beyond collision zone
    float effectiveInteractionRadius = math.max(
        properties.InteractionRadius,
        otherProperties.InteractionRadius
    );
    
    if (distance < effectiveInteractionRadius && distance >= effectiveCollisionRadius)
    {
        // Average the strengths - note: can be positive (attract) or negative (repel)
        float strength = (properties.InteractionStrength + otherProperties.InteractionStrength) * 0.5f;
        
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
```

**Burst Optimization Notes**:
- Use `math.*` functions, not `Mathf.*` or `Vector3.*`
- Avoid branches when possible (Burst can auto-vectorize branch-free code)
- The safety epsilon (0.01f) prevents division by zero without branching
- Inlining happens automatically with Burst

**How the Two Zones Work**:

1. **Collision Zone** (0 to CollisionRadius):
   - Always repulsive (pushes entities apart)
   - Inverse-square falloff: very strong when overlapping, weakens rapidly
   - Prevents entities from occupying the same space
   - Example: CollisionRadius = 1.0, entities at 0.5 units apart = strong push

2. **Interaction Zone** (CollisionRadius to InteractionRadius):
   - Can be attractive (positive strength) or repulsive (negative strength)
   - **Linear falloff: force weakens with distance** - strongest at inner edge, zero at outer edge
   - Allows for flocking, clustering, or dispersal behaviors
   - Example: InteractionRadius = 10.0, InteractionStrength = 5.0 (attraction), entities at 5.0 units apart = moderate pull together

**Visual Representation**:
```
Entity Center
     |
     |----[Collision Zone]----| <- Strong repulsion, inverse-square falloff
     |                        |
     |----[Interaction Zone]------------------| <- Attraction/repulsion, linear distance falloff
     0      CollisionRadius      InteractionRadius
     
Force magnitude at different distances (example with CollisionStrength=50, InteractionStrength=5):
Distance  Collision Force  Interaction Force  Behavior
--------  ---------------  -----------------  --------
0.1:      ~500.0           0.0                Very close - strong push apart
1.0:      ~10.0            5.0                At collision boundary - push + full interaction
3.0:      0.0              3.8                Mid interaction - distance falloff visible
7.0:      0.0              1.25               Far interaction - weaker pull
10.0:     0.0              0.0                At interaction boundary - no force
```

**Alternative Interaction Falloff Functions**:

The interaction zone force weakens with distance. Here are alternatives to linear falloff:

```csharp
// === CURRENT: Linear falloff (used in main example) ===
float falloff = 1f - (distanceIntoZone / interactionZoneSize);
// Force decreases steadily from collision boundary to interaction boundary

// === Quadratic falloff (smoother, more "natural") ===
float normalizedDist = distanceIntoZone / interactionZoneSize;
float falloff = 1f - (normalizedDist * normalizedDist);
// Stays stronger longer, then drops off quickly at outer edge

// === Square root falloff (opposite of quadratic - drops fast then levels) ===
float normalizedDist = distanceIntoZone / interactionZoneSize;
float falloff = 1f - math.sqrt(normalizedDist);
// Weakens quickly near collision boundary, slowly at outer edge

// === Inverse falloff (strong throughout most of range) ===
float epsilon = 0.1f; // Prevents division by zero
float falloff = interactionZoneSize / (distanceIntoZone + epsilon);
// Need to normalize: falloff /= (interactionZoneSize / epsilon);

// === Exponential falloff (very smooth, more expensive) ===
float normalizedDist = distanceIntoZone / interactionZoneSize;
float falloff = math.exp(-3f * normalizedDist);
// Smooth natural decay, similar to real physical forces

// === Gaussian falloff (bell curve - strongest in middle of zone) ===
float center = interactionZoneSize * 0.5f;
float sigma = interactionZoneSize * 0.3f;
float diff = distanceIntoZone - center;
float falloff = math.exp(-(diff * diff) / (2f * sigma * sigma));
// Peaks at middle of interaction zone, useful for specific ranges
```

**Falloff Comparison** (InteractionStrength = 10, zone from 1.0 to 10.0 units):
```
Distance:  1.0    3.0    5.5    8.0    10.0
Linear:    10.0   7.8    5.0    2.2    0.0
Quadratic: 10.0   9.1    7.0    3.8    0.0
Sqrt:      10.0   5.3    2.5    0.6    0.0
Exponent:  10.0   7.4    4.1    1.5    0.5
Gaussian:  7.4    9.9    10.0   7.4    2.8
```

**Alternative Collision Force Models**:

```csharp
// === CURRENT: Inverse square (used in main example) ===
float collisionMagnitude = strength / (normalizedDistance * normalizedDistance + 0.01f);
// Strong physically-motivated force, prevents overlap well

// === Exponential (smoother than inverse-square) ===
float collisionMagnitude = strength * math.exp(-normalizedDistance * 4f);
// Smoother behavior, less prone to instability, slightly more expensive

// === Lennard-Jones potential (molecular dynamics - has both zones) ===
float r6 = math.pow(effectiveCollisionRadius / distance, 6f);
float collisionMagnitude = 4f * strength * (r6 * r6 - r6);
// Realistic molecular simulation, combines repulsion and attraction

// === Simple spring model (Hooke's law - cheapest option) ===
float penetration = effectiveCollisionRadius - distance;
if (penetration > 0)
{
    float collisionMagnitude = strength * penetration;
    force += direction * collisionMagnitude;
}
// Linear force, very cheap, good for simple scenarios

// === Soft spring with damping (prevents oscillation) ===
float penetration = effectiveCollisionRadius - distance;
if (penetration > 0)
{
    float relativeVelocity = math.dot(velocities[index] - velocities[otherIndex], direction);
    float collisionMagnitude = strength * penetration - damping * relativeVelocity;
    force += direction * collisionMagnitude;
}
// Requires passing velocities into function, but very stable
```

**Performance Comparison** (approximate cycles per calculation):
- Inverse square (collision) + Linear (interaction): ~20 cycles
- Inverse square + Quadratic: ~25 cycles
- Exponential (collision) + Linear (interaction): ~30 cycles
- Spring (collision) + Linear (interaction): ~12 cycles (cheapest)
- Inverse square + Exponential (interaction): ~35 cycles
- Inverse square + Gaussian (interaction): ~45 cycles (most expensive)

**Tuning Guidelines**:

For **Collision Forces** (preventing overlap):
- **CollisionRadius**: Set to entity visual radius × 1.0 to 1.5
  - Too small: entities visually overlap
  - Too large: entities float apart unnaturally
  
- **CollisionStrength**: Start with 10-50 for small entities, 100-500 for large
  - Too weak: entities overlap despite force
  - Too strong: entities jitter or bounce erratically
  - Rule of thumb: `CollisionStrength ≈ Mass × 20`

For **Interaction Forces** (attraction/repulsion behavior):
- **InteractionRadius**: Typically 5-20× CollisionRadius
  - Small ratio (5×): tight clusters or local spacing
  - Large ratio (20×): long-range influence, flocking
  
- **InteractionStrength** (attraction - positive values):
  - 1-5: Gentle clustering, entities drift together slowly
  - 10-30: Moderate flocking, visible grouping
  - 50+: Strong attraction, tight swarms
  - Example: Fish schooling = 10-20, Magnetic attraction = 50-100
  
- **InteractionStrength** (repulsion - negative values):
  - -1 to -5: Gentle spacing, maintains personal space
  - -10 to -30: Active dispersal, entities avoid each other
  - -50+: Strong repulsion, entities flee from each other
  - Example: Territory spacing = -10, Explosive dispersal = -100

**Common Interaction Patterns**:
```csharp
// Flocking / Schooling behavior
CollisionRadius = 0.5f
CollisionStrength = 20f
InteractionRadius = 5.0f
InteractionStrength = 8f (attraction)

// Territory / Personal space
CollisionRadius = 1.0f
CollisionStrength = 30f
InteractionRadius = 4.0f
InteractionStrength = -5f (repulsion)

// Magnetic attraction with buffer
CollisionRadius = 0.3f
CollisionStrength = 50f
InteractionRadius = 10.0f
InteractionStrength = 15f (strong attraction)

// Explosive dispersal
CollisionRadius = 1.5f
CollisionStrength = 100f
InteractionRadius = 15.0f
InteractionStrength = -50f (strong repulsion)
```

## Performance Optimization Strategies

### 1. Cell Size Selection

```csharp
// Cell size should match your largest interaction radius
float maxInteractionRadius = 10f; // Query from your entity types
float optimalCellSize = maxInteractionRadius * 1.1f; // Small buffer
```

**Trade-offs**:
- **Too small**: More cells to check (27 neighbors), more hash lookups
- **Too large**: More entities per cell, more force calculations
- **Sweet spot**: Approximately equal to largest interaction radius

**Why this matters**: The spatial grid only needs to check entities in neighboring cells. If your interaction radius is 10 units and your cell size is 10 units, checking the 27 neighboring cells guarantees you'll find all entities within interaction range.

### 2. Batch Size Tuning

```csharp
// IJobParallelFor has a batch size parameter
.Schedule(entityCount, batchSize, dependency)

// Common values:
// 32  - Good for very simple jobs
// 64  - Default, good balance
// 128 - Better for jobs with more work per entity
// 256 - Use when job work is substantial
```

**How to tune**: Use Unity Profiler, look for job scheduling overhead vs execution time.

### 3. NativeArray Allocation Strategy

```csharp
// BAD: Allocating every frame
void Update()
{
    var tempArray = new NativeArray<float3>(entityCount, Allocator.TempJob);
    // ... use array ...
    tempArray.Dispose();
}

// GOOD: Allocate once, reuse
void Start()
{
    persistentArray = new NativeArray<float3>(maxEntities, Allocator.Persistent);
}

void OnDestroy()
{
    persistentArray.Dispose();
}
```

**Allocator types**:
- `Allocator.Temp`: Single frame, fastest allocation
- `Allocator.TempJob`: Lives until job completes
- `Allocator.Persistent`: Manual lifetime management (use this for your main arrays)

### 4. Burst Compilation Verification

```csharp
// Enable Burst compilation in Jobs menu
// Window -> Burst -> Enable Compilation

// Check Burst Inspector (Window -> Burst -> Inspector)
// Look for vectorization in assembly output
```

**Common Burst performance killers**:
- Managed references in job structs
- Using `Debug.Log` in jobs
- Using `Mathf` instead of `math`
- Virtual function calls
- Try-catch blocks

### 2. Update Frequency Tuning

For very large simulations, consider:
- **Spatial hash update**: Every frame
- **Force calculation**: Every frame for nearby entities, every N frames for distant ones
- **Distance-based LOD**: Reduce force calculation frequency for entities far from camera

### 5. Memory Layout and Cache Optimization

**Unity Jobs work best with Structure of Arrays (SoA)**:

```csharp
// GOOD: Separate arrays (SoA)
NativeArray<float3> positions;
NativeArray<PhysicsProperties> properties;
NativeArray<float3> velocities;

// BAD: Array of structures (AoS) - worse cache performance
struct EntityData 
{ 
    public float3 position;
    public PhysicsProperties properties;
    public float3 velocity;
}
NativeArray<EntityData> entities; // Don't do this for large counts
```

**Why SoA is better**:
- When calculating forces, you only read positions and properties
- With SoA, these are contiguous in memory = fewer cache misses
- With AoS, you load entire structs but only use parts = wasted bandwidth

**Cache line optimization**:
```csharp
// Ensure structs are sized for cache efficiency
// x86-64 cache line is 64 bytes
// float3 = 12 bytes, so 5 float3s = 60 bytes (good!)
// PhysicsProperties = 28 bytes (7 floats) - pad to 32 for alignment

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct PhysicsProperties
{
    public float Mass;                    // 4 bytes
    public float RepulsionRadius;         // 4 bytes
    public float RepulsionStrength;       // 4 bytes
    public float InteractionRadius;       // 4 bytes
    public float InteractionStrength;     // 4 bytes
    public float MaxForce;                // 4 bytes
    public float Damping;                 // 4 bytes
    private float padding;                // 4 bytes (align to 32)
}
```

### 6. Advanced Job Patterns

**Pattern 1: Using IJobParallelForBatch for Better Control**

```csharp
[BurstCompile]
struct CalculateForcesBatchJob : IJobParallelForBatch
{
    [ReadOnly] public NativeArray<float3> Positions;
    // ... other fields ...
    
    public void Execute(int startIndex, int count)
    {
        int endIndex = startIndex + count;
        for (int i = startIndex; i < endIndex; i++)
        {
            // Calculate force for entity i
        }
    }
}

// Better control over work distribution
job.ScheduleBatch(entityCount, entityCount / SystemInfo.processorCount);
```

**Pattern 2: Job Pooling for Reduced Allocations**

```csharp
// Reuse job structs instead of creating new ones each frame
private CalculateForcesJob forcesJob;
private bool jobInitialized = false;

void FixedUpdate()
{
    if (!jobInitialized)
    {
        forcesJob = new CalculateForcesJob
        {
            // Set up persistent references
            Positions = positions,
            Properties = properties,
            // ...
        };
        jobInitialized = true;
    }
    
    // Just schedule the existing job
    JobHandle handle = forcesJob.Schedule(entityCount, 64);
    handle.Complete();
}
```

**Pattern 3: Combining Related Operations**

```csharp
// Instead of separate velocity and position integration jobs,
// combine them to reduce job scheduling overhead

[BurstCompile]
struct IntegrateJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<PhysicsProperties> Properties;
    [ReadOnly] public NativeArray<float3> Forces;
    public NativeArray<float3> Velocities;
    public NativeArray<float3> Positions;
    public float DeltaTime;
    
    public void Execute(int index)
    {
        // Integrate velocity
        float3 acceleration = Forces[index] / Properties[index].Mass;
        float3 velocity = Velocities[index] + acceleration * DeltaTime;
        velocity *= (1f - Properties[index].Damping * DeltaTime);
        Velocities[index] = velocity;
        
        // Integrate position immediately
        Positions[index] += velocity * DeltaTime;
    }
}
```

**When to combine jobs**:
- When one job's output is immediately consumed by the next
- When individual jobs are very short (< 0.1ms)
- Trade-off: Less pipeline parallelism, but lower overhead

## Stability and Tuning

### Timestep Considerations

```csharp
// Fixed timestep for physics - critical for stability
public class PhysicsSystem
{
    private const float PHYSICS_TIMESTEP = 1f / 60f;
    private float accumulatedTime = 0f;
    
    public void Update(float deltaTime)
    {
        accumulatedTime += deltaTime;
        
        while (accumulatedTime >= PHYSICS_TIMESTEP)
        {
            RunPhysicsStep(PHYSICS_TIMESTEP);
            accumulatedTime -= PHYSICS_TIMESTEP;
        }
    }
    
    private void RunPhysicsStep(float fixedDeltaTime)
    {
        AssignSpatialCells();
        CalculateForces();
        IntegrateVelocity(fixedDeltaTime);
        IntegratePosition(fixedDeltaTime);
    }
}
```

**Why Fixed Timestep**: 
- Prevents instability from variable frame rates
- Ensures deterministic simulation
- Makes force tuning predictable

**Alternative: Adaptive Timestep**:
```csharp
// For very dynamic simulations
float adaptiveTimestep = Mathf.Min(deltaTime, PHYSICS_TIMESTEP);
float iterations = Mathf.Ceil(deltaTime / PHYSICS_TIMESTEP);

for (int i = 0; i < iterations; i++)
{
    RunPhysicsStep(adaptiveTimestep);
}
```

### Force Clamping

Always clamp forces to prevent entities from "exploding" when they overlap:

```csharp
public float MaxForce = 100f;  // Tune per entity type
```

### Damping

Critical for stability - prevents perpetual motion:

```csharp
public float Damping = 0.95f;  // 0.9-0.99 range typical

// Linear damping (simpler, less physically accurate)
velocity.Linear *= Mathf.Pow(Damping, deltaTime);

// Or frame-rate independent version:
velocity.Linear *= (1f - Damping * deltaTime);
```

**Damping per entity type**:
```csharp
// Heavy objects: less damping (momentum carries them)
heavyEntity.Damping = 0.98f;

// Light objects: more damping (air resistance affects them more)
lightEntity.Damping = 0.85f;
```

## Edge Cases and Considerations

### 1. Boundary Handling

```csharp
// Option A: Infinite world (no boundaries)
// Just let entities move freely

// Option B: Wrap-around (toroidal space)
public Vector3 WrapPosition(Vector3 position, Vector3 worldSize)
{
    position.x = (position.x % worldSize.x + worldSize.x) % worldSize.x;
    position.y = (position.y % worldSize.y + worldSize.y) % worldSize.y;
    position.z = (position.z % worldSize.z + worldSize.z) % worldSize.z;
    return position;
}

// Option C: Reflective boundaries (bounce off walls)
public void ApplyBoundaries(ref Vector3 position, ref Vector3 velocity, Vector3 boundsMin, Vector3 boundsMax)
{
    float restitution = 0.8f; // Bounce factor
    
    if (position.x < boundsMin.x) 
    { 
        position.x = boundsMin.x; 
        velocity.x *= -restitution; 
    }
    else if (position.x > boundsMax.x) 
    { 
        position.x = boundsMax.x; 
        velocity.x *= -restitution; 
    }
    
    // Repeat for y and z axes
}

// Option D: Soft boundaries (force field)
public Vector3 CalculateBoundaryForce(Vector3 position, Vector3 center, float radius, float strength)
{
    Vector3 offset = position - center;
    float distance = offset.magnitude;
    
    if (distance > radius)
    {
        // Push back toward center
        return -offset.normalized * strength * (distance - radius);
    }
    
    return Vector3.zero;
}
```

### 2. Entity Sleep/Wake System

For entities that haven't moved significantly, skip expensive calculations:

```csharp
public struct SleepState
{
    public bool IsSleeping;
    public float SleepTimer;
    public Vector3 LastPosition;
}

// In your physics system
public void UpdateSleepStates(float deltaTime)
{
    for (int i = 0; i < entityCount; i++)
    {
        // Check if entity is moving
        float velocitySqr = velocities[i].Linear.sqrMagnitude;
        
        if (velocitySqr < SLEEP_THRESHOLD * SLEEP_THRESHOLD)
        {
            sleepStates[i].SleepTimer += deltaTime;
            
            if (sleepStates[i].SleepTimer > SLEEP_DELAY)
            {
                sleepStates[i].IsSleeping = true;
                velocities[i].Linear = Vector3.zero;
            }
        }
        else
        {
            sleepStates[i].SleepTimer = 0f;
            sleepStates[i].IsSleeping = false;
        }
    }
}

// During force calculation
public void CalculateForces()
{
    for (int i = 0; i < entityCount; i++)
    {
        if (sleepStates[i].IsSleeping)
        {
            forces[i].Accumulated = Vector3.zero;
            continue;
        }
        
        // Normal force calculation...
    }
}

// Wake nearby entities
public void WakeNearbyEntities(int entityIndex, float wakeRadius)
{
    Vector3 position = positions[entityIndex];
    
    foreach (int otherIndex in GetEntitiesInRadius(position, wakeRadius))
    {
        if (sleepStates[otherIndex].IsSleeping)
        {
            sleepStates[otherIndex].IsSleeping = false;
            sleepStates[otherIndex].SleepTimer = 0f;
        }
    }
}
```

**Performance Impact**: Can reduce force calculations by 50-80% in stable simulations

### 3. Different Mass Interactions

The current system treats all interactions symmetrically. For asymmetric forces:

```csharp
// Apply inverse mass weighting
float massRatio = properties.Mass / (properties.Mass + otherProperties.Mass);
force *= massRatio;
```

## Testing and Debugging

### Visualization

```csharp
// In a MonoBehaviour for debug drawing (editor only)
void OnDrawGizmos()
{
    if (!Application.isPlaying) return;
    
    for (int i = 0; i < physicsSystem.EntityCount; i++)
    {
        Vector3 pos = physicsSystem.GetPosition(i);
        PhysicsProperties props = physicsSystem.GetProperties(i);
        
        // Draw repulsion radius
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(pos, props.RepulsionRadius);
        
        // Draw interaction radius
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(pos, props.InteractionRadius);
        
        // Draw velocity vector
        Gizmos.color = Color.green;
        Vector3 vel = physicsSystem.GetVelocity(i);
        Gizmos.DrawLine(pos, pos + vel * 0.5f);
    }
    
    // Draw spatial grid cells
    DrawSpatialGrid();
}

void DrawSpatialGrid()
{
    Gizmos.color = new Color(1, 1, 1, 0.2f);
    
    foreach (var cell in physicsSystem.SpatialGrid.Grid.Keys)
    {
        Vector3 cellCenter = new Vector3(
            cell.x * physicsSystem.SpatialGrid.CellSize,
            cell.y * physicsSystem.SpatialGrid.CellSize,
            cell.z * physicsSystem.SpatialGrid.CellSize
        );
        
        Gizmos.DrawWireCube(
            cellCenter, 
            Vector3.one * physicsSystem.SpatialGrid.CellSize
        );
    }
}
```

### Performance Metrics

```csharp
using System.Diagnostics;

public class PhysicsSystem
{
    private Stopwatch partitionTimer = new Stopwatch();
    private Stopwatch forceTimer = new Stopwatch();
    private Stopwatch integrationTimer = new Stopwatch();
    
    public void Update(float deltaTime)
    {
        partitionTimer.Restart();
        AssignSpatialCells();
        partitionTimer.Stop();
        
        forceTimer.Restart();
        CalculateForces();
        forceTimer.Stop();
        
        integrationTimer.Restart();
        IntegrateVelocity(deltaTime);
        IntegratePosition(deltaTime);
        integrationTimer.Stop();
        
        // Log every N frames
        if (Time.frameCount % 60 == 0)
        {
            Debug.Log($"Physics Timing - Partition: {partitionTimer.ElapsedMilliseconds}ms, " +
                      $"Forces: {forceTimer.ElapsedMilliseconds}ms, " +
                      $"Integration: {integrationTimer.ElapsedMilliseconds}ms");
        }
    }
}
```

### Unit Testing Force Calculations

```csharp
[Test]
public void TestRepulsionForce()
{
    PhysicsProperties props1 = new PhysicsProperties 
    {
        Mass = 1f,
        RepulsionRadius = 1f,
        RepulsionStrength = 10f
    };
    
    Vector3 pos1 = Vector3.zero;
    Vector3 pos2 = new Vector3(0.5f, 0, 0); // Within repulsion radius
    
    Vector3 force = CalculateForce(pos1, pos2, props1, props1);
    
    // Force should push away (negative x direction from perspective of pos2)
    Assert.IsTrue(force.x < 0);
    
    // Force should be strong when very close
    Assert.Greater(force.magnitude, 10f);
}

[Test]
public void TestAttractionForce()
{
    PhysicsProperties props = new PhysicsProperties 
    {
        Mass = 1f,
        RepulsionRadius = 1f,
        InteractionRadius = 3f,
        InteractionStrength = 5f // Positive = attraction
    };
    
    Vector3 pos1 = Vector3.zero;
    Vector3 pos2 = new Vector3(2f, 0, 0); // In attraction zone
    
    Vector3 force = CalculateForce(pos1, pos2, props, props);
    
    // Force should pull toward (positive x direction from perspective of pos1)
    Assert.IsTrue(force.x > 0);
}
```

## Common Pitfalls

1. **Cell size too small**: Overhead of partitioning exceeds benefits
2. **No force clamping**: Leads to explosive instability when entities overlap
3. **Missing damping**: System gains energy over time, entities accelerate indefinitely
4. **Synchronization points**: Using `.Complete()` unnecessarily blocks parallel execution
5. **Symmetric force violations**: Not applying equal-and-opposite forces correctly

## Scalability Targets

With proper implementation:
- **1,000 entities**: 60 FPS easily achievable
- **10,000 entities**: 60 FPS with proper spatial partitioning
- **100,000+ entities**: Possible with aggressive culling and LOD strategies

## Further Optimizations

### For Massive Scale (50k+ entities)

1. **Hierarchical spatial partitioning**: Octree or k-d tree for extremely non-uniform distributions
2. **GPU compute shaders**: Offload force calculations entirely to GPU
3. **Temporal coherence**: Cache neighbor lists, update incrementally
4. **Broad-phase culling**: Eliminate entities outside camera frustum from calculations entirely

## Conclusion

This data-oriented approach using Unity Jobs and Burst achieves high performance through:
- **Burst-compiled parallel jobs**: Automatic SIMD vectorization and optimal code generation
- **NativeContainer memory model**: Cache-friendly Structure of Arrays layout with zero GC
- **Spatial hash partitioning**: Reduces complexity from O(N²) to O(N×k)
- **Job dependency chains**: Optimal scheduling without synchronization overhead

### Performance Expectations

With proper implementation on modern hardware:
- **1,000 entities**: 60 FPS trivial, < 1ms physics
- **10,000 entities**: 60 FPS achievable, 2-5ms physics
- **100,000 entities**: 30-60 FPS possible with optimizations, 10-20ms physics

### Integration with GameObjects

```csharp
// After physics update, sync positions back to transforms
public class EntityTransformSync : MonoBehaviour
{
    private PhysicsManager physicsManager;
    private Transform[] transforms;
    
    void LateUpdate()
    {
        // Read from NativeArray (after job completion)
        NativeArray<float3> positions = physicsManager.GetPositions();
        
        for (int i = 0; i < transforms.Length; i++)
        {
            transforms[i].position = positions[i];
        }
    }
}

// Or use TransformAccessArray for Burst-compatible transform updates
[BurstCompile]
struct SyncTransformsJob : IJobParallelForTransform
{
    [ReadOnly] public NativeArray<float3> Positions;
    
    public void Execute(int index, TransformAccess transform)
    {
        transform.position = Positions[index];
    }
}

// Schedule
var syncJob = new SyncTransformsJob { Positions = positions };
JobHandle handle = syncJob.Schedule(transformAccessArray, physicsHandle);
handle.Complete();
```

### Common Gotchas and Debugging

**1. Job Safety System Errors**
```
InvalidOperationException: The previously scheduled job writes to the NativeArray
```
**Solution**: Make sure jobs complete before accessing NativeArrays:
```csharp
jobHandle.Complete();
// Now safe to access arrays
```

**2. Burst Compilation Failures**
Check console for "Burst compilation failed" messages. Common causes:
- Managed types in job structs
- Using `Mathf` instead of `math`
- Calling non-Burst-compatible methods

**3. Performance Not Improving with Jobs**
- Check batch size (try 32, 64, 128)
- Verify Burst is enabled (Window -> Burst -> Enable Compilation)
- Profile with Unity Profiler to find bottlenecks
- Check if `[BurstCompile]` attribute is present

### Extending the System

This foundation can be extended with:

**Different force layers**:
```csharp
// Separate systems for different entity types
NativeMultiHashMap<int3, int> typeA_Grid;
NativeMultiHashMap<int3, int> typeB_Grid;
// Calculate forces between types separately
```

**Deterministic simulation**:
```csharp
// Use fixed-point math or deterministic float mode
[BurstCompile(FloatMode = FloatMode.Deterministic)]
struct DeterministicForceJob : IJobParallelFor { }
```

**GPU acceleration for massive scale**:
```csharp
// For 100k+ entities, consider ComputeShaders
// Jobs handle up to ~50k entities well
// Beyond that, GPU compute becomes beneficial
```

### Next Steps

1. **Profile early and often**: Use Unity Profiler's Timeline view to visualize job execution
2. **Start simple**: Get a basic version working before optimizing
3. **Tune incrementally**: Adjust cell size, batch size, force parameters one at a time
4. **Visualize**: Use Gizmos to debug spatial partitioning and force interactions
