using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine.Rendering.Universal
{
    [BurstCompatible]
    public struct AssignLightsToClustersJob : IJobParallelFor
    {
        //input
        public int PointLightCount;
        public int MaxLightCountPerCluster;
        public Matrix4x4 CameraWorldMatrix;

        [ReadOnly] public NativeArray<AABB> InputClusterAABBs;
        [ReadOnly] public NativeArray<ClusterPointLight> PointLights;

        //output
        public NativeArray<LightIndex> LightAssignTable;
        public NativeParallelMultiHashMap<int, uint> PointLightIndex;

        public void Execute(int index)
        {
            int clusterIndex1D = index;
            int startIndex = clusterIndex1D * MaxLightCountPerCluster;
            int endIndex = startIndex;
            AABB clusterAABB = InputClusterAABBs[clusterIndex1D];
            for (int i = 0; i < PointLightCount; ++i)
            {
                ClusterPointLight pointLight = PointLights[i];
                Vector3 pointLightPositionVS = TransformWorldToView(pointLight.Position);
                Sphere sphere = new Sphere();
                sphere.position = pointLightPositionVS;
                sphere.range = pointLight.Range;

                if (SphereInsideAABB(sphere, clusterAABB))
                {
                    PointLightIndex.Add(clusterIndex1D, (uint)i);
                }
            }

            LightIndex lightIndex = new LightIndex();
            lightIndex.start = startIndex;
            lightIndex.count = endIndex - startIndex;
            // Debug.LogError(clusterIndex1D + "==" + lightIndex.start + "==" + lightIndex.count);
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
            Vector3 center = (aabb.Max + aabb.Min) * 0.5f;
            Vector3 extents = (aabb.Max - aabb.Min) * 0.5f;

            float x = Mathf.Max(0, Mathf.Abs(center.x - sphere.position.x) - extents.x);
            float y = Mathf.Max(0, Mathf.Abs(center.y - sphere.position.y) - extents.y);
            float z = Mathf.Max(0, Mathf.Abs(center.z - sphere.position.z) - extents.z);
            Vector3 vDelta = new Vector3(x, y, z);
            float fDistSq = Vector3.Dot(vDelta, vDelta);
            return fDistSq <= sphere.range * sphere.range;
        }


    }
}
