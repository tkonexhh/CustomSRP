using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;

public class ClusterBasedLightingJobPass : ScriptableRenderPass
{
    ClusterBasedLightingRenderFeature.Settings m_Settings;
    ClusterInfo m_ClusterInfo;

    int m_OldWidth = -1;
    int m_OldHeight = -1;

    //灯光数据
    List<Vector4> m_PointLightPosRangeList = new List<Vector4>();//存放点光源位置和范围 xyz:pos w:range

    //第一步 求Cluster
    NativeArray<Vector3> m_ClusterAABBMinArray;
    NativeArray<Vector3> m_ClusterAABBMaxArray;
    //第二部 光源求交
    NativeList<Vector4> m_PointLightsNativeList;
    NativeArray<LightIndex> m_AssignTableArray;
    NativeArray<uint> m_PointLightIndexArray;

    //
    NativeArray<uint> m_ZipedPointLightIndexArray;

    Matrix4x4 m_CameraLastViewMatrix;

    ComputeBuffer m_PointLightBuffer;//存放点光源参数
    ComputeBuffer m_ClusterPointLightIndexListBuffer;//光源分配结果
    ComputeBuffer m_AssignTableBuffer;//XYZ个  Vector2Int  x 是1D 坐标 y 是灯光个数

    bool m_Init = false;

    public NativeArray<Vector3> clusterAABBMinArray => m_ClusterAABBMinArray;
    public NativeArray<Vector3> clusterAABBMaxArray => m_ClusterAABBMaxArray;
    public ComputeBuffer assignTableBuffer => m_AssignTableBuffer;
    public ClusterInfo clusterInfo => m_ClusterInfo;

    public ClusterBasedLightingJobPass(ClusterBasedLightingRenderFeature.Settings settings)
    {
        m_Settings = settings;
        renderPassEvent = RenderPassEvent.BeforeRendering;
    }

