using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace GOE
{
    /// <summary>
    /// Spatial hash grid for efficient proximity queries.
    /// Divides 3D space into cells for O(1) neighbor lookups.
    /// </summary>
    public class GOESpatialHash
    {
        private float cellSize;
        private int3 gridDimensions;
        private int totalCells;
        
        // Native arrays for Burst compatibility
        private NativeArray<int> cellStarts;    // Start index in entityIndices for each cell
        private NativeArray<int> cellCounts;    // Number of entities in each cell
        private NativeArray<int> entityIndices; // Sorted entity indices by cell
        private NativeArray<int> entityCells;   // Which cell each entity belongs to
        
        private bool isInitialized = false;
        
        public GOESpatialHash(float cellSize, int3 gridDimensions)
        {
            this.cellSize = cellSize;
            this.gridDimensions = gridDimensions;
            this.totalCells = gridDimensions.x * gridDimensions.y * gridDimensions.z;
        }
        
        /// <summary>
        /// Initialize native arrays
        /// </summary>
        public void Initialize(int maxEntities)
        {
            if (isInitialized)
                Dispose();
            
            cellStarts = new NativeArray<int>(totalCells, Allocator.Persistent);
            cellCounts = new NativeArray<int>(totalCells, Allocator.Persistent);
            entityIndices = new NativeArray<int>(maxEntities, Allocator.Persistent);
            entityCells = new NativeArray<int>(maxEntities, Allocator.Persistent);
            
            isInitialized = true;
        }
        
        /// <summary>
        /// Get hash cell index from world position
        /// </summary>
        public int GetCellIndex(float3 position)
        {
            int3 cell = GetCellCoords(position);
            
            // Clamp to grid bounds
            cell = math.clamp(cell, int3.zero, gridDimensions - 1);
            
            return cell.x + cell.y * gridDimensions.x + cell.z * gridDimensions.x * gridDimensions.y;
        }
        
        /// <summary>
        /// Get 3D cell coordinates from world position
        /// </summary>
        public int3 GetCellCoords(float3 position)
        {
            return new int3(
                (int)math.floor(position.x / cellSize),
                (int)math.floor(position.y / cellSize),
                (int)math.floor(position.z / cellSize)
            );
        }
        
        /// <summary>
        /// Clear and rebuild spatial hash from entity positions
        /// </summary>
        public void Rebuild(NativeArray<float3> positions)
        {
            if (!isInitialized)
            {
                Debug.LogError("Spatial hash not initialized!");
                return;
            }
            
            int entityCount = positions.Length;
            
            // Clear counts
            for (int i = 0; i < totalCells; i++)
            {
                cellCounts[i] = 0;
            }
            
            // Count entities per cell
            for (int i = 0; i < entityCount; i++)
            {
                int cellIndex = GetCellIndex(positions[i]);
                entityCells[i] = cellIndex;
                cellCounts[cellIndex]++;
            }
            
            // Calculate start indices (prefix sum)
            int sum = 0;
            for (int i = 0; i < totalCells; i++)
            {
                cellStarts[i] = sum;
                sum += cellCounts[i];
            }
            
            // Reset counts for second pass
            for (int i = 0; i < totalCells; i++)
            {
                cellCounts[i] = 0;
            }
            
            // Fill entity indices
            for (int i = 0; i < entityCount; i++)
            {
                int cellIndex = entityCells[i];
                int index = cellStarts[cellIndex] + cellCounts[cellIndex];
                entityIndices[index] = i;
                cellCounts[cellIndex]++;
            }
        }
        
        /// <summary>
        /// Get neighbors within radius of a position
        /// </summary>
        public void GetNeighbors(float3 position, float radius, NativeList<int> neighbors)
        {
            neighbors.Clear();
            
            // Calculate cell range to check
            int cellRadius = (int)math.ceil(radius / cellSize);
            int3 centerCell = GetCellCoords(position);
            
            for (int z = -cellRadius; z <= cellRadius; z++)
            {
                for (int y = -cellRadius; y <= cellRadius; y++)
                {
                    for (int x = -cellRadius; x <= cellRadius; x++)
                    {
                        int3 cell = centerCell + new int3(x, y, z);
                        
                        // Skip out of bounds cells
                        if (math.any(cell < int3.zero) || math.any(cell >= gridDimensions))
                            continue;
                        
                        int cellIndex = cell.x + cell.y * gridDimensions.x + 
                                       cell.z * gridDimensions.x * gridDimensions.y;
                        
                        // Add all entities in this cell
                        int start = cellStarts[cellIndex];
                        int count = cellCounts[cellIndex];
                        
                        for (int i = 0; i < count; i++)
                        {
                            neighbors.Add(entityIndices[start + i]);
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Dispose native arrays
        /// </summary>
        public void Dispose()
        {
            if (!isInitialized) return;
            
            if (cellStarts.IsCreated) cellStarts.Dispose();
            if (cellCounts.IsCreated) cellCounts.Dispose();
            if (entityIndices.IsCreated) entityIndices.Dispose();
            if (entityCells.IsCreated) entityCells.Dispose();
            
            isInitialized = false;
        }
        
        // Accessors for Burst jobs
        public NativeArray<int> CellStarts => cellStarts;
        public NativeArray<int> CellCounts => cellCounts;
        public NativeArray<int> EntityIndices => entityIndices;
        public float CellSize => cellSize;
        public int3 GridDimensions => gridDimensions;
    }
}
