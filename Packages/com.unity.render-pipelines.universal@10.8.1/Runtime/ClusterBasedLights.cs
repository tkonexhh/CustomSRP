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

        ProfilingSampler m_ProfilingSampler;
        const int CLUSTER_GRID_BLOCK_SIZE = 64;//单个Block像素大小
        const int MAX_NUM_POINT_LIGHT = 1024;
        private int m_AVERAGE_OVERLAPPING_LIGHTS_PER_CLUSTER = 20;

        bool m_Init = false;
        CD_DIM m_DimData;
        //计算视锥AABB
        ComputeBuffer m_ClusterAABBBuffer;//存放计算好的ClusterAABB

        //使用深度裁剪Cluster 
        ComputeBuffer m_ClusterFlagBuffer;

        //寻找有效的Cluster
        ComputeBuffer m_UniqueClustersBuffer;
        ComputeBuffer m_UniqueClusterCountBuffer;
        ComputeBuffer m_IAB_AssignLightsToClusters;
        ComputeBuffer m_IAB_DrawDebugClusters;

        //光源求交
        ComputeBuffer m_PointLightPosRangeBuffer;//存放点光源参数
        ComputeBuffer m_ClusterPointLightIndexCounterBuffer;
        ComputeBuffer m_ClusterPointLightGridBuffer;//XYZ个  Vector2Int  x 是1D 坐标 y 是灯光个数
        ComputeBuffer m_ClusterPointLightIndexListBuffer;

        int m_KernelOfClusterAABB;
        int m_KernelOfClusterSampleDepth;
        int m_KernelOfFindUniqueCluster;
        int m_KernelOfUpdateIndirectArgument;
        int m_kernelAssignLightsToClusters;

        ComputeShader m_ClusterAABBCS;

        List<Vector4> m_PointLightPosRangeList = new List<Vector4>();//存放点光源位置和范围 xyz:pos w:range


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
            internal static readonly int DepthTexture = Shader.PropertyToID("DepthTexture");
            internal static readonly int RWClusterFlags = Shader.PropertyToID("RWClusterFlags");
        };

        public ClusterBasedLights()
        {
            m_ProfilingSampler = new ProfilingSampler("ClusterBasedLights");
            //TODO 这个只在编辑器下生效  需要改
            m_ClusterAABBCS = UniversalRenderPipeline.asset.clusterBasedLightingComputeShader;
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
                    Vector3 pos = renderingData.lightData.visibleLights[i].light.transform.position;
                    float range = renderingData.lightData.visibleLights[i].range;
                    m_PointLightPosRangeList.Add(new Vector4(pos.x, pos.y, pos.z, range));
                }
            }

            //Light Buffer
            if (m_PointLightPosRangeBuffer == null)
                m_PointLightPosRangeBuffer = ComputeHelper.CreateStructuredBuffer<Vector4>(MAX_NUM_POINT_LIGHT);

            m_PointLightPosRangeBuffer.SetData(m_PointLightPosRangeList);
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
                    m_ClusterPointLightGridBuffer = ComputeHelper.CreateStructuredBuffer<Vector2Int>(m_DimData.clusterDimXYZ);
                    m_ClusterPointLightIndexListBuffer = ComputeHelper.CreateStructuredBuffer<uint>(m_DimData.clusterDimXYZ * m_AVERAGE_OVERLAPPING_LIGHTS_PER_CLUSTER);//预估一个格子里面不会超过20个灯光
                    // m_ClusterPointLightIndexListBuffer = ComputeHelper.CreateStructuredBuffer<uint>(m_DimData.clusterDimXYZ * MAX_NUM_POINT_LIGHT);

                    m_ClusterFlagBuffer = ComputeHelper.CreateStructuredBuffer<float>(m_DimData.clusterDimXYZ);
                    m_UniqueClustersBuffer = new ComputeBuffer(m_DimData.clusterDimXYZ, sizeof(uint), ComputeBufferType.Counter);
                    m_UniqueClusterCountBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
                    m_IAB_AssignLightsToClusters = new ComputeBuffer(1, sizeof(uint) * 3, ComputeBufferType.IndirectArguments);
                    m_IAB_DrawDebugClusters = new ComputeBuffer(1, sizeof(uint) * 4, ComputeBufferType.IndirectArguments);

                    m_KernelOfClusterAABB = m_ClusterAABBCS.FindKernel("ClusterAABB");
                    m_KernelOfClusterSampleDepth = m_ClusterAABBCS.FindKernel("ClusterSampleDepth");
                    m_KernelOfFindUniqueCluster = m_ClusterAABBCS.FindKernel("FindUniqueCluster");
                    m_KernelOfUpdateIndirectArgument = m_ClusterAABBCS.FindKernel("UpdateIndirectArgument");
                    m_kernelAssignLightsToClusters = m_ClusterAABBCS.FindKernel("AssignLightsToClusters");


                    //---------
                    m_ClusterAABBCS.SetBuffer(m_KernelOfClusterAABB, "RWClusterAABBs", m_ClusterAABBBuffer);
                    //---------
                    m_ClusterAABBCS.SetBuffer(m_KernelOfClusterSampleDepth, ShaderIDs.RWClusterFlags, m_ClusterFlagBuffer);
                    //---------
                    m_ClusterAABBCS.SetBuffer(m_KernelOfFindUniqueCluster, "RWUniqueClusters", m_UniqueClustersBuffer);
                    m_ClusterAABBCS.SetBuffer(m_KernelOfFindUniqueCluster, "ClusterFlags", m_ClusterFlagBuffer);
                    //---------
                    m_ClusterAABBCS.SetBuffer(m_KernelOfUpdateIndirectArgument, "ClusterCounter", m_UniqueClusterCountBuffer);
                    m_ClusterAABBCS.SetBuffer(m_KernelOfUpdateIndirectArgument, "AssignLightsToClustersIndirectArgumentBuffer", m_IAB_AssignLightsToClusters);
                    m_ClusterAABBCS.SetBuffer(m_KernelOfUpdateIndirectArgument, "DebugClustersIndirectArgumentBuffer", m_IAB_DrawDebugClusters);
                    //m_kernelAssignLightsToClusters---------
                    //output
                    m_ClusterAABBCS.SetBuffer(m_kernelAssignLightsToClusters, "RWPointLightIndexCounter_Cluster", m_ClusterPointLightIndexCounterBuffer);
                    m_ClusterAABBCS.SetBuffer(m_kernelAssignLightsToClusters, "RWPointLightGrid_Cluster", m_ClusterPointLightGridBuffer);
                    m_ClusterAABBCS.SetBuffer(m_kernelAssignLightsToClusters, "RWPointLightIndexList_Cluster", m_ClusterPointLightIndexListBuffer);
                    //Input
                    m_ClusterAABBCS.SetBuffer(m_kernelAssignLightsToClusters, "PointLights", m_PointLightPosRangeBuffer);
                    m_ClusterAABBCS.SetBuffer(m_kernelAssignLightsToClusters, "ClusterAABBs", m_ClusterAABBBuffer);
                    m_ClusterAABBCS.SetBuffer(m_kernelAssignLightsToClusters, "UniqueClusters", m_IAB_AssignLightsToClusters);

                    ComputeClusterAABB(ref cmd, ref renderingData);

                    InitDebug();
                    m_Init = true;
                }

                var projectionMatrix = renderingData.cameraData.GetGPUProjectionMatrix();
                var projectionMatrixInvers = projectionMatrix.inverse;
                m_ClusterAABBCS.SetMatrix(ShaderIDs.InverseProjectionMatrix, projectionMatrixInvers);

                if (UpdateDebugPos)
                {
                    ClusterSampleDepth(ref cmd, ref renderingData);

                }

                FindUniqueCluster(ref cmd, ref renderingData);
                cmd.CopyCounterValue(m_UniqueClustersBuffer, m_UniqueClusterCountBuffer, 0);

                UpdateIndirectArgumentBuffers(ref cmd, ref renderingData);

                AssignLightsToClusts(ref cmd, ref renderingData.cameraData);

                DebugCluster(ref cmd, ref renderingData.cameraData);

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
        }

        void UpdateClusterBuffer(ComputeShader cs, ref RenderingData renderingData)
        {
            int width = renderingData.cameraData.cameraTargetDescriptor.width;
            int height = renderingData.cameraData.cameraTargetDescriptor.height;
            //TODO GC 问题
            int[] gridDims = { m_DimData.clusterDimX, m_DimData.clusterDimY, m_DimData.clusterDimZ };
            int[] sizes = { CLUSTER_GRID_BLOCK_SIZE, CLUSTER_GRID_BLOCK_SIZE };
            Vector4 screenDim = new Vector4((float)width, (float)height, 1.0f / width, 1.0f / height);
            float viewNear = m_DimData.zNear;

            cs.SetInts(ShaderIDs.ClusterCB_GridDim, gridDims);
            cs.SetFloat(ShaderIDs.ClusterCB_ViewNear, viewNear);
            cs.SetInts(ShaderIDs.ClusterCB_Size, sizes);
            cs.SetFloat(ShaderIDs.ClusterCB_NearK, 1.0f + m_DimData.sD);
            cs.SetFloat(ShaderIDs.ClusterCB_LogGridDimY, m_DimData.logDimY);
            cs.SetVector(ShaderIDs.ClusterCB_ScreenDimensions, screenDim);
        }

        //预计算视锥体AABB
        void ComputeClusterAABB(ref CommandBuffer commandBuffer, ref RenderingData renderingData)
        {
            if (m_ClusterAABBCS == null)
                return;



            UpdateClusterBuffer(m_ClusterAABBCS, ref renderingData);

            int threadGroups = Mathf.CeilToInt(m_DimData.clusterDimXYZ / 1024.0f);

            commandBuffer.DispatchCompute(m_ClusterAABBCS, m_KernelOfClusterAABB, threadGroups, 1, 1);
        }

        //使用深度裁剪Cluster
        void ClusterSampleDepth(ref CommandBuffer commandBuffer, ref RenderingData renderingData)
        {
            //TODO GC
            float[] flags = new float[m_DimData.clusterDimXYZ];
            for (int i = 0; i < m_DimData.clusterDimXYZ; i++)
            {
                flags[i] = 0.0f;
            }
            commandBuffer.SetComputeBufferData(m_ClusterFlagBuffer, flags);

            UpdateClusterBuffer(m_ClusterAABBCS, ref renderingData);

            commandBuffer.SetComputeTextureParam(m_ClusterAABBCS, m_KernelOfClusterSampleDepth, ShaderIDs.DepthTexture, ForwardRenderer.depthIdentifier);

            int width = renderingData.cameraData.cameraTargetDescriptor.width;
            int height = renderingData.cameraData.cameraTargetDescriptor.height;
            commandBuffer.DispatchCompute(m_ClusterAABBCS, m_KernelOfClusterSampleDepth, Mathf.CeilToInt(width / 32.0f), Mathf.CeilToInt(height / 32.0f), 1);

        }

        //寻找有效的Cluster
        void FindUniqueCluster(ref CommandBuffer commandBuffer, ref RenderingData renderingData)
        {
            //TODO GC
            //重置数据 全部记为无效
            uint[] uniqueClusters = new uint[m_DimData.clusterDimXYZ];
            for (int i = 0; i < m_DimData.clusterDimXYZ; i++)
            {
                uniqueClusters[i] = 0;
            }
            commandBuffer.SetComputeBufferData(m_UniqueClustersBuffer, uniqueClusters);
            commandBuffer.SetComputeBufferCounterValue(m_UniqueClustersBuffer, 0);

            int threadGroups = Mathf.CeilToInt(m_DimData.clusterDimXYZ / 1024.0f);
            commandBuffer.DispatchCompute(m_ClusterAABBCS, m_KernelOfFindUniqueCluster, threadGroups, 1, 1);
        }

        //创建合适大小的IndirectBuffer
        void UpdateIndirectArgumentBuffers(ref CommandBuffer commandBuffer, ref RenderingData renderingData)
        {
            commandBuffer.DispatchCompute(m_ClusterAABBCS, m_KernelOfUpdateIndirectArgument, 1, 1, 1);
        }

        //光源求交
        void AssignLightsToClusts(ref CommandBuffer commandBuffer, ref CameraData cameraData)
        {
            //TODO GC 问题
            //初始化数据
            uint[] uCounter = { 0 };
            commandBuffer.SetComputeBufferData(m_ClusterPointLightIndexCounterBuffer, uCounter);

            Vector2Int[] vec2Girds = new Vector2Int[m_DimData.clusterDimXYZ];
            for (int i = 0; i < m_DimData.clusterDimXYZ; i++)
            {
                vec2Girds[i] = new Vector2Int(0, 0);
            }
            commandBuffer.SetComputeBufferData(m_ClusterPointLightGridBuffer, vec2Girds);


            //Input
            commandBuffer.SetComputeIntParams(m_ClusterAABBCS, "PointLightCount", m_PointLightPosRangeList.Count);
            if (UpdateDebugPos)
                commandBuffer.SetComputeMatrixParam(m_ClusterAABBCS, "_CameraLastViewMatrix", cameraData.camera.transform.localToWorldMatrix.inverse);

            commandBuffer.DispatchCompute(m_ClusterAABBCS, m_kernelAssignLightsToClusters, m_DimData.clusterDimXYZ, 1, 1);
            //给定工作大小是从 GPU 直接读取的 直接运行CS
            // commandBuffer.DispatchCompute(m_ClusterAABBCS, m_kernelAssignLightsToClusters, m_IAB_AssignLightsToClusters, 0);
        }

        private void InitDebug()
        {
            m_ClusterDebugMaterial = new Material(Shader.Find("Hidden/ClusterBasedLighting/DebugClusterAABB"));
            m_ClusterDebugMaterial.SetBuffer("ClusterAABBs", m_ClusterAABBBuffer);
            m_ClusterDebugMaterial.SetBuffer("PointLightGrid_Cluster", m_ClusterPointLightGridBuffer);
            m_ClusterDebugMaterial.SetBuffer("UniqueClusters", m_IAB_DrawDebugClusters);

            m_DrawDebugClusterBuffer = ComputeHelper.CreateArgsBuffer(CubeMesh, m_DimData.clusterDimXYZ);
        }

        void DebugCluster(ref CommandBuffer commandBuffer, ref CameraData cameraData)
        {
            if (UpdateDebugPos)
                m_ClusterDebugMaterial.SetMatrix("_CameraWorldMatrix", cameraData.camera.transform.localToWorldMatrix);


            commandBuffer.DrawMeshInstancedIndirect(CubeMesh, 0, m_ClusterDebugMaterial, 0, m_DrawDebugClusterBuffer, 0);
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
