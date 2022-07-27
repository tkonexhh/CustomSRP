using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.Rendering.Universal
{

    //当相机视锥发生改变时调用
    public struct ClusterInfo
    {
        public float fieldOfViewY;
        public float zNear;
        public float zFar;

        public float sD;
        public float logDimY;
        public float logDepth;

        public float nearK;

        public Vector4 ScreenDimensions;

        public Matrix4x4 InverseProjectionMatrix;

        public float cluster_SizeX;
        public float cluster_SizeY;


        public int clusterDimX;
        public int clusterDimY;
        public int clusterDimZ;
        public int clusterDimXYZ;
    }

    public struct AABB
    {
        public Vector3 Min;
        public Vector3 Max;
    }


    public struct ClusterPointLight
    {
        public Vector3 Position;
        public float Range;
        public Vector3 color;
    }

    public struct LightIndex
    {
        public int start;
        public int count;
    }

    public struct Sphere
    {
        public Vector3 position;
        public float range;
    };

}
