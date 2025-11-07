# Axis Constraint Implementation Plan

## Overview

Adding axis constraints to lock GOE groups to specific planes (e.g., Y=0 for 2D movement, or constrain any axis).

**Use Cases:**
- 2D simulations (ground-dwelling creatures)
- Flying entities constrained to altitude layers
- Wall-crawling entities (constrain to X or Z planes)
- Flat scrolling games

---

## Design Options

### Option 1: Simple Boolean Flags (Recommended ⭐)
**Pros:** Simple, fast, clear intent
**Cons:** Limited to axis-aligned constraints

```csharp
// In GOEGroupConfig
public bool constrainX = false;
public bool constrainY = false;
public bool constrainZ = false;
public float constrainedXValue = 0f;
public float constrainedYValue = 0f;
public float constrainedZValue = 0f;
```

### Option 2: Enum-Based (More Flexible)
**Pros:** Easy to add presets (XY plane, XZ plane, etc.)
**Cons:** Slightly more complex

```csharp
public enum MovementConstraint
{
    None,           // 3D movement (default)
    PlaneXY,        // 2D - Z locked
    PlaneXZ,        // 2D - Y locked (ground plane)
    PlaneYZ,        // 2D - X locked (side view)
    AxisX,          // 1D - X only
    AxisY,          // 1D - Y only
    AxisZ,          // 1D - Z only
    Custom          // Manual bool flags
}
```

### Option 3: Flags Enum (Most Flexible)
**Pros:** Can combine constraints, compact storage
**Cons:** More complex for designers

```csharp
[System.Flags]
public enum AxisConstraints : byte
{
    None = 0,
    LockX = 1 << 0,  // 0b001
    LockY = 1 << 1,  // 0b010
    LockZ = 1 << 2,  // 0b100
}
```

---

## Recommended Implementation: Option 1 + Option 2 Hybrid

Combine presets with custom values for best usability.

---

## Step-by-Step Implementation

### 1️⃣ Add Constraint Data to GOEData Struct

**File:** `Assets/GOE/GOEData.cs`

```csharp
[System.Serializable]
public struct GOEData
{
    // ... existing fields ...

    // Axis constraints (NEW)
    public bool constrainX;
    public bool constrainY;
    public bool constrainZ;
    public float3 constrainedPosition;  // Target position for locked axes
}
```

**Why add to GOEData:**
- Needs to be accessible in Burst jobs
- Per-entity storage (different groups can have different constraints)
- Minimal memory overhead (4 bytes for bools + 12 bytes for float3 = 16 bytes)

**Impact on struct size:**
- Before: ~88 bytes
- After: ~104 bytes
- Still small and cache-friendly!

---

### 2️⃣ Add Configuration to GOEGroupConfig

**File:** `Assets/GOE/GOEGroupConfig.cs`

```csharp
[Header("Movement - Constraints")]
[Tooltip("Lock X axis (left/right movement)")]
public bool constrainX = false;
public float constrainedXValue = 0f;

[Tooltip("Lock Y axis (up/down movement) - useful for ground creatures")]
public bool constrainY = false;
public float constrainedYValue = 0f;

[Tooltip("Lock Z axis (forward/back movement)")]
public bool constrainZ = false;
public float constrainedZValue = 0f;

// Optional: Add preset buttons for common scenarios
[Header("Quick Presets")]
[Tooltip("Enable for 2D ground movement (XZ plane, Y locked)")]
public bool preset2DGround = false;
[Tooltip("Enable for 2D side-scrolling (XY plane, Z locked)")]
public bool preset2DSideScroll = false;
```

**Update InitializeGOE():**

```csharp
public void InitializeGOE(ref GOEData data)
{
    // ... existing initialization ...

    // Apply presets (override manual settings)
    if (preset2DGround)
    {
        constrainY = true;
        constrainedYValue = 0f;
    }
    else if (preset2DSideScroll)
    {
        constrainZ = true;
        constrainedZValue = 0f;
    }

    // Set constraints
    data.constrainX = constrainX;
    data.constrainY = constrainY;
    data.constrainZ = constrainZ;
    data.constrainedPosition = new float3(
        constrainedXValue,
        constrainedYValue,
        constrainedZValue
    );
}
```

