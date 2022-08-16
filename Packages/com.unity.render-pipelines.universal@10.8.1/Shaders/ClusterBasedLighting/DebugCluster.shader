Shader "Hidden/ClusterBasedLighting/DebugClusterAABB"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM

            #pragma vertex main_VS
            #pragma fragment main_PS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ClusterBasedLighting/ClusterBasedLightingCommon.hlsl"

            struct Attributes
            {
                float4 positionOS: POSITION;
                uint instanceID: SV_InstanceID;
            };


            struct Varyings
            {
                float4 positionCS: SV_POSITION;
                half4 color: COLOR;
            };

            

            StructuredBuffer<AABB> ClusterAABBs;
            StructuredBuffer<LightIndex> LightAssignTable;
            float4x4 _CameraWorldMatrix;

            Varyings main_VS(Attributes input)
            {
                uint clusterID = input.instanceID;

                Varyings vsOutput = (Varyings)0;

                AABB aabb = ClusterAABBs[clusterID];

                float3 center = (aabb.Max + aabb.Min) * 0.5;
                float3 scale = (aabb.Max - center) / 0.5;
                scale *= 0.2;
                input.positionOS.xyz = input.positionOS.xyz * scale + center;
                float4 positionWS = mul(_CameraWorldMatrix, input.positionOS);
                float4 positionCS = mul(UNITY_MATRIX_P, mul(UNITY_MATRIX_V, positionWS));
                vsOutput.positionCS = positionCS;
                vsOutput.color = half4(1, 1, 1, 0.2);

                float fClusterLightCount = LightAssignTable[clusterID].count;
                if (fClusterLightCount > 0)
                {
                    vsOutput.color = half4(1, 0, 0, 0.2);
                }

                return vsOutput;
            }
            

            half4 main_PS(Varyings IN): SV_Target
            {
                return IN.color;
            }

            ENDHLSL

        }
    }
}