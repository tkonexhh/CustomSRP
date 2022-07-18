Shader "Hidden/RenderFeature/ShowBuffer"
{
    Properties
    {
        _MainTex ("MainTex", 2D) = "white" { }
    }

    HLSLINCLUDE

    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
    float _Args;
    int _DepthMode;
    CBUFFER_END


    ///普通后处理
    struct AttributesDefault
    {
        float4 positionOS: POSITION;
        float2 uv: TEXCOORD0;
    };


    struct VaryingsDefault
    {
        float4 positionCS: SV_POSITION;
        float2 uv: TEXCOORD0;
    };


    VaryingsDefault VertDefault(AttributesDefault input)
    {
        VaryingsDefault output;
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.uv = input.uv;

        return output;
    }
    
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Depth Texture"
            
            HLSLPROGRAM

            #pragma vertex VertDefault
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthNormalsTexture.hlsl"

            float4 frag(VaryingsDefault input): SV_Target
            {
                float depth;

                if (_DepthMode == 1)
                {
                    depth = GetSceneDepth(input.uv);
                    return depth;// * _Args;

                }
                else
                {
                    depth = SampleSceneDepth(input.uv);
                    return Linear01Depth(depth, _ZBufferParams);// * _Args;

                }
            }
            
            ENDHLSL

        }

        Pass
        {
            Name "NormalVS"
            HLSLPROGRAM

            #pragma vertex VertDefault
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthNormalsTexture.hlsl"

            
            half4 frag(VaryingsDefault input): SV_Target
            {
                float depth;
                float3 normalVS;
                GetDepthNormal(input.uv, depth, normalVS);
                return float4(normalVS, 1);
            }

            ENDHLSL

        }
    }
    FallBack "Diffuse"
}