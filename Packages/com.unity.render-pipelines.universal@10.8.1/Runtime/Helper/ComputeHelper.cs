using UnityEngine;
using UnityEngine.Experimental.Rendering;
using System.Collections.Generic;


namespace UnityEngine.Rendering.Universal
{
    //ComputeBuffer 的一些工具方法
    public static class ComputeHelper
    {
        public const FilterMode defaultFilterMode = FilterMode.Bilinear;
        public const GraphicsFormat defaultGraphicsFormat = GraphicsFormat.R32G32B32A32_SFloat;

        public static Vector3Int GetThreadGroupSizes(ComputeShader compute, int kernelIndex = 0)
        {
            uint x, y, z;
            compute.GetKernelThreadGroupSizes(kernelIndex, out x, out y, out z);
            return new Vector3Int((int)x, (int)y, (int)z);
        }

        /// Convenience method for dispatching a compute shader.
        /// It calculates the number of thread groups based on the number of iterations needed.
        public static void Dispatch(ComputeShader cs, int numIterationsX, int numIterationsY = 1, int numIterationsZ = 1, int kernelIndex = 0)
        {
            Vector3Int threadGroupSizes = GetThreadGroupSizes(cs, kernelIndex);
            int numGroupsX = Mathf.CeilToInt(numIterationsX / (float)threadGroupSizes.x);
            int numGroupsY = Mathf.CeilToInt(numIterationsY / (float)threadGroupSizes.y);
            int numGroupsZ = Mathf.CeilToInt(numIterationsZ / (float)threadGroupSizes.y);
            cs.Dispatch(kernelIndex, numGroupsX, numGroupsY, numGroupsZ);
        }

        /// Convenience method for dispatching a compute shader.
        /// It calculates the number of thread groups based on the size of the given texture.
        public static void Dispatch(ComputeShader cs, RenderTexture texture, int kernelIndex = 0)
        {
            Vector3Int threadGroupSizes = GetThreadGroupSizes(cs, kernelIndex);
            Dispatch(cs, texture.width, texture.height, texture.volumeDepth, kernelIndex);
        }

        public static void Dispatch(ComputeShader cs, Texture2D texture, int kernelIndex = 0)
        {
            Vector3Int threadGroupSizes = GetThreadGroupSizes(cs, kernelIndex);
            Dispatch(cs, texture.width, texture.height, 1, kernelIndex);
        }

