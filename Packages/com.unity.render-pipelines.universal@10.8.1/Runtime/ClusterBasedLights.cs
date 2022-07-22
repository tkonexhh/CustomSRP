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
        struct CD_DIM
        {
            public float fieldOfViewY;
            public float zNear;
            public float zFar;

            public float sD;
            public float logDimY;
            public float logDepth;

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
        const int CLUSTER_GRID_BLOCK_SIZE = 64;//单个Block像素大小
        const int MAX_NUM_POINT_LIGHT = 1024;
        private int AVERAGE_LIGHTS_PER_CLUSTER = 20;

        bool m_Init = false;
        CD_DIM m_DimData;
        //计算视锥AABB
        ComputeBuffer m_ClusterAABBBuffer;//存放计算好的ClusterAABB
        //光源求交
        ComputeBuffer m_PointLightBuffer;//存放点光源参数
        ComputeBuffer m_ClusterPointLightIndexCounterBuffer;
        ComputeBuffer m_AssignTableBuffer;//XYZ个  Vector2Int  x 是1D 坐标 y 是灯光个数
        ComputeBuffer m_ClusterPointLightIndexListBuffer;//光源分配结果

        int m_KernelOfClusterAABB;
        int m_kernelAssignLightsToClusters;

        ComputeShader m_ComputeShader;

        List<PointLight> m_PointLightPosRangeList = new List<PointLight>();//存放点光源位置和范围 xyz:pos w:range

        //初始变量
        LightIndex[] m_Vec2Girds;
        uint[] m_UCounter = { 0 };

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
            internal static readonly int ClusterCB_NearK = Shader.PropertyToID("ClusterCB_NearK");
            internal static readonly int ClusterCB_LogGridDimY = Shader.PropertyToID("ClusterCB_LogGridDimY");

            //用于Shading
            internal static readonly int Cluster_GridCountX = Shader.PropertyToID("_Cluster_GridCountX");
            internal static readonly int Cluster_GridCountY = Shader.PropertyToID("_Cluster_GridCountY");
            internal static readonly int Cluster_GridCountZ = Shader.PropertyToID("_Cluster_GridCountZ");
            internal static readonly int Cluster_ViewNear = Shader.PropertyToID("_Cluster_ViewNear");
            internal static readonly int Cluster_SizeX = Shader.PropertyToID("_Cluster_SizeX");
            internal static readonly int Cluster_SizeY = Shader.PropertyToID("_Cluster_SizeY");
            internal static readonly int Cluster_NearK = Shader.PropertyToID("_Cluster_NearK");
            internal static readonly int Cluster_LogGridDimY = Shader.PropertyToID("_Cluster_LogGridDimY");
            internal static readonly int Cluster_LightAssignTable = Shader.PropertyToID("_LightAssignTable");
            internal static readonly int Cluster_PointLightBuffer = Shader.PropertyToID("_PointLightBuffer");
            internal static readonly int Cluster_AssignTable = Shader.PropertyToID("_AssignTable");
        };

        public ClusterBasedLights()
        {
            m_ProfilingSampler = new ProfilingSampler("ClusterBasedLights");
            //TODO 这个只在编辑器下生效  需要改
            m_ComputeShader = UniversalRenderPipeline.asset.clusterBasedLightingComputeShader;
        }

        ~ClusterBasedLights()
        {
            m_ClusterAABBBuffer.Dispose();
            m_PointLightBuffer.Dispose();
            m_ClusterPointLightIndexCounterBuffer.Dispose();
            m_AssignTableBuffer.Dispose();
            m_ClusterPointLightIndexListBuffer.Dispose();
            m_DrawDebugClusterBuffer.Dispose();
        }


        //准备灯光数据 最一开始调用
        public void SetupLights(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            m_PointLightPosRangeList.Clear();
            // 检索出全部点光源
            // renderingData.lightData.visibleLights
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
            Debug.LogError(m_PointLightPosRangeList.Count);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                //后续只算一次 并且只在主相机
                var camera = renderingData.cameraData.camera;
                if (!m_Init)
                {
                    //当FOV clipplane 发生变化 就需要重新计算
                    CalculateMDim(ref renderingData);

                    m_ClusterAABBBuffer = ComputeHelper.CreateStructuredBuffer<AABB>(m_DimData.clusterDimXYZ);

                    m_ClusterPointLightIndexCounterBuffer = ComputeHelper.CreateStructuredBuffer<uint>(1);
                    m_AssignTableBuffer = ComputeHelper.CreateStructuredBuffer<LightIndex>(m_DimData.clusterDimXYZ);
                    // m_ClusterPointLightIndexListBuffer = ComputeHelper.CreateStructuredBuffer<uint>(m_DimData.clusterDimXYZ * MAX_NUM_POINT_LIGHT);
                    m_ClusterPointLightIndexListBuffer = ComputeHelper.CreateStructuredBuffer<uint>(m_DimData.clusterDimXYZ * AVERAGE_LIGHTS_PER_CLUSTER);//预估一个格子里面不会超过20个灯光

                    m_KernelOfClusterAABB = m_ComputeShader.FindKernel("ClusterAABB");
                    m_kernelAssignLightsToClusters = m_ComputeShader.FindKernel("AssignLightsToClusters");

                    //---------
                    m_ComputeShader.SetBuffer(m_KernelOfClusterAABB, "RWClusterAABBs", m_ClusterAABBBuffer);
                    //---------
                    //output
                    m_ComputeShader.SetBuffer(m_kernelAssignLightsToClusters, "RWPointLightIndexCounter_Cluster", m_ClusterPointLightIndexCounterBuffer);
                    m_ComputeShader.SetBuffer(m_kernelAssignLightsToClusters, "RWLightAssignTable", m_AssignTableBuffer);
                    m_ComputeShader.SetBuffer(m_kernelAssignLightsToClusters, "RWPointLightIndexList_Cluster", m_ClusterPointLightIndexListBuffer);
                    //Input
                    m_ComputeShader.SetBuffer(m_kernelAssignLightsToClusters, "PointLights", m_PointLightBuffer);
                    m_ComputeShader.SetBuffer(m_kernelAssignLightsToClusters, "ClusterAABBs", m_ClusterAABBBuffer);

                    //初始化默认数据
                    m_Vec2Girds = new LightIndex[m_DimData.clusterDimXYZ];
                    for (int i = 0; i < m_Vec2Girds.Length; i++)
                    {
                        LightIndex lightIndex = new LightIndex();
                        lightIndex.start = 0;
                        lightIndex.count = 0;
                        m_Vec2Girds[i] = lightIndex;
                    }

                    ClusterGenerate(ref cmd, ref renderingData);

                    InitDebug();
                    m_Init = true;
                }

                var projectionMatrix = renderingData.cameraData.GetGPUProjectionMatrix();
                var projectionMatrixInvers = projectionMatrix.inverse;
                m_ComputeShader.SetMatrix(ShaderIDs.InverseProjectionMatrix, projectionMatrixInvers);

                AssignLightsToClusts(ref cmd, ref renderingData.cameraData);

                DebugCluster(ref cmd, ref renderingData.cameraData);

                SetShaderParameters(ref renderingData.cameraData);

            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }


        void CalculateMDim(ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            // The half-angle of the field of view in the Y-direction.
            float fieldOfViewY = camera.fieldOfView * Mathf.Deg2Rad * 0.5f;//Degree 2 Radiance:  Param.CameraInfo.Property.Perspective.fFovAngleY * 0.5f;
            float zNear = camera.nearClipPlane;// Param.CameraInfo.Property.Perspective.fMinVisibleDistance;
            float zFar = Mathf.Min(50, camera.farClipPlane);// 多光源只计算50米
            // float zFar = camera.farClipPlane;

            int width = renderingData.cameraData.cameraTargetDescriptor.width;
            int height = renderingData.cameraData.cameraTargetDescriptor.height;
            // Number of clusters in the screen X direction.
            int clusterDimX = Mathf.CeilToInt(width / (float)CLUSTER_GRID_BLOCK_SIZE);
            // Number of clusters in the screen Y direction.
            int clusterDimY = Mathf.CeilToInt(height / (float)CLUSTER_GRID_BLOCK_SIZE);

            // The depth of the cluster grid during clustered rendering is dependent on the 
            // number of clusters subdivisions in the screen Y direction.
            // Source: Clustered Deferred and Forward Shading (2012) (Ola Olsson, Markus Billeter, Ulf Assarsson).
            float sD = 2.0f * Mathf.Tan(fieldOfViewY) / (float)clusterDimY;
            float logDimY = 1.0f / Mathf.Log(1.0f + sD);

            float logDepth = Mathf.Log(zFar / zNear);
            int clusterDimZ = Mathf.FloorToInt(logDepth * logDimY);
            // Debug.LogError(logDepth + "---" + logDimY + "---" + clusterDimZ);
            m_DimData.zNear = zNear;
            m_DimData.zFar = zFar;
            m_DimData.sD = sD;
            m_DimData.fieldOfViewY = fieldOfViewY;
            m_DimData.logDepth = logDepth;
            m_DimData.logDimY = logDimY;
            m_DimData.clusterDimX = clusterDimX;
            m_DimData.clusterDimY = clusterDimY;
            m_DimData.clusterDimZ = clusterDimZ;
            m_DimData.clusterDimXYZ = clusterDimX * clusterDimY * clusterDimZ;//总个数
            Debug.LogError(clusterDimX + "|" + clusterDimY + "|" + clusterDimZ);
        }

        void UpdateClusterBuffer(ref RenderingData renderingData)
        {
            int width = renderingData.cameraData.cameraTargetDescriptor.width;
            int height = renderingData.cameraData.cameraTargetDescriptor.height;
            //TODO GC 问题
            int[] gridDims = { m_DimData.clusterDimX, m_DimData.clusterDimY, m_DimData.clusterDimZ };
            int[] sizes = { CLUSTER_GRID_BLOCK_SIZE, CLUSTER_GRID_BLOCK_SIZE };
            Vector4 screenDim = new Vector4((float)width, (float)height, 1.0f / width, 1.0f / height);
            float viewNear = m_DimData.zNear;

            m_ComputeShader.SetInts(ShaderIDs.ClusterCB_GridDim, gridDims);
            m_ComputeShader.SetFloat(ShaderIDs.ClusterCB_ViewNear, viewNear);
            m_ComputeShader.SetInts(ShaderIDs.ClusterCB_Size, sizes);
            m_ComputeShader.SetFloat(ShaderIDs.ClusterCB_NearK, 1.0f + m_DimData.sD);
            m_ComputeShader.SetFloat(ShaderIDs.ClusterCB_LogGridDimY, m_DimData.logDimY);
            m_ComputeShader.SetVector(ShaderIDs.ClusterCB_ScreenDimensions, screenDim);
        }

        //预计算视锥体AABB
        void ClusterGenerate(ref CommandBuffer commandBuffer, ref RenderingData renderingData)
        {
            if (m_ComputeShader == null)
                return;

            UpdateClusterBuffer(ref renderingData);

            int threadGroups = Mathf.CeilToInt(m_DimData.clusterDimXYZ / 1024.0f);

            commandBuffer.DispatchCompute(m_ComputeShader, m_KernelOfClusterAABB, threadGroups, 1, 1);
        }

        //光源求交
        void AssignLightsToClusts(ref CommandBuffer commandBuffer, ref CameraData cameraData)
        {
            //初始化数据
            commandBuffer.SetComputeBufferData(m_ClusterPointLightIndexCounterBuffer, m_UCounter);
            commandBuffer.SetComputeBufferData(m_AssignTableBuffer, m_Vec2Girds);

            //Input
            commandBuffer.SetComputeIntParams(m_ComputeShader, "PointLightCount", m_PointLightPosRangeList.Count);
            if (UpdateDebugPos)
                commandBuffer.SetComputeMatrixParam(m_ComputeShader, "_CameraLastViewMatrix", cameraData.camera.transform.localToWorldMatrix.inverse);

            //output
            m_ComputeShader.SetBuffer(m_kernelAssignLightsToClusters, "RWPointLightIndexCounter_Cluster", m_ClusterPointLightIndexCounterBuffer);
            m_ComputeShader.SetBuffer(m_kernelAssignLightsToClusters, "RWLightAssignTable", m_AssignTableBuffer);
            m_ComputeShader.SetBuffer(m_kernelAssignLightsToClusters, "RWPointLightIndexList_Cluster", m_ClusterPointLightIndexListBuffer);
            //Input
            m_ComputeShader.SetBuffer(m_kernelAssignLightsToClusters, "PointLights", m_PointLightBuffer);
            m_ComputeShader.SetBuffer(m_kernelAssignLightsToClusters, "ClusterAABBs", m_ClusterAABBBuffer);

            commandBuffer.DispatchCompute(m_ComputeShader, m_kernelAssignLightsToClusters, m_DimData.clusterDimXYZ, 1, 1);
        }

        private void InitDebug()
        {
            m_ClusterDebugMaterial = new Material(Shader.Find("Hidden/ClusterBasedLighting/DebugClusterAABB"));
            m_ClusterDebugMaterial.SetBuffer("ClusterAABBs", m_ClusterAABBBuffer);
            m_ClusterDebugMaterial.SetBuffer("LightAssignTable", m_AssignTableBuffer);

            m_DrawDebugClusterBuffer = ComputeHelper.CreateArgsBuffer(CubeMesh, m_DimData.clusterDimXYZ);
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
            Shader.SetGlobalInt(ShaderIDs.Cluster_GridCountX, m_DimData.clusterDimX);
            Shader.SetGlobalInt(ShaderIDs.Cluster_GridCountY, m_DimData.clusterDimY);
            Shader.SetGlobalInt(ShaderIDs.Cluster_GridCountZ, m_DimData.clusterDimZ);
            Shader.SetGlobalFloat(ShaderIDs.Cluster_ViewNear, m_DimData.zNear);
            Shader.SetGlobalInt(ShaderIDs.Cluster_SizeX, CLUSTER_GRID_BLOCK_SIZE);
            Shader.SetGlobalInt(ShaderIDs.Cluster_SizeY, CLUSTER_GRID_BLOCK_SIZE);
            // Shader.SetGlobalFloat(ShaderIDs.Cluster_NearK, 1.0f + m_DimData.sD);
            Shader.SetGlobalFloat(ShaderIDs.Cluster_LogGridDimY, m_DimData.logDimY);
            Shader.SetGlobalBuffer(ShaderIDs.Cluster_AssignTable, m_AssignTableBuffer);
            Shader.SetGlobalBuffer(ShaderIDs.Cluster_PointLightBuffer, m_PointLightBuffer);
            Shader.SetGlobalBuffer(ShaderIDs.Cluster_LightAssignTable, m_ClusterPointLightIndexListBuffer);
            Shader.SetGlobalMatrix("_CameraWorldMatrix", cameraData.camera.transform.localToWorldMatrix.inverse);
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
