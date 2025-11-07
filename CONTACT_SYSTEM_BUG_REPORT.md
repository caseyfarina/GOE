# Contact System Bug Report & Fix

## Critical Issues Identified

### 🔴 **BUG #1: Unidirectional Contact Rules (CRITICAL)**

**Status**: CONFIRMED - This is why only one entity attracts others!

**Symptom**: When you set "Group 1 Attracts Group 0" in the Inspector, only a single Group 1 entity seems to attract Group 0 members.

**Root Cause**: Contact rules are **unidirectional by design**, but the implementation is **confusing and likely incorrect** for your use case.

---

## Detailed Analysis

### Contact Rule Lookup - How It Currently Works

**File**: `GOEContactSystem.cs:110-120`

```csharp
// This creates a lookup: [sourceGroupID → targetGroupID] = rule
foreach (ContactRule rule in group.contactRules)
{
    int index = group.groupID * maxGroups + rule.targetGroupID;

    lookup[index] = new ContactInfluence
    {
        response = rule.response,      // e.g., Attract
        strength = rule.responseStrength,
        radius = rule.activationDistance
    };
}
```

**How Rules Are Applied**: `GOEContactSystem.cs:203-206`

```csharp
// For EACH entity, get the rule for "self → other"
int ruleIndex = self.groupID * maxGroupID + other.groupID;
ContactInfluence rule = contactRules[ruleIndex];

if (rule.response == ContactResponse.Slow) continue;  // Skip if no rule
```

---

### The Problem Explained

**Scenario**: You configure Group 1 with contactRules:
```
Group 1 Config:
  contactRules[0]:
    targetGroupID: 0
    response: Attract
    responseStrength: 50
    activationDistance: 10
```

**What Gets Created in Lookup Table**:
```
lookup[1 * 2 + 0] = Attract  // Group 1 → Group 0 = Attract
lookup[0 * 2 + 1] = Slow     // Group 0 → Group 1 = Nothing (default)
```

**What Happens During Contact Detection**:

When a **Group 1 entity** evaluates a **Group 0 entity**:
```csharp
self.groupID = 1
other.groupID = 0
ruleIndex = 1 * 2 + 0 = 2
rule = lookup[2] = Attract ✅

// Result: Group 1 entity is attracted TOWARD Group 0 entity
influence = direction * strength  // Toward Group 0
```

When a **Group 0 entity** evaluates a **Group 1 entity**:
```csharp
self.groupID = 0
other.groupID = 1
ruleIndex = 0 * 2 + 1 = 1
rule = lookup[1] = Slow (default - no influence) ❌

// Result: Group 0 entity ignores Group 1 entity completely
if (rule.response == ContactResponse.Slow) continue;  // Skipped!
```

---

## Why Only One Entity Seems to Attract

**The Misunderstanding**:

You probably expected: "Group 1 entities attract Group 0 entities" to mean:
- ✅ Group 0 entities are **pulled toward** Group 1 entities

**What it actually means**:
- ❌ Group 1 entities are **pulled toward** Group 0 entities
- ❌ Group 0 entities **ignore** Group 1 entities entirely

**Why only one entity?**

If you have many Group 1 entities, they're ALL being attracted toward Group 0 entities, but Group 0 doesn't react! It might **look** like only one is attracting because:
- Group 1 entities cluster around a single Group 0 entity
- Group 0 entities don't move toward Group 1 (no reciprocal rule)

---

## 🔴 **BUG #2: Spatial Hash Coverage vs. Activation Distance**

**Status**: POTENTIAL - Depends on your settings

**Location**: `GOESystemConfig.cs:20-21` and `GOEContactSystem.cs:173-178`

**Current Default Settings**:
```csharp
public float cellSize = 5f;
public int3 gridDimensions = new int3(20, 20, 5);
```

**Contact Detection Search Area**:
```csharp
// Only checks 3×3×3 cells around self (27 cells maximum)
for (int z = -1; z <= 1; z++)
{
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            int3 neighborCell = selfCell + new int3(x, y, z);
            // ...
        }
    }
}
```

