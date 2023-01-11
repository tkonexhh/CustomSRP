Shader "Hidden/ClusterBasedLighting/DebugLightSphere"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        ZWrite On
        ZTest Always

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
                half4 color: COLOR;
            };

            StructuredBuffer<float4> LightPosRanges;// : register(t1);



            Varyings main_VS(Attributes input)
            {
                uint clusterID = input.instanceID;

                Varyings vsOutput = (Varyings)0;

                float4 pointLight = LightPosRanges[clusterID];

                float3 center = pointLight.xyz;
                float scale = pointLight.w * 2;
                input.positionOS.xyz = input.positionOS.xyz * scale + center;
                float4 positionCS = mul(UNITY_MATRIX_P, mul(UNITY_MATRIX_V, input.positionOS));
                vsOutput.positionCS = positionCS;
                vsOutput.color = half4(1, 0, 0, 1);


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