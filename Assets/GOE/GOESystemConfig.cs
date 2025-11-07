using Unity.Mathematics;
using UnityEngine;

namespace GOE
{
    /// <summary>
    /// Main system configuration for the GOE system.
    /// </summary>
    [CreateAssetMenu(fileName = "GOESystemConfig", menuName = "GOE/System Configuration")]
    public class GOESystemConfig : ScriptableObject
    {
        [Header("Groups")]
        public GOEGroupConfig[] groups;
        
        [Header("Spawn Settings")]
        public int[] spawnCountsPerGroup;  // How many of each group
        public Bounds spawnBounds = new Bounds(Vector3.zero, Vector3.one * 20f);
        
        [Header("Spatial Partitioning")]
        public float cellSize = 5f;
        public int3 gridDimensions = new int3(20, 20, 5);
        
        [Header("Boundaries")]
        public bool enableBoundaries = true;
        public Bounds movementBounds = new Bounds(Vector3.zero, Vector3.one * 50f);
        public BoundaryBehavior boundaryBehavior = BoundaryBehavior.Reflect;
        
        /// <summary>
        /// Get group config by ID
        /// </summary>
        public GOEGroupConfig GetGroupConfig(int groupID)
        {
            foreach (var group in groups)
            {
                if (group.groupID == groupID)
                    return group;
            }
            return null;
        }
        
        void OnValidate()
        {
            // Ensure spawn counts array matches groups array
            if (groups != null && (spawnCountsPerGroup == null || spawnCountsPerGroup.Length != groups.Length))
            {
                int[] newCounts = new int[groups.Length];
                if (spawnCountsPerGroup != null)
                {
                    for (int i = 0; i < Mathf.Min(spawnCountsPerGroup.Length, newCounts.Length); i++)
                    {
                        newCounts[i] = spawnCountsPerGroup[i];
                    }
                }
                spawnCountsPerGroup = newCounts;
            }
        }
    }

    public enum BoundaryBehavior
    {
        Reflect,      // Bounce off walls
        Wrap,         // Teleport to opposite side
        Dampen        // Slow down near edges
    }
}
