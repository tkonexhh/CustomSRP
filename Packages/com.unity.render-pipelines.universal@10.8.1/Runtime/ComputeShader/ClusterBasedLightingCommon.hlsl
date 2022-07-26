#pragma once

float4x4 _InverseProjectionMatrix;

//Cluster Data
uint3 ClusterCB_GridDim;      // The 3D dimensions of the cluster grid.
float ClusterCB_ViewNear;     // The distance to the near clipping plane. (Used for computing the index in the cluster grid)
uint2 ClusterCB_Size;         // The size of a cluster in screen space (pixels).
float ClusterCB_NearK;        // ( 1 + ( 2 * tan( fov * 0.5 ) / ClusterGridDim.y ) ) // Used to compute the near plane for clusters at depth k.
float ClusterCB_LogGridDimY;  // 1.0f / log( 1 + ( tan( fov * 0.5 ) / ClusterGridDim.y )
float4 ClusterCB_ScreenDimensions;

struct AABB
{
    float3 Min;
    float3 Max;
};

struct Sphere
{
    float3 position;
    float range;
};

struct Plane
{
    float3 N;   // Plane normal.
    float d;   // Distance to origin.

};

struct PointLight
{
    float3 position;
    float range;
    float3 color;
};

struct LightIndex
{
    int start;
    int count;
};


struct ComputeShaderInput
{
    uint3 GroupID: SV_GroupID;           // 3D index of the thread group in the dispatch.
    uint3 GroupThreadID: SV_GroupThreadID;     // 3D index of local thread ID in a thread group.
    uint3 DispatchThreadID: SV_DispatchThreadID;  // 3D index of global thread ID in the dispatch.
    uint GroupIndex: SV_GroupIndex;        // Flattened local index of the thread within a thread group.

};


//1D坐标转3D坐标
uint3 ComputeClusterIndex3D(uint clusterIndex1D)
{
    uint i = clusterIndex1D % ClusterCB_GridDim.x;
    uint j = clusterIndex1D % (ClusterCB_GridDim.x * ClusterCB_GridDim.y) / ClusterCB_GridDim.x;
    uint k = clusterIndex1D / (ClusterCB_GridDim.x * ClusterCB_GridDim.y);

    return uint3(i, j, k);
}

//3D坐标转1D坐标
uint ComputeClusterIndex1D(uint3 clusterIndex3D)
{
    return clusterIndex3D.x + (ClusterCB_GridDim.x * (clusterIndex3D.y + ClusterCB_GridDim.y * clusterIndex3D.z));
}


/**
* Find the intersection of a line segment with a plane.
* This function will return true if an intersection point
* was found or false if no intersection could be found.
* Source: Real-time collision detection, Christer Ericson (2005)
*/
bool IntersectLinePlane(float3 a, float3 b, Plane p, out float3 q)
{
    float3 ab = b - a;

    float t = (p.d - dot(p.N, a)) / dot(p.N, ab);

    bool intersect = (t >= 0.0f && t <= 1.0f);

    q = float3(0, 0, 0);
    if (intersect)
    {
        q = a + t * ab;
    }

    return intersect;
}

/// Functions.hlsli
// Convert clip space coordinates to view space
float4 ClipToView(float4 clip)
{
    // View space position.
    //float4 view = mul(clip, g_Com.Camera.CameraProjectInv);
    float4 view = mul(_InverseProjectionMatrix, clip);
    // Perspecitive projection.
    view = view / view.w;

    return view;
}

// Convert screen space coordinates to view space.
float4 ScreenToView(float4 screen)
{
    // Convert to normalized texture coordinates in the range [0 .. 1].
    float2 texCoord = screen.xy * ClusterCB_ScreenDimensions.zw;

    // Convert to clip space
    //float4 clip = float4(texCoord * 2.0f - 1.0f, screen.z, screen.w);
    float4 clip = float4(float2(texCoord.x, 1.0f - texCoord.y) * 2.0f - 1.0f, screen.z, screen.w);

    return ClipToView(clip);
}




// 球和AABB是否相交
// Source: Real-time collision detection, Christer Ericson (2005)
bool SphereInsideAABB(Sphere sphere, AABB aabb)
{
    float3 center = (aabb.Max.xyz + aabb.Min.xyz) * 0.5f;
    float3 extents = (aabb.Max.xyz - aabb.Min.xyz) * 0.5f;
    
    float3 vDelta = max(0, abs(center - sphere.position) - extents);
    float fDistSq = dot(vDelta, vDelta);
    return fDistSq <= sphere.range * sphere.range;
}
