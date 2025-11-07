using Unity.Mathematics;
using UnityEngine;

namespace GOE
{
    /// <summary>
    /// Handles impulse-based movement for GOEs.
    /// GOEs receive random bursts of forward energy and coast with damping between impulses.
    /// </summary>
    public static class GOEImpulseSystem
    {
        /// <summary>
        /// Update movement for all GOEs
        /// </summary>
        public static void UpdateMovement(GOEData[] data, float deltaTime)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (!data[i].isActive) continue;
                
                ref GOEData goe = ref data[i];
                
                // Apply damping to velocity (coasting/decay)
                goe.velocity *= goe.damping;
                
                // Update impulse timer
                goe.impulseTimer -= deltaTime;
                
                // Fire impulse when timer expires
                if (goe.impulseTimer <= 0f)
                {
                    ApplyImpulse(ref goe);
                    
                    // Reset timer with new random interval
                    goe.impulseTimer = UnityEngine.Random.Range(
                        goe.minImpulseInterval, 
                        goe.maxImpulseInterval
                    );
                }
                
                // Update position
                goe.position += goe.velocity * deltaTime;
                
                // Update rotation to face velocity direction
                if (math.lengthsq(goe.velocity) > 0.01f)
                {
                    float3 forward = math.normalize(goe.velocity);
                    goe.rotation = quaternion.LookRotationSafe(forward, math.up());
                }
                
                // Update animation phase based on velocity magnitude
                float speed = math.length(goe.velocity);
                goe.animPhase += speed * goe.animSpeed * deltaTime;
                if (goe.animPhase > 1f) goe.animPhase -= 1f;
            }
        }
        
        /// <summary>
        /// Apply a forward impulse to a GOE
        /// </summary>
        public static void ApplyImpulse(ref GOEData goe)
        {
            // Apply impulse in local forward direction (Z-axis)
            float3 forward = math.mul(goe.rotation, new float3(0, 0, 1));
            
            // Add some random variation to impulse strength (±20%)
            float variation = UnityEngine.Random.Range(0.8f, 1.2f);
            float impulse = goe.impulseStrength * variation;
            
            goe.velocity += forward * impulse;
            
            // Optional: Add slight random lateral deviation
            float3 randomOffset = new float3(
                UnityEngine.Random.Range(-0.1f, 0.1f),
                UnityEngine.Random.Range(-0.1f, 0.1f),
                0f
            );
            goe.velocity += randomOffset;
        }
        
        /// <summary>
        /// Initialize a GOE with impulse parameters
        /// </summary>
        public static void InitializeImpulseData(
            ref GOEData goe, 
            float minInterval = 3f, 
            float maxInterval = 5f,
            float impulseStrength = 5f,
            float damping = 0.92f)
        {
            goe.minImpulseInterval = minInterval;
            goe.maxImpulseInterval = maxInterval;
            goe.impulseStrength = impulseStrength;
            goe.damping = damping;
            
            // Start with random initial timer
            goe.impulseTimer = UnityEngine.Random.Range(minInterval, maxInterval);
            
            // Give initial impulse so they don't all start stationary
            ApplyImpulse(ref goe);
        }
    }
}
