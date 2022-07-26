using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;

namespace UnityEngine.Rendering.Universal
{
    // 只有主相机才进行多光源操作
    // 集成ScriptableRenderPass 是为了可视化Cluster 
    public class ClusterBasedLights : ScriptableRenderPass
    {
        //当相机视锥发生改变时调用
        struct ClusterInfo
        {
            public float fieldOfViewY;
            public float zNear;
            public float zFar;

            public Vector4 ScreenDimensions;
            public float blockSizeX;
            public float blockSizeY;
            public float blockSizeZ;

            public int clusterDimX;
            public int clusterDimY;
            public int clusterDimZ;
            public int clusterDimXYZ;
        }

        struct AABB
        {
            public Vector3 Min;
            public Vector3 Max;
        }

        struct PointLight
        {
            public Vector3 Position;
            public float Range;
            public Vector3 color;
        }

        struct LightIndex
        {
            public int start;
            public int count;
        }

        ProfilingSampler m_ProfilingSampler;
        // const int CLUSTER_GRID_BLOCK_SIZE = 64;//单个Block像素大小
        const int MAX_NUM_POINT_LIGHT = 1024;
        private int AVERAGE_LIGHTS_PER_CLUSTER = 20;
        const int NUM_CLUSTER_X = 16;
        const int NUM_CLUSTER_Y = 16;
        const int NUM_CLUSTER_Z = 16;

        bool m_Init = false;
        ClusterInfo m_ClusterInfo;
        //计算视锥AABB
        ComputeBuffer m_ClusterAABBBuffer;//存放计算好的ClusterAABB
        //光源求交
        ComputeBuffer m_PointLightBuffer;//存放点光源参数
        ComputeBuffer m_AssignTableBuffer;//XYZ个  Vector2Int  x 是1D 坐标 y 是灯光个数
        ComputeBuffer m_ClusterPointLightIndexListBuffer;//光源分配结果

        int m_KernelOfClusterAABB;
        int m_kernelAssignLightsToClusters;

        ComputeShader m_ComputeShader;

        List<PointLight> m_PointLightPosRangeList = new List<PointLight>();//存放点光源位置和范围 xyz:pos w:range

        //初始变量
        LightIndex[] m_Vec2Girds;

        //debug
        ComputeBuffer m_DrawDebugClusterBuffer;
        Material m_ClusterDebugMaterial;
        public static bool UpdateDebugPos = true;


        struct ShaderIDs
        {
            internal static readonly int InverseProjectionMatrix = Shader.PropertyToID("_InverseProjectionMatrix");
            internal static readonly int ClusterCB_ViewNear = Shader.PropertyToID("ClusterCB_ViewNear");
            internal static readonly int ClusterCB_ScreenDimensions = Shader.PropertyToID("ClusterCB_ScreenDimensions");
            internal static readonly int ClusterCB_GridDim = Shader.PropertyToID("ClusterCB_GridDim");
            internal static readonly int ClusterCB_Size = Shader.PropertyToID("ClusterCB_Size");

            //用于Shading
            internal static readonly int Cluster_GridCountX = Shader.PropertyToID("_Cluster_GridCountX");
            internal static readonly int Cluster_GridCountY = Shader.PropertyToID("_Cluster_GridCountY");
            internal static readonly int Cluster_GridCountZ = Shader.PropertyToID("_Cluster_GridCountZ");
            internal static readonly int Cluster_SizeX = Shader.PropertyToID("_Cluster_SizeX");
            internal static readonly int Cluster_SizeY = Shader.PropertyToID("_Cluster_SizeY");
            internal static readonly int Cluster_SizeZ = Shader.PropertyToID("_Cluster_SizeZ");
            internal static readonly int Cluster_LightAssignTable = Shader.PropertyToID("_LightAssignTable");
            internal static readonly int Cluster_PointLightBuffer = Shader.PropertyToID("_PointLightBuffer");
            internal static readonly int Cluster_AssignTable = Shader.PropertyToID("_AssignTable");
        };