---

### 3️⃣ Enforce Constraints in Movement System

**File:** `Assets/GOE/GOEImpulseSystemBurst.cs`

**Add constraint enforcement after position update:**

```csharp
public void Execute(int index)
{
    GOEData goe = data[index];

    if (!goe.isActive) return;

    // ... existing movement logic ...

    // Update position
    goe.position += goe.velocity * deltaTime;

    // NEW: Apply axis constraints
    ApplyAxisConstraints(ref goe);

    // ... rest of the code ...

    data[index] = goe;
}

// NEW METHOD
private void ApplyAxisConstraints(ref GOEData goe)
{
    // Lock position on constrained axes
    if (goe.constrainX)
    {
        goe.position.x = goe.constrainedPosition.x;
        goe.velocity.x = 0f;  // Zero velocity on locked axis
    }

    if (goe.constrainY)
    {
        goe.position.y = goe.constrainedPosition.y;
        goe.velocity.y = 0f;
    }

    if (goe.constrainZ)
    {
        goe.position.z = goe.constrainedPosition.z;
        goe.velocity.z = 0f;
    }
}
```

**Optimization:** SIMD-friendly version using masks:

```csharp
private void ApplyAxisConstraints(ref GOEData goe)
{
    // Create mask for free axes (1.0 = free, 0.0 = locked)
    float3 mask = new float3(
        goe.constrainX ? 0f : 1f,
        goe.constrainY ? 0f : 1f,
        goe.constrainZ ? 0f : 1f
    );

    // Apply constraint: locked axes use constrained value, free axes keep current
    goe.position = goe.position * mask + goe.constrainedPosition * (1f - mask);
    goe.velocity *= mask;  // Zero velocity on locked axes
}
```

**Why the mask approach is better:**
- SIMD-friendly (no branching!)
- Works on all 3 axes simultaneously
- Burst can vectorize it efficiently

---

### 4️⃣ Constrain Impulse Direction

**File:** `Assets/GOE/GOEImpulseSystemBurst.cs`

When applying impulses, respect axis constraints:

```csharp
private void ApplyImpulse(ref GOEData goe, ref Unity.Mathematics.Random random)
{
    // Apply impulse in local forward direction (Z-axis)
    float3 forward = math.mul(goe.rotation, new float3(0, 0, 1));

    // Add random variation to impulse strength (±20%)
    float variation = random.NextFloat(0.8f, 1.2f);
    float impulse = goe.impulseStrength * variation;

    goe.velocity += forward * impulse;

    // Add slight random lateral deviation
    float3 randomOffset = new float3(
        random.NextFloat(-0.1f, 0.1f),
        random.NextFloat(-0.1f, 0.1f),
        0f
    );
    goe.velocity += randomOffset;

    // NEW: Apply constraints to velocity
    if (goe.constrainX) goe.velocity.x = 0f;
    if (goe.constrainY) goe.velocity.y = 0f;
    if (goe.constrainZ) goe.velocity.z = 0f;

    // OR use mask version (SIMD-friendly):
    float3 mask = new float3(
        goe.constrainX ? 0f : 1f,
        goe.constrainY ? 0f : 1f,
        goe.constrainZ ? 0f : 1f
    );
    goe.velocity *= mask;
}
```

---

### 5️⃣ Update Rotation for 2D Movement

For 2D constraints, you may want to constrain rotation too:

