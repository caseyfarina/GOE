using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace GOE
{
    /// <summary>
    /// Integration system that combines:
    /// - Continuous force-based physics (F=ma)
    /// - Impulse-based organic movement (bursts)
    /// - Velocity damping
    /// </summary>
    public static class GOEIntegrationSystem
    {
        /// <summary>
        /// Integrate forces and impulses into velocity
        /// Combines DOTS physics model with original GOE impulse system
        /// </summary>
        [BurstCompile]
        public struct IntegrateVelocityJob : IJobParallelFor
        {
            public NativeArray<PhysicsProperties> properties;
            [ReadOnly] public NativeArray<float3> forces;
            [ReadOnly] public NativeArray<quaternion> rotations;
            [ReadOnly] public NativeArray<bool> isActive;

            public NativeArray<float3> velocities;

            public float deltaTime;
            public uint randomSeed;

            public void Execute(int index)
            {
                if (!isActive[index])
                    return;

                PhysicsProperties props = properties[index];
                float3 velocity = velocities[index];

                // === 1. APPLY CONTINUOUS FORCES (F = ma) ===
                float3 acceleration = forces[index] / props.mass;
                velocity += acceleration * deltaTime;

                // === 2. APPLY IMPULSE-BASED MOVEMENT (Organic Bursts) ===
                // Update impulse timer
                props.impulseTimer -= deltaTime;

                // Apply forward impulse when timer expires
                if (props.impulseTimer <= 0f)
                {
                    // Get forward direction from rotation
                    float3 forward = math.mul(rotations[index], new float3(0, 0, 1));

                    // Apply burst of force in forward direction
                    float3 impulseForce = forward * props.impulseStrength;
                    float3 impulseAcceleration = impulseForce / props.mass;
                    velocity += impulseAcceleration;

                    // Reset timer with random interval
                    Unity.Mathematics.Random rng = Unity.Mathematics.Random.CreateFromIndex((uint)index + randomSeed);
                    props.impulseTimer = rng.NextFloat(props.minImpulseInterval, props.maxImpulseInterval);
                    props.impulseInterval = props.impulseTimer;
                }

                // === 3. APPLY DAMPING ===
                // Frame-rate independent damping
                velocity *= (1f - props.damping * deltaTime);

                // Write back
                velocities[index] = velocity;
                properties[index] = props;
            }
        }

        /// <summary>
        /// Integrate velocity into position
        /// </summary>
        [BurstCompile]
        public struct IntegratePositionJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> velocities;
            [ReadOnly] public NativeArray<bool> isActive;
            [ReadOnly] public NativeArray<bool> constrainX;
            [ReadOnly] public NativeArray<bool> constrainY;
            [ReadOnly] public NativeArray<bool> constrainZ;

            public NativeArray<float3> positions;
            public float deltaTime;

            public void Execute(int index)
            {
                if (!isActive[index])
                    return;

                float3 newPosition = positions[index] + velocities[index] * deltaTime;

                // Apply axis constraints (for 2D movement, etc.)
                if (constrainX[index])
                    newPosition.x = positions[index].x;
                if (constrainY[index])
                    newPosition.y = positions[index].y;
                if (constrainZ[index])
                    newPosition.z = positions[index].z;

                positions[index] = newPosition;
            }
        }

        /// <summary>
        /// Update rotations to face movement direction
        /// </summary>
        [BurstCompile]
        public struct UpdateRotationsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> velocities;
            [ReadOnly] public NativeArray<bool> isActive;

            public NativeArray<quaternion> rotations;
            public float rotationSpeed;
            public float deltaTime;

            public void Execute(int index)
            {
                if (!isActive[index])
                    return;

                float3 velocity = velocities[index];
                float speedSq = math.lengthsq(velocity);

                // Only rotate if moving fast enough
                if (speedSq > 0.001f)
                {
                    // Target rotation faces velocity direction
                    float3 forward = math.normalize(velocity);
                    quaternion targetRotation = quaternion.LookRotationSafe(forward, new float3(0, 1, 0));

                    // Smoothly interpolate toward target rotation
                    float t = math.min(1f, rotationSpeed * deltaTime);
                    rotations[index] = math.slerp(rotations[index], targetRotation, t);
                }
            }
        }

        /// <summary>
        /// Update animation phases based on movement
        /// </summary>
        [BurstCompile]
        public struct UpdateAnimationJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> velocities;
            [ReadOnly] public NativeArray<bool> isActive;
            [ReadOnly] public NativeArray<float> animSpeeds;

            public NativeArray<float> animPhases;
            public float deltaTime;

            public void Execute(int index)
            {
                if (!isActive[index])
                    return;

                // Animation speed based on velocity magnitude
                float speed = math.length(velocities[index]);
                float animSpeed = animSpeeds[index] * (0.5f + speed * 0.5f);

                // Update phase (wraps at 2*PI)
                float phase = animPhases[index] + animSpeed * deltaTime;
                animPhases[index] = phase - math.floor(phase / (2f * math.PI)) * (2f * math.PI);
            }
        }

        /// <summary>
        /// Update entity lifetimes and deactivate expired entities
        /// </summary>
        [BurstCompile]
        public struct UpdateLifetimeJob : IJobParallelFor
        {
            public NativeArray<PhysicsProperties> properties;
            public NativeArray<bool> isActive;
            public float deltaTime;

            public void Execute(int index)
            {
                if (!isActive[index])
                    return;

                PhysicsProperties props = properties[index];

                // Skip if infinite lifetime (lifetime == 0)
                if (props.lifetime <= 0f)
                    return;

                // Increment age
                props.age += deltaTime;

                // Deactivate if expired
                if (props.age >= props.lifetime)
                {
                    isActive[index] = false;
                }

                properties[index] = props;
            }
        }
    }
}
