using Unity.Mathematics;

namespace GOE
{
    /// <summary>
    /// DOTS-style physics properties for two-zone force model
    /// Combines impulse-based movement with continuous force evaluation
    /// </summary>
    public struct PhysicsProperties
    {
        // Mass-based dynamics
        public float mass;
        public float maxForce;          // Force clamping for stability
        public float damping;           // Velocity damping coefficient (0.0-1.0)

        // Zone 1: Collision Avoidance (Short Range - Always Repulsive)
        public float collisionRadius;    // Inner zone radius
        public float collisionStrength;  // Inverse-square repulsion strength

        // Zone 2: Interaction (Long Range - Attraction OR Repulsion)
        public float interactionRadius;  // Outer zone radius
        public float interactionStrength; // Positive = attraction, Negative = repulsion

        // Impulse-based organic movement (from original GOE system)
        public float impulseStrength;    // Forward burst force
        public float impulseInterval;    // Time between bursts
        public float impulseTimer;       // Countdown to next impulse
        public float minImpulseInterval;
        public float maxImpulseInterval;

        /// <summary>
        /// Creates physics properties with default values
        /// </summary>
        public static PhysicsProperties Default()
        {
            return new PhysicsProperties
            {
                mass = 1.0f,
                maxForce = 50.0f,
                damping = 0.95f,

                // Collision zone (tight, strong repulsion)
                collisionRadius = 1.0f,
                collisionStrength = 20.0f,

                // Interaction zone (medium range, light attraction)
                interactionRadius = 5.0f,
                interactionStrength = 5.0f,

                // Impulse movement
                impulseStrength = 3.0f,
                minImpulseInterval = 1.0f,
                maxImpulseInterval = 2.0f,
                impulseTimer = 1.5f,
                impulseInterval = 1.5f
            };
        }
    }

    /// <summary>
    /// Separate arrays for Structure of Arrays (SoA) layout
    /// Better cache performance for Burst jobs
    /// </summary>
    public struct PhysicsArrays
    {
        // Transform data
        public Unity.Collections.NativeArray<float3> positions;
        public Unity.Collections.NativeArray<float3> velocities;
        public Unity.Collections.NativeArray<quaternion> rotations;

        // Physics data
        public Unity.Collections.NativeArray<PhysicsProperties> properties;
        public Unity.Collections.NativeArray<float3> accumulatedForces;

        // Visual/behavior data
        public Unity.Collections.NativeArray<int> groupIDs;
        public Unity.Collections.NativeArray<float> animPhases;
        public Unity.Collections.NativeArray<float> animSpeeds;
        public Unity.Collections.NativeArray<float> colorVariations;
        public Unity.Collections.NativeArray<float> scaleVariations;

        // State flags
        public Unity.Collections.NativeArray<bool> isActive;

        // Axis constraints (for 2D movement, etc.)
        public Unity.Collections.NativeArray<bool> constrainX;
        public Unity.Collections.NativeArray<bool> constrainY;
        public Unity.Collections.NativeArray<bool> constrainZ;
        public Unity.Collections.NativeArray<float3> constrainedPositions;
    }
}
