# GOE System - Quick Reference Card

## Setup Checklist

### 1. Create Group Configurations
```
Right-click → Create → GOE → Group Configuration
- Set unique Group ID (0, 1, 2, ...)
- Assign prefab with GOEView component
- Set material and colors
- Configure movement parameters
- Add contact rules (optional)
```

### 2. Create System Configuration
```
Right-click → Create → GOE → System Configuration  
- Add all group configs to array
- Set spawn counts for each group
- Define spawn and movement bounds
- Configure spatial grid (cell size ≈ 5 units)
```

### 3. Setup Scene
```
Create GameObject → Add GOEManagerBurst
- Assign System Configuration
- Enable Burst Compilation ✓
- Enable Contact Detection ✓ (if using attraction/repulsion)
- Play!
```

## Key Parameters

### Movement (Per Group)
```
Impulse Strength: 1-10      // Forward burst power
Damping: 0.85-0.95          // Velocity decay per second
Interval Range: 2-6 sec     // Time between bursts
Anim Speed: 1-4             // Wing flap rate
```

### Contact Rules (Per Group)
```
Target Group ID: 0-N        // Which group to respond to
Response: Attract/Repel     // Type of influence
Strength: 1-20              // How strong (1=subtle, 20=extreme)
Distance: 2-10              // Activation radius
```

### System Config
```
Spawn Bounds: 20x20x20      // Initial spawn area
Movement Bounds: 50x50x50   // Containment area  
Cell Size: 5                // Spatial hash cell size
Grid Dimensions: 20x20x5    // Spatial grid size
```

## Response Types

| Type | Effect | Use Case |
|------|--------|----------|
| **Attract** | Pull together | Schooling, swarming, pollination |
| **Repel** | Push apart | Separation, predator avoidance |
| **Boost** | Speed up | Excitement, feeding frenzy |
| **Slow** | Slow down | Resting, grazing, exhaustion |
| **Custom** | Your code | Special behaviors |

## Typical Values

### Damping by Behavior
```
0.95  Floaty fish, lazy drift
0.92  Hummingbirds, moderate
0.88  Fast insects, darting
0.85  Rapid, twitchy movement
```

### Contact Strength by Range
```
0.5-2.0    Subtle guidance
2.0-5.0    Noticeable steering
5.0-10.0   Strong influence
10.0+      Extreme (predator flee)
```

### Contact Distance by Type
```
1-2   Personal space only
2-5   Local neighborhood  
5-10  Long-range sensing
10+   Very far (expensive!)
```

## Performance Targets

| Entity Count | Burst OFF | Burst ON |
|-------------|-----------|----------|
| 1000 | 60+ FPS | 120+ FPS |
| 3000 | 30-40 FPS | 90-120 FPS |
| 5000 | 15-25 FPS | 60-90 FPS |
| 10000 | <15 FPS | 30-60 FPS |

*Mid-range hardware (GTX 1060, i5-8400)*

## Common Patterns

### Basic School (Single Species)
```
Group 0 → 0 (self):
  - Attract (strength: 1.5, distance: 5)  // Cohesion
  - Repel (strength: 4.0, distance: 2)    // Separation
```

### Predator-Prey
```
Prey (Group 0) → Predator (Group 1):
  - Repel (strength: 15.0, distance: 8)
  
Predator (Group 1) → Prey (Group 0):
  - Attract (strength: 8.0, distance: 10)
```

### Pollination
```
Bees (Group 0) → Flowers (Group 1):
  - Attract (strength: 6.0, distance: 3)
  
Bees (Group 0) → Bees (Group 0):
  - Repel (strength: 2.0, distance: 1.5)
```

## Debug Controls

### In GOEManagerBurst Inspector
```
✓ Draw Bounds           // Show spawn/movement areas
✓ Draw Spatial Grid     // Show collision cells
✓ Show Stats            // FPS, entity count, etc.
```

### Runtime Stats Display
```
Active GOEs: 2000/2000
Average Speed: 3.45
FPS: 95
Burst: ON | Contacts: ON
Active Contacts: 342
```

## Troubleshooting

### Problem: Entities not moving
- Check impulse strength > 0
- Verify damping < 1.0
- Ensure isActive = true

### Problem: Erratic movement
- Reduce contact strengths
- Check for conflicting rules (attract + repel)
- Increase damping for smoother motion

### Problem: Low FPS
- Enable Burst Compilation
- Reduce entity count
- Lower contact activation distances
- Reduce rules per group
- Decrease spatial grid resolution

### Problem: No contact behavior
- Verify Group IDs match in rules
- Check activation distance is large enough
- Enable Contact Detection in manager
- Ensure entities get close enough

### Problem: Entities clumping/stuck
- Add repel rules at small distances
- Increase boundary size
- Add random lateral offset in impulses

## Code Snippets

### Access GOE Data at Runtime
```csharp
GOEManagerBurst manager = GetComponent<GOEManagerBurst>();
GOEData[] data = manager.GetGOEData();

// Modify specific entity
data[0].velocity += new float3(5, 0, 0);
```

### Manually Trigger Impulse
```csharp
GOEImpulseSystem.ApplyImpulse(ref data[index]);
```

### Query Spatial Hash
```csharp
GOESpatialHash hash = manager.GetSpatialHash();
NativeList<int> neighbors = new NativeList<int>(Allocator.Temp);
hash.GetNeighbors(position, radius, neighbors);
// Use neighbors...
neighbors.Dispose();
```

### Custom Contact Response
```csharp
// In GOEContactSystem.cs, add case:
case ContactResponse.Custom:
    // Your logic here
    float3 customForce = CalculateCustomBehavior(direction, falloff);
    return customForce * strength * dt;
```

## File Locations

```
Assets/
└── GOE/
    ├── Scripts/
    │   ├── GOEData.cs
    │   ├── GOEView.cs  
    │   ├── GOE*System*.cs
    │   └── GOEManager*.cs
    ├── Configs/
    │   ├── SystemConfig.asset
    │   └── Groups/
    │       ├── Hummingbirds.asset
    │       └── Flowers.asset
    └── Prefabs/
        ├── HummingbirdPrefab.prefab
        └── FlowerPrefab.prefab
```

## Dependencies

Required Unity packages:
- `Unity.Mathematics`
- `Unity.Collections`
- `Unity.Jobs`
- `Unity.Burst`

Install via Package Manager if missing.

## Support

See full documentation:
- `README.md` - Complete system overview
- `CONTACT_SYSTEM_GUIDE.md` - Detailed contact rules examples

## License

Free for educational and commercial use.
