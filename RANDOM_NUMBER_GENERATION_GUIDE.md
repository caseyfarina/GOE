# Random Number Generation in Unity Burst Jobs - Best Practices

## The Problem That Was Fixed

### ❌ **BEFORE (Broken Code in GOESpawner.cs:117)**
```csharp
for (int i = 0; i < spawnCount; i++)
{
    // WRONG: Creating RNG inside loop
    Unity.Mathematics.Random rng = new Unity.Mathematics.Random((uint)System.Environment.TickCount);

    data.position = new float3(
        rng.NextFloat(config.spawnBounds.min.x, config.spawnBounds.max.x),
        rng.NextFloat(config.spawnBounds.min.y, config.spawnBounds.max.y),
        rng.NextFloat(config.spawnBounds.min.z, config.spawnBounds.max.z)
    );
}
```

**What went wrong:**
1. `Environment.TickCount` only updates every ~15ms (system tick resolution)
2. Loop executes in microseconds - creates 1000+ RNGs in < 1ms
3. **All RNGs get the SAME seed** = identical "random" values
4. Result: All entities spawn at the same position (or very clustered)

### ✅ **AFTER (Fixed Code in GOESpawner.cs:32)**
```csharp
// Create single RNG outside loop to avoid duplicate seeds
Unity.Mathematics.Random rng = new Unity.Mathematics.Random((uint)System.Environment.TickCount);

for (int i = 0; i < spawnCount; i++)
{
    // Reuse same RNG - internal state evolves with each call
    data.position = new float3(
        rng.NextFloat(config.spawnBounds.min.x, config.spawnBounds.max.x),
        rng.NextFloat(config.spawnBounds.min.y, config.spawnBounds.max.y),
        rng.NextFloat(config.spawnBounds.min.z, config.spawnBounds.max.z)
    );

    // Each call advances RNG state, producing different values
}
```

**Why this works:**
- Single RNG instance created once
- Each `NextFloat()` call advances internal state (like a stateful iterator)
- Produces high-quality pseudo-random sequence
- Fast and Burst-compatible

---

## Unity.Mathematics.Random - Complete Guide

### Why Use Unity.Mathematics.Random?

✅ **Burst Compatible** - Works in jobs without managed memory
✅ **SIMD Optimized** - Uses fast vectorized instructions
✅ **Deterministic** - Same seed = same sequence (useful for debugging/replays)
✅ **High Quality** - Based on xorshift128+ algorithm
✅ **Fast** - Much faster than System.Random in jobs

❌ **Don't use UnityEngine.Random** in jobs - not Burst-compatible, managed memory
❌ **Don't use System.Random** in jobs - not Burst-compatible, slower

---

## Best Practices by Scenario

### 1️⃣ **Single-Threaded / Non-Job Code**

```csharp
using Unity.Mathematics;

public class Spawner : MonoBehaviour
{
    void SpawnEntities(int count)
    {
        // Create RNG once, outside loop
        Unity.Mathematics.Random rng = new Unity.Mathematics.Random((uint)System.Environment.TickCount);

        for (int i = 0; i < count; i++)
        {
            // Reuse RNG - state evolves automatically
            float x = rng.NextFloat(0f, 100f);
            float y = rng.NextFloat(-50f, 50f);

            // Spawn entity at (x, y)
        }
    }
}
```

**Key Points:**
- One RNG instance per method/scope
- Reuse across multiple calls
- Seed with `Environment.TickCount` or `Time.frameCount`

---

### 2️⃣ **Parallel Jobs (IJobParallelFor) - Per-Entity Unique RNG**

This is the **most common pattern** for entity systems like GOE.