    public void SetupLights(ref RenderingData renderingData)
    {
        if (!m_PointLightsNativeList.IsCreated)
            m_PointLightsNativeList = new NativeList<Vector4>(ClusterBasedLightDefine.MAX_NUM_POINT_LIGHT, Allocator.Persistent);

        m_PointLightPosRangeList.Clear();
        m_PointLightsNativeList.Clear();
        var visibleLights = renderingData.lightData.visibleLights;
        for (int i = 0; i < Mathf.Min(renderingData.lightData.visibleLights.Length, ClusterBasedLightDefine.MAX_NUM_POINT_LIGHT); i++)
        {
            if (renderingData.lightData.visibleLights[i].lightType == LightType.Point)
            {
                ClusterPointLight pointLight = new ClusterPointLight();
                pointLight.Position = renderingData.lightData.visibleLights[i].light.transform.position;

                //pointLight.Position = renderingData.cameraData.camera.transform.InverseTransformPoint(pointLight.Position);
                pointLight.Range = renderingData.lightData.visibleLights[i].range;
                var color = renderingData.lightData.visibleLights[i].finalColor;
                pointLight.color = new Vector3(color.r, color.g, color.b);
                // m_PointLightPosRangeList.Add(pointLight);
                Vector4 lightPosRange = new Vector4(pointLight.Position.x, pointLight.Position.y, pointLight.Position.z, pointLight.Range);
                m_PointLightPosRangeList.Add(lightPosRange);
                if (m_PointLightsNativeList.Length < ClusterBasedLightDefine.MAX_NUM_POINT_LIGHT)
                    m_PointLightsNativeList.Add(lightPosRange);
            }
        }

        //Light Buffer
        if (m_PointLightBuffer == null)
            m_PointLightBuffer = ComputeHelper.CreateStructuredBuffer<Vector4>(ClusterBasedLightDefine.MAX_NUM_POINT_LIGHT);
        m_PointLightBuffer.SetData(m_PointLightPosRangeList);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        //当视图大小发生变化时 就需要重新Init
        int width = renderingData.cameraData.cameraTargetDescriptor.width;
        int height = renderingData.cameraData.cameraTargetDescriptor.height;
        if (m_OldWidth != width || m_OldHeight != height)
        {
            m_OldWidth = width;
            m_OldHeight = height;
            m_Init = false;
        }

        if (!m_Init)
        {
            m_Init = true;
            // m_CurrentCamera = camera;
            //当FOV clipplane 发生变化 就需要重新计算
            m_ClusterInfo = ClusterInfo.CalcClusterInfo(ref renderingData, ClusterBasedLightDefine.CLUSTER_GRID_BLOCK_SIZE_XY, ClusterBasedLightDefine.CLUSTER_GRID_BLOCK_SIZE_Z);

            if (m_ClusterAABBMinArray.IsCreated)
                m_ClusterAABBMinArray.Dispose();
            m_ClusterAABBMinArray = new NativeArray<Vector3>(m_ClusterInfo.clusterDimXYZ, Allocator.Persistent);

            if (m_ClusterAABBMaxArray.IsCreated)
                m_ClusterAABBMaxArray.Dispose();
            m_ClusterAABBMaxArray = new NativeArray<Vector3>(m_ClusterInfo.clusterDimXYZ, Allocator.Persistent);

            m_PointLightIndexArray = new NativeArray<uint>(m_ClusterInfo.clusterDimXYZ * ClusterBasedLightDefine.AVERAGE_LIGHTS_PER_CLUSTER, Allocator.Persistent);
            m_ZipedPointLightIndexArray = new NativeArray<uint>(m_ClusterInfo.clusterDimXYZ * ClusterBasedLightDefine.AVERAGE_LIGHTS_PER_CLUSTER, Allocator.Persistent);
            m_AssignTableArray = new NativeArray<LightIndex>(m_ClusterInfo.clusterDimXYZ, Allocator.Persistent);
            m_AssignTableBuffer = ComputeHelper.CreateStructuredBuffer<Vector2Int>(m_ClusterInfo.clusterDimXYZ);
            m_ClusterPointLightIndexListBuffer = ComputeHelper.CreateStructuredBuffer<uint>(m_ClusterInfo.clusterDimXYZ * ClusterBasedLightDefine.AVERAGE_LIGHTS_PER_CLUSTER);//预估一个格子里面不会超过20个灯光

            //Job 计算clusterjob
            var generateClusterJob = new GenerateClusterJob()
            {
                ClusterInfo = m_ClusterInfo,
                clusterAABBMinArray = m_ClusterAABBMinArray,
                clusterAABBMaxArray = m_ClusterAABBMaxArray,
            };
            var handle = generateClusterJob.Schedule(m_ClusterInfo.clusterDimXYZ, 64);
            handle.Complete();

            LogDebug();
        }
        // Debug.LogError(m_ClusterAABBMinArray.Length + "===" + m_ClusterAABBMaxArray.Length);
        //光源求交

        if (ClusterBasedLightingRenderFeature.UpdateDebugPos)
            m_CameraLastViewMatrix = renderingData.cameraData.camera.transform.localToWorldMatrix.inverse;
        // commandBuffer.SetComputeMatrixParam(m_ComputeShader, "_CameraLastViewMatrix", cameraData.camera.transform.localToWorldMatrix.inverse);

        for (int i = 0; i < m_PointLightIndexArray.Length; i++)
        {
            m_PointLightIndexArray[i] = 0;
            m_ZipedPointLightIndexArray[i] = 0;
        }

        var assignLightsToClusterJob = new AssignLightsToClustersJob()
        {
            // ClusterInfo = m_ClusterInfo,
            clusterAABBMinArray = m_ClusterAABBMinArray,
            clusterAABBMaxArray = m_ClusterAABBMaxArray,
            PointLights = m_PointLightsNativeList,
            CameraWorldMatrix = m_CameraLastViewMatrix,
            PointLightIndex = m_PointLightIndexArray,
            LightAssignTable = m_AssignTableArray,
            MaxLightCountPerCluster = ClusterBasedLightDefine.AVERAGE_LIGHTS_PER_CLUSTER,
        };

        var assignHandle = assignLightsToClusterJob.Schedule(m_ClusterInfo.clusterDimXYZ, 64);
        // assignHandle.Complete();

        //为了job并行 这个也需要改为jobs
        //上一步 得到了一串未压缩的数据 接下来进行行程压缩
        //用非job计算真正startindex
        var calcStartIndexJob = new CalcStartIndexJob()
        {
            LightAssignTable = m_AssignTableArray
        };

        var calcStartIndexHandle = calcStartIndexJob.Schedule(1, 1, assignHandle);


        var zipLightIndexJob = new ZipLightIndexJob()
        {
            MaxLightCountPerCluster = ClusterBasedLightDefine.AVERAGE_LIGHTS_PER_CLUSTER,
            PointLightIndex = m_PointLightIndexArray,
            LightAssignTable = m_AssignTableArray,
            zipedPointLightIndex = m_ZipedPointLightIndexArray,
        };

        var zipHandle = zipLightIndexJob.Schedule(m_ClusterInfo.clusterDimXYZ, 64, calcStartIndexHandle);
        zipHandle.Complete();

        // for (int i = 0; i < 10; i++)
        // {
        //     Debug.LogError("JOB:" + i + "---" + m_ZipedPointLightIndexArray[i]);
        // }

        m_ClusterPointLightIndexListBuffer.SetData(m_ZipedPointLightIndexArray);
        m_AssignTableBuffer.SetData(m_AssignTableArray);

        SetShaderParameters(ref renderingData.cameraData);
    }