**Coverage Calculation**:
```
Cell size: 5 units
Search radius: ±1 cell = 3 cells total per axis
Maximum search distance: 3 cells × 5 units/cell = 15 units
```

**Problem**:
- If `activationDistance > 15 units`, entities **beyond the 3×3×3 cell range won't be detected**!
- Even if they're within your activation radius, they're not in the checked cells

**Example**:
```
Entity A at position (0, 0, 0)
Entity B at position (17, 0, 0)
Activation distance: 20 units

Actual distance: 17 units (within activation range)
Cell distance: 17/5 = 3.4 cells away

Result: Entity B is NOT checked because it's 4 cells away (only checks ±1 cell)
❌ Contact NOT detected despite being in range!
```

---

## Solutions

### Fix #1: Bidirectional Contact Rules (Recommended)

**Option A: Automatic Reciprocal Rules**

Modify `BuildContactRuleLookup()` to create bidirectional rules:

```csharp
// File: GOEContactSystem.cs:110-120
foreach (ContactRule rule in group.contactRules)
{
    int forwardIndex = group.groupID * maxGroups + rule.targetGroupID;
    int reverseIndex = rule.targetGroupID * maxGroups + group.groupID;

    ContactInfluence influence = new ContactInfluence
    {
        response = rule.response,
        strength = rule.responseStrength,
        radius = rule.activationDistance
    };

    // Apply rule in BOTH directions
    lookup[forwardIndex] = influence;  // Self → Target
    lookup[reverseIndex] = influence;  // Target → Self (NEW!)
}
```

**Effect**:
- "Group 1 Attracts Group 0" now means:
  - ✅ Group 1 entities pulled toward Group 0
  - ✅ Group 0 entities pulled toward Group 1

---

**Option B: Manual Reciprocal Configuration (Current System)**

Don't change code, just configure both rules in Inspector:

**Group 0 Config:**
```
contactRules[0]:
  targetGroupID: 1
  response: Attract
  responseStrength: 50
  activationDistance: 10
```

**Group 1 Config:**
```
contactRules[0]:
  targetGroupID: 0
  response: Attract
  responseStrength: 50
  activationDistance: 10
```

**Effect**: Both groups now attract each other (manually configured)

---

**Option C: Semantic Flip (Change Interpretation)**

Change the job to flip the rule lookup:

```csharp
// File: GOEContactSystem.cs:203
// OLD: Get rule for "self → other"
// int ruleIndex = self.groupID * maxGroupID + other.groupID;

// NEW: Get rule for "other → self" (flipped!)
int ruleIndex = other.groupID * maxGroupID + self.groupID;
```

**Effect**:
- "Group 1 Attracts Group 0" now means:
  - ✅ Group 0 entities are pulled toward Group 1 (matches expectation!)
  - ❌ Group 1 entities ignore Group 0 (unless reciprocal rule configured)

---

### Fix #2: Dynamic Spatial Hash Search Radius

**Problem**: Hard-coded ±1 cell search doesn't adapt to activation distance

**Solution**: Calculate search radius based on maximum activation distance

```csharp
// File: GOEContactSystem.cs - Add to ContactInfluenceJob
public float maxActivationRadius;  // Add this field

// In Execute() method, replace lines 173-178:
// OLD: for (int z = -1; z <= 1; z++)

// NEW: Calculate cell search radius from activation distance
int cellSearchRadius = (int)math.ceil(maxActivationRadius / cellSize) + 1;

for (int z = -cellSearchRadius; z <= cellSearchRadius; z++)
{
    for (int y = -cellSearchRadius; y <= cellSearchRadius; y++)
    {
        for (int x = -cellSearchRadius; x <= cellSearchRadius; x++)
        {
            // ... rest of code unchanged
        }
    }
}
```

**Pass maxActivationRadius to job** (File: GOEContactSystem.cs:43):
```csharp
// Calculate max activation radius from all rules
float maxRadius = 0f;
for (int i = 0; i < influences_lookup.Length; i++)
{
    if (influences_lookup[i].radius > maxRadius)
        maxRadius = influences_lookup[i].radius;
}

var job = new ContactInfluenceJob
{
    // ... existing fields ...
    maxActivationRadius = maxRadius,  // NEW!
};
```