```csharp
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Collections;

[BurstCompile]
public struct EntityUpdateJob : IJobParallelFor
{
    public NativeArray<EntityData> entities;
    public uint randomSeed;  // Base seed passed from main thread
    public float deltaTime;

    public void Execute(int index)
    {
        // Create unique RNG per entity using index
        // randomSeed changes each frame, index is unique per entity
        Unity.Mathematics.Random rng = new Unity.Mathematics.Random(
            randomSeed + (uint)index + 1  // +1 avoids seed=0
        );

        // Now generate random values for this specific entity
        float randomForce = rng.NextFloat(0.8f, 1.2f);
        float randomAngle = rng.NextFloat(-math.PI, math.PI);

        // Apply to entity
        entities[index].velocity *= randomForce;
        entities[index].rotation += randomAngle * deltaTime;
    }
}

// Usage in main thread:
void Update()
{
    uint seed = (uint)UnityEngine.Random.Range(1, int.MaxValue); // New seed each frame

    var job = new EntityUpdateJob
    {
        entities = entityArray,
        randomSeed = seed,
        deltaTime = Time.deltaTime
    };

    job.Schedule(entityArray.Length, 32).Complete();
}
```

**Key Points:**
- Each parallel job execution gets **unique seed** = `baseSeed + index`
- Base seed changes each frame (time-based or random)
- Adding index ensures different RNG per entity
- `+1` prevents seed of 0 (which can have poor randomness)

**Example from GOEImpulseSystemBurst.cs:57-59:**
```csharp
Unity.Mathematics.Random random = new Unity.Mathematics.Random(
    randomSeed + (uint)index + 1
);
```

---

### 3️⃣ **Shared Random State (All Entities See Same Random)**

Sometimes you want all entities to respond to the same "global" random event.

```csharp
[BurstCompile]
public struct SharedRandomJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float> sharedRandomValues; // Pre-generated
    public NativeArray<EntityData> entities;
    public float globalRandomFactor; // Same for all entities this frame

    public void Execute(int index)
    {
        // All entities use same global random factor
        entities[index].velocity *= globalRandomFactor;

        // But can also use pre-generated per-entity randoms
        float entityRandom = sharedRandomValues[index];
        entities[index].color = new float4(entityRandom, 0, 0, 1);
    }
}

// Pre-generate random values on main thread:
void GenerateSharedRandoms(NativeArray<float> output, uint seed)
{
    Unity.Mathematics.Random rng = new Unity.Mathematics.Random(seed);
    for (int i = 0; i < output.Length; i++)
    {
        output[i] = rng.NextFloat();
    }
}
```

---

## Unity.Mathematics.Random API Reference

### Creating RNG

```csharp
// From seed (deterministic)
Unity.Mathematics.Random rng = new Unity.Mathematics.Random(12345u);

// From time (different each run)
Unity.Mathematics.Random rng = new Unity.Mathematics.Random((uint)System.Environment.TickCount);

// From Unity's random (bridges managed/native)
Unity.Mathematics.Random rng = new Unity.Mathematics.Random((uint)UnityEngine.Random.Range(1, int.MaxValue));
```

### Generating Float Values

```csharp
Unity.Mathematics.Random rng = new Unity.Mathematics.Random(seed);

// Float in [0, 1)
float f = rng.NextFloat();

// Float in [min, max)
float f = rng.NextFloat(min, max);

// Float2 in [0, 1) for both components
float2 f = rng.NextFloat2();

// Float2 in [min, max) for both components
float2 f = rng.NextFloat2(min, max);

// Float3, Float4 work the same way
float3 f3 = rng.NextFloat3();
float4 f4 = rng.NextFloat4(min, max);
```

### Generating Integer Values

```csharp
// Int in [0, max) - EXCLUSIVE upper bound
int i = rng.NextInt(max);

// Int in [min, max) - EXCLUSIVE upper bound
int i = rng.NextInt(min, max);

// UInt versions
uint u = rng.NextUInt();
uint u = rng.NextUInt(max);
uint u = rng.NextUInt(min, max);

// Int2, Int3, Int4
int2 i2 = rng.NextInt2(max);
int3 i3 = rng.NextInt3(min, max);
```

### Generating Directions & Rotations

```csharp
// Random point on unit sphere surface
float3 direction = rng.NextFloat3Direction();

// Random point inside unit sphere
float3 point = rng.NextFloat3(new float3(-1), new float3(1));

// Random rotation (quaternion)
quaternion rot = rng.NextQuaternionRotation();
```

