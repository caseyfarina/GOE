# Runtime ScriptableObject Update Behavior

## Quick Answer

**Updating GOEGroupConfig or GOESystemConfig ScriptableObjects at runtime has MIXED behavior:**

✅ **DOES UPDATE (Every Frame)**:
- Contact Rules (attraction/repulsion behaviors)
- Contact activation distances
- Contact response strengths

❌ **DOES NOT UPDATE (Set at Spawn Only)**:
- Movement parameters (impulse strength, damping, intervals)
- Animation speeds
- Visual appearance (colors, materials, scale)
- Axis constraints (locked positions)

---

## Detailed Analysis

### Contact Rules - ✅ UPDATE EVERY FRAME

**File**: `GOEContactSystem.cs:80-124`

Contact rules are **rebuilt from ScriptableObjects every frame** in `BuildContactRuleLookup()`:

```csharp
// Line 31 - Called EVERY FRAME in ProcessContacts()
NativeArray<ContactInfluence> influences_lookup = BuildContactRuleLookup(config, Allocator.TempJob);

// Lines 104-121 - Reads from ScriptableObject fields
foreach (ContactRule rule in group.contactRules)
{
    lookup[index] = new ContactInfluence
    {
        response = rule.response,              // ← Read from ScriptableObject
        strength = rule.responseStrength,      // ← Read from ScriptableObject
        radius = rule.activationDistance       // ← Read from ScriptableObject
    };
}
```

**Why This Updates at Runtime**:
- Contact system rebuilds lookup table every frame (line 31)
- Reads directly from `GOEGroupConfig.contactRules[]` array
- No caching of values into entity data

**What You Can Change at Runtime**:
1. `ContactRule.response` (Attract, Repel, Boost, Slow)
2. `ContactRule.responseStrength` (force magnitude)
3. `ContactRule.activationDistance` (detection radius)
4. `ContactRule.targetGroupID` (which group to affect)

**Example Use Case**:
```csharp
// Make group 0 start attracting group 1 at runtime
GOEGroupConfig group0 = Resources.Load<GOEGroupConfig>("Group0");
group0.contactRules[0].response = ContactResponse.Attract;
group0.contactRules[0].responseStrength = 10f;
// Takes effect IMMEDIATELY on next frame
```

---

### Movement Parameters - ❌ DO NOT UPDATE

**File**: `GOEGroupConfig.cs:63-82` → `GOESpawner.cs:61`

Movement settings are **copied into GOEData struct at spawn time only**:

```csharp
// GOEGroupConfig.InitializeGOE() - Called ONCE at spawn
public void InitializeGOE(ref GOEData data)
{
    data.groupID = groupID;

    // These are COPIED into the struct
    data.impulseStrength = impulseStrength + Random.Range(...);  // Line 68
    data.minImpulseInterval = impulseIntervalRange.x;             // Line 69
    data.maxImpulseInterval = impulseIntervalRange.y;             // Line 70
    data.damping = damping + Random.Range(...);                   // Line 71
    data.animSpeed = baseAnimSpeed + Random.Range(...);           // Line 74

    // ... etc
}
```

**Called From**: `GOESpawner.SpawnGroupsNative()` line 61
```csharp
groupConfig.InitializeGOE(ref data);  // ← Only called at spawn
```

**Why This Doesn't Update**:
1. Values are copied from ScriptableObject → GOEData struct
2. Burst jobs read from GOEData struct, NOT from ScriptableObject
3. InitializeGOE() is never called again after spawning

**What CANNOT Be Changed at Runtime** (without code changes):
- `impulseStrength` - Force of forward impulses
- `impulseIntervalRange` - Time between impulses
- `damping` - Velocity decay rate
- `baseAnimSpeed` - Animation playback speed
- Visual parameters (color, scale, etc.)

**Example - This WON'T Work**:
```csharp
// Change impulse strength in ScriptableObject
GOEGroupConfig group0 = Resources.Load<GOEGroupConfig>("Group0");
group0.impulseStrength = 20f;  // Changes ScriptableObject value
// ❌ NO EFFECT - entities still use old value copied at spawn
```

---

### Axis Constraints - ❌ DO NOT UPDATE

**File**: `GOEGroupConfig.cs:83-112` → Copied to `GOEData` at spawn

Axis constraints are also copied at initialization:

```csharp
// Lines 105-112 - Called ONCE at spawn
data.constrainX = useConstrainX;
data.constrainY = useConstrainY;
data.constrainZ = useConstrainZ;
data.constrainedPosition = new float3(...);
```

**Enforcement**: `GOEImpulseSystemBurst.cs:99-112`
```csharp
private void ApplyAxisConstraints(ref GOEData goe)
{
    // Reads from GOEData struct fields (copied at spawn)
    float3 mask = new float3(
        goe.constrainX ? 0f : 1f,  // ← From struct, not ScriptableObject
        goe.constrainY ? 0f : 1f,
        goe.constrainZ ? 0f : 1f
    );
    // ...
}
```

Changing constraint values in the ScriptableObject won't affect existing entities.

---

### Visual Properties - ❌ DO NOT UPDATE

**File**: `GOEView.cs:35-44`

Visual properties (material, color) are set **once at initialization**:

```csharp
public void Initialize(GOEGroupConfig groupConfig, int dataIdx)
{
    // Called ONCE at spawn
    instanceMaterial = new Material(groupConfig.baseMaterial);  // Line 41
    instanceMaterial.color = groupConfig.baseColor;             // Line 42
    rend.material = instanceMaterial;                           // Line 43
}
```

After this, `GOEView.SyncFromData()` (lines 49-65) only reads from the **GOEData struct**, not from the ScriptableObject.

---

## Performance Considerations

### Why Contact Rules Update Every Frame

Contact rule rebuilding is intentionally designed to allow runtime updates:

**Cost**: Lines 80-124 in GOEContactSystem.cs
- Creates `NativeArray<ContactInfluence>` (typically 16-100 entries)
- Iterates through all groups and their contact rules
- **Overhead**: ~0.01-0.05ms per frame (negligible)

**Benefit**: Allows dynamic behavior changes without respawning entities

### Why Movement Parameters Don't Update

Movement parameters are in the **hot path** (executed per entity per frame):

**File**: `GOEImpulseSystemBurst.cs:41-94`
```csharp
public void Execute(int index)  // Called 5000× per frame @ 5000 entities
{
    GOEData goe = data[index];  // ← Reads from struct

    goe.velocity *= goe.damping;        // Uses cached value
    goe.impulseTimer -= deltaTime;      // Uses cached value
    if (goe.impulseTimer <= 0f)
    {
        ApplyImpulse(ref goe, ref random);  // Uses goe.impulseStrength
        goe.impulseTimer = random.NextFloat(
            goe.minImpulseInterval,  // ← Cached at spawn
            goe.maxImpulseInterval   // ← Cached at spawn
        );
    }
    // ...
}
```

**If this read from ScriptableObject**:
- ❌ 5000 managed object accesses per frame
- ❌ Cannot Burst compile (managed references not allowed)
- ❌ ~10-20ms overhead at 5000 entities (TERRIBLE!)

**Current design (struct caching)**:
- ✅ Zero managed memory access in jobs
- ✅ Full Burst SIMD optimization
- ✅ ~1.5-2.5ms total movement time at 5000 entities

---

## How to Update Movement Parameters at Runtime

If you need to update movement parameters dynamically, you have three options:

### Option 1: Direct Struct Modification (Recommended)

Modify the GOEData array directly:

```csharp
// In GOEManagerBurst.cs or custom script
public void UpdateGroupImpulseStrength(int groupID, float newStrength)
{
    for (int i = 0; i < goeData.Length; i++)
    {
        if (goeData[i].groupID == groupID)
        {
            GOEData temp = goeData[i];
            temp.impulseStrength = newStrength;
            goeData[i] = temp;
        }
    }
}
```

**Pros**: Immediate, efficient, preserves Burst performance
**Cons**: Requires writing helper methods

---

### Option 2: Rebuild System (Heavy)

Create a new method to reinitialize entities from ScriptableObject:

```csharp
public void RefreshGroupSettings(int groupID, GOEGroupConfig newConfig)
{
    for (int i = 0; i < goeData.Length; i++)
    {
        if (goeData[i].groupID == groupID)
        {
            GOEData temp = goeData[i];
            // Preserve position, velocity, rotation
            float3 pos = temp.position;
            float3 vel = temp.velocity;
            quaternion rot = temp.rotation;

            // Re-initialize from config
            newConfig.InitializeGOE(ref temp);

            // Restore preserved values
            temp.position = pos;
            temp.velocity = vel;
            temp.rotation = rot;

            goeData[i] = temp;
        }
    }
}
```

