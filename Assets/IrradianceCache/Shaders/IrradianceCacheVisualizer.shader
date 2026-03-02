// Upgrade NOTE: replaced 'defined IRRADIANCE_CACHE' with 'defined (IRRADIANCE_CACHE)'
// Upgrade NOTE: replaced 'defined UNIFORM_LIGHT_PROBE' with 'defined (UNIFORM_LIGHT_PROBE)'

Shader "IrradianceCache/Visualizer"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        [KeywordEnum(IRRADIANCE_CACHE,UNIFORM_LIGHT_PROBE)]_Mode("Mode", float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Name "IC Visualizer"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile IRRADIANCE_CACHE UNIFORM_LIGHT_PROBE

            #include "UnityCG.cginc"
            #include "UniformGridLightProbeMulti.hlsl"
            #include "IrradianceCacheMulti.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _IrradianceCacheIntensity;
            float _Mode;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);

    // 方法1：使用预处理器指令
    #if defined(IRRADIANCE_CACHE)
        if (IsInsideVolume(i.worldPos))
        {
            float3 shColor = SampleIrradianceCacheAuto(i.worldPos, normal);
            return fixed4(shColor, 1);
        }
    #elif defined(UNIFORM_LIGHT_PROBE)
        if (IsInsideVolume(i.worldPos))
        {
            float3 shColor = SampleGridLightProbeAuto(i.worldPos, normal);
            return fixed4(shColor, 1);
        }
    #endif
                return fixed4(0, 0, 0, 1);
            }
            ENDCG
        }
    }
    FallBack "Unlit/Texture"
}