```csharp
// In Execute(), after updating rotation:
if (math.lengthsq(goe.velocity) > 0.01f)
{
    float3 forward = math.normalize(goe.velocity);
    goe.rotation = quaternion.LookRotationSafe(forward, math.up());

    // NEW: For 2D constraints, flatten rotation
    if (goe.constrainY)  // XZ plane (ground)
    {
        // Keep only Y-axis rotation (yaw)
        float3 eulerAngles = MathHelpers.QuaternionToEuler(goe.rotation);
        goe.rotation = quaternion.Euler(0, eulerAngles.y, 0);
    }
    else if (goe.constrainZ)  // XY plane (side-scroller)
    {
        // Keep only Z-axis rotation (roll)
        float3 eulerAngles = MathHelpers.QuaternionToEuler(goe.rotation);
        goe.rotation = quaternion.Euler(eulerAngles.x, eulerAngles.y, 0);
    }
}
```

**Helper method for quaternion to euler (if needed):**

```csharp
public static class MathHelpers
{
    public static float3 QuaternionToEuler(quaternion q)
    {
        // Simple conversion (may have gimbal lock issues)
        float4 qv = q.value;

        float sinr_cosp = 2 * (qv.w * qv.x + qv.y * qv.z);
        float cosr_cosp = 1 - 2 * (qv.x * qv.x + qv.y * qv.y);
        float roll = math.atan2(sinr_cosp, cosr_cosp);

        float sinp = 2 * (qv.w * qv.y - qv.z * qv.x);
        float pitch = math.abs(sinp) >= 1 ?
            math.sign(sinp) * math.PI / 2 :
            math.asin(sinp);

        float siny_cosp = 2 * (qv.w * qv.z + qv.x * qv.y);
        float cosy_cosp = 1 - 2 * (qv.y * qv.y + qv.z * qv.z);
        float yaw = math.atan2(siny_cosp, cosy_cosp);

        return new float3(pitch, yaw, roll);
    }
}
```

---

### 6️⃣ Update Boundary System

**File:** `Assets/GOE/GOEBoundarySystemBurst.cs`

Respect constraints when enforcing boundaries:

```csharp
private void ReflectAtBounds(ref GOEData goe)
{
    // Only enforce boundaries on non-constrained axes

    // X axis
    if (!goe.constrainX)  // NEW: Check constraint
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

    // Y axis
    if (!goe.constrainY)  // NEW: Check constraint
    {
        // ... same pattern
    }

    // Z axis
    if (!goe.constrainZ)  // NEW: Check constraint
    {
        // ... same pattern
    }
}
```

**Same for WrapAtBounds() and DampenNearBounds().**

---

### 7️⃣ Update Contact System (Optional)

**File:** `Assets/GOE/GOEContactSystem.cs`

For 2D simulations, you might want to ignore one dimension in spatial hash:

```csharp
// In ContactInfluenceJob.Execute()
int3 GetCellCoords(float3 position, GOEData self)
{
    int3 coords = new int3(
        (int)math.floor(position.x / cellSize),
        (int)math.floor(position.y / cellSize),
        (int)math.floor(position.z / cellSize)
    );

    // NEW: Flatten coordinates for constrained axes
    // (All entities on same Y will be in same vertical cells)
    if (self.constrainY) coords.y = 0;
    if (self.constrainZ) coords.z = 0;
    if (self.constrainX) coords.x = 0;

    return coords;
}
```

**Note:** This is optional. Current system will still work correctly, just might check unnecessary cells.

---

### 8️⃣ Update Spawner

**File:** `Assets/GOE/GOESpawner.cs`

Ensure spawned positions respect constraints:

```csharp
// After generating random position
data.position = new float3(
    rng.NextFloat(config.spawnBounds.min.x, config.spawnBounds.max.x),
    rng.NextFloat(config.spawnBounds.min.y, config.spawnBounds.max.y),
    rng.NextFloat(config.spawnBounds.min.z, config.spawnBounds.max.z)
);

// NEW: Apply constraints immediately after spawn
if (data.constrainX) data.position.x = data.constrainedPosition.x;
if (data.constrainY) data.position.y = data.constrainedPosition.y;
if (data.constrainZ) data.position.z = data.constrainedPosition.z;
```

---

## Performance Impact

### Memory
- **Per-entity overhead**: +16 bytes (4 bools + 12 byte float3)
- **5000 entities**: +80 KB total
- **Negligible impact** on cache performance

