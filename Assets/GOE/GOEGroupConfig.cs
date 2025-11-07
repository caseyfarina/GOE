using UnityEngine;

namespace GOE
{
    /// <summary>
    /// ScriptableObject defining a group of GOEs with shared behavior and appearance.
    /// </summary>
    [CreateAssetMenu(fileName = "GroupConfig", menuName = "GOE/Group Configuration")]
    public class GOEGroupConfig : ScriptableObject
    {
        [Header("Identity")]
        public string groupName;
        public int groupID;  // Unique identifier
        
        [Header("Visual")]
        public GameObject prefab;  // Reference to the prefab
        public Material baseMaterial;
        public Color baseColor = Color.white;
        [Range(0f, 1f)]
        public float colorVariationRange = 0.2f;  // HSV variation
        public Vector2 scaleRange = new Vector2(0.8f, 1.2f);
        
        [Header("Movement - Impulse")]
        public float impulseStrength = 5f;
        public float impulseStrengthVariation = 1f;
        public Vector2 impulseIntervalRange = new Vector2(3f, 5f);
        
        [Header("Movement - Damping")]
        [Range(0f, 1f)]
        public float damping = 0.92f;
        [Range(0f, 0.2f)]
        public float dampingVariation = 0.05f;
        
        [Header("Animation")]
        public float baseAnimSpeed = 2f;
        public float animSpeedVariation = 0.5f;

        [Header("Movement - Constraints")]
        [Tooltip("Lock X axis (left/right) to a fixed value")]
        public bool constrainX = false;
        public float constrainedXValue = 0f;

        [Tooltip("Lock Y axis (up/down) to a fixed value - useful for ground creatures")]
        public bool constrainY = false;
        public float constrainedYValue = 0f;

        [Tooltip("Lock Z axis (forward/back) to a fixed value")]
        public bool constrainZ = false;
        public float constrainedZValue = 0f;

        [Header("Quick Constraint Presets")]
        [Tooltip("Enable for 2D ground movement (XZ plane, Y locked to 0)")]
        public bool preset2DGround = false;
        [Tooltip("Enable for 2D side-scrolling (XY plane, Z locked to 0)")]
        public bool preset2DSideScroll = false;

        [Header("Contact Behaviors")]
        public ContactRule[] contactRules;
        
        /// <summary>
        /// Initialize a GOE with this group's settings
        /// </summary>
        public void InitializeGOE(ref GOEData data)
        {
            data.groupID = groupID;

            // Movement
            data.impulseStrength = impulseStrength + Random.Range(-impulseStrengthVariation, impulseStrengthVariation);
            data.minImpulseInterval = impulseIntervalRange.x;
            data.maxImpulseInterval = impulseIntervalRange.y;
            data.damping = damping + Random.Range(-dampingVariation, dampingVariation);

            // Animation
            data.animSpeed = baseAnimSpeed + Random.Range(-animSpeedVariation, animSpeedVariation);

            // Visual variation
            data.colorVariation = Random.Range(-colorVariationRange, colorVariationRange);
            data.scaleVariation = Random.Range(scaleRange.x, scaleRange.y);

            // Initialize timer with random offset
            data.impulseTimer = Random.Range(data.minImpulseInterval, data.maxImpulseInterval);

            // Apply constraint presets (override manual settings if enabled)
            bool useConstrainX = constrainX;
            bool useConstrainY = constrainY;
            bool useConstrainZ = constrainZ;
            float useConstrainedX = constrainedXValue;
            float useConstrainedY = constrainedYValue;
            float useConstrainedZ = constrainedZValue;

            if (preset2DGround)
            {
                // Lock to XZ plane at Y=0 (ground movement)
                useConstrainY = true;
                useConstrainedY = 0f;
            }
            else if (preset2DSideScroll)
            {
                // Lock to XY plane at Z=0 (side-scroller)
                useConstrainZ = true;
                useConstrainedZ = 0f;
            }

            // Set constraints
            data.constrainX = useConstrainX;
            data.constrainY = useConstrainY;
            data.constrainZ = useConstrainZ;
            data.constrainedPosition = new Unity.Mathematics.float3(
                useConstrainedX,
                useConstrainedY,
                useConstrainedZ
            );
        }
    }

    /// <summary>
    /// Defines how this group responds to contact with another group
    /// </summary>
    [System.Serializable]
    public class ContactRule
    {
        public int targetGroupID;
        public ContactResponse response;
        public float responseStrength = 1f;
        public float activationDistance = 2f;
    }

    public enum ContactResponse
    {
        Attract,
        Repel,
        Boost,      // Extra impulse forward
        Slow,       // Reduce velocity
        ChangeColor, // Temporary color change
        Custom
    }
}
