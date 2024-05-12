using System;
using System.Collections.Generic;
using UnityEngine;

public static class MeshSplitter
{
    // Constants to define the limits for recursive splitting and triangle count per chunk
    const int maxDepth = 6;     // Maximum recursion depth for splitting
    const int maxTrisPerChunk = 48;     // Maximum number of triangles per chunk

    // Main entry point to create mesh chunks from a given mesh
    public static MeshChunk[] CreateChunks(Mesh mesh)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));

        // Initialize array to hold sub-meshes based on the number of sub-meshes in the original mesh
        MeshChunk[] subMeshes = new MeshChunk[mesh.subMeshCount];
        // Extract vertices and normals for use in sub-mesh creation
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        int[] indices = mesh.triangles;

        // Iterate over each sub-mesh descriptor to create sub-meshes
        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            var subMeshData = mesh.GetSubMesh(i);
            var subMeshIndices = indices.AsSpan(subMeshData.indexStart, subMeshData.indexCount);
            subMeshes[i] = CreateSubMesh(vertices, normals, subMeshIndices, i);
        }

        // List to store all chunks after potential splitting
        List<MeshChunk> splitChunksList = new List<MeshChunk>();
        foreach (MeshChunk subMesh in subMeshes)
        {
            Split(subMesh, splitChunksList);
        }

        return splitChunksList.ToArray();
    }

    // Creates a sub-mesh chunk from given vertices, normals, and indices
    static MeshChunk CreateSubMesh(Vector3[] vertices, Vector3[] normals, Span<int> indices, int subMeshIndex)
    {
        Triangle[] triangles = new Triangle[indices.Length / 3];
        // Initialize bounds with a small size to be expanded during triangle processing
        Bounds bounds = new Bounds(vertices[indices[0]], Vector3.one * 0.01f);

        // Create triangles and calculate bounds for the new sub-mesh
        for (int i = 0; i < indices.Length; i += 3)
        {
            int a = indices[i];
            int b = indices[i + 1];
            int c = indices[i + 2];

            // Expand the bounds to include each vertex
            bounds.Encapsulate(vertices[a]);
            bounds.Encapsulate(vertices[b]);
            bounds.Encapsulate(vertices[c]);

            // Create a triangle and add to the triangle array
            Triangle triangle = new Triangle(
                vertices[a], vertices[b], vertices[c],
                normals[a], normals[b], normals[c]
            );
            triangles[i / 3] = triangle;
        }

        return new MeshChunk(triangles, bounds, subMeshIndex);
    }

    // Recursively splits the given mesh chunk into smaller chunks if necessary
    static void Split(MeshChunk currChunk, List<MeshChunk> splitChunks, int depth = 0)
    {
        // Only split if the triangle count exceeds the limit and the maximum depth has not been reached
        if (currChunk.triangles.Length > maxTrisPerChunk && depth < maxDepth)
        {
            Vector3 quarterSize = currChunk.bounds.size / 4;
            HashSet<int> takenTriangles = new HashSet<int>();

            // Attempt to split the current chunk into eight sub-chunks based on its bounds
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 offset = new Vector3(quarterSize.x * x, quarterSize.y * y, quarterSize.z * z);
                        Bounds splitBounds = new Bounds(currChunk.bounds.center + offset, quarterSize * 2);
                        MeshChunk splitChunk = Extract(currChunk.triangles, takenTriangles, splitBounds, currChunk.subMeshIndex);
                        if (splitChunk.triangles.Length > 0)
                        {
                            Split(splitChunk, splitChunks, depth + 1);
                        }
                    }
                }
            }
        }
        else
        {
            // Add the chunk as is if it cannot be split further
            splitChunks.Add(currChunk);
        }
    }

    // Extracts triangles from the given array that fall within the specified bounds
    static MeshChunk Extract(Triangle[] triangles, HashSet<int> takenTriangles, Bounds bounds, int subMeshIndex)
    {
        List<Triangle> newTriangles = new List<Triangle>();

        // Check each triangle to see if it falls within the bounds and has not been taken yet
        for (int i = 0; i < triangles.Length; i++)
        {
            if (!takenTriangles.Contains(i))
            {
                Triangle triangle = triangles[i];
                if (bounds.Contains(triangle.posA) || bounds.Contains(triangle.posB) || bounds.Contains(triangle.posC))
                {
                    // Add the triangle to the new list and mark it as taken
                    newTriangles.Add(triangle);
                    takenTriangles.Add(i);
                }
            }
        }

        return new MeshChunk(newTriangles.ToArray(), bounds, subMeshIndex);
    }
}
