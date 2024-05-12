using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.Mathf;

[ExecuteAlways, ImageEffectAllowedInSceneView]
public class RayTracingManager : MonoBehaviour
{
    // Limit for the maximum number of triangles a mesh can contain to ensure performance.
    public const int TriangleLimit = 1500;

    [Header("Ray Tracing Settings")]
    [SerializeField, Range(0, 32)] int MaxRayBounce = 4;
    [SerializeField, Range(0, 64)] int RaysPerPixel = 2;
    [SerializeField, Min(0)] float FocusBlurAmount = 0;
    [SerializeField, Min(0)] float RaySpreadFactor = 0.3f;
    [SerializeField, Min(0)] float focusDistance = 1;
    [SerializeField] EnvironmentSettings environmentSettings;

    [Header("View Settings")]
    [SerializeField] bool useShaderInSceneView;
    [Header("References")]
    [SerializeField] Shader rayTracingShader;
    [SerializeField] Shader accumulateShader;

    [Header("Info")]
    [SerializeField] int numRenderedFrames;
    [SerializeField] int numMeshChunks;
    [SerializeField] int numTriangles;

    // Materials and textures used during rendering.
    Material rayTracingMaterial;
    Material accumulateMaterial;
    RenderTexture resultTexture;

    // Compute buffers for storing mesh data for the shader.
    ComputeBuffer sphereBuffer;
    ComputeBuffer triangleBuffer;
    ComputeBuffer meshDataBuffer;

    List<Triangle> allTriangles;
    List<MeshData> allMeshData;

    void Start()
    {
        numRenderedFrames = 0;
    }

    void OnRenderImage(RenderTexture src, RenderTexture target)
    {
        if (IsSceneCamera())
        {
            ProcessSceneCamera(src, target);
        }
        else
        {
            ProcessRayTracingCamera(src, target);
        }
    }

    bool IsSceneCamera()
    {
        return Camera.current.name == "SceneCamera";    // Check if the current camera is the scene camera.
    }

    void ProcessSceneCamera(RenderTexture src, RenderTexture target)
    {
        if (useShaderInSceneView)
        {
            SetupRenderingResources();
            Graphics.Blit(null, target, rayTracingMaterial);    // Render using the ray tracing shader.
        }
        else
        {
            Graphics.Blit(src, target); // Pass-through if not using shader.
        }
    }

    void ProcessRayTracingCamera(RenderTexture src, RenderTexture target)
    {
        SetupRenderingResources();

        // Handle the accumulation of frames to smooth out the final image.
        RenderTexture prevFrameCopy = RenderTexture.GetTemporary(src.width, src.height, 0, ShaderToolkit.RGBA_SFloat);
        Graphics.Blit(resultTexture, prevFrameCopy);

        // Run the ray tracing shader and draw the result to a temporary texture
        RenderTexture currentFrame = RenderTexture.GetTemporary(src.width, src.height, 0, ShaderToolkit.RGBA_SFloat);
        rayTracingMaterial.SetInt("Frame", numRenderedFrames);
        Graphics.Blit(null, currentFrame, rayTracingMaterial);

        // Perform image accumulation
        accumulateMaterial.SetInt("_Frame", numRenderedFrames);
        accumulateMaterial.SetTexture("_PrevFrame", prevFrameCopy);
        Graphics.Blit(currentFrame, resultTexture, accumulateMaterial);

        // Draw the final result to the screen
        Graphics.Blit(resultTexture, target);

        // Release temporary textures
        RenderTexture.ReleaseTemporary(prevFrameCopy);
        RenderTexture.ReleaseTemporary(currentFrame);

        // Increment the frame counter if the application is playing
        numRenderedFrames += Application.isPlaying ? 1 : 0;
    }

    void SetupRenderingResources()
    {
        // Set up the materials and textures for ray tracing.
        ShaderToolkit.SetupMaterialShader(rayTracingShader, ref rayTracingMaterial);
        ShaderToolkit.SetupMaterialShader(accumulateShader, ref accumulateMaterial);
        // Create result render texture
        ShaderToolkit.EnsureRenderTextureSetup(ref resultTexture, Screen.width, Screen.height, FilterMode.Bilinear, ShaderToolkit.RGBA_SFloat, "Result");

        // Update data
        UpdateRayTracingCameraParams(Camera.current);
        SetupRayTracingMeshData();
        SetRayTracingShaderParams();

    }

