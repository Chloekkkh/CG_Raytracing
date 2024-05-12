using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using static UnityEngine.Mathf;

public static class ShaderToolkit
{
    // Depth options for RenderTextures
    public enum DepthMode { None = 0, Depth16 = 16, Depth24 = 24 }
    // Standard graphics format for RenderTextures
    public static readonly GraphicsFormat RGBA_SFloat = GraphicsFormat.R32G32B32A32_SFloat;

    #region ComputeShaders
    // Dispatches a Compute Shader, configuring the number of thread groups based on input dimensions and kernel thread group sizes.
    public static void DispatchComputeShader(ComputeShader computeShader, int iterationsX, int iterationsY = 1, int iterationsZ = 1, int kernelIndex = 0)
    {
        Vector3Int threadGroupSizes = GetKernelThreadGroupSizes(computeShader, kernelIndex);
        // Calculate the number of groups for each dimension, ensuring at least one group is dispatched.
        int numGroupsX = Mathf.CeilToInt(iterationsX / (float)threadGroupSizes.x);
        int numGroupsY = Mathf.CeilToInt(iterationsY / (float)threadGroupSizes.y);
        int numGroupsZ = Mathf.CeilToInt(iterationsZ / (float)threadGroupSizes.z);

        // Ensure at least one group in each dimension
        numGroupsX = Mathf.Max(numGroupsX, 1);
        numGroupsY = Mathf.Max(numGroupsY, 1);
        numGroupsZ = Mathf.Max(numGroupsZ, 1);

        computeShader.Dispatch(kernelIndex, numGroupsX, numGroupsY, numGroupsZ);
    }

    // Retrieves the number of threads per group that a compute shader kernel can use.
    public static Vector3Int GetKernelThreadGroupSizes(ComputeShader computeShader, int kernelIndex = 0)
    {
        computeShader.GetKernelThreadGroupSizes(kernelIndex, out uint x, out uint y, out uint z);
        return new Vector3Int((int)x, (int)y, (int)z);
    }
    #endregion

    #region Create Buffers
    // Configures or re-creates a Compute Buffer if necessary, based on type and count requirements.
    public static void ConfigureComputeBuffer<T>(ref ComputeBuffer buffer, int count) where T : struct
    {
        if (count <= 0)
        {
            throw new ArgumentException("Count must be greater than zero.", nameof(count));
        }

        int stride = GetStride<T>();
        // Check if buffer needs to be recreated based on its current state and desired configuration.
        bool needsRecreation = buffer == null || !buffer.IsValid() || buffer.count != count || buffer.stride != stride;

        if (needsRecreation)
        {
            Release(buffer); // Release the existing buffer before creating a new one.
            buffer = new ComputeBuffer(count, stride, ComputeBufferType.Structured);
        }
    }

    // Calculates the stride (size in bytes) of any type T, typically used for buffer allocations.
    public static int GetStride<T>() where T : struct
    {
        return System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));
    }

    // Creates a Compute Buffer capable of holding a specified number of elements of type T.
    public static ComputeBuffer CreateComputeBuffer<T>(int count) where T : struct
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than zero.");

        return new ComputeBuffer(count, GetStride<T>());
    }

    // Initializes and fills a Compute Buffer with data from a list, recreating the buffer if its current configuration doesn't match the data requirements.
    public static void SetupAndFillComputeBuffer<T>(ref ComputeBuffer buffer, List<T> data) where T : struct
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data), "Data list cannot be null.");
        }

        int length = Max(1, data.Count);  // Ensuring a minimum buffer length of 1.
        int stride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));

        // Recreate the buffer if necessary based on its current state and the data requirements.
        bool needsRecreation = buffer == null || !buffer.IsValid() || buffer.count != length || buffer.stride != stride;
        if (needsRecreation)
        {
            buffer?.Release();  // Safe release if the buffer already exists.
            buffer = new ComputeBuffer(length, stride, ComputeBufferType.Structured);
        }

        buffer.SetData(data);
    }
    #endregion

    #region Create Textures
    // Creates a RenderTexture based on a template, copying its settings and initializing a new instance.
    public static RenderTexture CreateRenderTextureFromTemplate(RenderTexture template)
    {
        if (template == null)
        {
            throw new ArgumentNullException(nameof(template), "Template RenderTexture cannot be null.");
        }

        RenderTexture renderTexture = new RenderTexture(template.descriptor);
        renderTexture.enableRandomWrite = template.enableRandomWrite;
        renderTexture.Create();
        return renderTexture;
    }

    // Sets up a Material to use a specified Shader, creating a new Material if the current one is null or uses a different Shader.
    public static void SetupMaterialShader(Shader shader, ref Material mat)
    {
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Texture");
            Debug.LogWarning("Shader was null; defaulted to Unlit/Texture.");
        }

        if (mat == null || mat.shader != shader)
        {
            mat = new Material(shader);
        }
    }

    // Creates a new RenderTexture with specified properties, ensuring it is correctly configured before returning.
    public static RenderTexture CreateConfiguredRenderTexture(int width, int height, FilterMode filterMode, GraphicsFormat format, string name = "Unnamed", DepthMode depthMode = DepthMode.None, bool useMipMaps = false)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("Width and height must be greater than zero.");
        }

        RenderTexture texture = new RenderTexture(width, height, (int)depthMode, format)
        {
            enableRandomWrite = true,
            autoGenerateMips = false,
            useMipMap = useMipMaps,
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = filterMode
        };

        texture.Create();
        return texture;
    }

    // Ensures that a RenderTexture is properly configured and recreates it if necessary based on the specified parameters.
    public static bool EnsureRenderTextureSetup(ref RenderTexture texture, int width, int height, FilterMode filterMode, GraphicsFormat format, string name = "Unnamed", DepthMode depthMode = DepthMode.None, bool useMipMaps = false)
    {
        // Check if texture is null or has been destroyed
        if (texture == null || !texture.IsCreated() || texture.width != width || texture.height != height || texture.graphicsFormat != format || texture.depth != (int)depthMode || texture.useMipMap != useMipMaps)
        {
            if (texture != null && !texture.IsCreated())
            {
                texture.Release();
            }
            texture = CreateConfiguredRenderTexture(width, height, filterMode, format, name, depthMode, useMipMaps);
            return true;
        }
        else
        {
            // Update properties only if texture is valid
            if (texture != null && texture.IsCreated())
            {
                texture.name = name;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = filterMode;
            }
        }

        return false;
    }
    #endregion

    // Releases a ComputeBuffer if it is not null.
    public static void Release(ComputeBuffer buffer)
    {
        if (buffer != null)
        {
            buffer.Release();
        }
    }

    // Releases multiple ComputeBuffers, ensuring each is not null before releasing.
    public static void Release(params ComputeBuffer[] buffers)
    {
        for (int i = 0; i < buffers.Length; i++)
        {
            Release(buffers[i]);
        }
    }

    // Releases a RenderTexture if it is not null.
    public static void Release(RenderTexture tex)
    {
        if (tex != null)
        {
            tex.Release();
        }
    }

}
