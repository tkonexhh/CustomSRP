#ifndef UNIVERSAL_DEPTH_ONLY_PASS_INCLUDED
#define UNIVERSAL_DEPTH_ONLY_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthNormalsTexture.hlsl"

struct Attributes
{
    float4 positionOS: POSITION;
    float2 texcoord: TEXCOORD0;
    float3 normal: NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS: SV_POSITION;
    float2 uv: TEXCOORD1;
    float4 normaldepth: TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings DepthNormalsVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);

    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    float4 positionVS = mul(UNITY_MATRIX_MV, input.positionOS);
    output.normaldepth.w = -positionVS.z * _ProjectionParams.w;
    output.normaldepth.xyz = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, input.normal));
    return output;
}

float4 DepthNormalsFragment(Varyings input): SV_TARGET
{
    Alpha(SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)).a, _BaseColor, _Cutoff);
    float4 depthnormal = EncodeDepthNormal(input.normaldepth.w, input.normaldepth.xyz);
    return depthnormal;
}
#endif
