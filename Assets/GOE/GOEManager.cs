using UnityEngine;

namespace GOE
{
    /// <summary>
    /// Main manager for the GOE system.
    /// Orchestrates spawning, updates, and rendering of all GOEs.
    /// </summary>
    public class GOEManager : MonoBehaviour
    {
        [Header("Configuration")]
        public GOESystemConfig systemConfig;
        
        [Header("Debug")]
        public bool drawBounds = true;
        public bool showStats = true;
        
        private GOEData[] goeData;
        private GOEView[] goeViews;
        
        void Start()
        {
            if (systemConfig == null)
            {
                Debug.LogError("GOEManager: No system config assigned!");
                enabled = false;
                return;
            }
            
            // Spawn all groups
            GOESpawner.SpawnGroups(
                systemConfig,
                out goeData,
                out goeViews,
                transform
            );
        }
        
        void Update()
        {
            if (goeData == null || goeViews == null) return;
            
            float dt = Time.deltaTime;
            
            // Update all movement
            GOEImpulseSystem.UpdateMovement(goeData, dt);
            
            // Enforce boundaries
            if (systemConfig.enableBoundaries)
            {
                GOEBoundarySystem.EnforceBounds(
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
                    goeViews[i].SyncFromData(ref goeData[i]);
                    goeViews[i].AnimateWings(goeData[i].animPhase);
                }
            }
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
        }
        
        void OnGUI()
        {
            if (!showStats || goeData == null) return;
            
            int activeCount = 0;
            float avgSpeed = 0f;
            
            for (int i = 0; i < goeData.Length; i++)
            {
                if (goeData[i].isActive)
                {
                    activeCount++;
                    avgSpeed += Unity.Mathematics.math.length(goeData[i].velocity);
                }
            }
            
            avgSpeed /= activeCount;
            
            GUILayout.BeginArea(new Rect(10, 10, 300, 100));
            GUILayout.Label($"GOE System Stats", new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold });
            GUILayout.Label($"Active GOEs: {activeCount}/{goeData.Length}");
            GUILayout.Label($"Average Speed: {avgSpeed:F2}");
            GUILayout.Label($"FPS: {1f / Time.deltaTime:F0}");
            GUILayout.EndArea();
        }
        
        /// <summary>
        /// Get reference to GOE data array (for external systems)
        /// </summary>
        public GOEData[] GetGOEData() => goeData;
        
        /// <summary>
        /// Get reference to GOE view array (for external systems)
        /// </summary>
        public GOEView[] GetGOEViews() => goeViews;
    }
}
