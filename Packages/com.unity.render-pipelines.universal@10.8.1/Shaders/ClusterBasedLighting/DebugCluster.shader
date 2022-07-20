Shader "Hidden/ClusterBasedLighting"
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

            struct Attributes
            {
                float4 positionOS: POSITION;
                uint instanceID: SV_InstanceID;
            };


            struct Varyings
            {
                float4 positionCS: SV_POSITION;
                // float3 positionWS: TEXCOORD2;

            };

            struct AABB
            {
                float3 Min;
                float3 Max;
            };

            StructuredBuffer<AABB> ClusterAABBs;// : register(t1);
            float4x4 _CameraWorldMatrix;

            bool CMin(float3 a, float3 b)
            {
                if (a.x < b.x && a.y < b.y && a.z < b.z)
                    return true;
                else
                    return false;
            }

            bool CMax(float3 a, float3 b)
            {
                if (a.x > b.x && a.y > b.y && a.z > b.z)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            float4 WorldToProject(float3 posWorld)
            {
                float4 l_posWorld = mul(_CameraWorldMatrix, posWorld);
                float4 posVP0 = TransformObjectToHClip(l_posWorld);
                return posVP0;
            }

            Varyings main_VS(Attributes input)
            {
                uint clusterID = input.instanceID;

                Varyings vsOutput = (Varyings)0;

                AABB aabb = ClusterAABBs[clusterID];

                float3 center = (aabb.Max + aabb.Min) * 0.5;
                float3 scale = (aabb.Max - center) / 0.5;
                scale *= 0.5;
                input.positionOS.xyz = input.positionOS.xyz * scale + center;
                // vsOutput.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                // vsOutput.positionCS = TransformWorldToHClip(input.positionOS.xyz);//(positionWS);
                
                vsOutput.positionCS = WorldToProject(input.positionOS.xyz);

                return vsOutput;
            }
            

            half4 main_PS(Varyings IN): SV_Target
            {
                return half4(1, 1, 1, 0.2);
            }

            ENDHLSL

        }
    }
}