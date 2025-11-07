using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace GOE
{
    /// <summary>
    /// Burst-compiled movement system for maximum performance
    /// </summary>
    public static class GOEImpulseSystemBurst
    {
        /// <summary>
        /// Update movement for all GOEs using Burst compilation (NativeArray version - zero copy)
        /// </summary>
        public static void UpdateMovementBurst(NativeArray<GOEData> data, float deltaTime, uint randomSeed)
        {
            // Schedule job directly on persistent NativeArray - no copying needed!
            var job = new MovementUpdateJob
            {
                data = data,
                deltaTime = deltaTime,
                randomSeed = randomSeed
            };

            JobHandle handle = job.Schedule(data.Length, 32);
            handle.Complete();
        }
    }
    
    /// <summary>
    /// Burst-compiled job for updating GOE movement
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct MovementUpdateJob : IJobParallelFor
    {
        public NativeArray<GOEData> data;
        public float deltaTime;
        public uint randomSeed;
        
        public void Execute(int index)
        {
            GOEData goe = data[index];
            
            if (!goe.isActive) return;
            
            // Apply damping to velocity
            goe.velocity *= goe.damping;
            
            // Update impulse timer
            goe.impulseTimer -= deltaTime;
            
            // Fire impulse when timer expires
            if (goe.impulseTimer <= 0f)
            {
                // Create random generator with unique seed per entity
                Unity.Mathematics.Random random = new Unity.Mathematics.Random(
                    randomSeed + (uint)index + 1
                );
                
                ApplyImpulse(ref goe, ref random);
                
                // Reset timer with new random interval
                goe.impulseTimer = random.NextFloat(
                    goe.minImpulseInterval, 
                    goe.maxImpulseInterval
                );
            }
            
            // Update position
            goe.position += goe.velocity * deltaTime;

            // Apply axis constraints (lock position/velocity on constrained axes)
            ApplyAxisConstraints(ref goe);

            // Update rotation to face velocity direction
            if (math.lengthsq(goe.velocity) > 0.01f)
            {
                float3 forward = math.normalize(goe.velocity);
                goe.rotation = quaternion.LookRotationSafe(forward, math.up());
            }
            
            // Update animation phase based on velocity magnitude
            float speed = math.length(goe.velocity);
            goe.animPhase += speed * goe.animSpeed * deltaTime;
            
            // Wrap animation phase
            if (goe.animPhase > 1f)
            {
                goe.animPhase = math.fmod(goe.animPhase, 1f);
            }
            
            data[index] = goe;
        }
        
        /// <summary>
        /// Apply axis constraints using SIMD-friendly mask approach
        /// </summary>
        private void ApplyAxisConstraints(ref GOEData goe)
        {
            // Create mask: 1.0 for free axes, 0.0 for locked axes
            float3 mask = new float3(
                goe.constrainX ? 0f : 1f,
                goe.constrainY ? 0f : 1f,
                goe.constrainZ ? 0f : 1f
            );

            // Apply constraints: locked axes use constrained value, free axes keep current
            // This is SIMD-friendly - no branching!
            goe.position = goe.position * mask + goe.constrainedPosition * (1f - mask);
            goe.velocity *= mask;  // Zero velocity on locked axes
        }

        private void ApplyImpulse(ref GOEData goe, ref Unity.Mathematics.Random random)
        {
            // Apply impulse in local forward direction (Z-axis)
            float3 forward = math.mul(goe.rotation, new float3(0, 0, 1));
            
            // Add random variation to impulse strength (±20%)
            float variation = random.NextFloat(0.8f, 1.2f);
            float impulse = goe.impulseStrength * variation;
            
            goe.velocity += forward * impulse;
            
            // Add slight random lateral deviation
            float3 randomOffset = new float3(
                random.NextFloat(-0.1f, 0.1f),
                random.NextFloat(-0.1f, 0.1f),
                0f
            );
            goe.velocity += randomOffset;
        }
    }
}
