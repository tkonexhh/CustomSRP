using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;

namespace UnityEngine.Rendering.Universal
{
    //只有主相机才进行多光源操作
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
        const int CLUSTER_GRID_BLOCK_SIZE = 32;//单个Block像素大小
        const int MAX_NUM_POINT_LIGHT = 1024;

        bool m_Init = false;
        CD_DIM m_DimData;
        ComputeBuffer m_ClusterAABBBuffer;//存放计算好的ClusterAABB

        ComputeBuffer m_PointLightPosRangeBuffer;//存放点光源参数
        ComputeBuffer m_ClusterPointLightIndexCounterBuffer;
        ComputeBuffer m_ClusterPointLightGridBuffer;
        ComputeBuffer m_ClusterPointLightIndexList;

        ComputeBuffer m_AssignLightsToClusters;

        int m_KernelOfClusterAABB;
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
            internal static readonly int RWClusterFlags = Shader.PropertyToID("RWClusterFlags");
        };

        public ClusterBasedLights()
        {
            m_ProfilingSampler = new ProfilingSampler("ClusterBasedLights");
            //TODO 这个只在编辑器下生效  需要改
            m_ClusterAABBCS = UniversalRenderPipeline.asset.clusterBasedLightingComputeShader;

            m_PointLightPosRangeBuffer = ComputeHelper.CreateStructuredBuffer<Vector4>(MAX_NUM_POINT_LIGHT);
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
                    CalculateMDim(ref renderingData);

                    m_ClusterAABBBuffer = ComputeHelper.CreateStructuredBuffer<AABB>(m_DimData.clusterDimXYZ);

                    m_KernelOfClusterAABB = m_ClusterAABBCS.FindKernel("ClusterAABB");
                    m_kernelAssignLightsToClusters = m_ClusterAABBCS.FindKernel("AssignLightsToClusters");

                    //m_KernelOfClusterAABB
                    m_ClusterAABBCS.SetBuffer(m_KernelOfClusterAABB, "RWClusterAABBs", m_ClusterAABBBuffer);

                    //m_kernelAssignLightsToClusters
                    m_ClusterAABBCS.SetBuffer(m_kernelAssignLightsToClusters, "PointLights", m_PointLightPosRangeBuffer);

                    ComputeClusterAABB(camera, ref cmd);

                    InitDebug();
                    m_Init = true;
                }


                DebugCluster(ref cmd, ref renderingData.cameraData);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void SetupLights(ScriptableRenderContext context, ref RenderingData renderingData)
        {
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

            m_PointLightPosRangeBuffer.SetData(m_PointLightPosRangeList);
            // Debug.LogError("Point light count:" + m_PointLightPosRadiusList.Count);
            m_PointLightPosRangeList.Clear();
        }


        void CalculateMDim(ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            // The half-angle of the field of view in the Y-direction.
            float fieldOfViewY = camera.fieldOfView * Mathf.Deg2Rad * 0.5f;//Degree 2 Radiance:  Param.CameraInfo.Property.Perspective.fFovAngleY * 0.5f;
            float zNear = camera.nearClipPlane;// Param.CameraInfo.Property.Perspective.fMinVisibleDistance;
            float zFar = Mathf.Min(50, camera.farClipPlane);// 多光源只计算50米

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

        void UpdateClusterBuffer(ComputeShader cs)
        {
            int[] gridDims = { m_DimData.clusterDimX, m_DimData.clusterDimY, m_DimData.clusterDimZ };
            int[] sizes = { CLUSTER_GRID_BLOCK_SIZE, CLUSTER_GRID_BLOCK_SIZE };
            Vector4 screenDim = new Vector4((float)Screen.width, (float)Screen.height, 1.0f / Screen.width, 1.0f / Screen.height);
            float viewNear = m_DimData.zNear;

            cs.SetInts(ShaderIDs.ClusterCB_GridDim, gridDims);
            cs.SetFloat(ShaderIDs.ClusterCB_ViewNear, viewNear);
            cs.SetInts(ShaderIDs.ClusterCB_Size, sizes);
            cs.SetFloat(ShaderIDs.ClusterCB_NearK, 1.0f + m_DimData.sD);
            cs.SetFloat(ShaderIDs.ClusterCB_LogGridDimY, m_DimData.logDimY);
            cs.SetVector(ShaderIDs.ClusterCB_ScreenDimensions, screenDim);
        }

        void ComputeClusterAABB(Camera camera, ref CommandBuffer commandBuffer)
        {
            if (m_ClusterAABBCS == null)
                return;

            var projectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            var projectionMatrixInvers = projectionMatrix.inverse;
            m_ClusterAABBCS.SetMatrix(ShaderIDs.InverseProjectionMatrix, projectionMatrixInvers);

            UpdateClusterBuffer(m_ClusterAABBCS);

            int threadGroups = Mathf.CeilToInt(m_DimData.clusterDimXYZ / 1024.0f);

            commandBuffer.SetComputeBufferParam(m_ClusterAABBCS, m_KernelOfClusterAABB, "RWClusterAABBs", m_ClusterAABBBuffer);
            commandBuffer.DispatchCompute(m_ClusterAABBCS, m_KernelOfClusterAABB, threadGroups, 1, 1);
        }

        void AssignLightsToClusts(ref CommandBuffer commandBuffer)
        {
            //Output
            commandBuffer.SetComputeBufferParam(m_ClusterAABBCS, m_kernelAssignLightsToClusters, "RWPointLightIndexCounter_Cluster", m_ClusterPointLightIndexCounterBuffer);
            commandBuffer.SetComputeBufferParam(m_ClusterAABBCS, m_kernelAssignLightsToClusters, "RWPointLightGrid_Cluster", m_ClusterPointLightGridBuffer);
            commandBuffer.SetComputeBufferParam(m_ClusterAABBCS, m_kernelAssignLightsToClusters, "RWPointLightIndexList_Cluster", m_ClusterPointLightIndexList);


            //Input
            commandBuffer.SetComputeIntParams(m_ClusterAABBCS, "PointLightCount", m_PointLightPosRangeList.Count);
            commandBuffer.SetComputeBufferParam(m_ClusterAABBCS, m_kernelAssignLightsToClusters, "PointLights", m_PointLightPosRangeBuffer);
            //  commandBuffer.SetComputeBufferParam(kernel, "ClusterAABBs", cb_ClusterAABBs);

            //给定工作大小是从 GPU 直接读取的 直接运行CS
            commandBuffer.DispatchCompute(m_ClusterAABBCS, m_kernelAssignLightsToClusters, m_AssignLightsToClusters, 0);
        }

        private void InitDebug()
        {
            m_ClusterDebugMaterial = new Material(Shader.Find("Hidden/ClusterBasedLighting"));
            m_ClusterDebugMaterial.SetBuffer("ClusterAABBs", m_ClusterAABBBuffer);

            m_DrawDebugClusterBuffer = ComputeHelper.CreateArgsBuffer(CubeMesh, m_DimData.clusterDimXYZ);
        }

        void DebugCluster(ref CommandBuffer commandBuffer, ref CameraData cameraData)
        {
            if (UpdateDebugPos)
            {
                m_ClusterDebugMaterial.SetMatrix("_CameraWorldMatrix", cameraData.camera.transform.localToWorldMatrix);
            }
            commandBuffer.DrawMeshInstancedIndirect(CubeMesh, 0, m_ClusterDebugMaterial, 0, m_DrawDebugClusterBuffer, 0);
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
