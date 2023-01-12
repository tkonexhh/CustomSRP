using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.Rendering.Universal
{
    public class ClusterBasedLightDefine
    {
        public const int CLUSTER_GRID_BLOCK_SIZE_XY = 64;//单个Block像素大小
        public const int CLUSTER_GRID_BLOCK_SIZE_Z = 5;//单个Block像素大小
        public const int MAX_NUM_POINT_LIGHT = 100;//最大点光源数量
        public const int AVERAGE_LIGHTS_PER_CLUSTER = 10;//每个cluster 最大light

        public const int CLUSTER_GRID_SIZE_X = 10;
        public const int CLUSTER_GRID_SIZE_Y = 10;
        public const int CLUSTER_GRID_SIZE_Z = 10;
    }

    //当相机视锥发生改变时调用
    public struct ClusterInfo
    {

        public float zNear;
        public float zFar;

        public Vector4 ScreenDimensions;

        public Matrix4x4 InverseProjectionMatrix;

        public float cluster_SizeX;
        public float cluster_SizeY;
        public float cluster_SizeZ;


        public int clusterDimX;
        public int clusterDimY;
        public int clusterDimZ;
        public int clusterDimXYZ;


        public static ClusterInfo CalcClusterInfo(ref RenderingData renderingData, int CLUSTER_GRID_BLOCK_SIZE_XY, int CLUSTER_GRID_BLOCK_SIZE_Z)
        {
            var cameraData = renderingData.cameraData;
            var camera = cameraData.camera;

            // The half-angle of the field of view in the Y-direction.
            // float fieldOfViewY = camera.fieldOfView * Mathf.Deg2Rad * 0.5f;//Degree 2 Radiance:  Param.CameraInfo.Property.Perspective.fFovAngleY * 0.5f;
            float zNear = camera.nearClipPlane;// Param.CameraInfo.Property.Perspective.fMinVisibleDistance;
            float zFar = Mathf.Min(100, camera.farClipPlane);// 多光源只计算50米
                                                             // float zFar = camera.farClipPlane;

            int width = renderingData.cameraData.cameraTargetDescriptor.width;
            int height = renderingData.cameraData.cameraTargetDescriptor.height;//.pixelHeight;
            Vector4 screenDimensions = new Vector4(width, height, 1.0f / (float)width, 1.0f / (float)height);

            int clusterDimX = Mathf.CeilToInt(width / (float)CLUSTER_GRID_BLOCK_SIZE_XY);
            int clusterDimY = Mathf.CeilToInt(height / (float)CLUSTER_GRID_BLOCK_SIZE_XY);
            int clusterDimZ = Mathf.CeilToInt(zFar / (float)CLUSTER_GRID_BLOCK_SIZE_Z);

            ClusterInfo clusterInfo;
            clusterInfo.zNear = zNear;
            clusterInfo.zFar = zFar;
            clusterInfo.ScreenDimensions = screenDimensions;
            // m_ClusterInfo.fieldOfViewY = fieldOfViewY;
            clusterInfo.cluster_SizeX = width / (float)clusterDimX;//CLUSTER_GRID_BLOCK_SIZE_XY;
            clusterInfo.cluster_SizeY = height / (float)clusterDimY;//CLUSTER_GRID_BLOCK_SIZE_XY;
            clusterInfo.cluster_SizeZ = zFar / (float)clusterDimZ;
            var projectionMatrix = renderingData.cameraData.GetGPUProjectionMatrix();
            var projectionMatrixInvers = projectionMatrix.inverse;
            clusterInfo.InverseProjectionMatrix = projectionMatrixInvers;
            clusterInfo.clusterDimX = clusterDimX;
            clusterInfo.clusterDimY = clusterDimY;
            clusterInfo.clusterDimZ = clusterDimZ;
            clusterInfo.clusterDimXYZ = clusterDimX * clusterDimY * clusterDimZ;//总个数
            return clusterInfo;
        }
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
