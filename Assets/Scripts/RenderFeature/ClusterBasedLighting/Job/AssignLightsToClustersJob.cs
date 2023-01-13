using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace UnityEngine.Rendering.Universal
{
    [BurstCompatible]
    public struct AssignLightsToClustersJob : IJobParallelFor
    {
        //input
        [ReadOnly] public int MaxLightCountPerCluster;
        [ReadOnly] public Matrix4x4 CameraWorldMatrix;

        [ReadOnly] public NativeArray<Vector3> clusterAABBMinArray;//AABB min
        [ReadOnly] public NativeArray<Vector3> clusterAABBMaxArray;//AABB max
        [ReadOnly] public NativeList<Vector4> PointLights;//全部光源的List

        //output
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<LightIndex> LightAssignTable;
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<uint> PointLightIndex;//一个ClusterXYZ * per cluster light 长度的容器 


        public void Execute(int index)
        {
            int clusterIndex1D = index;

            Vector3 min = clusterAABBMinArray[clusterIndex1D];
            Vector3 max = clusterAABBMaxArray[clusterIndex1D];

            int startIndex = clusterIndex1D * MaxLightCountPerCluster;
            int endIndex = startIndex;

            AABB clusterAABB;
            clusterAABB.Min = min;
            clusterAABB.Max = max;
            int count = 0;

            //是否可以优化这部分求交代码
            for (int i = 0; i < PointLights.Length; ++i)
            {
                // ClusterPointLight pointLight = PointLights[i];
                Vector4 pointLightPosRange = PointLights[i];
                Vector3 pointLightPositionVS = TransformWorldToView(pointLightPosRange);
                Sphere sphere = new Sphere();
                sphere.position = pointLightPositionVS;
                sphere.range = pointLightPosRange.w;

                if (SphereInsideAABB(sphere, clusterAABB) && count < MaxLightCountPerCluster)
                {
                    PointLightIndex[startIndex + count] = (uint)i;
                    count++;
                }
            }

            LightIndex lightIndex = new LightIndex();
            lightIndex.start = 0;//这个时候的lightcount还是错误的
            lightIndex.count = count;
            LightAssignTable[clusterIndex1D] = lightIndex;
        }


        Vector3 TransformWorldToView(Vector3 posWorld)
        {
            Vector3 posView = CameraWorldMatrix * new Vector4(posWorld.x, posWorld.y, posWorld.z, 1.0f);
            // posView.z *= -1;
            return posView;
        }


        // 球和AABB是否相交
        // Source: Real-time collision detection, Christer Ericson (2005)
        bool SphereInsideAABB(Sphere sphere, AABB aabb)
        {
            float3 center = (aabb.Max + aabb.Min) * 0.5f;
            float3 extents = (aabb.Max - aabb.Min) * 0.5f;
            float3 spherePos = sphere.position;

            float3 vDelta = math.max(0, math.abs(center - spherePos) - extents);

            float fDistSq = math.dot(vDelta, vDelta);
            return fDistSq <= sphere.range * sphere.range;
        }
    }

    [BurstCompatible]
    //只执行一次
    public struct CalcStartIndexJob : IJobParallelFor
    {
        //output
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<LightIndex> LightAssignTable;



        public void Execute(int index)
        {
            int startIndex = 0;
            int count;
            for (int i = 0; i < LightAssignTable.Length; i++)
            {
                count = LightAssignTable[i].count;
                LightIndex lightIndex;
                lightIndex.start = startIndex;
                lightIndex.count = count;
                LightAssignTable[i] = lightIndex;
                startIndex += count;
            }
        }
    }


    [BurstCompatible]
    public struct ZipLightIndexJob : IJobParallelFor
    {
        [ReadOnly] public int MaxLightCountPerCluster;
        [ReadOnly] public NativeArray<uint> PointLightIndex;//一个ClusterXYZ * per cluster light 长度的容器 
        [ReadOnly] public NativeArray<LightIndex> LightAssignTable;

        //output
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<uint> zipedPointLightIndex;

        public void Execute(int index)
        {
            int clusterIndex1D = index;

            int originStartIndex = clusterIndex1D * MaxLightCountPerCluster;

            var lightIndex = LightAssignTable[clusterIndex1D];
            int zipedStartIndex = lightIndex.start;
            int count = lightIndex.count;
            for (int i = 0; i < count; i++)
            {
                zipedPointLightIndex[zipedStartIndex + i] = PointLightIndex[originStartIndex + i];
            }
        }
    }
}