        public ClusterBasedLights()
        {
            m_ProfilingSampler = new ProfilingSampler("ClusterBasedLights");
            //TODO 这个只在编辑器下生效  需要改
            m_ComputeShader = UniversalRenderPipeline.asset.clusterBasedLightingComputeShader;
            m_KernelOfClusterAABB = m_ComputeShader.FindKernel("ClusterAABB");
            m_kernelAssignLightsToClusters = m_ComputeShader.FindKernel("AssignLightsToClusters");


            int numClusters = NUM_CLUSTER_X * NUM_CLUSTER_Y * NUM_CLUSTER_Z;

            //初始化默认数据
            m_Vec2Girds = new LightIndex[numClusters];
            for (int i = 0; i < m_Vec2Girds.Length; i++)
            {
                LightIndex lightIndex = new LightIndex();
                lightIndex.start = 0;
                lightIndex.count = 0;
                m_Vec2Girds[i] = lightIndex;
            }

            m_ClusterAABBBuffer = ComputeHelper.CreateStructuredBuffer<AABB>(numClusters);

            m_ClusterPointLightIndexListBuffer = ComputeHelper.CreateStructuredBuffer<uint>(numClusters * AVERAGE_LIGHTS_PER_CLUSTER);
            m_AssignTableBuffer = ComputeHelper.CreateStructuredBuffer<LightIndex>(numClusters);
        }


        //准备灯光数据 最一开始调用
        public void SetupLights(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            m_PointLightPosRangeList.Clear();
            // 检索出全部点光源
            for (int i = 0; i < renderingData.lightData.visibleLights.Length; i++)
            {
                if (renderingData.lightData.visibleLights[i].lightType == LightType.Point)
                {
                    PointLight pointLight = new PointLight();
                    pointLight.Position = renderingData.lightData.visibleLights[i].light.transform.position;
                    pointLight.Range = renderingData.lightData.visibleLights[i].range;
                    var color = renderingData.lightData.visibleLights[i].finalColor;
                    pointLight.color = new Vector3(color.r, color.g, color.b);
                    m_PointLightPosRangeList.Add(pointLight);
                }
            }

            //Light Buffer
            if (m_PointLightBuffer == null)
                m_PointLightBuffer = ComputeHelper.CreateStructuredBuffer<PointLight>(MAX_NUM_POINT_LIGHT);

            m_PointLightBuffer.SetData(m_PointLightPosRangeList);
            // Debug.LogError(m_PointLightPosRangeList.Count);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                //后续只算一次 并且只在主相机
                var camera = renderingData.cameraData.camera;
                if (camera.name.Contains("Preview"))
                {
                    return;
                }

                if (!m_Init)
                {
                    // m_CurrentCamera = camera;
                    //当FOV clipplane 发生变化 就需要重新计算
                    CalculateClusterInfo(ref renderingData);

                    ClusterGenerate(ref cmd, ref renderingData);

                    InitDebug();
                    m_Init = true;
                }



                AssignLightsToClusts(ref cmd, ref renderingData.cameraData);
                DebugCluster(ref cmd, ref renderingData.cameraData);

                SetShaderParameters(ref renderingData.cameraData);


            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }


        void CalculateClusterInfo(ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;
            var camera = cameraData.camera;

            // The half-angle of the field of view in the Y-direction.
            float fieldOfViewY = camera.fieldOfView * Mathf.Deg2Rad * 0.5f;//Degree 2 Radiance:  Param.CameraInfo.Property.Perspective.fFovAngleY * 0.5f;
            float zNear = camera.nearClipPlane;// Param.CameraInfo.Property.Perspective.fMinVisibleDistance;
            float zFar = Mathf.Min(50, camera.farClipPlane);// 多光源只计算50米
            // float zFar = camera.farClipPlane;

            int width = renderingData.cameraData.pixelWidth;
            int height = renderingData.cameraData.pixelHeight;

            Vector4 screenDimensions = new Vector4(width, height, 1.0f / (float)width, 1.0f / (float)height);

            int clusterDimX = NUM_CLUSTER_X;
            int clusterDimY = NUM_CLUSTER_Y;
            int clusterDimZ = NUM_CLUSTER_Z;

            float blockSizeX = (float)width / clusterDimX;
            float blockSizeY = (float)height / clusterDimY;
            float blockSizeZ = (zFar - zNear) / clusterDimZ;

            m_ClusterInfo.zNear = zNear;
            m_ClusterInfo.zFar = zFar;
            m_ClusterInfo.ScreenDimensions = screenDimensions;
            m_ClusterInfo.fieldOfViewY = fieldOfViewY;

            m_ClusterInfo.blockSizeX = blockSizeX;
            m_ClusterInfo.blockSizeY = blockSizeY;
            m_ClusterInfo.blockSizeZ = blockSizeZ;

            m_ClusterInfo.clusterDimX = clusterDimX;
            m_ClusterInfo.clusterDimY = clusterDimY;
            m_ClusterInfo.clusterDimZ = clusterDimZ;
            m_ClusterInfo.clusterDimXYZ = m_ClusterInfo.clusterDimX * m_ClusterInfo.clusterDimY * m_ClusterInfo.clusterDimZ;//总个数
            // Debug.LogError(clusterDimX + "|" + clusterDimY + "|" + clusterDimZ);
        }

        void UpdateClusterBuffer(ref RenderingData renderingData)
        {
            //TODO GC 问题
            int[] gridDims = { m_ClusterInfo.clusterDimX, m_ClusterInfo.clusterDimY, m_ClusterInfo.clusterDimZ };
            Vector3 sizes = new Vector3(m_ClusterInfo.blockSizeX, m_ClusterInfo.blockSizeY, m_ClusterInfo.blockSizeZ);
            m_ComputeShader.SetInts(ShaderIDs.ClusterCB_GridDim, gridDims);
            m_ComputeShader.SetVector(ShaderIDs.ClusterCB_Size, sizes);
            m_ComputeShader.SetVector(ShaderIDs.ClusterCB_ScreenDimensions, m_ClusterInfo.ScreenDimensions);
        }

        //预计算视锥体AABB
        void ClusterGenerate(ref CommandBuffer commandBuffer, ref RenderingData renderingData)
        {
            if (m_ComputeShader == null)
                return;

            UpdateClusterBuffer(ref renderingData);

            // int threadGroups = Mathf.CeilToInt(m_ClusterInfo.clusterDimXYZ / 1024.0f);
            Matrix4x4 viewMatrix = renderingData.cameraData.camera.worldToCameraMatrix;
            Matrix4x4 projMatrix = renderingData.cameraData.GetGPUProjectionMatrix();
            Matrix4x4 vpMatrix = projMatrix * viewMatrix;
            Matrix4x4 vpMatrixInv = vpMatrix.inverse;

            commandBuffer.SetComputeMatrixParam(m_ComputeShader, ShaderIDs.InverseProjectionMatrix, projMatrix.inverse);
            commandBuffer.SetComputeBufferParam(m_ComputeShader, m_KernelOfClusterAABB, "RWClusterAABBs", m_ClusterAABBBuffer);
            commandBuffer.DispatchCompute(m_ComputeShader, m_KernelOfClusterAABB, NUM_CLUSTER_Z, 1, 1);
        }

