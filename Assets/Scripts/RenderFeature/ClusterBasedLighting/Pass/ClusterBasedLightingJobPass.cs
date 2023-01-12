using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;

public class ClusterBasedLightingJobPass : ScriptableRenderPass
{
    const int CLUSTER_GRID_BLOCK_SIZE_XY = 64;//单个Block像素大小
    const int CLUSTER_GRID_BLOCK_SIZE_Z = 5;//单个Block像素大小

    ClusterBasedLightingRenderFeature.Settings m_Settings;
    ClusterInfo m_ClusterInfo;

    int m_OldWidth = -1;
    int m_OldHeight = -1;

    NativeArray<Vector3> m_ClusterAABBMinArray;
    NativeArray<Vector3> m_ClusterAABBMaxArray;

    bool m_Init = false;

    public NativeArray<Vector3> clusterAABBMinArray => m_ClusterAABBMinArray;
    public NativeArray<Vector3> clusterAABBMaxArray => m_ClusterAABBMaxArray;
    public ClusterInfo clusterInfo => m_ClusterInfo;

    public ClusterBasedLightingJobPass(ClusterBasedLightingRenderFeature.Settings settings)
    {
        m_Settings = settings;
        renderPassEvent = RenderPassEvent.BeforeRendering;
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
            m_ClusterInfo = ClusterInfo.CalcClusterInfo(ref renderingData, CLUSTER_GRID_BLOCK_SIZE_XY, CLUSTER_GRID_BLOCK_SIZE_Z);

            if (m_ClusterAABBMinArray.IsCreated)
                m_ClusterAABBMinArray.Dispose();
            m_ClusterAABBMinArray = new NativeArray<Vector3>(m_ClusterInfo.clusterDimXYZ, Allocator.Persistent);

            if (m_ClusterAABBMaxArray.IsCreated)
                m_ClusterAABBMaxArray.Dispose();
            m_ClusterAABBMaxArray = new NativeArray<Vector3>(m_ClusterInfo.clusterDimXYZ, Allocator.Persistent);

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


        //DebugDraw
        // if (ClusterBasedLightingRenderFeature.UpdateDebugPos)
        //     m_ClusterDebugMaterial.SetMatrix("_CameraWorldMatrix", renderingData.cameraData.camera.transform.localToWorldMatrix);

        // commandBuffer.DrawMeshInstancedIndirect(CubeMesh, 0, m_ClusterDebugMaterial, 0, m_DrawDebugClusterBuffer, 0);

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
    }
}
