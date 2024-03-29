using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;

public class ClusterBasedLightingPass : ScriptableRenderPass
{
    ProfilingSampler m_ProfilingSampler;
    ClusterBasedLightingRenderFeature.Settings m_Settings;
    bool m_Init = false;
    ClusterInfo m_ClusterInfo;
    //计算视锥AABB
    ComputeBuffer m_ClusterAABBMinBuffer;//存放计算好的ClusterAABB
    ComputeBuffer m_ClusterAABBMaxBuffer;
    //光源求交
    ComputeBuffer m_PointLightBuffer;//存放点光源参数
    ComputeBuffer m_ClusterPointLightIndexCounterBuffer;
    ComputeBuffer m_AssignTableBuffer;//XYZ个  Vector2Int  x 是1D 坐标 y 是灯光个数
    ComputeBuffer m_ClusterPointLightIndexListBuffer;//光源分配结果

    int m_KernelOfClusterAABB;
    int m_kernelAssignLightsToClusters;

    ComputeShader m_ComputeShader;

    List<Vector4> m_PointLightPosRangeList = new List<Vector4>();//存放点光源位置和范围 xyz:pos w:range

    //初始变量
    Vector2Int[] m_Vec2Girds;
    uint[] m_UCounter = { 0 };

    int m_OldWidth = -1;
    int m_OldHeight = -1;


    public ComputeBuffer clusterAABBMinBuffer => m_ClusterAABBMinBuffer;
    public ComputeBuffer clusterAABBMaxBuffer => m_ClusterAABBMaxBuffer;
    public ComputeBuffer assignTableBuffer => m_AssignTableBuffer;
    public ClusterInfo clusterInfo => m_ClusterInfo;



    public struct ShaderIDs
    {
        internal static readonly int InverseProjectionMatrix = Shader.PropertyToID("_InverseProjectionMatrix");
        // internal static readonly int ClusterCB_ViewNear = Shader.PropertyToID("ClusterCB_ViewNear");
        internal static readonly int ClusterCB_ScreenDimensions = Shader.PropertyToID("ClusterCB_ScreenDimensions");
        internal static readonly int ClusterCB_GridDim = Shader.PropertyToID("ClusterCB_GridDim");
        internal static readonly int ClusterCB_Size = Shader.PropertyToID("ClusterCB_Size");

        //用于Shading
        internal static readonly int Cluster_GridCountX = Shader.PropertyToID("_Cluster_GridCountX");
        internal static readonly int Cluster_GridCountY = Shader.PropertyToID("_Cluster_GridCountY");
        internal static readonly int Cluster_GridCountZ = Shader.PropertyToID("_Cluster_GridCountZ");
        // internal static readonly int Cluster_ViewNear = Shader.PropertyToID("_Cluster_ViewNear");
        internal static readonly int Cluster_SizeX = Shader.PropertyToID("_Cluster_SizeX");
        internal static readonly int Cluster_SizeY = Shader.PropertyToID("_Cluster_SizeY");
        internal static readonly int Cluster_SizeZ = Shader.PropertyToID("_Cluster_SizeZ");
        // internal static readonly int Cluster_NearK = Shader.PropertyToID("_Cluster_NearK");
        // internal static readonly int Cluster_LogGridDimY = Shader.PropertyToID("_Cluster_LogGridDimY");
        internal static readonly int Cluster_LightAssignTable = Shader.PropertyToID("_LightAssignTable");
        internal static readonly int Cluster_PointLightBuffer = Shader.PropertyToID("_PointLightBuffer");
        internal static readonly int Cluster_AssignTable = Shader.PropertyToID("_AssignTable");
    };

    public ClusterBasedLightingPass(ClusterBasedLightingRenderFeature.Settings settings)
    {
        m_Settings = settings;
        renderPassEvent = RenderPassEvent.BeforeRendering;
        m_ProfilingSampler = new ProfilingSampler("ClusterBasedLights");
        m_ComputeShader = m_Settings.mainComputeShader;
    }