        //光源求交
        void AssignLightsToClusts(ref CommandBuffer commandBuffer, ref CameraData cameraData)
        {
            //output
            commandBuffer.SetComputeBufferParam(m_ComputeShader, m_kernelAssignLightsToClusters, "RWLightAssignTable", m_AssignTableBuffer);
            commandBuffer.SetComputeBufferParam(m_ComputeShader, m_kernelAssignLightsToClusters, "RWPointLightIndexList_Cluster", m_ClusterPointLightIndexListBuffer);
            //Input
            commandBuffer.SetComputeBufferParam(m_ComputeShader, m_kernelAssignLightsToClusters, "PointLights", m_PointLightBuffer);
            commandBuffer.SetComputeBufferParam(m_ComputeShader, m_kernelAssignLightsToClusters, "ClusterAABBs", m_ClusterAABBBuffer);

            //初始化数据
            commandBuffer.SetComputeBufferData(m_AssignTableBuffer, m_Vec2Girds);

            //Input
            commandBuffer.SetComputeIntParams(m_ComputeShader, "PointLightCount", m_PointLightPosRangeList.Count);
            if (UpdateDebugPos)
                commandBuffer.SetComputeMatrixParam(m_ComputeShader, "_CameraLastViewMatrix", cameraData.camera.transform.localToWorldMatrix.inverse);

            commandBuffer.DispatchCompute(m_ComputeShader, m_kernelAssignLightsToClusters, m_ClusterInfo.clusterDimZ, 1, 1);
        }

        private void InitDebug()
        {
            m_ClusterDebugMaterial = new Material(Shader.Find("Hidden/ClusterBasedLighting/DebugClusterAABB"));
            m_ClusterDebugMaterial.SetBuffer("ClusterAABBs", m_ClusterAABBBuffer);
            m_ClusterDebugMaterial.SetBuffer("LightAssignTable", m_AssignTableBuffer);

            m_DrawDebugClusterBuffer = ComputeHelper.CreateArgsBuffer(CubeMesh, m_ClusterInfo.clusterDimXYZ);
        }

        void DebugCluster(ref CommandBuffer commandBuffer, ref CameraData cameraData)
        {
            if (UpdateDebugPos)
                m_ClusterDebugMaterial.SetMatrix("_CameraWorldMatrix", cameraData.camera.transform.localToWorldMatrix);

            commandBuffer.DrawMeshInstancedIndirect(CubeMesh, 0, m_ClusterDebugMaterial, 0, m_DrawDebugClusterBuffer, 0);
        }






        void SetShaderParameters(ref CameraData cameraData)
        {
            //设置全部参数 用于shading
            Shader.SetGlobalInt(ShaderIDs.Cluster_GridCountX, m_ClusterInfo.clusterDimX);
            Shader.SetGlobalInt(ShaderIDs.Cluster_GridCountY, m_ClusterInfo.clusterDimY);
            Shader.SetGlobalInt(ShaderIDs.Cluster_GridCountZ, m_ClusterInfo.clusterDimZ);
            Shader.SetGlobalFloat(ShaderIDs.Cluster_SizeX, m_ClusterInfo.blockSizeX);
            Shader.SetGlobalFloat(ShaderIDs.Cluster_SizeY, m_ClusterInfo.blockSizeY);
            Shader.SetGlobalFloat(ShaderIDs.Cluster_SizeZ, m_ClusterInfo.blockSizeZ);
            Shader.SetGlobalBuffer(ShaderIDs.Cluster_AssignTable, m_AssignTableBuffer);
            Shader.SetGlobalBuffer(ShaderIDs.Cluster_PointLightBuffer, m_PointLightBuffer);
            Shader.SetGlobalBuffer(ShaderIDs.Cluster_LightAssignTable, m_ClusterPointLightIndexListBuffer);
            Shader.SetGlobalMatrix("_CameraWorldMatrix", cameraData.camera.transform.localToWorldMatrix);
        }


        private Mesh m_CubeMesh;
        private Mesh CubeMesh
        {
            get
            {
                if (m_CubeMesh == null)
                    m_CubeMesh = UniversalRenderPipeline.asset.m_EditorResourcesAsset.meshs.cubeMesh;
                return m_CubeMesh;
            }
        }
    }
}
