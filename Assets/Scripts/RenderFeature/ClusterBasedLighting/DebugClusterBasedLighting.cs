using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class DebugClusterBasedLighting : MonoBehaviour
{

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.U))
        {
            ClusterBasedLightingRenderFeature.UpdateDebugPos = !ClusterBasedLightingRenderFeature.UpdateDebugPos;

        }
    }

    private void OnEnable()
    {
#if UNITY_EDITOR
        SceneView.duringSceneGui += OnSceneView; //注册场景视图刷新回调
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        SceneView.duringSceneGui -= OnSceneView; //移除场景视图刷新回调
#endif
    }

#if UNITY_EDITOR
    private void OnSceneView(SceneView sceneView)
    {
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.U)
        {
            ClusterBasedLightingRenderFeature.UpdateDebugPos = !ClusterBasedLightingRenderFeature.UpdateDebugPos;
        }
    }
#endif
}
