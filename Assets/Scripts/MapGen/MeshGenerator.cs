using UnityEngine;
using System.Collections;

namespace DuckRoulette.MapGen
{
    public static class MeshGenerator {

    	public static MeshData GenerateTerrainMesh(float[,] heightMap, float heightMultiplier = 1f) {
    		int width = heightMap.GetLength(0);
    		int height = heightMap.GetLength(1);

    		// Remove centering offsets
    		float bottomLeftX = 0;
    		float bottomLeftZ = 0;

    		MeshData meshData = new MeshData(width, height);
    		int vertexIndex = 0;

    		for (int y = 0; y < height; y++) {
    			for (int x = 0; x < width; x++) {
    				// Align vertices with the grid
    				meshData.vertices[vertexIndex] = new Vector3(bottomLeftX + x, heightMap[x, y] * heightMultiplier, bottomLeftZ + y);
    				meshData.uvs[vertexIndex] = new Vector2(x / (float)width, y / (float)height);

    				if (x < width - 1 && y < height - 1) {
    					meshData.AddTriangle(vertexIndex, vertexIndex + width + 1, vertexIndex + width);
    					meshData.AddTriangle(vertexIndex + width + 1, vertexIndex, vertexIndex + 1);
    				}

    				vertexIndex++;
    			}
    		}

    		return meshData;
    	}
    }

    public class MeshData {
    	public Vector3[] vertices;
    	public int[] triangles;
    	public Vector2[] uvs;

    	int triangleIndex;

    	public MeshData(int meshWidth, int meshHeight) {
    		vertices = new Vector3[meshWidth * meshHeight];
    		uvs = new Vector2[meshWidth * meshHeight];
    		triangles = new int[(meshWidth-1)*(meshHeight-1)*6];
    	}

    	public void AddTriangle(int a, int b, int c) {
    		// Reverse the order of the vertices to flip the normals
    		triangles[triangleIndex] = a;
    		triangles[triangleIndex + 1] = c; // Swap b and c
    		triangles[triangleIndex + 2] = b;
    		triangleIndex += 3;
    	}

    	public Mesh CreateMesh() {
    		Mesh mesh = new Mesh ();
    		mesh.vertices = vertices;
    		mesh.triangles = triangles;
    		mesh.uv = uvs;
    		mesh.RecalculateNormals ();
    		return mesh;
    	}

    }
}
