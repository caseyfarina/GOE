# GOE System - Optimization Log

## Date: 2025-11-02

### Critical Issue Fixed: Persistent NativeArray Storage

#### Problem Identified
The Burst-optimized systems were copying entire GOEData arrays to/from NativeArray every frame:
- **Movement System**: 2 full copies per frame (to native + from native)
- **Boundary System**: 2 full copies per frame (to native + from native)
- **Contact System**: 3+ partial copies per frame (data to native + positions extraction + influences back)

**Total per frame**: 7 major array copy operations

For 5,000 entities @ 60 FPS:
- ~2.6 MB copied per frame
- ~156 MB/sec memory bandwidth wasted
- Negated most Burst compilation benefits

#### Solution Implemented
Changed core storage from `GOEData[]` (managed) to `NativeArray<GOEData>` (persistent):

**Files Modified:**

1. **GOEManagerBurst.cs**
   - Changed `private GOEData[] goeData` → `private NativeArray<GOEData> goeData`
   - Added `using Unity.Collections`
   - Added `isInitialized` flag for safe disposal
   - Updated `OnDestroy()` to properly dispose NativeArray
   - Changed spawner call to use `SpawnGroupsNative()`
   - Updated public accessor to return `NativeArray<GOEData>`

2. **GOESpawner.cs**
   - Added new method `SpawnGroupsNative()` that creates persistent NativeArray
   - Allocates with `Allocator.Persistent` instead of managed array
   - Fixed random number generation bug (single RNG instance instead of per-entity)
   - Kept legacy `SpawnGroups()` for backward compatibility with GOEManager (non-Burst)

3. **GOEImpulseSystemBurst.cs**
   - Changed signature: `UpdateMovementBurst(NativeArray<GOEData> data, ...)`
   - **REMOVED**: All allocation, copying, and disposal code
   - Now passes NativeArray directly to job - zero copy!
   - Reduced from ~30 lines to ~10 lines

4. **GOEBoundarySystemBurst.cs**
   - Changed signature: `EnforceBoundsBurst(NativeArray<GOEData> data, ...)`
   - **REMOVED**: All allocation, copying, and disposal code
   - Now passes NativeArray directly to job - zero copy!
   - Reduced from ~35 lines to ~15 lines

5. **GOEContactSystem.cs**
   - Changed signature: `ProcessContacts(NativeArray<GOEData> data, ...)`
   - **REMOVED**: Full GOEData array copying
   - Still allocates: `influences`, `positions`, `contactRules` (necessary temp arrays)
   - Reduced from 3 array copies to 1 (positions extraction for spatial hash)
   - Data array passed read-only to job

#### Performance Impact

**Expected Improvements:**
- **50-70% reduction** in per-frame overhead for Burst systems
- **2-3x overall FPS improvement** at high entity counts (3000+)
- Memory bandwidth reduced from ~156 MB/sec to ~20 MB/sec
- Enables scaling to **8,000-12,000 entities** vs previous 5,000 limit

**Memory Changes:**
- Before: 7 temporary allocations per frame
- After: 3 temporary allocations per frame (contact system only)
- Persistent allocation: +500KB-1MB (for 5000 entities)

#### Backward Compatibility

**Preserved:**
- `GOEManager.cs` (non-Burst version) still uses managed arrays
- Legacy `GOESpawner.SpawnGroups()` method still exists
- `GOEImpulseSystem.cs` and `GOEBoundarySystem.cs` (non-Burst) unchanged

**Breaking Changes:**
- External code accessing `GOEManagerBurst.GetGOEData()` now receives `NativeArray<GOEData>` instead of `GOEData[]`
- Must use NativeArray indexing syntax
- Must not store references beyond manager lifetime

#### Testing Checklist

- [ ] Scene loads without errors
- [ ] Entities spawn correctly
- [ ] Movement system works (entities move with impulses)
- [ ] Boundary system works (reflect/wrap/dampen)
- [ ] Contact detection works (attraction/repulsion)
- [ ] No memory leaks (check Profiler after extended play)
- [ ] Performance improvement visible in Profiler
- [ ] Stats overlay shows correct entity counts
- [ ] Play mode exit disposes NativeArray properly (no errors)

#### Code Quality Improvements

**Also Fixed:**
1. **Random Number Generation Bug** in `GOESpawner.cs:32`
   - Was: `new Random(Environment.TickCount)` inside loop (same seed for all)
   - Now: Single RNG instance created outside loop
   - Impact: Entities now have truly random positions/rotations

**Code Reduction:**
- Removed ~80 lines of copying boilerplate across 3 files
- Clearer intent - job systems operate on native data directly
- Better performance characteristics visible in code

#### Next Steps (Not Implemented Yet)

These are documented for future optimization but NOT part of this change:
1. Job chaining with dependencies (instead of Complete() after each)
2. LOD system and culling
3. Transform batching with TransformAccessArray
4. Temporal coherence for spatial hash
5. ComputeBuffer-based instancing for materials

---

## Notes

This optimization addresses the single biggest performance bottleneck in the system. All subsequent optimizations will build on this foundation. The key insight: **Burst compilation only helps if you're not spending all your time copying memory.**

Performance testing should show dramatic improvements, especially at entity counts above 2,000 where memory bandwidth becomes the primary bottleneck.
