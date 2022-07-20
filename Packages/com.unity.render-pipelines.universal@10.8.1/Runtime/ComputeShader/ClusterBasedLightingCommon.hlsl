#pragma once


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
* Compute the 3D cluster index from a 2D screen position and Z depth in view space.
*/
uint3 ComputeClusterIndex3D(float2 screenPos, float viewZ)
{
    uint i = screenPos.x / ClusterCB_Size.x;
    uint j = screenPos.y / ClusterCB_Size.y;
    // It is assumed that view space z is negative (right-handed coordinate system)
    // so the view-space z coordinate needs to be negated to make it positive.
    uint k = log(viewZ / ClusterCB_ViewNear) * ClusterCB_LogGridDimY;

    return uint3(i, j, k);
}


// Compute the square distance between a point p and an AABB b.
// Source: Real-time collision detection, Christer Ericson (2005)
float SqDistancePointAABB(float3 p, AABB b)
{
    float sqDistance = 0.0f;

    for (int i = 0; i < 3; ++i)
    {
        float v = p[i];

        if (v < b.Min[i]) sqDistance += pow(b.Min[i] - v, 2);
        if (v > b.Max[i]) sqDistance += pow(v - b.Max[i], 2);
    }

    return sqDistance;
}



// 球和AABB是否相交
// Source: Real-time collision detection, Christer Ericson (2005)
bool SphereInsideAABB(Sphere sphere, AABB aabb)
{
    float sqDistance = SqDistancePointAABB(sphere.position, aabb);

    return sqDistance <= sphere.range * sphere.range;
}
