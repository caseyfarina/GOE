using UnityEngine;

namespace GOE
{
    /// <summary>
    /// Spawner for DOTS-based GOE system
    /// Spawns entities with full physics properties and axis locking
    /// </summary>
    public class GOESpawnerDOTS : MonoBehaviour
    {
        [Header("References")]
        public GOEManagerDOTS manager;
        public GOEGroupConfig[] groups;

        [Header("Spawn Configuration")]
        public bool spawnOnStart = true;
        public int entitiesToSpawn = 1000;
        public Vector3 spawnCenter = Vector3.zero;
        public Vector3 spawnExtents = new Vector3(20, 20, 20);

        [Header("Initial Velocity")]
        public bool randomInitialVelocity = true;
        public float initialSpeed = 2f;

        [Header("Group Distribution")]
        [Tooltip("Leave empty for equal distribution")]
        public int[] entitiesPerGroup;

        void Start()
        {
            if (spawnOnStart)
            {
                SpawnEntities();
            }
        }

        [ContextMenu("Spawn Entities")]
        public void SpawnEntities()
        {
            if (manager == null)
            {
                Debug.LogError("GOEManagerDOTS reference is missing!");
                return;
            }

            if (groups == null || groups.Length == 0)
            {
                Debug.LogError("No groups configured!");
                return;
            }

            // Determine distribution
            int[] distribution = CalculateDistribution();

            int totalSpawned = 0;
            for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            {
                GOEGroupConfig group = groups[groupIndex];
                int countForGroup = distribution[groupIndex];

                for (int i = 0; i < countForGroup; i++)
                {
                    // Random position within spawn volume
                    Vector3 position = spawnCenter + new Vector3(
                        Random.Range(-spawnExtents.x, spawnExtents.x),
                        Random.Range(-spawnExtents.y, spawnExtents.y),
                        Random.Range(-spawnExtents.z, spawnExtents.z)
                    );

                    // Random initial velocity
                    Vector3? velocity = null;
                    if (randomInitialVelocity)
                    {
                        velocity = Random.insideUnitSphere * initialSpeed;
                    }

                    // Spawn entity
                    manager.SpawnEntity(position, group, velocity);
                    totalSpawned++;
                }
            }

            Debug.Log($"Spawned {totalSpawned} entities across {groups.Length} groups");
        }

        private int[] CalculateDistribution()
        {
            // Use custom distribution if provided
            if (entitiesPerGroup != null && entitiesPerGroup.Length == groups.Length)
            {
                return entitiesPerGroup;
            }

            // Equal distribution
            int[] distribution = new int[groups.Length];
            int perGroup = entitiesToSpawn / groups.Length;
            int remainder = entitiesToSpawn % groups.Length;

            for (int i = 0; i < groups.Length; i++)
            {
                distribution[i] = perGroup + (i < remainder ? 1 : 0);
            }

            return distribution;
        }

        void OnDrawGizmosSelected()
        {
            // Draw spawn volume
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(spawnCenter, spawnExtents * 2f);
        }
    }
}
