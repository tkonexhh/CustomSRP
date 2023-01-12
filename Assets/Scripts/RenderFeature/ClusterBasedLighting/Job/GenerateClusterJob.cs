using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;

namespace UnityEngine.Rendering.Universal
{
    [BurstCompatible]
    public struct GenerateClusterJob : IJobParallelFor
    {
        //pass 中计算好得clusterInfo
        [ReadOnly] public ClusterInfo ClusterInfo;


        public NativeArray<Vector3> clusterAABBMinArray;
        public NativeArray<Vector3> clusterAABBMaxArray;

        public void Execute(int index)
        {
            Vector3Int clusterIndex3D = ComputeClusterIndex3D(index);
            Plane nearPlane = new Plane(Vector3.forward, GetZ0(clusterIndex3D.z));
            Plane farPlane = new Plane(Vector3.forward, GetZ0(clusterIndex3D.z + 1));

            // The top-left point of cluster K in screen space.
            Vector4 pMin = new Vector4(clusterIndex3D.x * ClusterInfo.cluster_SizeX, clusterIndex3D.y * ClusterInfo.cluster_SizeY, 0.0f, 1.0f);
            // The bottom-right point of cluster K in screen space.
            Vector4 pMax = new Vector4((clusterIndex3D.x + 1) * ClusterInfo.cluster_SizeX, (clusterIndex3D.y + 1) * ClusterInfo.cluster_SizeY, 0.0f, 1.0f);

            // Transform the screen space points to view space.
            pMin = ScreenToView(pMin);
            pMax = ScreenToView(pMax);

            pMin.z *= -1;
            pMax.z *= -1;

            Vector3 nearMin, nearMax, farMin, farMax;
            Vector3 eye = Vector3.zero;
            IntersectLinePlane(eye, new Vector3(pMin.x, pMin.y, pMin.z), nearPlane, out nearMin);
            IntersectLinePlane(eye, new Vector3(pMax.x, pMax.y, pMax.z), nearPlane, out nearMax);
            IntersectLinePlane(eye, new Vector3(pMin.x, pMin.y, pMin.z), farPlane, out farMin);
            IntersectLinePlane(eye, new Vector3(pMax.x, pMax.y, pMax.z), farPlane, out farMax);

            Vector3 aabbMin = Vector3.Min(nearMin, Vector3.Min(nearMax, Vector3.Min(farMin, farMax)));
            Vector3 aabbMax = Vector3.Max(nearMin, Vector3.Max(nearMax, Vector3.Max(farMin, farMax)));

            clusterAABBMinArray[index] = aabbMin;
            clusterAABBMaxArray[index] = aabbMax;
        }

        /**
        * Find the intersection of a line segment with a plane.
        * This function will return true if an intersection point
        * was found or false if no intersection could be found.
        * Source: Real-time collision detection, Christer Ericson (2005)
*/
        bool IntersectLinePlane(Vector3 a, Vector3 b, Plane p, out Vector3 q)
        {
            Vector3 ab = b - a;

            float t = (p.distance - Vector3.Dot(p.normal, a)) / Vector3.Dot(p.normal, ab);

            bool intersect = (t >= 0.0f && t <= 1.0f);

            q = Vector3.zero;
            if (intersect)
            {
                q = a + t * ab;
            }

            return intersect;
        }


        Vector4 ClipToView(Vector4 clip)
        {
            // View space position.
            Vector4 view = ClusterInfo.InverseProjectionMatrix * clip;// ClusterCB_InverseProjectionMatrix.MultiplyVector(clip);
            // Perspecitive projection.
            if (view.w == 0) view.w = 0.000001f;//?
            view = view / view.w;

            return view;
        }

        // Convert screen space coordinates to view space.
        Vector4 ScreenToView(Vector4 screen)
        {
            // Convert to normalized texture coordinates in the range [0 .. 1].
            Vector2 texCoord = new Vector2(screen.x * ClusterInfo.ScreenDimensions.z, screen.y * ClusterInfo.ScreenDimensions.w);

            // Convert to clip space
            Vector4 clip = new Vector4(texCoord.x * 2.0f - 1.0f, (1.0f - texCoord.y) * 2.0f - 1.0f, screen.z, screen.w);
            //Vector4 clip = new Vector4(texCoord.x * 2.0f - 1.0f, texCoord.y * 2.0f - 1.0f, screen.z, screen.w);
            return ClipToView(clip);
        }


        Vector3Int ComputeClusterIndex3D(int clusterIndex1D)
        {
            int i = clusterIndex1D % ClusterInfo.clusterDimX;
            int j = clusterIndex1D % (ClusterInfo.clusterDimX * ClusterInfo.clusterDimY) / ClusterInfo.clusterDimX;
            int k = clusterIndex1D / (ClusterInfo.clusterDimX * ClusterInfo.clusterDimY);

            return new Vector3Int(i, j, k);
        }


        float GetZ0(int slice)
        {
            return slice * ClusterInfo.cluster_SizeZ;
        }

    }
}
