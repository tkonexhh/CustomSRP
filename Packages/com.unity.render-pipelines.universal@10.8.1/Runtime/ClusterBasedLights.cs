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

            public float sD;
            public float logDimY;
            public float logDepth;

            public float nearK;
            public float SizeX;
            public float SizeY;

            public Vector4 ScreenDimensions;

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
        private int AVERAGE_LIGHTS_PER_CLUSTER = 10;

        bool m_Init = false;
        ClusterInfo m_ClusterInfo;
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

        public ClusterBasedLights(RenderPassEvent evt)
        {
            renderPassEvent = evt;
            m_ProfilingSampler = new ProfilingSampler("ClusterBasedLights");
            //TODO 这个只在编辑器下生效  需要改
            m_ComputeShader = UniversalRenderPipeline.asset.clusterBasedLightingComputeShader;
        }

        ~ClusterBasedLights()
        {
            m_ClusterAABBBuffer?.Dispose();
            m_PointLightBuffer?.Dispose();
            m_ClusterPointLightIndexCounterBuffer?.Dispose();
            m_AssignTableBuffer?.Dispose();
            m_ClusterPointLightIndexListBuffer?.Dispose();
            m_DrawDebugClusterBuffer?.Dispose();

            m_ClusterAABBBuffer = null;
            m_PointLightBuffer = null;
            m_ClusterPointLightIndexCounterBuffer = null;
            m_AssignTableBuffer = null;
            m_ClusterPointLightIndexListBuffer = null;
            m_DrawDebugClusterBuffer = null;
            m_ComputeShader = null;
            m_Init = false;
            UpdateDebugPos = true;
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

                    //pointLight.Position = renderingData.cameraData.camera.transform.InverseTransformPoint(pointLight.Position);
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
            if (m_ComputeShader == null)
                return;

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
                    CalculateMDim(ref renderingData);

                    m_ClusterAABBBuffer = ComputeHelper.CreateStructuredBuffer<AABB>(m_ClusterInfo.clusterDimXYZ);

                    m_ClusterPointLightIndexCounterBuffer = ComputeHelper.CreateStructuredBuffer<uint>(1);
                    m_AssignTableBuffer = ComputeHelper.CreateStructuredBuffer<LightIndex>(m_ClusterInfo.clusterDimXYZ);
                    // m_ClusterPointLightIndexListBuffer = ComputeHelper.CreateStructuredBuffer<uint>(m_DimData.clusterDimXYZ * MAX_NUM_POINT_LIGHT);
                    m_ClusterPointLightIndexListBuffer = ComputeHelper.CreateStructuredBuffer<uint>(m_ClusterInfo.clusterDimXYZ * AVERAGE_LIGHTS_PER_CLUSTER);//预估一个格子里面不会超过20个灯光

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
                    m_Vec2Girds = new LightIndex[m_ClusterInfo.clusterDimXYZ];
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
                //cmd.SetComputeMatrixParam(m_ComputeShader, ShaderIDs.InverseProjectionMatrix, projectionMatrix);
                cmd.SetComputeMatrixParam(m_ComputeShader, ShaderIDs.InverseProjectionMatrix, projectionMatrixInvers);

                AssignLightsToClusts(ref cmd, ref renderingData.cameraData);

                // DebugCluster(ref cmd, ref renderingData.cameraData);

                SetShaderParameters(ref renderingData.cameraData);

            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }


        void CalculateMDim(ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;
            var camera = cameraData.camera;

            // The half-angle of the field of view in the Y-direction.
            float fieldOfViewY = camera.fieldOfView * Mathf.Deg2Rad * 0.5f;//Degree 2 Radiance:  Param.CameraInfo.Property.Perspective.fFovAngleY * 0.5f;
            float zNear = camera.nearClipPlane;// Param.CameraInfo.Property.Perspective.fMinVisibleDistance;
            float zFar = Mathf.Min(200, camera.farClipPlane);// 多光源只计算50米
            // float zFar = camera.farClipPlane;

            int width = renderingData.cameraData.pixelWidth;
            int height = renderingData.cameraData.pixelHeight;

            Vector4 screenDimensions = new Vector4(cameraData.pixelWidth,
                      cameraData.pixelHeight,
                      1.0f / (float)cameraData.pixelWidth,
                      1.0f / (float)cameraData.pixelHeight
                      );
            // Debug.LogError(width + "---" + height);
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
            //Debug.LogError(camera.fieldOfView);
            // Debug.LogError(logDepth + "---" + logDimY + "---" + clusterDimZ);
            m_ClusterInfo.zNear = zNear;
            m_ClusterInfo.zFar = zFar;
            m_ClusterInfo.ScreenDimensions = screenDimensions;
            m_ClusterInfo.sD = sD;
            m_ClusterInfo.nearK = 1.0f + sD;
            m_ClusterInfo.fieldOfViewY = fieldOfViewY;
            m_ClusterInfo.logDepth = logDepth;
            m_ClusterInfo.logDimY = logDimY;
            m_ClusterInfo.clusterDimX = clusterDimX;
            m_ClusterInfo.clusterDimY = clusterDimY;
            m_ClusterInfo.clusterDimZ = clusterDimZ;
            m_ClusterInfo.clusterDimXYZ = clusterDimX * clusterDimY * clusterDimZ;//总个数
            // Debug.LogError(clusterDimX + "|" + clusterDimY + "|" + clusterDimZ);
        }

        void UpdateClusterBuffer(ref RenderingData renderingData)
        {
            //TODO GC 问题
            int[] gridDims = { m_ClusterInfo.clusterDimX, m_ClusterInfo.clusterDimY, m_ClusterInfo.clusterDimZ };
            int[] sizes = { CLUSTER_GRID_BLOCK_SIZE, CLUSTER_GRID_BLOCK_SIZE };
            float viewNear = m_ClusterInfo.zNear;



            m_ComputeShader.SetInts(ShaderIDs.ClusterCB_GridDim, gridDims);
            m_ComputeShader.SetFloat(ShaderIDs.ClusterCB_ViewNear, viewNear);
            m_ComputeShader.SetInts(ShaderIDs.ClusterCB_Size, sizes);
            m_ComputeShader.SetFloat(ShaderIDs.ClusterCB_NearK, m_ClusterInfo.nearK);
            m_ComputeShader.SetFloat(ShaderIDs.ClusterCB_LogGridDimY, m_ClusterInfo.logDimY);
            m_ComputeShader.SetVector(ShaderIDs.ClusterCB_ScreenDimensions, m_ClusterInfo.ScreenDimensions);
        }

        //预计算视锥体AABB
        void ClusterGenerate(ref CommandBuffer commandBuffer, ref RenderingData renderingData)
        {
            if (m_ComputeShader == null)
                return;

            UpdateClusterBuffer(ref renderingData);

            int threadGroups = Mathf.CeilToInt(m_ClusterInfo.clusterDimXYZ / 1024.0f);
            commandBuffer.SetComputeBufferParam(m_ComputeShader, m_KernelOfClusterAABB, "RWClusterAABBs", m_ClusterAABBBuffer);
            commandBuffer.DispatchCompute(m_ComputeShader, m_KernelOfClusterAABB, threadGroups, 1, 1);
        }

        //光源求交
        void AssignLightsToClusts(ref CommandBuffer commandBuffer, ref CameraData cameraData)
        {
            //output
            commandBuffer.SetComputeBufferParam(m_ComputeShader, m_kernelAssignLightsToClusters, "RWPointLightIndexCounter_Cluster", m_ClusterPointLightIndexCounterBuffer);
            commandBuffer.SetComputeBufferParam(m_ComputeShader, m_kernelAssignLightsToClusters, "RWLightAssignTable", m_AssignTableBuffer);
            commandBuffer.SetComputeBufferParam(m_ComputeShader, m_kernelAssignLightsToClusters, "RWPointLightIndexList_Cluster", m_ClusterPointLightIndexListBuffer);
            //Input
            commandBuffer.SetComputeBufferParam(m_ComputeShader, m_kernelAssignLightsToClusters, "PointLights", m_PointLightBuffer);
            commandBuffer.SetComputeBufferParam(m_ComputeShader, m_kernelAssignLightsToClusters, "ClusterAABBs", m_ClusterAABBBuffer);

            //初始化数据
            commandBuffer.SetComputeBufferData(m_ClusterPointLightIndexCounterBuffer, m_UCounter);
            commandBuffer.SetComputeBufferData(m_AssignTableBuffer, m_Vec2Girds);

            //Input
            commandBuffer.SetComputeIntParams(m_ComputeShader, "PointLightCount", m_PointLightPosRangeList.Count);
            if (UpdateDebugPos)
                commandBuffer.SetComputeMatrixParam(m_ComputeShader, "_CameraLastViewMatrix", cameraData.camera.transform.localToWorldMatrix.inverse);

            commandBuffer.SetComputeMatrixParam(m_ComputeShader, "_W2CMatrix", cameraData.GetViewMatrix().inverse);

            //Debug.LogError((cameraData.camera.worldToCameraMatrix * m_PointLightPosRangeList[0].Position));
            //Debug.LogError("相机矩阵" + cameraData.camera.transform.localToWorldMatrix);
            //Debug.LogError("V" + cameraData.camera.worldToCameraMatrix);
            // commandBuffer.DispatchCompute(m_ComputeShader, m_kernelAssignLightsToClusters, m_DimData.clusterDimXYZ, 1, 1);
            commandBuffer.DispatchCompute(m_ComputeShader, m_kernelAssignLightsToClusters, m_ClusterInfo.clusterDimX, m_ClusterInfo.clusterDimY, m_ClusterInfo.clusterDimZ);
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
            Shader.SetGlobalFloat(ShaderIDs.Cluster_ViewNear, m_ClusterInfo.zNear);
            Shader.SetGlobalInt(ShaderIDs.Cluster_SizeX, CLUSTER_GRID_BLOCK_SIZE);
            Shader.SetGlobalInt(ShaderIDs.Cluster_SizeY, CLUSTER_GRID_BLOCK_SIZE);
            Shader.SetGlobalFloat(ShaderIDs.Cluster_LogGridDimY, m_ClusterInfo.logDimY);
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
                {
                    Vector3 Point = Vector3.zero;
                    float length = 1, width = 1, heigth = 1;
                    //vertices(顶点、必须):
                    int vertices_count = 4 * 6;                                 //顶点数（每个面4个点，六个面）
                    Vector3[] vertices = new Vector3[vertices_count];
                    vertices[0] = new Vector3(Point.x - length / 2, Point.y - heigth / 2, Point.z - width / 2);                     //前面的左下角的点
                    vertices[1] = new Vector3(Point.x - length / 2, Point.y + heigth / 2, Point.z - width / 2);                //前面的左上角的点
                    vertices[2] = new Vector3(Point.x + length / 2, Point.y - heigth / 2, Point.z - width / 2);                 //前面的右下角的点
                    vertices[3] = new Vector3(Point.x + length / 2, Point.y + heigth / 2, Point.z - width / 2);           //前面的右上角的点

                    vertices[4] = new Vector3(Point.x + length / 2, Point.y - heigth / 2, Point.z + width / 2);           //后面的右下角的点
                    vertices[5] = new Vector3(Point.x + length / 2, Point.y + heigth / 2, Point.z + width / 2);      //后面的右上角的点
                    vertices[6] = new Vector3(Point.x - length / 2, Point.y - heigth / 2, Point.z + width / 2);                //后面的左下角的点
                    vertices[7] = new Vector3(Point.x - length / 2, Point.y + heigth / 2, Point.z + width / 2);           //后面的左上角的点

                    vertices[8] = vertices[6];                              //左
                    vertices[9] = vertices[7];
                    vertices[10] = vertices[0];
                    vertices[11] = vertices[1];

                    vertices[12] = vertices[2];                              //右
                    vertices[13] = vertices[3];
                    vertices[14] = vertices[4];
                    vertices[15] = vertices[5];

                    vertices[16] = vertices[1];                              //上
                    vertices[17] = vertices[7];
                    vertices[18] = vertices[3];
                    vertices[19] = vertices[5];

                    vertices[20] = vertices[2];                              //下
                    vertices[21] = vertices[4];
                    vertices[22] = vertices[0];
                    vertices[23] = vertices[6];


                    //triangles(索引三角形、必须):
                    int SplitTriangle = 6 * 2;//分割三角形数量
                    int triangles_cout = SplitTriangle * 3;                  //索引三角形的索引点个数
                    int[] triangles = new int[triangles_cout];            //索引三角形数组
                    for (int i = 0, vi = 0; i < triangles_cout; i += 6, vi += 4)
                    {
                        triangles[i] = vi;
                        triangles[i + 1] = vi + 1;
                        triangles[i + 2] = vi + 2;

                        triangles[i + 3] = vi + 3;
                        triangles[i + 4] = vi + 2;
                        triangles[i + 5] = vi + 1;

                    }
                    //负载属性与mesh
                    m_CubeMesh = new Mesh();
                    m_CubeMesh.vertices = vertices;
                    m_CubeMesh.triangles = triangles;
                    m_CubeMesh.RecalculateBounds();
                    m_CubeMesh.RecalculateNormals();
                }
                return m_CubeMesh;

            }
        }
    }
}
