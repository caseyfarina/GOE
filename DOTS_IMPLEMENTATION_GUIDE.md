# DOTS Physics Implementation Guide

## Overview

This implementation combines the DOTS Physics Design Document with the original GOE system's impulse-based organic movement. The result is a high-performance entity system that supports thousands of entities with realistic physics interactions AND organic burst-based locomotion.

## Architecture

### Key Features Implemented

✅ **Two-Zone Force Model**
- Collision Zone (short-range, inverse-square repulsion)
- Interaction Zone (long-range, linear falloff attraction/repulsion)

✅ **Impulse-Based Movement**
- Organic burst locomotion from original GOE system
- Random intervals between forward impulses
- Natural-looking swarm behavior

✅ **Structure of Arrays (SoA) Layout**
- Separate `NativeArray` for each property type
- Better cache locality for Burst jobs
- Optimized memory access patterns

✅ **Job Dependency Chain Pipeline**
- 9-phase pipeline with chained dependencies
- Maximum parallel execution across CPU cores
- No unnecessary synchronization points

✅ **Parallel Transform Synchronization**
- Burst-compiled `IJobParallelForTransform`
- Uses `TransformAccessArray` for fast GameObject updates
- Much faster than main thread loops

✅ **Axis Locking Support**
- Lock X, Y, or Z axes independently
- Global settings or per-entity control
- Presets for 2D movement (ground, side-scroller)

## File Structure

### Core Systems

**GOEPhysicsData.cs**
- `PhysicsProperties` struct: Two-zone physics + impulse movement
- `PhysicsArrays` struct: SoA layout container

**GOEForceSystem.cs**
- `CalculateForcesJob`: Two-zone force calculation
- `AssignCellsJob`: Spatial hash phase 1
- `BuildSpatialHashJob`: Spatial hash phase 2
- Implements both physics forces AND group-based contact rules

**GOEIntegrationSystem.cs**
- `IntegrateVelocityJob`: F=ma + impulses + damping
- `IntegratePositionJob`: Velocity → position with axis constraints
- `UpdateRotationsJob`: Face movement direction
- `UpdateAnimationJob`: Procedural wing/tail animation

**GOEManagerDOTS.cs**
- Main orchestration MonoBehaviour
- Job dependency chain management
- Transform synchronization
- Boundary enforcement
- Entity spawning with physics properties

**GOESpawnerDOTS.cs**
- Easy spawning interface
- Group distribution configuration
- Initial velocity settings

## Usage

### Step 1: Setup Scene

1. Create empty GameObject named "GOE System"
2. Add `GOEManagerDOTS` component
3. Add `GOESpawnerDOTS` component
4. Create empty GameObject named "System Config"
5. Add existing `GOESystemConfig` ScriptableObject reference

### Step 2: Configure Group

In your `GOEGroupConfig` ScriptableObject, you now have:

```csharp
// Original GOE Properties
impulseStrength = 5f
impulseIntervalRange = (3f, 5f)
damping = 0.92f

// NEW: DOTS Physics Properties
mass = 1.0f                     // F=ma mass
maxForce = 50.0f                // Force clamping
collisionRadius = 1.0f          // Inner collision zone
collisionStrength = 20.0f       // Inverse-square repulsion
interactionRadius = 5.0f        // Outer interaction zone
interactionStrength = 5.0f      // Positive=attract, Negative=repel
```

### Step 3: Configure Axis Locking

**Global (all entities):**
```csharp
// In GOEManagerDOTS inspector
lockXAxis = false
lockYAxis = true  // Lock to XZ plane (ground movement)
lockZAxis = false
```

**Per-Entity:**
```csharp
int entityIndex = manager.SpawnEntity(position, group);
manager.SetAxisConstraints(entityIndex, lockX: false, lockY: true, lockZ: false);
```

**Using Group Presets:**
```csharp
// In GOEGroupConfig inspector
preset2DGround = true        // Locks Y axis to 0
preset2DSideScroll = false   // Would lock Z axis to 0
```

### Step 4: Spawn Entities

```csharp
// Using spawner (automatic)
spawner.entitiesToSpawn = 2000;
spawner.spawnOnStart = true;

// Programmatically
Vector3 position = new Vector3(0, 5, 0);
Vector3? velocity = Random.insideUnitSphere * 3f;
int index = manager.SpawnEntity(position, groupConfig, velocity);
```

## Physics Tuning Guide

### Collision Zone (Prevent Overlap)

**collisionRadius** - How close entities can get
- Too small: Visual overlapping
- Too large: Entities float apart unnaturally
- **Recommended**: Entity visual radius × 1.0 to 1.5

**collisionStrength** - How hard they push apart
- Too weak: Entities still overlap
- Too strong: Jittering, bouncing
- **Recommended**: `mass × 20`
- **Examples**: 20-50 for small entities, 100-500 for large

### Interaction Zone (Behavior)

**interactionRadius** - Range of influence
- **Recommended**: 5-20× collision radius
- Small ratio (5×): Tight clusters
- Large ratio (20×): Long-range flocking

**interactionStrength** - Attraction/Repulsion
- **Positive** = Attraction (entities group together)
  - 1-5: Gentle clustering
  - 10-30: Moderate flocking
  - 50+: Tight swarms

