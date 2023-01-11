Shader "Hidden/ClusterBasedLighting/DebugClusterAABB"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            HLSLPROGRAM

            #pragma vertex main_VS
            #pragma fragment main_PS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
            #include "./ClusterBasedLightingCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                uint instanceID : SV_InstanceID;
            };


            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
            };

            

            StructuredBuffer<float3> ClusterAABBMins;
            StructuredBuffer<float3> ClusterAABBMaxs;
            StructuredBuffer<LightIndex> LightAssignTable;
            float4x4 _CameraWorldMatrix;
            float4 _DebugColor;

            Varyings main_VS(Attributes input)
            {
                uint clusterID = input.instanceID;

                Varyings vsOutput = (Varyings)0;

                float3 aabbMin = ClusterAABBMins[clusterID];
                float3 aabbMax = ClusterAABBMaxs[clusterID];

                float3 center = (aabbMin + aabbMax) * 0.5;
                float3 scale = (aabbMax - center) / 0.5;
                scale *= 0.5;
                input.positionOS.xyz = input.positionOS.xyz * scale + center;
                float4 positionWS = mul(_CameraWorldMatrix, input.positionOS);
                float4 positionCS = mul(UNITY_MATRIX_P, mul(UNITY_MATRIX_V, positionWS));
                vsOutput.positionCS = positionCS;
                vsOutput.color = _DebugColor;

                float fClusterLightCount = LightAssignTable[clusterID].count;
                if (fClusterLightCount > 0)
                {
                    vsOutput.color = half4(1, 0, 0, 0.2);
                }

                return vsOutput;
            }
            

            half4 main_PS(Varyings IN) : SV_Target
            {
                return IN.color;
            }

            ENDHLSL
        }
    }
}