    //准备灯光数据 最一开始调用
    public void SetupLights(ref RenderingData renderingData)
    {
        m_PointLightPosRangeList.Clear();
        // 检索出全部点光源
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
                m_PointLightPosRangeList.Add(new Vector4(pointLight.Position.x, pointLight.Position.y, pointLight.Position.z, pointLight.Range));
            }
        }

        //Light Buffer
        if (m_PointLightBuffer == null)
            m_PointLightBuffer = ComputeHelper.CreateStructuredBuffer<Vector4>(ClusterBasedLightDefine.MAX_NUM_POINT_LIGHT);
        m_PointLightBuffer.SetData(m_PointLightPosRangeList);

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
                // m_CurrentCamera = camera;
                //当FOV clipplane 发生变化 就需要重新计算
                m_ClusterInfo = ClusterInfo.CalcClusterInfo(ref renderingData, ClusterBasedLightDefine.CLUSTER_GRID_BLOCK_SIZE_XY, ClusterBasedLightDefine.CLUSTER_GRID_BLOCK_SIZE_Z);
                // CalculateClusterInfo(ref renderingData);

                if (m_ClusterAABBMinBuffer != null) m_ClusterAABBMinBuffer.Release();
                if (m_ClusterAABBMaxBuffer != null) m_ClusterAABBMaxBuffer.Release();
                m_ClusterAABBMinBuffer = ComputeHelper.CreateStructuredBuffer<Vector3>(m_ClusterInfo.clusterDimXYZ);
                m_ClusterAABBMaxBuffer = ComputeHelper.CreateStructuredBuffer<Vector3>(m_ClusterInfo.clusterDimXYZ);

                m_ClusterPointLightIndexCounterBuffer = ComputeHelper.CreateStructuredBuffer<uint>(1);
                m_AssignTableBuffer = ComputeHelper.CreateStructuredBuffer<Vector2Int>(m_ClusterInfo.clusterDimXYZ);
                m_ClusterPointLightIndexListBuffer = ComputeHelper.CreateStructuredBuffer<uint>(m_ClusterInfo.clusterDimXYZ * ClusterBasedLightDefine.AVERAGE_LIGHTS_PER_CLUSTER);//预估一个格子里面不会超过20个灯光

                m_KernelOfClusterAABB = m_ComputeShader.FindKernel("ClusterAABB");
                m_kernelAssignLightsToClusters = m_ComputeShader.FindKernel("AssignLightsToClusters");

                // //---------
                // cmd.SetComputeBufferParam(m_ComputeShader, m_KernelOfClusterAABB, "RWClusterAABBs", m_ClusterAABBBuffer);
                // cmd.SetComputeBufferParam(m_ComputeShader, m_KernelOfClusterAABB, "RWClusterAABBMaxs", m_ClusterAABBMaxBuffer);

                // m_ComputeShader.SetBuffer(m_KernelOfClusterAABB, "RWClusterAABBs", m_ClusterAABBBuffer);
                //---------
                //output
                cmd.SetComputeBufferParam(m_ComputeShader, m_kernelAssignLightsToClusters, "RWPointLightIndexCounter_Cluster", m_ClusterPointLightIndexCounterBuffer);
                cmd.SetComputeBufferParam(m_ComputeShader, m_kernelAssignLightsToClusters, "RWLightAssignTable", m_AssignTableBuffer);
                cmd.SetComputeBufferParam(m_ComputeShader, m_kernelAssignLightsToClusters, "RWPointLightIndexList_Cluster", m_ClusterPointLightIndexListBuffer);
                // m_ComputeShader.SetBuffer(m_kernelAssignLightsToClusters, "RWPointLightIndexCounter_Cluster", m_ClusterPointLightIndexCounterBuffer);
                // m_ComputeShader.SetBuffer(m_kernelAssignLightsToClusters, "RWLightAssignTable", m_AssignTableBuffer);
                // m_ComputeShader.SetBuffer(m_kernelAssignLightsToClusters, "RWPointLightIndexList_Cluster", m_ClusterPointLightIndexListBuffer);
                //Input
                cmd.SetComputeBufferParam(m_ComputeShader, m_kernelAssignLightsToClusters, "PointLights", m_PointLightBuffer);
                cmd.SetComputeBufferParam(m_ComputeShader, m_kernelAssignLightsToClusters, "ClusterAABBMins", m_ClusterAABBMinBuffer);
                cmd.SetComputeBufferParam(m_ComputeShader, m_kernelAssignLightsToClusters, "ClusterAABBMaxs", m_ClusterAABBMaxBuffer);
                // m_ComputeShader.SetBuffer(m_kernelAssignLightsToClusters, "PointLights", m_PointLightBuffer);
                // m_ComputeShader.SetBuffer(m_kernelAssignLightsToClusters, "ClusterAABBMins", m_ClusterAABBBuffer);

                //初始化默认数据
                m_Vec2Girds = new Vector2Int[m_ClusterInfo.clusterDimXYZ];
                for (int i = 0; i < m_Vec2Girds.Length; i++)
                {
                    LightIndex lightIndex = new LightIndex();
                    lightIndex.start = i * ClusterBasedLightDefine.AVERAGE_LIGHTS_PER_CLUSTER;
                    lightIndex.count = 0;
                    // m_Vec2Girds[i] = lightIndex;
                    m_Vec2Girds[i] = new Vector2Int(lightIndex.start, lightIndex.count);
                }

                ClusterGenerate(ref cmd, ref renderingData);
                // LogDebug();
                m_Init = true;
            }

            var projectionMatrix = renderingData.cameraData.GetGPUProjectionMatrix();
            var projectionMatrixInvers = projectionMatrix.inverse;
            //cmd.SetComputeMatrixParam(m_ComputeShader, ShaderIDs.InverseProjectionMatrix, projectionMatrix);
            cmd.SetComputeMatrixParam(m_ComputeShader, ShaderIDs.InverseProjectionMatrix, projectionMatrixInvers);


            AssignLightsToClusts(ref cmd, ref renderingData.cameraData);

            LogDebug();
            SetShaderParameters(ref renderingData.cameraData);

        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }


    void UpdateClusterBuffer(ref RenderingData renderingData)
    {
        //TODO GC 问题
        int[] gridDims = { m_ClusterInfo.clusterDimX, m_ClusterInfo.clusterDimY, m_ClusterInfo.clusterDimZ };
        float[] sizes = { m_ClusterInfo.cluster_SizeX, m_ClusterInfo.cluster_SizeY, m_ClusterInfo.cluster_SizeZ };

        m_ComputeShader.SetInts(ShaderIDs.ClusterCB_GridDim, gridDims);
        m_ComputeShader.SetFloats(ShaderIDs.ClusterCB_Size, sizes);
        m_ComputeShader.SetVector(ShaderIDs.ClusterCB_ScreenDimensions, m_ClusterInfo.ScreenDimensions);
    }

    //预计算视锥体AABB
    void ClusterGenerate(ref CommandBuffer commandBuffer, ref RenderingData renderingData)
    {
        if (m_ComputeShader == null)
            return;

        UpdateClusterBuffer(ref renderingData);

        int threadGroups = Mathf.CeilToInt(m_ClusterInfo.clusterDimXYZ / ClusterBasedLightDefine.MAX_NUM_POINT_LIGHT);
        commandBuffer.SetComputeBufferParam(m_ComputeShader, m_KernelOfClusterAABB, "RWClusterAABBMins", m_ClusterAABBMinBuffer);
        commandBuffer.SetComputeBufferParam(m_ComputeShader, m_KernelOfClusterAABB, "RWClusterAABBMaxs", m_ClusterAABBMaxBuffer);
        var projectionMatrix = renderingData.cameraData.GetGPUProjectionMatrix();
        var projectionMatrixInvers = projectionMatrix.inverse;
        //cmd.SetComputeMatrixParam(m_ComputeShader, ShaderIDs.InverseProjectionMatrix, projectionMatrix);
        commandBuffer.SetComputeMatrixParam(m_ComputeShader, ShaderIDs.InverseProjectionMatrix, projectionMatrixInvers);
        if (threadGroups > 0)
            commandBuffer.DispatchCompute(m_ComputeShader, m_KernelOfClusterAABB, threadGroups, 1, 1);
    }

    //光源求交
    void AssignLightsToClusts(ref CommandBuffer commandBuffer, ref CameraData cameraData)
    {
        int width = cameraData.cameraTargetDescriptor.width;
        int height = cameraData.cameraTargetDescriptor.height;
        // Debug.LogError("Point Light Count:" + m_PointLightPosRangeList.Count);
        //output
        commandBuffer.SetComputeBufferParam(m_ComputeShader, m_kernelAssignLightsToClusters, "RWPointLightIndexCounter_Cluster", m_ClusterPointLightIndexCounterBuffer);
        commandBuffer.SetComputeBufferParam(m_ComputeShader, m_kernelAssignLightsToClusters, "RWLightAssignTable", m_AssignTableBuffer);
        commandBuffer.SetComputeBufferParam(m_ComputeShader, m_kernelAssignLightsToClusters, "RWPointLightIndexList_Cluster", m_ClusterPointLightIndexListBuffer);
        //Input
        commandBuffer.SetComputeBufferParam(m_ComputeShader, m_kernelAssignLightsToClusters, "PointLights", m_PointLightBuffer);
        commandBuffer.SetComputeBufferParam(m_ComputeShader, m_kernelAssignLightsToClusters, "ClusterAABBMins", m_ClusterAABBMinBuffer);
        commandBuffer.SetComputeBufferParam(m_ComputeShader, m_kernelAssignLightsToClusters, "ClusterAABBMaxs", m_ClusterAABBMaxBuffer);

        //初始化数据
        commandBuffer.SetComputeBufferData(m_ClusterPointLightIndexCounterBuffer, m_UCounter);
        commandBuffer.SetComputeBufferData(m_AssignTableBuffer, m_Vec2Girds);

        //Input
        commandBuffer.SetComputeIntParams(m_ComputeShader, "PointLightCount", m_PointLightPosRangeList.Count);
        if (ClusterBasedLightingRenderFeature.UpdateDebugPos)
            commandBuffer.SetComputeMatrixParam(m_ComputeShader, "_CameraLastViewMatrix", cameraData.camera.transform.localToWorldMatrix.inverse);

        commandBuffer.SetComputeMatrixParam(m_ComputeShader, "_W2CMatrix", cameraData.GetViewMatrix().inverse);

        //Debug.LogError((cameraData.camera.worldToCameraMatrix * m_PointLightPosRangeList[0].Position));
        //Debug.LogError("相机矩阵" + cameraData.camera.transform.localToWorldMatrix);
        //Debug.LogError("V" + cameraData.camera.worldToCameraMatrix);
        // commandBuffer.DispatchCompute(m_ComputeShader, m_kernelAssignLightsToClusters, m_ClusterInfo.clusterDimZ, 1, 1);
        if (m_ClusterInfo.clusterDimXYZ > 0)
            commandBuffer.DispatchCompute(m_ComputeShader, m_kernelAssignLightsToClusters, m_ClusterInfo.clusterDimX, m_ClusterInfo.clusterDimY, m_ClusterInfo.clusterDimZ);
    }




    void SetShaderParameters(ref CameraData cameraData)
    {
        //设置全部参数 用于shading
        Shader.SetGlobalInt(ShaderIDs.Cluster_GridCountX, m_ClusterInfo.clusterDimX);
        Shader.SetGlobalInt(ShaderIDs.Cluster_GridCountY, m_ClusterInfo.clusterDimY);
        Shader.SetGlobalInt(ShaderIDs.Cluster_GridCountZ, m_ClusterInfo.clusterDimZ);
        // Shader.SetGlobalFloat(ShaderIDs.Cluster_ViewNear, m_ClusterInfo.zNear);
        Shader.SetGlobalFloat(ShaderIDs.Cluster_SizeX, m_ClusterInfo.cluster_SizeX);
        Shader.SetGlobalFloat(ShaderIDs.Cluster_SizeY, m_ClusterInfo.cluster_SizeY);
        Shader.SetGlobalFloat(ShaderIDs.Cluster_SizeZ, m_ClusterInfo.cluster_SizeZ);
        // Shader.SetGlobalFloat(ShaderIDs.Cluster_LogGridDimY, m_ClusterInfo.logDimY);
        Shader.SetGlobalBuffer(ShaderIDs.Cluster_AssignTable, m_AssignTableBuffer);
        Shader.SetGlobalBuffer(ShaderIDs.Cluster_PointLightBuffer, m_PointLightBuffer);
        Shader.SetGlobalBuffer(ShaderIDs.Cluster_LightAssignTable, m_ClusterPointLightIndexListBuffer);
        Shader.SetGlobalMatrix("_CameraWorldMatrix", cameraData.camera.transform.localToWorldMatrix);
    }

    void LogDebug()
    {
        // Debug.LogError("CS======================");

        // //检查计算结果是否与CS计算的一致
        // Vector3[] tempMin = new Vector3[m_ClusterInfo.clusterDimXYZ];
        // Vector3[] tempMax = new Vector3[m_ClusterInfo.clusterDimXYZ];
        // m_ClusterAABBMinBuffer.GetData(tempMin);
        // m_ClusterAABBMaxBuffer.GetData(tempMax);
        // for (int i = 20; i < 40; i++)
        // {
        //     Debug.LogError(tempMin[i] + "===" + tempMax[i]);
        // }

        // uint[] lightIndex = new uint[m_ClusterInfo.clusterDimXYZ * ClusterBasedLightDefine.AVERAGE_LIGHTS_PER_CLUSTER];
        // m_ClusterPointLightIndexListBuffer.GetData(lightIndex);
        // for (int i = 0; i < 10; i++)
        // {

        //     Debug.LogError("CS:" + i + "---" + lightIndex[i]);
        // }

        // for (int i = 0; i < AVERAGE_LIGHTS_PER_CLUSTER * m_ClusterInfo.clusterDimXYZ; i++)
        // {
        //     if (m_PointLightIndex[i] > 0)
        //     {
        //         Debug.LogError(i + "==" + m_PointLightIndex[i]);
        //     }
        // }
    }

    public void Release()
    {
        m_ClusterAABBMinBuffer?.Dispose();
        m_ClusterAABBMaxBuffer?.Dispose();
        m_PointLightBuffer?.Dispose();
        m_ClusterPointLightIndexCounterBuffer?.Dispose();
        m_AssignTableBuffer?.Dispose();
        m_ClusterPointLightIndexListBuffer?.Dispose();

        m_ClusterAABBMinBuffer = null;
        m_ClusterAABBMaxBuffer = null;
        m_PointLightBuffer = null;
        m_ClusterPointLightIndexCounterBuffer = null;
        m_AssignTableBuffer = null;
        m_ClusterPointLightIndexListBuffer = null;
        m_ComputeShader = null;
        m_Init = false;
    }
}