- **Negative** = Repulsion (entities spread out)
  - -1 to -5: Personal space
  - -10 to -30: Active dispersal
  - -50+: Explosive separation

### Common Patterns

**Fish Schooling:**
```
collisionRadius = 0.5
collisionStrength = 20
interactionRadius = 5.0
interactionStrength = 8  (attract)
```

**Birds Flocking:**
```
collisionRadius = 0.8
collisionStrength = 25
interactionRadius = 10.0
interactionStrength = 12  (attract)
```

**Territory Spacing:**
```
collisionRadius = 1.0
collisionStrength = 30
interactionRadius = 4.0
interactionStrength = -5  (repel)
```

**Ground Creatures (2D):**
```
collisionRadius = 1.5
collisionStrength = 40
interactionRadius = 6.0
interactionStrength = 0  (neutral)
lockYAxis = true
```

## Performance Characteristics

### Expected Performance

Based on DOTS Design Document targets:

| Entity Count | Target FPS | Notes |
|--------------|------------|-------|
| 1,000 | 120+ FPS | Trivial |
| 3,000 | 90-120 FPS | Easy |
| 5,000 | 60-90 FPS | Good |
| 10,000 | 30-60 FPS | Achievable |

### Performance Settings

**Cell Size** (in GOEManagerDOTS)
- Should be ≥ largest interactionRadius
- Too small: More cells to check
- Too large: More entities per cell
- **Recommended**: `max(interactionRadius) × 1.1`

**Job Batch Size**
- Default: 64 (good balance)
- Simple jobs: 32
- Heavy jobs: 128-256

## Pipeline Details

### 9-Phase Update Pipeline

All jobs run in FixedUpdate with dependency chaining:

1. **Assign Cells** - Categorize entities into spatial grid
2. **Build Hash** - Populate NativeMultiHashMap
3. **Calculate Forces** - Two-zone physics + contact rules
4. **Integrate Velocity** - F=ma + impulses + damping
5. **Update Rotations** - Face movement direction
6. **Integrate Position** - Apply velocity with axis constraints
7. **Enforce Boundaries** - Reflect/wrap/dampen at edges
8. **Update Animation** - Procedural animation phases
9. **Sync Transforms** - Parallel GameObject updates

Each phase waits for previous completion, enabling optimal CPU utilization.

## Axis Locking Implementation

### How It Works

Axis locking is enforced in `IntegratePositionJob`:

```csharp
float3 newPosition = positions[index] + velocities[index] * deltaTime;

if (constrainX[index])
    newPosition.x = positions[index].x;  // Keep X fixed
if (constrainY[index])
    newPosition.y = positions[index].y;  // Keep Y fixed
if (constrainZ[index])
    newPosition.z = positions[index].z;  // Keep Z fixed

positions[index] = newPosition;
```

### Use Cases

**2D Ground Movement (XZ plane)**
- Lock Y axis
- Entities move on ground level
- Example: Crabs, ground beetles

**2D Side-Scroller (XY plane)**
- Lock Z axis
- Classic 2D platformer movement
- Example: Flying in fixed depth

**1D Movement (single axis)**
- Lock two axes
- Movement along rail/track
- Example: Conveyor belt entities

## Comparison: Original vs DOTS

| Feature | Original GOE | DOTS Implementation |
|---------|-------------|---------------------|
| Movement | Impulse only | Impulse + continuous forces |
| Physics | None | Two-zone F=ma model |
| Interactions | Group rules | Group rules + universal physics |
| Data Layout | Array of Structs | Structure of Arrays |
| Job Pipeline | Sequential complete | Dependency chain |
| Transform Sync | Main thread loop | Parallel job |
| Force Model | N/A | Inverse-square + linear falloff |
| Axis Locking | ✅ Yes | ✅ Yes (enhanced) |
| Scalability | 1k-5k entities | 5k-10k+ entities |

## Troubleshooting

### Entities Overlap

- Increase `collisionRadius`
- Increase `collisionStrength`
- Check that `cellSize ≥ interactionRadius`

### Entities Float Apart

- Decrease `collisionRadius`
- Add positive `interactionStrength` (attraction)

### Jittering/Instability

- Decrease `collisionStrength`
- Increase `damping`
- Decrease `maxForce`
- Check `Time.fixedDeltaTime` (should be ~0.02)

### Poor Performance

- Reduce entity count
- Increase `cellSize` if too small
- Increase `jobBatchSize` for heavy jobs
- Check Burst compilation is enabled

### Entities Don't Stay on Ground

- Enable `lockYAxis = true` globally
- OR set `preset2DGround = true` in group config
- Check boundary behavior isn't pushing them up

## Next Steps

1. **Test** the implementation with different entity counts
2. **Tune** physics parameters for desired behavior
3. **Profile** using Unity Profiler to identify bottlenecks
4. **Experiment** with different force falloff curves
5. **Extend** with additional features (formations, leaders, obstacles)

## Credits

Implementation based on:
- DOTS_Physics_System_Design.md (force-based physics)
- Original GOE system (impulse movement, visual variation)
- Unity DOTS documentation (job system, Burst)
