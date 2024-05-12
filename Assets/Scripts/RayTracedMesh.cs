using UnityEngine;

public class RayTracedMesh : MonoBehaviour
{
    [Header("Ray Tracing Settings")]
    public RayTracingMaterial[] rayTracingMaterials;

    [Header("Mesh Components")]
    public MeshRenderer meshRenderer;
    public MeshFilter meshFilter;
    public int triangleCount;

    [SerializeField, HideInInspector]
    private int materialObjectID;
    [SerializeField]
    private Mesh localMesh;
    [SerializeField]
    private MeshChunk[] localMeshChunks;
    private MeshChunk[] worldSpaceChunks;

    // Calculate mesh chunks in world space
    public MeshChunk[] GetSubMeshChunks()
    {
        if (localMesh.triangles.Length / 3 > RayTracingManager.TriangleLimit)
        {
            throw new System.Exception($"Mesh exceeds the maximum triangle count of {RayTracingManager.TriangleLimit}.");
        }

        // Update local mesh chunks if necessary
        if (meshFilter != null && (localMesh != meshFilter.sharedMesh || localMeshChunks.Length == 0))
        {
            localMesh = meshFilter.sharedMesh;
            localMeshChunks = MeshSplitter.CreateChunks(localMesh);
        }

        // Initialize world space chunks if needed
        if (worldSpaceChunks == null || worldSpaceChunks.Length != localMeshChunks.Length)
        {
            worldSpaceChunks = new MeshChunk[localMeshChunks.Length];
        }

        TransformLocalChunksToWorld();
        return worldSpaceChunks;
    }

    // Transforms local mesh chunks to their corresponding world space positions
    private void TransformLocalChunksToWorld()
    {
        Vector3 position = transform.position;
        Quaternion rotation = transform.rotation;
        Vector3 scale = transform.lossyScale;

        for (int i = 0; i < worldSpaceChunks.Length; i++)
        {
            MeshChunk localChunk = localMeshChunks[i];

            if (worldSpaceChunks[i] == null || worldSpaceChunks[i].triangles.Length != localChunk.triangles.Length)
            {
                worldSpaceChunks[i] = new MeshChunk(new Triangle[localChunk.triangles.Length], localChunk.bounds, localChunk.subMeshIndex);
            }
            UpdateWorldSpaceChunk(worldSpaceChunks[i], localChunk, position, rotation, scale);
        }
    }

    // Update a single chunk's triangles to world space using position, rotation, and scale
    private void UpdateWorldSpaceChunk(MeshChunk worldChunk, MeshChunk localChunk, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Triangle[] localTriangles = localChunk.triangles;
        Vector3 boundsMin = TransformPointToWorld(localTriangles[0].posA, position, rotation, scale);
        Vector3 boundsMax = boundsMin;

        for (int i = 0; i < localTriangles.Length; i++)
        {
            Vector3 worldA = TransformPointToWorld(localTriangles[i].posA, position, rotation, scale);
            Vector3 worldB = TransformPointToWorld(localTriangles[i].posB, position, rotation, scale);
            Vector3 worldC = TransformPointToWorld(localTriangles[i].posC, position, rotation, scale);
            Triangle worldTriangle = new Triangle(worldA, worldB, worldC, TransformDirectionToWorld(localTriangles[i].normalA, rotation), TransformDirectionToWorld(localTriangles[i].normalB, rotation), TransformDirectionToWorld(localTriangles[i].normalC, rotation));
            worldChunk.triangles[i] = worldTriangle;

            boundsMin = Vector3.Min(boundsMin, Vector3.Min(worldA, Vector3.Min(worldB, worldC)));
            boundsMax = Vector3.Max(boundsMax, Vector3.Max(worldA, Vector3.Max(worldB, worldC)));
        }

        worldChunk.bounds = new Bounds((boundsMin + boundsMax) / 2, boundsMax - boundsMin);
        worldChunk.subMeshIndex = localChunk.subMeshIndex;
    }

    // Helper method to transform a point to world coordinates
    private static Vector3 TransformPointToWorld(Vector3 point, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        return rotation * Vector3.Scale(point, scale) + position;
    }

    // Helper method to transform a direction to world coordinates
    private static Vector3 TransformDirectionToWorld(Vector3 direction, Quaternion rotation)
    {
        return rotation * direction;
    }

    // Returns the material for a specific submesh index, handling out-of-range safely
    public RayTracingMaterial GetMaterial(int subMeshIndex)
    {
        return rayTracingMaterials[Mathf.Min(subMeshIndex, rayTracingMaterials.Length - 1)];
    }

    // Validate and setup default values and components
    void OnValidate()
    {
        if (rayTracingMaterials == null || rayTracingMaterials.Length == 0)
        {
            rayTracingMaterials = new RayTracingMaterial[1];
            rayTracingMaterials[0].SetDefaultValues();
        }

        if (meshRenderer == null || meshFilter == null)
        {
            meshRenderer = GetComponent<MeshRenderer>();
            meshFilter = GetComponent<MeshFilter>();
        }

        SetUpMaterialDisplay();
        triangleCount = meshFilter.sharedMesh.triangles.Length / 3;
    }

    // Sets up the materials for visual inspection and debugging in the editor
    private void SetUpMaterialDisplay()
    {
        if (gameObject.GetInstanceID() != materialObjectID)
        {
            materialObjectID = gameObject.GetInstanceID();
            Material[] originalMaterials = meshRenderer.sharedMaterials;
            Material[] newMaterials = new Material[originalMaterials.Length];
            Shader shader = Shader.Find("Standard");
            for (int i = 0; i < originalMaterials.Length; i++)
            {
                newMaterials[i] = new Material(shader);
            }
            meshRenderer.sharedMaterials = newMaterials;
        }

        for (int i = 0; i < meshRenderer.sharedMaterials.Length; i++)
        {
            RayTracingMaterial mat = rayTracingMaterials[Mathf.Min(i, rayTracingMaterials.Length - 1)];
            bool displayEmissiveColor = mat.colour.maxColorComponent < mat.emissionColour.maxColorComponent * mat.emissionStrength;
            Color displayColor = displayEmissiveColor ? mat.emissionColour * mat.emissionStrength : mat.colour;
            meshRenderer.sharedMaterials[i].color = displayColor;
        }
    }
}
