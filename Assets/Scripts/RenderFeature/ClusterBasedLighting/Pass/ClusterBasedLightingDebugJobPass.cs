using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;

public class ClusterBasedLightingDebugJobPass : ScriptableRenderPass
{
    ProfilingSampler m_ProfilingSampler;
    ClusterBasedLightingRenderFeature.Settings m_Setting;

    ComputeBuffer m_DrawDebugClusterArgsBuffer;
    ComputeBuffer m_ClusterAABBMinBuffer;
    ComputeBuffer m_ClusterAABBMaxBuffer;
    NativeArray<Vector3> m_ClusterAABBMinArray;
    NativeArray<Vector3> m_ClusterAABBMaxArray;
    Material m_ClusterDebugMaterial = null;
    int m_OldCount;



    public ClusterBasedLightingDebugJobPass(ClusterBasedLightingRenderFeature.Settings settings)
    {
        renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        m_ProfilingSampler = new ProfilingSampler("ClusterBasedLights_DebugJob");
        m_Setting = settings;

        var shader = Shader.Find("Hidden/ClusterBasedLighting/DebugClusterAABB");
        if (shader != null)
            m_ClusterDebugMaterial = new Material(shader);
    }

    public void Setup(int count, NativeArray<Vector3> clusterAABBMinArray, NativeArray<Vector3> clusterAABBMaxArray)
    {
        if (m_ClusterDebugMaterial == null || count <= 0)
            return;

        if (m_DrawDebugClusterArgsBuffer == null || m_OldCount != count)
        {
            m_OldCount = count;
            if (m_DrawDebugClusterArgsBuffer != null) m_DrawDebugClusterArgsBuffer.Release();
            m_DrawDebugClusterArgsBuffer = ComputeHelper.CreateArgsBuffer(ClusterBasedLightingDebugPass.CubeMesh, count);

            if (m_ClusterAABBMinBuffer != null) m_ClusterAABBMinBuffer.Release();
            if (m_ClusterAABBMaxBuffer != null) m_ClusterAABBMaxBuffer.Release();
            m_ClusterAABBMinBuffer = ComputeHelper.CreateStructuredBuffer<Vector3>(count);
            m_ClusterAABBMaxBuffer = ComputeHelper.CreateStructuredBuffer<Vector3>(count);
        }

        if (!m_ClusterAABBMinArray.IsCreated || m_ClusterAABBMinArray.Length != clusterAABBMinArray.Length)
        {
            if (m_ClusterAABBMinArray.IsCreated)
                m_ClusterAABBMinArray.Dispose();
            m_ClusterAABBMinArray = new NativeArray<Vector3>(clusterAABBMinArray.Length, Allocator.Persistent);
        }

        if (!m_ClusterAABBMaxArray.IsCreated || m_ClusterAABBMaxArray.Length != clusterAABBMaxArray.Length)
        {
            if (m_ClusterAABBMaxArray.IsCreated)
                m_ClusterAABBMaxArray.Dispose();
            m_ClusterAABBMaxArray = new NativeArray<Vector3>(clusterAABBMaxArray.Length, Allocator.Persistent);
        }

        for (int i = 0; i < m_ClusterAABBMinArray.Length; i++)
        {
            m_ClusterAABBMinArray[i] = clusterAABBMinArray[i];
        }

        for (int i = 0; i < m_ClusterAABBMinArray.Length; i++)
        {
            m_ClusterAABBMaxArray[i] = clusterAABBMaxArray[i];
        }

        if (m_ClusterAABBMinArray.Length > 0)
            m_ClusterAABBMinBuffer.SetData(m_ClusterAABBMinArray);
        if (m_ClusterAABBMaxArray.Length > 0)
            m_ClusterAABBMaxBuffer.SetData(m_ClusterAABBMaxArray);

        m_ClusterDebugMaterial.SetBuffer("ClusterAABBMins", m_ClusterAABBMinBuffer);
        m_ClusterDebugMaterial.SetBuffer("ClusterAABBMaxs", m_ClusterAABBMaxBuffer);
        m_ClusterDebugMaterial.SetColor("_DebugColor", m_Setting.debugColor);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (m_ClusterDebugMaterial == null)
            return;

        CommandBuffer cmd = CommandBufferPool.Get();
        using (new ProfilingScope(cmd, m_ProfilingSampler))
        {
            DebugCluster(ref cmd, ref renderingData.cameraData);
        }
        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    void DebugCluster(ref CommandBuffer commandBuffer, ref CameraData cameraData)
    {
        if (m_DrawDebugClusterArgsBuffer == null)
            return;

        if (ClusterBasedLightingRenderFeature.UpdateDebugPos)
            m_ClusterDebugMaterial.SetMatrix("_CameraWorldMatrix", cameraData.camera.transform.localToWorldMatrix);

        commandBuffer.DrawMeshInstancedIndirect(ClusterBasedLightingDebugPass.CubeMesh, 0, m_ClusterDebugMaterial, 0, m_DrawDebugClusterArgsBuffer, 0);
    }

    public void Release()
    {
        if (m_DrawDebugClusterArgsBuffer != null)
        {
            m_DrawDebugClusterArgsBuffer.Release();
            m_DrawDebugClusterArgsBuffer = null;
        }

        if (m_ClusterAABBMinArray.IsCreated)
            m_ClusterAABBMinArray.Dispose();
        if (m_ClusterAABBMaxArray.IsCreated)
            m_ClusterAABBMaxArray.Dispose();
    }
}