    void SetRayTracingShaderParams()
    {
        if (rayTracingMaterial == null)
        {
            Debug.LogError("Ray tracing material is not assigned.");
            return;
        }

        // Set ray tracing quality and performance parameters
        rayTracingMaterial.SetInt("MaxRayBounce", MaxRayBounce); // Max number of ray bounces
        rayTracingMaterial.SetInt("RaysPerPixel", RaysPerPixel); // Rays per pixel for anti-aliasing
        rayTracingMaterial.SetFloat("FocusBlurAmount", FocusBlurAmount); // Depth of field blur
        rayTracingMaterial.SetFloat("RaySpreadFactor", RaySpreadFactor); // Ray divergence factor

        // Environment-related shader parameters
        rayTracingMaterial.SetInteger("EnvironmentEnabled", environmentSettings.enabled ? 1 : 0);
        rayTracingMaterial.SetColor("GroundColour", environmentSettings.groundColour); // Color of the ground plane
        rayTracingMaterial.SetColor("SkyColourHorizon", environmentSettings.skyColourHorizon); // Horizon color
        rayTracingMaterial.SetColor("SkyColour", environmentSettings.SkyColour); // Sky color at zenith
        rayTracingMaterial.SetFloat("SunFocus", environmentSettings.sunFocus); // Sharpness of sun's disc
        rayTracingMaterial.SetFloat("SunIntensity", environmentSettings.sunIntensity); // Sunlight intensity
    }

    void UpdateRayTracingCameraParams(Camera cam)
    {
        if (cam == null)
        {
            Debug.LogError("Camera reference is null in UpdateRayTracingCameraParams");
            return;
        }

        if (rayTracingMaterial == null)
        {
            Debug.LogError("Ray tracing material has not been assigned.");
            return;
        }

        // Calculate the height of the camera's projection plane based on the focus distance and field of view
        float planeHeight = focusDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 2;
        // Calculate the width of the camera's projection plane using the aspect ratio
        float planeWidth = planeHeight * cam.aspect;

        // Send camera projection dimensions and transformation matrix to the shader
        rayTracingMaterial.SetVector("CameraParameters", new Vector3(planeWidth, planeHeight, focusDistance));
        rayTracingMaterial.SetMatrix("CamLocalToWorldMatrix", cam.transform.localToWorldMatrix);
    }


    void SetupRayTracingMeshData()
    {
        RayTracedMesh[] meshObjects = FindObjectsOfType<RayTracedMesh>();

        allTriangles ??= new List<Triangle>();
        allMeshData ??= new List<MeshData>();
        allTriangles.Clear();
        allMeshData.Clear();

        foreach (var meshObject in meshObjects)
        {
            ProcessMeshObject(meshObject);
        }

        numMeshChunks = allMeshData.Count;
        numTriangles = allTriangles.Count;

        UpdateComputeBuffers();
        UpdateShaderBuffers();
    }

    void ProcessMeshObject(RayTracedMesh meshObject)
    {
        MeshChunk[] chunks = meshObject.GetSubMeshChunks();
        foreach (MeshChunk chunk in chunks)
        {
            RayTracingMaterial material = meshObject.GetMaterial(chunk.subMeshIndex);
            allMeshData.Add(new MeshData(allTriangles.Count, chunk.triangles.Length, material, chunk.bounds));
            allTriangles.AddRange(chunk.triangles);
        }
    }

    void UpdateComputeBuffers()
    {
        ShaderToolkit.SetupAndFillComputeBuffer(ref triangleBuffer, allTriangles);
        ShaderToolkit.SetupAndFillComputeBuffer(ref meshDataBuffer, allMeshData);
    }

    void UpdateShaderBuffers()
    {
        rayTracingMaterial.SetBuffer("Triangles", triangleBuffer);
        rayTracingMaterial.SetBuffer("AllmeshData", meshDataBuffer);
        rayTracingMaterial.SetInt("NumMeshes", allMeshData.Count);
    }

    void OnDisable()
    {
        // Release all allocated ComputeBuffers and RenderTextures when the component is disabled or destroyed.
        // This helps prevent memory leaks especially when using ComputeBuffers.
        if (sphereBuffer != null) ShaderToolkit.Release(sphereBuffer);
        if (triangleBuffer != null) ShaderToolkit.Release(triangleBuffer);
        if (meshDataBuffer != null) ShaderToolkit.Release(meshDataBuffer);
        if (resultTexture != null) ShaderToolkit.Release(resultTexture);
    }

    void OnValidate()
    {
        // Ensure that the ray tracing settings fall within acceptable limits to prevent runtime errors or performance issues.
        MaxRayBounce = Mathf.Max(0, MaxRayBounce); // Ensure non-negative number of ray bounces
        RaysPerPixel = Mathf.Max(1, RaysPerPixel); // At least one ray per pixel is necessary
        environmentSettings.sunFocus = Mathf.Max(1, environmentSettings.sunFocus); // Sun focus must be at least 1 to avoid graphical errors
        environmentSettings.sunIntensity = Mathf.Max(0, environmentSettings.sunIntensity); // Sun intensity cannot be negative
    }

}