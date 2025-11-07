using Unity.Mathematics;
using UnityEngine;

namespace GOE
{
    /// <summary>
    /// Core data structure for GameObject Entity (GOE).
    /// Keep this lightweight and cache-friendly for performance.
    /// </summary>
    [System.Serializable]
    public struct GOEData
    {
        // Transform
        public float3 position;
        public float3 velocity;
        public quaternion rotation;
        
        // Behavior state
        public int groupID;
        public float animPhase;       // 0-1 cycle for wing flaps
        public float animSpeed;       // Individual variation in animation speed
        
        // Visual variation
        public float colorVariation;  // 0-1 for color tint offset
        public float scaleVariation;  // 0.8-1.2 for size variation
        
        // Impulse-based movement
        public float damping;              // 0.95 = slow decay, 0.85 = fast decay
        public float impulseStrength;      // Base forward impulse magnitude
        public float impulseTimer;         // Countdown to next impulse
        public float impulseInterval;      // Current interval (randomized)
        public float minImpulseInterval;   // Min seconds between impulses
        public float maxImpulseInterval;   // Max seconds between impulses
        
        // State flags
        public bool isActive;
        public byte contactState;     // 0 = none, 1 = contact A, 2 = contact B

        // Axis constraints (for 2D movement or locking to planes)
        public bool constrainX;       // Lock X axis movement
        public bool constrainY;       // Lock Y axis movement (common for ground creatures)
        public bool constrainZ;       // Lock Z axis movement
        public float3 constrainedPosition;  // Target position values for locked axes
    }

    /// <summary>
    /// Spatial hash cell for contact detection
    /// </summary>
    public struct SpatialCell
    {
        public int startIndex;
        public int count;
    }

    /// <summary>
    /// Contact event data
    /// </summary>
    public struct ContactEvent
    {
        public int goeIndexA;
        public int goeIndexB;
        public float3 contactPoint;
        public float distance;
    }
}
