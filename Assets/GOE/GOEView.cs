using UnityEngine;
using GOE;

namespace GOE
{
    /// <summary>
    /// Lightweight view component attached to GameObject.
    /// Acts as a visual proxy for GOEData.
    /// </summary>
    public class GOEView : MonoBehaviour
    {
        [HideInInspector] public int dataIndex;
        [HideInInspector] public int groupID;
        
        private Renderer rend;
        private MaterialPropertyBlock propBlock;
        private Transform cachedTransform;
        private Material instanceMaterial;
        
        // Animation targets (for procedural animation)
        public Transform wingLeft;
        public Transform wingRight;
        public Transform tail;
        
        void Awake()
        {
            rend = GetComponent<Renderer>();
            propBlock = new MaterialPropertyBlock();
            cachedTransform = transform;
        }
        
        /// <summary>
        /// Initialize with group-specific material
        /// </summary>
        public void Initialize(GOEGroupConfig groupConfig, int dataIdx)
        {
            dataIndex = dataIdx;
            groupID = groupConfig.groupID;
            
            // Create material instance for this group
            instanceMaterial = new Material(groupConfig.baseMaterial);
            instanceMaterial.color = groupConfig.baseColor;
            rend.material = instanceMaterial;
        }
        
        /// <summary>
        /// Sync GameObject transform and visuals from data
        /// </summary>
        public void SyncFromData(ref GOEData data)
        {
            cachedTransform.position = data.position;
            cachedTransform.rotation = data.rotation;
            
            // Apply per-instance variations via property block
            rend.GetPropertyBlock(propBlock);
            
            // Color variation (HSV shift)
            Color variedColor = ShiftColorHSV(instanceMaterial.color, data.colorVariation);
            propBlock.SetColor("_Color", variedColor);
            
            rend.SetPropertyBlock(propBlock);
            
            // Apply scale
            cachedTransform.localScale = Vector3.one * data.scaleVariation;
        }
        
        /// <summary>
        /// Apply procedural animation to wings and tail
        /// </summary>
        public void AnimateWings(float phase)
        {
            if (wingLeft != null && wingRight != null)
            {
                float angle = Mathf.Sin(phase * Mathf.PI * 2f) * 45f;
                wingLeft.localRotation = Quaternion.Euler(0, 0, angle);
                wingRight.localRotation = Quaternion.Euler(0, 0, -angle);
            }
            
            if (tail != null)
            {
                float tailWag = Mathf.Sin(phase * Mathf.PI * 4f) * 15f;
                tail.localRotation = Quaternion.Euler(0, tailWag, 0);
            }
        }
        
        private Color ShiftColorHSV(Color baseColor, float variation)
        {
            Color.RGBToHSV(baseColor, out float h, out float s, out float v);
            h += variation;
            if (h > 1f) h -= 1f;
            if (h < 0f) h += 1f;
            return Color.HSVToRGB(h, s, v);
        }
        
        void OnDestroy()
        {
            if (instanceMaterial != null)
                Destroy(instanceMaterial);
        }
    }
}