**Effect**:
- Activation distance = 5 units → searches ±2 cells
- Activation distance = 10 units → searches ±3 cells
- Activation distance = 50 units → searches ±11 cells
- Always covers the full activation range!

---

## Recommended Implementation Order

### Step 1: Fix Unidirectional Rules (Choose One)

**If you want intuitive "Group X attracts Group Y" behavior**:
→ Implement **Option A: Automatic Reciprocal Rules**

**If you want fine control (Group 0 → 1 different from 1 → 0)**:
→ Use **Option B: Manual Configuration** (no code changes)

**If you want "Group 1 attracts Group 0" to mean "0 is pulled to 1"**:
→ Implement **Option C: Semantic Flip**

### Step 2: Fix Spatial Hash Coverage

→ Implement **Dynamic Search Radius** to ensure activation distance is always covered

---

## Testing Checklist

After implementing fixes:

1. ✅ **Verify bidirectional attraction**:
   - Set Group 1 "Attract Group 0"
   - Confirm ALL Group 0 entities are pulled toward ALL Group 1 entities

2. ✅ **Verify large activation distances**:
   - Set activation distance = 50 units
   - Confirm entities detect each other across the full distance

3. ✅ **Verify asymmetric rules work**:
   - Set Group 0 "Attract Group 1" (strength 10)
   - Set Group 1 "Repel Group 0" (strength 50)
   - Confirm Group 0 pulls toward 1, while Group 1 pushes away from 0

4. ✅ **Performance check**:
   - Large activation distances increase cells checked (more expensive)
   - Monitor frame time with profiler

---

## Performance Impact

### Option A (Bidirectional Rules)
- **Memory**: No change (same lookup table size)
- **CPU**: No change (same number of checks)
- **Behavior**: 2× interactions (both directions active)

### Option C (Semantic Flip)
- **Memory**: No change
- **CPU**: No change
- **Behavior**: Same as current (just different interpretation)

### Dynamic Search Radius
- **Memory**: +4 bytes per job (one float)
- **CPU**: Scales with activation distance
  - Activation = 5 units: ~27 cells (current)
  - Activation = 10 units: ~125 cells (5× checks)
  - Activation = 50 units: ~2,197 cells (81× checks!)
- **Recommendation**: Keep activation distances reasonable (< 20 units)

---

## Example: Current vs. Fixed Behavior

### Current System (Broken)

**Config**:
```
Group 1: contactRules = [Attract Group 0, strength=50, radius=10]
Group 0: contactRules = []  (empty)
```

**Result**:
- Group 1 entities swarm toward Group 0 entities
- Group 0 entities completely ignore Group 1
- Looks like "only one entity attracts" because Group 0 doesn't respond

---

### Fixed System (Option A: Bidirectional)

**Config** (same):
```
Group 1: contactRules = [Attract Group 0, strength=50, radius=10]
Group 0: contactRules = []  (empty)
```

**Result**:
- Group 1 entities pulled toward Group 0 ✅
- Group 0 entities pulled toward Group 1 ✅ (automatic reciprocal)
- ALL entities in both groups participate in attraction

---

### Fixed System (Option B: Manual)

**Config** (updated):
```
Group 1: contactRules = [Attract Group 0, strength=50, radius=10]
Group 0: contactRules = [Attract Group 1, strength=50, radius=10]  (NEW!)
```

**Result**:
- Group 1 entities pulled toward Group 0 ✅
- Group 0 entities pulled toward Group 1 ✅
- ALL entities in both groups participate in attraction

---

## Summary

**Why only one entity attracts**: Because contact rules are unidirectional, and Group 0 has no rule to be attracted to Group 1. Group 1 entities are clustering around Group 0, making it look like only one is "attracting."

**Fixes Needed**:
1. Make contact rules bidirectional (automatic or manual)
2. Fix spatial hash search radius to cover full activation distance

**Recommended Approach**: Implement Option A (Bidirectional) + Dynamic Search Radius for intuitive behavior and full coverage.

Let me know which approach you'd like to implement, and I'll make the code changes!
