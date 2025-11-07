// GOE Simple Instance Color Shader
// Simpler version without HSV conversion
// Uses direct color tinting via MaterialPropertyBlock

Shader "GOE/SimpleInstanceColor"
{
    Properties
    {
        _Color ("Base Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        #pragma multi_compile_instancing

        sampler2D _MainTex;

        struct Input
        {
            float2 uv_MainTex;
        };

        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
        UNITY_INSTANCING_BUFFER_END(Props)

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float4 color = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
            fixed4 tex = tex2D(_MainTex, IN.uv_MainTex);
            o.Albedo = color.rgb * tex.rgb;
            o.Alpha = color.a * tex.a;
        }
        ENDCG
    }
    
    FallBack "Diffuse"
}
