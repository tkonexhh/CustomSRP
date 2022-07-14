#ifndef UNITY_DECLARE_DEPTH_NORMAL_TEXTURE_INCLUDED
#define UNITY_DECLARE_DEPTH_NORMAL_TEXTURE_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D_X_FLOAT(_CameraDepthNormalsTexture);
SAMPLER(sampler_CameraDepthNormalsTexture);

// Encoding/decoding view space normals into 2D 0..1 vector
inline float2 EncodeViewNormalStereo(float3 n)
{
    float kScale = 1.7777;
    float2 enc;
    enc = n.xy / (n.z + 1);
    enc /= kScale;
    enc = enc * 0.5 + 0.5;
    return enc;
}
inline float3 DecodeViewNormalStereo(float4 enc4)
{
    float kScale = 1.7777;
    float3 nn = enc4.xyz * float3(2 * kScale, 2 * kScale, 0) + float3(-kScale, -kScale, 1);
    float g = 2.0 / dot(nn.xyz, nn.xyz);
    float3 n;
    n.xy = g * nn.xy;
    n.z = g - 1;
    return n;
}

// Encoding/decoding [0..1) floats into 8 bit/channel RG. Note that 1.0 will not be encoded properly.
inline float2 EncodeFloatRG(float v)
{
    float2 kEncodeMul = float2(1.0, 255.0);
    float kEncodeBit = 1.0 / 255.0;
    float2 enc = kEncodeMul * v;
    enc = frac(enc);
    enc.x -= enc.y * kEncodeBit;
    return enc;
}
inline float DecodeFloatRG(float2 enc)
{
    float2 kDecodeDot = float2(1.0, 1 / 255.0);
    return dot(enc, kDecodeDot);
}


inline float4 EncodeDepthNormal(float depth, float3 normal)
{
    float4 enc;
    enc.xy = EncodeViewNormalStereo(normal);
    enc.zw = EncodeFloatRG(depth);
    return enc;
}

inline void DecodeDepthNormal(float4 enc, out float depth, out float3 normal)
{
    depth = DecodeFloatRG(enc.zw);
    normal = DecodeViewNormalStereo(enc);
}

inline void GetDepthNormal(float2 uv, out float depth, out float3 normalVS)
{
    float4 depthnormal = SAMPLE_TEXTURE2D(_CameraDepthNormalsTexture, sampler_CameraDepthNormalsTexture, uv);
    DecodeDepthNormal(depthnormal, depth, normalVS);
}

float GetSceneDepth(float2 uv)
{
    float4 depthnormal = SAMPLE_TEXTURE2D(_CameraDepthNormalsTexture, sampler_CameraDepthNormalsTexture, uv);
    return DecodeFloatRG(depthnormal.zw);
}

float3 GetViewDepth(float2 uv)
{
    float4 depthnormal = SAMPLE_TEXTURE2D(_CameraDepthNormalsTexture, sampler_CameraDepthNormalsTexture, uv);
    return DecodeViewNormalStereo(depthnormal);
}
#endif
