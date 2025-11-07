# GOE Contact System - Setup Guide

## Overview

The contact system allows you to define **soft interactions** between different groups of GOEs. Entities can attract, repel, or otherwise influence each other based on proximity without rigid collision physics.

## Key Concepts

### Soft Contacts
- Entities can **overlap** momentarily - no instant collisions
- Forces apply gradually based on **distance falloff**
- Closer = stronger influence
- At edge of radius = no influence

### Contact Rules
Each group can have multiple rules defining how it responds to other groups:
- **Target Group**: Which group this rule applies to
- **Response Type**: Attract, Repel, Boost, Slow, etc.
- **Strength**: How strong the influence is
- **Activation Distance**: How far away the influence reaches

## Setting Up Contact Rules

### Step 1: Open Group Configuration

In your `GOEGroupConfig` ScriptableObject, find the **Contact Behaviors** section.

### Step 2: Add Contact Rules

Click the `+` button to add a new rule. For each rule, set:

```
Target Group ID: [ID of the group to react to]
Response: [Attract/Repel/Boost/Slow/ChangeColor/Custom]
Response Strength: [1-20 typical range]
Activation Distance: [2-10 typical range]
```

## Response Types Explained

### Attract
Pulls entities toward each other like magnets.

**Use Cases:**
- Schooling fish behavior
- Swarm cohesion
- Pollination (insects to flowers)

**Example Config:**
```
Target Group ID: 0 (same species)
Response: Attract
Response Strength: 2.0
Activation Distance: 5.0
```

### Repel
Pushes entities away from each other.

**Use Cases:**
- Personal space / separation
- Predator avoidance
- Territorial behavior

**Example Config:**
```
Target Group ID: 1 (predator group)
Response: Repel
Response Strength: 8.0
Activation Distance: 4.0
```

### Boost
Gives entity extra forward momentum (not directional toward/away).

**Use Cases:**
- Excitement response
- Feeding frenzy
- Mating display

**Example Config:**
```
Target Group ID: 2 (food group)
Response: Boost
Response Strength: 5.0
Activation Distance: 3.0
```

### Slow
Reduces velocity (extra damping).

**Use Cases:**
- Resting near shelter
- Exhaustion near predators
- Grazing behavior

**Example Config:**
```
Target Group ID: 3 (shelter group)
Response: Slow
Response Strength: 0.5
Activation Distance: 6.0
```

## Example Configurations

### Hummingbird Ecosystem

**Group 0: Hummingbirds**
```
Contact Rule 1:
  Target: 0 (other hummingbirds)
  Response: Repel
  Strength: 3.0
  Distance: 2.0
  
Contact Rule 2:
  Target: 1 (flowers)
  Response: Attract
  Strength: 5.0
  Distance: 4.0
```

**Group 1: Flowers (Static or Slow)**
```
No contact rules - flowers don't move
```

### Fish School with Predator

**Group 0: Small Fish**
```
Contact Rule 1:
  Target: 0 (same species - cohesion)
  Response: Attract
  Strength: 1.5
  Distance: 5.0
  
Contact Rule 2:
  Target: 0 (same species - separation, smaller radius)
  Response: Repel
  Strength: 4.0
  Distance: 2.0
  
Contact Rule 3:
  Target: 1 (shark - flee!)
  Response: Repel
  Strength: 15.0
  Distance: 8.0
```

**Group 1: Shark**
```
Contact Rule 1:
  Target: 0 (prey fish)
  Response: Attract
  Strength: 8.0
  Distance: 10.0
```

### Pollinator Simulation

**Group 0: Bees**
```
Contact Rule 1:
  Target: 0 (other bees - maintain distance)
  Response: Repel
  Strength: 2.0
  Distance: 1.5
  
Contact Rule 2:
  Target: 1 (flowers - seek nectar)
  Response: Attract
  Strength: 6.0
  Distance: 3.0
  
Contact Rule 3:
  Target: 2 (hive - return home periodically)
  Response: Attract
  Strength: 3.0
  Distance: 15.0
```

**Group 1: Flowers**
```
No movement - static
```

**Group 2: Hive**
```
No movement - static landmark
```

## Tuning Tips

### Strength Values
- **0.5 - 2.0**: Subtle influence, gentle steering
- **2.0 - 5.0**: Moderate influence, noticeable behavior change
- **5.0 - 10.0**: Strong influence, dominant behavior
- **10.0+**: Very strong, emergency response (predator avoidance)

### Distance Values
- **1.0 - 2.0**: Personal space, immediate surroundings
- **2.0 - 5.0**: Local awareness, neighborhood
- **5.0 - 10.0**: Long-range detection
- **10.0+**: Very far sensing (rare, expensive to compute)

### Falloff Behavior
The influence **automatically fades** with distance:
- At **0% of radius**: 100% strength (center)
- At **50% of radius**: 50% strength
- At **100% of radius**: 0% strength (edge)

### Performance Considerations

**Number of Rules:**
- Keep rules per group under 5 when possible
- More rules = more calculations per entity

**Activation Distance:**
- Larger distances = more entities checked
- Keep under 10 units for good performance
- Use spatial hash cell size = largest activation distance / 2

**Entity Count:**
- System can handle 3000-5000 entities efficiently
- With many rules, reduce entity count
- Profile and adjust based on target FPS

## Common Patterns

### Three-Zone Boid Flocking
```
Rule 1: Repel from same group (distance 2) - Separation
Rule 2: Attract to same group (distance 5) - Cohesion  
Rule 3: Attract to same group (distance 7) - Alignment
```

### Predator-Prey
```
Prey: Repel from predator (high strength, medium distance)
Predator: Attract to prey (medium strength, large distance)
```

### Territorial
```
Rule 1: Attract to territory center (landmark group)
Rule 2: Repel from same species (maintain spacing)
```

### Mating Swarm
```
Rule 1: Attract to opposite gender (medium strength)
Rule 2: Repel from same gender (low strength)
Rule 3: Boost when near opposite gender (excitement)
```

## Debugging

### Enable Visual Debugging
In `GOEManagerBurst`:
- `Draw Spatial Grid`: See the collision detection cells
- `Show Stats`: Monitor active contacts

### Console Logging
The system logs initialization info:
```
GOE System Initialized: 2000 entities, Burst=ON, Contacts=ON
```

### Testing Individual Rules
1. Start with ONE rule at a time
2. Set strength to 1.0 as baseline
3. Gradually increase until behavior is visible
4. Add more rules once basic behavior works

## Advanced: Custom Responses

Set `Response: Custom` and extend `GOEContactSystem.cs` to add your own behavior:

```csharp
case ContactResponse.Custom:
    // Your custom logic here
    // Access: direction, falloff, strength, deltaTime
    return YourCustomCalculation();
```

## Troubleshooting

**Entities moving erratically?**
- Reduce strength values
- Check for conflicting rules (attract + repel on same target)

**No visible contact behavior?**
- Verify Group IDs match between configs
- Increase activation distance
- Check that entities actually get close enough

**Performance drops?**
- Reduce activation distances
- Lower entity counts
- Reduce rules per group
- Ensure Burst compilation is enabled

**Entities clustering/stuck?**
- Add repel rules at small distances
- Increase damping
- Add boundary enforcement
