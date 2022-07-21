using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class ShaderDefine
{
    // Properties
    public static readonly int CAMERA_COLOR_TEXTURE = Shader.PropertyToID("_CameraColorTexture");
    public static readonly int CAMERA_DEPTH_TEXTURE = Shader.PropertyToID("_CameraDepthTexture");

    // Keywords



    // Custom Render Queues
    public const int OPAQUE_RENDER_QUEUE = 2500;
    // public const int ALPHA_TEST_RENDER_QUEUE = 2450;
    public const int TRANSPARENT_QUEUE = 5000;

    // Render Queue Ranges
    public static readonly RenderQueueRange OPAQUE_RENDER_QUEUE_RANGE = new RenderQueueRange(0, OPAQUE_RENDER_QUEUE);//0-2500
    public static readonly RenderQueueRange TRANSPARENT_RENDER_QUEUE_RANGE = new RenderQueueRange(OPAQUE_RENDER_QUEUE + 1, TRANSPARENT_QUEUE);//2501-5000
}
