using UnityEngine;

namespace GOE
{
    /// <summary>
    /// Spawner for DOTS-based GOE system
    /// Supports instant spawning and spawn-over-time
    /// </summary>
    public class GOESpawnerDOTS : MonoBehaviour
    {
        [Header("References")]
        public GOEManagerDOTS manager;
        public GOEGroupConfig[] groups;

        [Header("Spawn Mode")]
        [Tooltip("Spawn all entities instantly on start")]
        public bool instantSpawn = false;
        [Tooltip("Spawn entities gradually over time")]
        public bool spawnOverTime = true;

        [Header("Instant Spawn Configuration")]
        public int totalEntitiesToSpawn = 1000;
        public Vector3 spawnCenter = Vector3.zero;
        public Vector3 spawnExtents = new Vector3(20, 20, 20);

        [Header("Spawn Over Time Configuration")]
        [Tooltip("Entities to spawn per second")]
        public float spawnRate = 10f;
        [Tooltip("Maximum entities to spawn (0 = unlimited)")]
        public int maxTotalEntities = 5000;
        [Tooltip("Stop spawning after this many seconds (0 = never stop)")]
        public float spawnDuration = 0f;

        [Header("Initial Velocity")]
        public bool randomInitialVelocity = true;
        public float initialSpeed = 2f;

        [Header("Group Distribution")]
        [Tooltip("Leave empty for equal distribution")]
        public int[] entitiesPerGroup;

        // Runtime state
        private float spawnTimer = 0f;
        private float elapsedTime = 0f;
        private int totalSpawned = 0;
        private float[] groupSpawnAccumulator;
        private int[] groupsSpawned;

        void Start()
        {
            if (groups == null || groups.Length == 0)
            {
                Debug.LogError("No groups configured!");
                return;
            }

            // Initialize group tracking
            groupSpawnAccumulator = new float[groups.Length];
            groupsSpawned = new int[groups.Length];

            if (instantSpawn)
            {
                SpawnAllEntitiesInstantly();
            }
        }

        void Update()
        {
            if (!spawnOverTime || manager == null)
                return;

            // Check if we should stop spawning
            if (spawnDuration > 0f && elapsedTime >= spawnDuration)
                return;

            if (maxTotalEntities > 0 && totalSpawned >= maxTotalEntities)
                return;

            elapsedTime += Time.deltaTime;
            spawnTimer += Time.deltaTime;

            // Calculate how many entities to spawn this frame
            float entitiesThisFrame = spawnRate * Time.deltaTime;

            // Distribute across groups
            for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            {
                float groupPortion = entitiesThisFrame / groups.Length;
                groupSpawnAccumulator[groupIndex] += groupPortion;

                // Spawn full entities that have accumulated
                int toSpawn = Mathf.FloorToInt(groupSpawnAccumulator[groupIndex]);
                if (toSpawn > 0)
                {
                    for (int i = 0; i < toSpawn; i++)
                    {
                        // Check max limit
                        if (maxTotalEntities > 0 && totalSpawned >= maxTotalEntities)
                            return;

                        SpawnSingleEntity(groups[groupIndex]);
                        totalSpawned++;
                        groupsSpawned[groupIndex]++;
                    }

                    groupSpawnAccumulator[groupIndex] -= toSpawn;
                }
            }
        }

        [ContextMenu("Spawn All Entities Instantly")]
        public void SpawnAllEntitiesInstantly()
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

            totalSpawned = 0;
            for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            {
                GOEGroupConfig group = groups[groupIndex];
                int countForGroup = distribution[groupIndex];

                for (int i = 0; i < countForGroup; i++)
                {
                    SpawnSingleEntity(group);
                    totalSpawned++;
                }
            }

            Debug.Log($"Instantly spawned {totalSpawned} entities across {groups.Length} groups");
        }

        private void SpawnSingleEntity(GOEGroupConfig group)
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
            int perGroup = totalEntitiesToSpawn / groups.Length;
            int remainder = totalEntitiesToSpawn % groups.Length;

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

            // Draw spawn info text
            if (spawnOverTime && Application.isPlaying)
            {
                // Show spawn rate visualization
                Gizmos.color = Color.green;
                float radius = 0.5f + (spawnRate * 0.1f);
                Gizmos.DrawWireSphere(spawnCenter, radius);
            }
        }

        // Public methods for runtime control
        public void StartSpawning()
        {
            spawnOverTime = true;
            elapsedTime = 0f;
        }

        public void StopSpawning()
        {
            spawnOverTime = false;
        }

        public void SetSpawnRate(float rate)
        {
            spawnRate = Mathf.Max(0f, rate);
        }

        public int GetTotalSpawned()
        {
            return totalSpawned;
        }

        public void ResetSpawnCounts()
        {
            totalSpawned = 0;
            elapsedTime = 0f;
            if (groupsSpawned != null)
            {
                for (int i = 0; i < groupsSpawned.Length; i++)
                {
                    groupsSpawned[i] = 0;
                    groupSpawnAccumulator[i] = 0f;
                }
            }
        }
    }
}
