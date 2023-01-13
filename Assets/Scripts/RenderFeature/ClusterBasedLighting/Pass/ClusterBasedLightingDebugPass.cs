using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;

public class ClusterBasedLightingDebugPass : ScriptableRenderPass
{
    ProfilingSampler m_ProfilingSampler;
    ComputeBuffer m_DrawDebugClusterBuffer;
    Material m_ClusterDebugMaterial = null;
    int m_OldCount;


    ClusterBasedLightingRenderFeature.Settings m_Setting;

    public ClusterBasedLightingDebugPass(ClusterBasedLightingRenderFeature.Settings settings)
    {
        renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        m_ProfilingSampler = new ProfilingSampler("ClusterBasedLights_Debug");
        m_Setting = settings;

        var shader = Shader.Find("Hidden/ClusterBasedLighting/DebugClusterAABB");
        if (shader != null)
            m_ClusterDebugMaterial = new Material(shader);


    }

    public void Setup(ComputeBuffer clusterAABBMinBuffer, ComputeBuffer clusterAABBMaxBuffer, ComputeBuffer assignTableBuffer, int count)
    {
        if (m_ClusterDebugMaterial == null)
            return;

        m_ClusterDebugMaterial.SetBuffer("ClusterAABBMins", clusterAABBMinBuffer);
        m_ClusterDebugMaterial.SetBuffer("ClusterAABBMaxs", clusterAABBMaxBuffer);
        m_ClusterDebugMaterial.SetBuffer("LightAssignTable", assignTableBuffer);
        m_ClusterDebugMaterial.SetColor("_DebugColor", m_Setting.debugColor);
        m_ClusterDebugMaterial.SetFloat("_Scale", m_Setting.debugScale);

        if (m_DrawDebugClusterBuffer == null || m_OldCount != count)
        {
            m_OldCount = count;
            if (m_DrawDebugClusterBuffer != null)
                m_DrawDebugClusterBuffer.Release();
            m_DrawDebugClusterBuffer = ComputeHelper.CreateArgsBuffer(CubeMesh, count);
        }
    }

    void DebugCluster(ref CommandBuffer commandBuffer, ref CameraData cameraData)
    {
        if (ClusterBasedLightingRenderFeature.UpdateDebugPos)
            m_ClusterDebugMaterial.SetMatrix("_CameraWorldMatrix", cameraData.camera.transform.localToWorldMatrix);

        commandBuffer.DrawMeshInstancedIndirect(CubeMesh, 0, m_ClusterDebugMaterial, 0, m_DrawDebugClusterBuffer, 0);
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

    public void Release()
    {
        m_DrawDebugClusterBuffer?.Dispose();
        m_DrawDebugClusterBuffer = null;
    }

    private static Mesh m_CubeMesh;
    public static Mesh CubeMesh
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