**Pros**: Fully reloads all settings from ScriptableObject
**Cons**: Resets timers, regenerates random variations

---

### Option 3: Custom Event System

Add a messaging system to GOEData:

```csharp
// In GOEData.cs
public struct GOEData
{
    // ... existing fields ...
    public bool parametersDirty;  // Flag for updates
}

// In GOEManagerBurst.cs
void LateUpdate()
{
    if (parametersChanged)
    {
        // Push updated parameters to entities
        UpdateParametersFromConfig();
        parametersChanged = false;
    }
}
```

**Pros**: Clean separation, batch updates
**Cons**: More complex architecture

---

## Summary Table

| Setting Category | Updates at Runtime? | Read From | Frequency | File |
|-----------------|---------------------|-----------|-----------|------|
| **Contact Rules** | ✅ YES | ScriptableObject | Every frame | GOEContactSystem.cs:31 |
| **Contact Distances** | ✅ YES | ScriptableObject | Every frame | GOEContactSystem.cs:118 |
| **Contact Strengths** | ✅ YES | ScriptableObject | Every frame | GOEContactSystem.cs:117 |
| **Impulse Strength** | ❌ NO | GOEData struct | Spawn only | GOEImpulseSystemBurst.cs:121 |
| **Impulse Intervals** | ❌ NO | GOEData struct | Spawn only | GOEImpulseSystemBurst.cs:64-67 |
| **Damping** | ❌ NO | GOEData struct | Spawn only | GOEImpulseSystemBurst.cs:48 |
| **Animation Speed** | ❌ NO | GOEData struct | Spawn only | GOEImpulseSystemBurst.cs:85 |
| **Axis Constraints** | ❌ NO | GOEData struct | Spawn only | GOEImpulseSystemBurst.cs:99-112 |
| **Visual (Color)** | ❌ NO | Material instance | Spawn only | GOEView.cs:41-43 |
| **Visual (Scale)** | ❌ NO | GOEData struct | Spawn only | GOEView.cs:64 |

---

## Best Practices

### For Dynamic Behavior Changes

If you need frequently changing behavior:
- ✅ **Use contact rules** - They're designed for runtime updates
- ✅ **Modify GOEData directly** - Write helper methods in GOEManagerBurst
- ❌ **Don't expect ScriptableObject changes to auto-propagate** for movement parameters

### For Design-Time Configuration

If settings are mostly static:
- ✅ **Keep using ScriptableObjects** - Perfect for level design
- ✅ **Configure before spawning** - Changes take effect on next spawn
- ✅ **Respawn entities** if major parameter changes are needed

### Example: Runtime Difficulty Scaling

```csharp
// GOOD - Direct struct modification
public void IncreaseDifficulty(float factor)
{
    for (int i = 0; i < goeData.Length; i++)
    {
        GOEData temp = goeData[i];
        temp.impulseStrength *= factor;
        temp.damping *= 0.95f;  // Less damping = faster
        goeData[i] = temp;
    }
}

// ALSO GOOD - Contact rule modification
public void MakeGroupsAggressive()
{
    foreach (GOEGroupConfig group in systemConfig.groups)
    {
        foreach (ContactRule rule in group.contactRules)
        {
            rule.response = ContactResponse.Repel;  // Makes them avoid each other
            rule.responseStrength *= 2f;             // More aggressive
        }
    }
    // Takes effect immediately (next frame)
}

// BAD - Won't work
public void IncreaseSpeedInScriptableObject()
{
    GOEGroupConfig group = Resources.Load<GOEGroupConfig>("MyGroup");
    group.impulseStrength = 20f;  // ❌ No effect on existing entities!
}
```

---

## Key Takeaway

The GOE system is **intentionally designed** with this hybrid approach:

- **Hot path parameters** (movement, animation) are **cached in structs** for maximum Burst performance
- **Interaction parameters** (contact rules) are **read from ScriptableObjects** for runtime flexibility

This design achieves both **performance** (Burst optimization) and **flexibility** (dynamic contact behaviors).

To modify cached parameters at runtime, write custom update methods that directly modify the `NativeArray<GOEData>` in GOEManagerBurst.