### Boolean & Probability

```csharp
// Random bool (50/50)
bool b = rng.NextBool();

// Probability check (30% chance = 0.3f)
if (rng.NextFloat() < 0.3f)
{
    // 30% of the time, this executes
}
```

---

## Common Patterns in GOE System

### Pattern 1: Spawn Position (GOESpawner.cs:47-51)
```csharp
Unity.Mathematics.Random rng = new Unity.Mathematics.Random((uint)System.Environment.TickCount);

data.position = new float3(
    rng.NextFloat(bounds.min.x, bounds.max.x),
    rng.NextFloat(bounds.min.y, bounds.max.y),
    rng.NextFloat(bounds.min.z, bounds.max.z)
);
```

### Pattern 2: Random Rotation (GOESpawner.cs:54-57)
```csharp
data.rotation = quaternion.Euler(
    0,
    rng.NextFloat(0f, 360f) * Mathf.Deg2Rad,  // Convert degrees to radians
    0
);

// OR use Unity.Mathematics directly:
data.rotation = quaternion.Euler(
    0,
    rng.NextFloat(0f, math.PI * 2f),  // 0 to 2π radians
    0
);
```

### Pattern 3: Variation Range (GOEImpulseSystemBurst.cs:99)
```csharp
// Add ±20% variation to a base value
float variation = rng.NextFloat(0.8f, 1.2f);
float finalValue = baseValue * variation;
```

### Pattern 4: Random Offset (GOEImpulseSystemBurst.cs:124-128)
```csharp
float3 randomOffset = new float3(
    rng.NextFloat(-0.1f, 0.1f),
    rng.NextFloat(-0.1f, 0.1f),
    0f
);
goe.velocity += randomOffset;
```

---

## Performance Tips

### ✅ DO
- Create RNG once, reuse across many calls
- Use appropriate seed strategy (per-frame or per-entity)
- Use vectorized calls (`NextFloat3()` faster than 3x `NextFloat()`)
- Pass RNG by `ref` to jobs when possible

### ❌ DON'T
- Create new RNG every iteration (seed collision!)
- Use `UnityEngine.Random` in Burst jobs (not compatible)
- Use seed = 0 (poor randomness quality)
- Generate random values outside job then pass in (defeats parallelism)

---

## Debugging Random Issues

### Problem: "All values are the same!"
**Cause:** Creating RNG inside loop with time-based seed
**Fix:** Create RNG once outside loop

### Problem: "Sequence repeats every frame!"
**Cause:** Using same seed every frame
**Fix:** Increment seed or use time-based seed that changes

### Problem: "Entities behave identically!"
**Cause:** All entities using same RNG seed in parallel job
**Fix:** Add entity index to seed: `seed + (uint)index`

### Problem: "Pattern looks non-random!"
**Cause:** Seed is too small or predictable
**Fix:** Use larger seed range, combine multiple sources:
```csharp
uint seed = (uint)(Time.frameCount * 1000 + entityIndex);
```

---

## Advanced: Deterministic Replay

For debugging or replay systems, you want repeatable randomness:

```csharp
public class ReplayableSystem
{
    private uint savedSeed;

    void StartRecording()
    {
        // Save initial seed
        savedSeed = (uint)System.Environment.TickCount;
    }

    void UpdateFrame(int frameNumber)
    {
        // Seed changes predictably each frame
        uint frameSeed = savedSeed + (uint)frameNumber;

        // Use frameSeed in jobs - always produces same result
        // Given same savedSeed and frameNumber
    }
}
```

---

## Summary

**For GOE-style entity systems:**

1. **Main thread spawning**: One RNG outside loop, reuse
2. **Burst jobs (parallel)**: `randomSeed + (uint)index + 1`
3. **Per-frame variation**: Increment seed each frame
4. **Always use**: `Unity.Mathematics.Random`, never `UnityEngine.Random` in jobs

**The golden rule:**
> "One RNG instance per scope, unique seed per parallel execution, reuse within scope"

This gives you high-performance, high-quality randomness that works perfectly with Burst compilation!
