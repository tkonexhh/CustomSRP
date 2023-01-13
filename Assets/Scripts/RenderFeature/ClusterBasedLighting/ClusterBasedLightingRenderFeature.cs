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
        public bool jobMode = false;

        public ComputeShader mainComputeShader;

        [Header("--------Debug-------")]
        public bool enableDebug;
        public Color debugColor = new Color(1, 1, 1, 0.2f);
        [Range(0, 1)]
        public float debugScale = 0.5f;
    }

    public Settings settings;

    ClusterBasedLightingPass m_ClusterBasedLightingPass;
    ClusterBasedLightingJobPass m_ClusterBasedLightingJobPass;


#if UNITY_EDITOR
    ClusterBasedLightingDebugPass m_DebugPass;
    ClusterBasedLightingDebugJobPass m_DebugJobPass;
#endif

    public static bool UpdateDebugPos;

    public override void Create()
    {
        if (settings.mainComputeShader)
        {
            if (m_ClusterBasedLightingPass != null)
                m_ClusterBasedLightingPass.Release();
            m_ClusterBasedLightingPass = new ClusterBasedLightingPass(settings);
        }

        if (settings.jobMode)
        {
            if (m_ClusterBasedLightingJobPass != null)
                m_ClusterBasedLightingJobPass.Release();
            m_ClusterBasedLightingJobPass = new ClusterBasedLightingJobPass(settings);
        }

#if UNITY_EDITOR
        if (m_DebugPass != null)
            m_DebugPass.Release();
        m_DebugPass = new ClusterBasedLightingDebugPass(settings);

        if (m_DebugJobPass != null)
            m_DebugJobPass.Release();
        m_DebugJobPass = new ClusterBasedLightingDebugJobPass(settings);
#endif
    }


    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {

        if (!settings.jobMode)
        {
            if (m_ClusterBasedLightingPass != null)
            {
                m_ClusterBasedLightingPass.SetupLights(ref renderingData);
                renderer.EnqueuePass(m_ClusterBasedLightingPass);
            }
        }
        else
        {
            if (settings.jobMode && m_ClusterBasedLightingJobPass != null)
            {
                m_ClusterBasedLightingJobPass.SetupLights(ref renderingData);
                renderer.EnqueuePass(m_ClusterBasedLightingJobPass);
            }
        }


#if UNITY_EDITOR
        //Debug
        if (settings.enableDebug)
        {
            if (!settings.jobMode)
            {
                if (m_DebugPass != null && m_ClusterBasedLightingPass != null)
                {
                    m_DebugPass.Setup(m_ClusterBasedLightingPass.clusterAABBMinBuffer, m_ClusterBasedLightingPass.clusterAABBMaxBuffer, m_ClusterBasedLightingPass.assignTableBuffer, m_ClusterBasedLightingPass.clusterInfo.clusterDimXYZ);
                    renderer.EnqueuePass(m_DebugPass);
                }
            }
            else
            {
                if (m_DebugJobPass != null && m_ClusterBasedLightingJobPass != null)
                {
                    m_DebugJobPass.Setup(m_ClusterBasedLightingJobPass.clusterInfo.clusterDimXYZ, m_ClusterBasedLightingJobPass.clusterAABBMinArray, m_ClusterBasedLightingJobPass.clusterAABBMaxArray, m_ClusterBasedLightingJobPass.assignTableBuffer);
                    renderer.EnqueuePass(m_DebugJobPass);
                }
            }
        }

#endif


    }


    private void OnDisable()
    {
        if (m_ClusterBasedLightingPass != null)
            m_ClusterBasedLightingPass.Release();

        if (m_ClusterBasedLightingJobPass != null)
            m_ClusterBasedLightingJobPass.Release();

#if UNITY_EDITOR
        if (m_DebugPass != null)
            m_DebugPass.Release();

        if (m_DebugJobPass != null)
            m_DebugJobPass.Release();
#endif
    }
}