    void SetShaderParameters(ref CameraData cameraData)
    {
        //设置全部参数 用于shading
        Shader.SetGlobalInt(ClusterBasedLightingPass.ShaderIDs.Cluster_GridCountX, m_ClusterInfo.clusterDimX);
        Shader.SetGlobalInt(ClusterBasedLightingPass.ShaderIDs.Cluster_GridCountY, m_ClusterInfo.clusterDimY);
        Shader.SetGlobalInt(ClusterBasedLightingPass.ShaderIDs.Cluster_GridCountZ, m_ClusterInfo.clusterDimZ);
        // Shader.SetGlobalFloat(ShaderIDs.Cluster_ViewNear, m_ClusterInfo.zNear);
        Shader.SetGlobalFloat(ClusterBasedLightingPass.ShaderIDs.Cluster_SizeX, m_ClusterInfo.cluster_SizeX);
        Shader.SetGlobalFloat(ClusterBasedLightingPass.ShaderIDs.Cluster_SizeY, m_ClusterInfo.cluster_SizeY);
        Shader.SetGlobalFloat(ClusterBasedLightingPass.ShaderIDs.Cluster_SizeZ, m_ClusterInfo.cluster_SizeZ);

        Shader.SetGlobalBuffer(ClusterBasedLightingPass.ShaderIDs.Cluster_AssignTable, m_AssignTableBuffer);
        Shader.SetGlobalBuffer(ClusterBasedLightingPass.ShaderIDs.Cluster_PointLightBuffer, m_PointLightBuffer);
        Shader.SetGlobalBuffer(ClusterBasedLightingPass.ShaderIDs.Cluster_LightAssignTable, m_ClusterPointLightIndexListBuffer);
        Shader.SetGlobalMatrix("_CameraWorldMatrix", cameraData.camera.transform.localToWorldMatrix);
    }

    void LogDebug()
    {
        // Debug.LogError("Job======================");

        // for (int i = 20; i < 40; i++)
        // {
        //     Debug.LogError(m_ClusterAABBMinArray[i] + "===" + m_ClusterAABBMaxArray[i]);
        // }
    }

    public void Release()
    {
        if (m_ClusterAABBMinArray.IsCreated)
            m_ClusterAABBMinArray.Dispose();

        if (m_ClusterAABBMaxArray.IsCreated)
            m_ClusterAABBMaxArray.Dispose();

        if (m_PointLightsNativeList.IsCreated)
            m_PointLightsNativeList.Dispose();

        if (m_PointLightIndexArray.IsCreated)
            m_PointLightIndexArray.Dispose();

        if (m_AssignTableArray.IsCreated)
            m_AssignTableArray.Dispose();

        if (m_ZipedPointLightIndexArray.IsCreated)
            m_ZipedPointLightIndexArray.Dispose();

        m_PointLightBuffer?.Release();
        m_PointLightBuffer = null;
    }
}
