using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace GOE
{
    /// <summary>
    /// Handles spawning and initialization of GOE groups.
    /// </summary>
    public static class GOESpawner
    {
        /// <summary>
        /// Spawn all groups defined in the system config (NativeArray version for Burst)
        /// </summary>
        public static void SpawnGroupsNative(
            GOESystemConfig config,
            out NativeArray<GOEData> dataArray,
            out GOEView[] viewArray,
            Transform parent)
        {
            // Calculate total count
            int totalCount = 0;
            foreach (int count in config.spawnCountsPerGroup)
                totalCount += count;

            dataArray = new NativeArray<GOEData>(totalCount, Allocator.Persistent);
            viewArray = new GOEView[totalCount];

            int currentIndex = 0;

            // Create single RNG outside loop to avoid duplicate seeds
            Unity.Mathematics.Random rng = new Unity.Mathematics.Random((uint)System.Environment.TickCount);

            // Spawn each group
            for (int groupIdx = 0; groupIdx < config.groups.Length; groupIdx++)
            {
                GOEGroupConfig groupConfig = config.groups[groupIdx];
                int spawnCount = config.spawnCountsPerGroup[groupIdx];

                for (int i = 0; i < spawnCount; i++)
                {
                    // Initialize data directly in NativeArray
                    GOEData data = new GOEData();
                    data.isActive = true;

                    // Random position in spawn bounds
                    data.position = new float3(
                        rng.NextFloat(config.spawnBounds.min.x, config.spawnBounds.max.x),
                        rng.NextFloat(config.spawnBounds.min.y, config.spawnBounds.max.y),
                        rng.NextFloat(config.spawnBounds.min.z, config.spawnBounds.max.z)
                    );

                    // Random initial rotation
                    data.rotation = quaternion.Euler(
                        0,
                        rng.NextFloat(0f, 360f) * Mathf.Deg2Rad,
                        0
                    );

                    // Apply group settings
                    groupConfig.InitializeGOE(ref data);

                    // Apply axis constraints to spawn position
                    if (data.constrainX) data.position.x = data.constrainedPosition.x;
                    if (data.constrainY) data.position.y = data.constrainedPosition.y;
                    if (data.constrainZ) data.position.z = data.constrainedPosition.z;

                    // Give initial impulse
                    GOEImpulseSystem.ApplyImpulse(ref data);

                    // Write to NativeArray
                    dataArray[currentIndex] = data;

                    // Spawn view GameObject
                    GameObject viewGO = Object.Instantiate(groupConfig.prefab, parent);
                    GOEView view = viewGO.GetComponent<GOEView>();

                    if (view == null)
                        view = viewGO.AddComponent<GOEView>();

                    view.Initialize(groupConfig, currentIndex);
                    viewArray[currentIndex] = view;

                    currentIndex++;
                }
            }

            Debug.Log($"GOE System: Spawned {totalCount} entities across {config.groups.Length} groups (NativeArray)");
        }

        /// <summary>
        /// Spawn all groups defined in the system config (Legacy managed array version)
        /// </summary>
        public static void SpawnGroups(
            GOESystemConfig config,
            out GOEData[] dataArray,
            out GOEView[] viewArray,
            Transform parent)
        {
            // Calculate total count
            int totalCount = 0;
            foreach (int count in config.spawnCountsPerGroup)
                totalCount += count;
            
            dataArray = new GOEData[totalCount];
            viewArray = new GOEView[totalCount];
            
            int currentIndex = 0;
            
            // Spawn each group
            for (int groupIdx = 0; groupIdx < config.groups.Length; groupIdx++)
            {
                GOEGroupConfig groupConfig = config.groups[groupIdx];
                int spawnCount = config.spawnCountsPerGroup[groupIdx];
                
                for (int i = 0; i < spawnCount; i++)
                {
                    // Initialize data
                    ref GOEData data = ref dataArray[currentIndex];
                    data.isActive = true;

                    Unity.Mathematics.Random rng = new Unity.Mathematics.Random((uint)System.Environment.TickCount);

                    // Random position in spawn bounds
                    data.position = new float3(
                        rng.NextFloat(config.spawnBounds.min.x, config.spawnBounds.max.x),
                         rng.NextFloat(config.spawnBounds.min.y, config.spawnBounds.max.y),
                         rng.NextFloat(config.spawnBounds.min.z, config.spawnBounds.max.z)
                    );
                    
                    // Random initial rotation
                    data.rotation = quaternion.Euler(
                        0,
                        rng.NextFloat(0f, 360f) * Mathf.Deg2Rad,
                        0
                    );
                    
                    // Apply group settings
                    groupConfig.InitializeGOE(ref data);

                    // Apply axis constraints to spawn position
                    if (data.constrainX) data.position.x = data.constrainedPosition.x;
                    if (data.constrainY) data.position.y = data.constrainedPosition.y;
                    if (data.constrainZ) data.position.z = data.constrainedPosition.z;

                    // Give initial impulse
                    GOEImpulseSystem.ApplyImpulse(ref data);

                    // Spawn view GameObject
                    GameObject viewGO = Object.Instantiate(groupConfig.prefab, parent);
                    GOEView view = viewGO.GetComponent<GOEView>();
                    
                    if (view == null)
                        view = viewGO.AddComponent<GOEView>();
                    
                    view.Initialize(groupConfig, currentIndex);
                    viewArray[currentIndex] = view;
                    
                    currentIndex++;
                }
            }
            
            Debug.Log($"GOE System: Spawned {totalCount} entities across {config.groups.Length} groups");
        }
        
        /// <summary>
        /// Spawn a single group with custom count and bounds
        /// </summary>
        public static void SpawnSingleGroup(
            GOEGroupConfig groupConfig,
            int count,
            Bounds spawnBounds,
            out GOEData[] dataArray,
            out GOEView[] viewArray,
            Transform parent)
        {
            dataArray = new GOEData[count];
            viewArray = new GOEView[count];
            
            for (int i = 0; i < count; i++)
            {
                ref GOEData data = ref dataArray[i];
                data.isActive = true;

                Unity.Mathematics.Random rng = new Unity.Mathematics.Random((uint)System.Environment.TickCount);
                // Random position
                data.position = new float3(
                    rng.NextFloat(spawnBounds.min.x, spawnBounds.max.x),
                    rng.NextFloat(spawnBounds.min.y, spawnBounds.max.y),
                    rng.NextFloat(spawnBounds.min.z, spawnBounds.max.z)
                );
                
                // Random rotation
                data.rotation = quaternion.Euler(
                    0,
                    rng.NextFloat(0f, 360f) * Mathf.Deg2Rad,
                    0
                );
                
                // Apply group settings
                groupConfig.InitializeGOE(ref data);
                GOEImpulseSystem.ApplyImpulse(ref data);
                
                // Spawn view
                GameObject viewGO = Object.Instantiate(groupConfig.prefab, parent);
                GOEView view = viewGO.GetComponent<GOEView>();
                
                if (view == null)
                    view = viewGO.AddComponent<GOEView>();
                
                view.Initialize(groupConfig, i);
                viewArray[i] = view;
            }
        }
    }
}
