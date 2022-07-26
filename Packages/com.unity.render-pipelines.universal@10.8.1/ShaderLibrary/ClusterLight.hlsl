#ifndef UNITY_CLUSTER_LIGHT_INCLUDED
#define UNITY_CLUSTER_LIGHT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/ComputeShader/ClusterBasedLightingCommon.hlsl"

CBUFFER_START(UnityPerFrame)
int _Cluster_GridCountX;
int _Cluster_GridCountY;
int _Cluster_GridCountZ;
int _Cluster_SizeX;
int _Cluster_SizeY;
int _Cluster_SizeZ;
CBUFFER_END
float4x4 _CameraWorldMatrix;
StructuredBuffer<PointLight> _PointLightBuffer;
StructuredBuffer<LightIndex> _AssignTable;
StructuredBuffer<uint> _LightAssignTable;//按照顺序摆放的LightIndex


//3D坐标转1D坐标
uint ClusterIndex1D(uint3 clusterIndex3D)
{
    return clusterIndex3D.x + (_Cluster_GridCountX * (clusterIndex3D.y + _Cluster_GridCountY * clusterIndex3D.z));
}

/**
* Compute the 3D cluster index from a 2D screen position and Z depth in view space.
*/
uint3 ClusterIndex3D(float2 screenPos, float viewZ)
{
    uint i = screenPos.x / _Cluster_SizeX;
    uint j = screenPos.y / _Cluster_SizeY;
    // It is assumed that view space z is negative (right-handed coordinate system)
    // so the view-space z coordinate needs to be negated to make it positive.
    uint k = viewZ / _Cluster_SizeZ;

    return uint3(i, j, k);
}

uint ComputeClusterIndex1D(float2 screenPos, float viewZ)
{
    uint3 clusterIndex3D = ClusterIndex3D(screenPos, viewZ);
    uint clusterIndex1D = ClusterIndex1D(clusterIndex3D);
    return clusterIndex1D;
}

half3 ShadeAdditionalPoint(float4 positionCS, float3 positionWS, float3 normalWS)
{
    float4 positionSS = ComputeScreenPos(positionCS);
    float viewPosz = mul(_CameraWorldMatrix, positionWS).z;
    uint clusterIndex1D = ComputeClusterIndex1D(positionSS.xy, positionCS.w);
    uint startOffset = _AssignTable[clusterIndex1D].start;
    uint lightCount = _AssignTable[clusterIndex1D].count;
    return lightCount / 20.0;

    half3 finalRGB = 0;
    for (uint i = 0; i < lightCount; ++i)
    {
        uint lightIndex = _LightAssignTable[startOffset +i];
        PointLight pointLight = _PointLightBuffer[lightIndex];

        float3 lightPos = pointLight.position;
        float radius = pointLight.range;

        float distanceToLight = distance(positionWS, lightPos);
        float3 lightDir = -normalize(positionWS - lightPos);

        //Shading
        //光源衰减
        // float d2 = distanceToLight * distanceToLight;
        // float r2 = radius * radius;
        // float distanceFactor = saturate(1 - (d2 / r2) * (d2 / r2));
        // distanceFactor *= distanceFactor;
        float distanceFactor = (saturate(radius - distanceToLight)) / radius;
        float NdotL = saturate(dot(normalWS, lightDir));
        finalRGB += NdotL * pointLight.color * distanceFactor;
    }

    return finalRGB;
}

#endif
