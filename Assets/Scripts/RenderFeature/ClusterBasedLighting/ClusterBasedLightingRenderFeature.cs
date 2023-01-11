using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class ClusterBasedLightingRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public ComputeShader mainComputeShader;

        [Header("--------Debug-------")]
        public bool enableDebug;
        public Color debugColor = new Color(1, 1, 1, 0.2f);
    }

    public Settings settings;

    ClusterBasedLightingPass m_ClusterBasedLightingPass;
    ClusterBasedLightingDebugPass m_DebugPass;

    public override void Create()
    {
        if (settings.mainComputeShader)
        {
            if (m_ClusterBasedLightingPass != null)
                m_ClusterBasedLightingPass.Release();

            m_ClusterBasedLightingPass = new ClusterBasedLightingPass(settings.mainComputeShader);
        }

        if (m_DebugPass != null)
            m_DebugPass.Release();
        m_DebugPass = new ClusterBasedLightingDebugPass(settings);
    }


    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_ClusterBasedLightingPass != null)
        {
            m_ClusterBasedLightingPass.SetupLights(ref renderingData);
            renderer.EnqueuePass(m_ClusterBasedLightingPass);
        }


        if (settings.enableDebug && m_DebugPass != null && m_ClusterBasedLightingPass != null)
        {
            m_DebugPass.Setup(m_ClusterBasedLightingPass.clusterAABBMinBuffer, m_ClusterBasedLightingPass.clusterAABBMaxBuffer, m_ClusterBasedLightingPass.assignTableBuffer, m_ClusterBasedLightingPass.clusterInfo.clusterDimXYZ);
            renderer.EnqueuePass(m_DebugPass);
        }
    }


    private void OnDisable()
    {
        if (m_ClusterBasedLightingPass != null)
            m_ClusterBasedLightingPass.Release();

        if (m_DebugPass != null)
            m_DebugPass.Release();
    }
}