### CPU
- **With branching** (`if` statements): Minimal cost, ~1-2% overhead
- **With SIMD masks** (recommended): Nearly zero cost (vectorized)
- **Contact system**: Could save cycles by reducing spatial hash dimensions

### Summary
✅ **Very low performance impact**
✅ **High flexibility**
✅ **Easy to use**

---

## Usage Examples

### Example 1: Ground-Dwelling Creatures (Y=0)
```csharp
// In GOEGroupConfig inspector:
Movement - Constraints:
  ☑ Constrain Y
  Constrained Y Value: 0

// Or use preset:
Quick Presets:
  ☑ Preset 2D Ground
```

**Result:** Entities move only on XZ plane at ground level

### Example 2: Fish in Shallow Water (Y=2)
```csharp
Movement - Constraints:
  ☑ Constrain Y
  Constrained Y Value: 2  // Water surface level
```

**Result:** Fish swim at constant depth

### Example 3: Side-Scroller Game
```csharp
Movement - Constraints:
  ☑ Constrain Z
  Constrained Z Value: 0

// Or use preset:
Quick Presets:
  ☑ Preset 2D Side Scroll
```

**Result:** Entities move only on XY plane (side view)

### Example 4: Vertical Shaft (X and Z locked)
```csharp
Movement - Constraints:
  ☑ Constrain X
  Constrained X Value: 0
  ☑ Constrain Z
  Constrained Z Value: 0
```

**Result:** Entities can only move up/down (Y axis)

---

## Testing Checklist

- [ ] Entities spawn at constrained position
- [ ] Movement stays on constrained plane
- [ ] Velocity zeroed on constrained axes
- [ ] Impulses respect constraints
- [ ] Boundaries work on constrained axes
- [ ] Rotation appropriate for 2D (if applicable)
- [ ] Contact detection still works
- [ ] No performance regression (<5% slower)
- [ ] Multiple groups with different constraints work
- [ ] Presets apply correctly

---

## Alternative Approach: Per-System Constraint

If you want ALL groups to share same constraint (simpler, but less flexible):

**Add to GOESystemConfig:**
```csharp
[Header("Global Constraints")]
public bool globalConstrainY = false;
public float globalConstrainedYValue = 0f;
```

**Pros:**
- Simpler UI
- Easier to toggle entire simulation to 2D
- Slightly better performance (one check vs per-entity)

**Cons:**
- Can't mix 3D and 2D groups
- Less flexible

---

## Recommended Implementation Order

1. ✅ Add fields to `GOEData` (5 min)
2. ✅ Add config to `GOEGroupConfig` (10 min)
3. ✅ Add constraint enforcement to `GOEImpulseSystemBurst` (15 min)
4. ✅ Test basic constraint (Y=0) (10 min)
5. ✅ Update spawner (5 min)
6. ✅ Update boundary system (10 min)
7. ✅ Add rotation constraints (optional, 15 min)
8. ✅ Add presets/polish (10 min)

**Total time:** ~1-1.5 hours

---

## Future Enhancements

- **Cylindrical constraints**: Lock radius from center
- **Spherical constraints**: Lock distance from point
- **Path constraints**: Follow predefined curves
- **Collision planes**: Invisible walls on constrained axes
- **Animated constraints**: Move constrained plane over time

---

## Summary

Adding axis constraints to GOE groups is straightforward:

1. **Add 4 fields to GOEData** (16 bytes)
2. **Add config UI to GOEGroupConfig**
3. **Add one method call in movement system** (`ApplyAxisConstraints()`)
4. **Update spawner and boundaries** to respect constraints
5. **Optional: Add rotation constraints** for true 2D feel

**Performance:** Negligible (~1-2% overhead with branching, near-zero with SIMD masks)
**Complexity:** Low (< 50 lines of code total)
**Flexibility:** High (per-group control, multiple constraint types)

This feature would make the system much more versatile for different game genres and simulation types!
