using Unity.Collections;
using UnityEngine;

namespace GOE
{
    /// <summary>
    /// Main manager for the GOE system with Burst-optimized updates and contact detection.
    /// </summary>
    public class GOEManagerBurst : MonoBehaviour
    {
        [Header("Configuration")]
        public GOESystemConfig systemConfig;
        
        [Header("Performance")]
        [Tooltip("Contact detection enables attraction/repulsion between entity groups")]
        public bool enableContactDetection = true;
        
        [Header("Debug")]
        public bool drawBounds = true;
        public bool drawSpatialGrid = false;
        public bool showStats = true;
        
        private NativeArray<GOEData> goeData;
        private GOEView[] goeViews;
        private GOESpatialHash spatialHash;

        private uint randomSeed;
        private bool isInitialized = false;
        
        void Start()
        {
            if (systemConfig == null)
            {
                Debug.LogError("GOEManager: No system config assigned!");
                enabled = false;
                return;
            }
            
            // Initialize random seed
            randomSeed = (uint)Random.Range(1, int.MaxValue);

            // Spawn all groups using NativeArray version
            GOESpawner.SpawnGroupsNative(
                systemConfig,
                out goeData,
                out goeViews,
                transform
            );

            isInitialized = true;
            
            // Initialize spatial hash
            if (enableContactDetection)
            {
                spatialHash = new GOESpatialHash(
                    systemConfig.cellSize,
                    systemConfig.gridDimensions
                );
                spatialHash.Initialize(goeData.Length);
            }
            
            Debug.Log($"GOE System Initialized: {goeData.Length} entities, Burst=ON (NativeArray), Contacts={enableContactDetection}");
        }
        
        void Update()
        {
            if (!isInitialized || !goeData.IsCreated || goeViews == null) return;
            
            float dt = Time.deltaTime;
            
            // Update movement (only Burst version supported with NativeArray)
            // Increment seed each frame for varied randomness
            randomSeed++;
            GOEImpulseSystemBurst.UpdateMovementBurst(goeData, dt, randomSeed);
            
            // Process contacts (attraction/repulsion)
            if (enableContactDetection && spatialHash != null)
            {
                GOEContactSystem.ProcessContacts(goeData, systemConfig, spatialHash, dt);
            }
            
            // Enforce boundaries (only Burst version supported with NativeArray)
            if (systemConfig.enableBoundaries)
            {
                GOEBoundarySystemBurst.EnforceBoundsBurst(
                    goeData,
                    systemConfig.movementBounds,
                    systemConfig.boundaryBehavior
                );
            }
            
            // Sync views from data
            for (int i = 0; i < goeData.Length; i++)
            {
                if (goeViews[i] != null)
                {
                    // Get temp copy since NativeArray indexer can't be passed by ref
                    GOEData temp = goeData[i];
                    goeViews[i].SyncFromData(ref temp);
                    goeViews[i].AnimateWings(temp.animPhase);
                }
            }
        }
        
        void OnDestroy()
        {
            // Cleanup native arrays
            if (isInitialized && goeData.IsCreated)
            {
                goeData.Dispose();
            }

            // Cleanup spatial hash
            if (spatialHash != null)
            {
                spatialHash.Dispose();
            }

            isInitialized = false;
        }
        
        void OnDrawGizmos()
        {
            if (!drawBounds || systemConfig == null) return;
            
            // Draw spawn bounds
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(systemConfig.spawnBounds.center, systemConfig.spawnBounds.size);
            
            // Draw movement bounds
            if (systemConfig.enableBoundaries)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireCube(systemConfig.movementBounds.center, systemConfig.movementBounds.size);
            }
            
            // Draw spatial grid
            if (drawSpatialGrid && spatialHash != null)
            {
                Gizmos.color = new Color(0.5f, 0.5f, 1f, 0.3f);
                
                float cellSize = spatialHash.CellSize;
                var gridDim = spatialHash.GridDimensions;
                
                // Draw a subset of grid lines (not all, would be too many)
                for (int x = 0; x <= gridDim.x; x += 2)
                {
                    for (int z = 0; z <= gridDim.z; z += 2)
                    {
                        Vector3 start = new Vector3(x * cellSize, 0, z * cellSize);
                        Vector3 end = new Vector3(x * cellSize, gridDim.y * cellSize, z * cellSize);
                        Gizmos.DrawLine(start, end);
                    }
                }
            }
        }
        
        void OnGUI()
        {
            if (!showStats || !isInitialized || !goeData.IsCreated) return;
            
            int activeCount = 0;
            float avgSpeed = 0f;
            int contactCount = 0;
            
            for (int i = 0; i < goeData.Length; i++)
            {
                if (goeData[i].isActive)
                {
                    activeCount++;
                    avgSpeed += Unity.Mathematics.math.length(goeData[i].velocity);
                    if (goeData[i].contactState > 0) contactCount++;
                }
            }
            
            avgSpeed /= activeCount;
            
            GUILayout.BeginArea(new Rect(10, 10, 350, 150));
            
            GUIStyle titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            
            GUILayout.Label("GOE System Stats", titleStyle);
            GUILayout.Label($"Active GOEs: {activeCount}/{goeData.Length}", labelStyle);
            GUILayout.Label($"Average Speed: {avgSpeed:F2}", labelStyle);
            GUILayout.Label($"FPS: {1f / Time.deltaTime:F0}", labelStyle);
            GUILayout.Label($"Burst: ON (Native) | Contacts: {(enableContactDetection ? "ON" : "OFF")}", labelStyle);
            
            if (enableContactDetection)
            {
                GUILayout.Label($"Active Contacts: {contactCount}", labelStyle);
            }
            
            GUILayout.EndArea();
        }
        
        /// <summary>
        /// Get reference to GOE data array (for external systems)
        /// </summary>
        public NativeArray<GOEData> GetGOEData() => goeData;
        
        /// <summary>
        /// Get reference to GOE view array (for external systems)
        /// </summary>
        public GOEView[] GetGOEViews() => goeViews;
        
        /// <summary>
        /// Get spatial hash (for debugging/visualization)
        /// </summary>
        public GOESpatialHash GetSpatialHash() => spatialHash;
    }
}