        public static int GetStride<T>()
        {
            return System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));
        }

        public static ComputeBuffer CreateAppendBuffer<T>(int capacity)
        {
            int stride = GetStride<T>();
            ComputeBuffer buffer = new ComputeBuffer(capacity, stride, ComputeBufferType.Append);
            buffer.SetCounterValue(0);
            return buffer;
        }

        public static void CreateStructuredBuffer<T>(ref ComputeBuffer buffer, int count)
        {
            int stride = GetStride<T>();
            bool createNewBuffer = buffer == null || !buffer.IsValid() || buffer.count != count || buffer.stride != stride;
            if (createNewBuffer)
            {
                Release(buffer);
                buffer = new ComputeBuffer(count, stride, ComputeBufferType.Structured);
            }
        }

        public static ComputeBuffer CreateStructuredBuffer<T>(T[] data)
        {
            var buffer = new ComputeBuffer(data.Length, GetStride<T>());
            buffer.SetData(data);
            return buffer;
        }

        public static ComputeBuffer CreateStructuredBuffer<T>(List<T> data) where T : struct
        {
            var buffer = new ComputeBuffer(data.Count, GetStride<T>());
            buffer.SetData<T>(data);
            return buffer;
        }
        public static ComputeBuffer CreateStructuredBuffer<T>(int count)
        {
            return new ComputeBuffer(count, GetStride<T>());
        }

        public static void CreateStructuredBuffer<T>(ref ComputeBuffer buffer, T[] data)
        {
            CreateStructuredBuffer<T>(ref buffer, data.Length);
            buffer.SetData(data);
        }
        public static ComputeBuffer CreateAndSetBuffer<T>(T[] data, ComputeShader cs, string nameID, int kernelIndex = 0)
        {
            ComputeBuffer buffer = null;
            CreateAndSetBuffer<T>(ref buffer, data, cs, nameID, kernelIndex);
            return buffer;
        }
        public static void CreateAndSetBuffer<T>(ref ComputeBuffer buffer, T[] data, ComputeShader cs, string nameID, int kernelIndex = 0)
        {
            int stride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));
            CreateStructuredBuffer<T>(ref buffer, data.Length);
            buffer.SetData(data);
            cs.SetBuffer(kernelIndex, nameID, buffer);
        }
        public static ComputeBuffer CreateAndSetBuffer<T>(int length, ComputeShader cs, string nameID, int kernelIndex = 0)
        {
            ComputeBuffer buffer = null;
            CreateAndSetBuffer<T>(ref buffer, length, cs, nameID, kernelIndex);
            return buffer;
        }
        public static void CreateAndSetBuffer<T>(ref ComputeBuffer buffer, int length, ComputeShader cs, string nameID, int kernelIndex = 0)
        {
            CreateStructuredBuffer<T>(ref buffer, length);
            cs.SetBuffer(kernelIndex, nameID, buffer);
        }
        // Read data in append buffer to array
        // Note: this is very slow as it reads the data from the GPU to the CPU
        public static T[] ReadDataFromBuffer<T>(ComputeBuffer buffer, bool isAppendBuffer)
        {
            int numElements = buffer.count;
            if (isAppendBuffer)
            {
                // Get number of elements in append buffer
                ComputeBuffer sizeBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
                ComputeBuffer.CopyCount(buffer, sizeBuffer, 0);
                int[] bufferCountData = new int[1];
                sizeBuffer.GetData(bufferCountData);
                numElements = bufferCountData[0];
                Release(sizeBuffer);
            }
            // Read data from append buffer
            T[] data = new T[numElements];
            buffer.GetData(data);
            return data;
        }
        public static void ResetAppendBuffer(ComputeBuffer appendBuffer)
        {
            appendBuffer.SetCounterValue(0);
        }
        /// Releases supplied buffer/s if not null
        public static void Release(params ComputeBuffer[] buffers)
        {
            for (int i = 0; i < buffers.Length; i++)
            {
                if (buffers[i] != null)
                {
                    buffers[i].Release();
                }
            }
        }
        /// Releases supplied render textures/s if not null
        public static void Release(params RenderTexture[] textures)
        {
            for (int i = 0; i < textures.Length; i++)
            {
                if (textures[i] != null)
                {
                    textures[i].Release();
                }
            }
        }

        // ------ Texture Helpers ------
        public static RenderTexture CreateRenderTexture(RenderTexture template)
        {
            RenderTexture renderTexture = null;
            CreateRenderTexture(ref renderTexture, template);
            return renderTexture;
        }

        public static void CreateRenderTexture(ref RenderTexture texture, RenderTexture template)
        {
            if (texture != null)
            {
                texture.Release();
            }
            texture = new RenderTexture(template.descriptor);
            texture.enableRandomWrite = true;
            texture.Create();
        }


        public static void CreateRenderTexture3D(ref RenderTexture texture, RenderTexture template)
        {
            CreateRenderTexture(ref texture, template);
        }
        public static void CreateRenderTexture3D(ref RenderTexture texture, int size, GraphicsFormat format, TextureWrapMode wrapMode = TextureWrapMode.Repeat, string name = "Untitled", bool mipmaps = false)
        {
            if (texture == null || !texture.IsCreated() || texture.width != size || texture.height != size || texture.volumeDepth != size || texture.graphicsFormat != format)
            {
                //Debug.Log ("Create tex: update noise: " + updateNoise);
                if (texture != null)
                {
                    texture.Release();
                }
                const int numBitsInDepthBuffer = 0;
                texture = new RenderTexture(size, size, numBitsInDepthBuffer);
                texture.graphicsFormat = format;
                texture.volumeDepth = size;
                texture.enableRandomWrite = true;
                texture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
                texture.useMipMap = mipmaps;
                texture.autoGenerateMips = false;
                texture.Create();
            }
            texture.wrapMode = wrapMode;
            texture.filterMode = FilterMode.Bilinear;
            texture.name = name;
        }
        /// Copy the contents of one render texture into another. Assumes textures are the same size.
        public static void CopyRenderTexture(Texture source, RenderTexture target)
        {
            Graphics.Blit(source, target);
        }

        // ------ Instancing Helpers
        // Create args buffer for instanced indirect rendering
        public static ComputeBuffer CreateArgsBuffer(Mesh mesh, int numInstances)
        {
            const int subMeshIndex = 0;
            uint[] args = new uint[5];
            args[0] = (uint)mesh.GetIndexCount(subMeshIndex);
            args[1] = (uint)numInstances;
            args[2] = (uint)mesh.GetIndexStart(subMeshIndex);
            args[3] = (uint)mesh.GetBaseVertex(subMeshIndex);
            args[4] = 0; // offset
            ComputeBuffer argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
            argsBuffer.SetData(args);
            return argsBuffer;
        }
        public static void CreateArgsBuffer(ref ComputeBuffer argsBuffer, Mesh mesh, int numInstances)
        {
            Release(argsBuffer);
            argsBuffer = CreateArgsBuffer(mesh, numInstances);
        }
        // Create args buffer for instanced indirect rendering (number of instances comes from size of append buffer)
        public static ComputeBuffer CreateArgsBuffer(Mesh mesh, ComputeBuffer appendBuffer)
        {
            ComputeBuffer argsBuffer = CreateArgsBuffer(mesh, 0);
            SetArgsBufferCount(argsBuffer, appendBuffer);
            return argsBuffer;
        }
        public static void SetArgsBufferCount(ComputeBuffer argsBuffer, ComputeBuffer appendBuffer)
        {
            ComputeBuffer.CopyCount(appendBuffer, argsBuffer, sizeof(uint));
        }
    }
}
