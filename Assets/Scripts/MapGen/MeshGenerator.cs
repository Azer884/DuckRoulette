using UnityEngine;
using System.Collections;
using System.Collections.Generic;

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

    	/// <summary>
    	/// Cuts a terrain mesh from <see cref="GenerateTerrainMesh"/> into square pieces of
    	/// <paramref name="chunkCells"/> cells. Neighbouring pieces share their border vertices, and
    	/// normals and UVs are copied from the whole mesh rather than recalculated, so there is no
    	/// lighting seam or texture jump where two pieces meet.
    	/// </summary>
    	public static List<Mesh> SplitIntoChunks(Mesh source, int width, int height, int chunkCells) {
    		Vector3[] vertices = source.vertices;
    		Vector3[] normals = source.normals;
    		Vector2[] uvs = source.uv;
    		var chunks = new List<Mesh>();
    		chunkCells = Mathf.Max(1, chunkCells);

    		for (int startY = 0; startY < height - 1; startY += chunkCells) {
    			for (int startX = 0; startX < width - 1; startX += chunkCells) {
    				int endX = Mathf.Min(startX + chunkCells, width - 1);
    				int endY = Mathf.Min(startY + chunkCells, height - 1);
    				int rowLength = endX - startX + 1;
    				int count = rowLength * (endY - startY + 1);

    				var chunkVertices = new Vector3[count];
    				var chunkNormals = new Vector3[count];
    				var chunkUvs = new Vector2[count];
    				int index = 0;

    				for (int y = startY; y <= endY; y++) {
    					for (int x = startX; x <= endX; x++) {
    						int sourceIndex = y * width + x;
    						chunkVertices[index] = vertices[sourceIndex];
    						chunkNormals[index] = normals[sourceIndex];
    						chunkUvs[index] = uvs[sourceIndex];
    						index++;
    					}
    				}

    				// Same winding as AddTriangle produces for the whole mesh.
    				var triangles = new List<int>((rowLength - 1) * (endY - startY) * 6);
    				for (int y = 0; y < endY - startY; y++) {
    					for (int x = 0; x < rowLength - 1; x++) {
    						int i = y * rowLength + x;
    						triangles.Add(i);
    						triangles.Add(i + rowLength);
    						triangles.Add(i + rowLength + 1);
    						triangles.Add(i + rowLength + 1);
    						triangles.Add(i + 1);
    						triangles.Add(i);
    					}
    				}

    				var mesh = new Mesh { name = $"TerrainChunk_{startX / chunkCells}_{startY / chunkCells}" };
    				mesh.vertices = chunkVertices;
    				mesh.normals = chunkNormals;
    				mesh.uv = chunkUvs;
    				mesh.SetTriangles(triangles, 0);
    				mesh.RecalculateBounds();
    				chunks.Add(mesh);
    			}
    		}

    		return chunks;
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
