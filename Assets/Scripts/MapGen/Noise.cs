using UnityEngine;
using System.Collections;

namespace DuckRoulette.MapGen
{
    public static class Noise {

    	public enum NormalizeMode {Local, Global};

    	public static float[,] GenerateNoiseMap(int mapWidth, int mapHeight, NoiseSettings settings, Vector2 sampleCentre) {
    		float[,] noiseMap = new float[mapWidth,mapHeight];

    		System.Random prng = new System.Random (settings.SeedHashCode);
    		Vector2[] octaveOffsets = new Vector2[settings.octaves];

    		float maxPossibleHeight = 0;
    		float amplitude = 1;
    		float frequency = 1;

    		for (int i = 0; i < settings.octaves; i++) {
    			float offsetX = prng.Next (-100000, 100000) + settings.offset.x + sampleCentre.x;
    			float offsetY = prng.Next (-100000, 100000) - settings.offset.y - sampleCentre.y;
    			octaveOffsets [i] = new Vector2 (offsetX, offsetY);

    			maxPossibleHeight += amplitude;
    			amplitude *= settings.persistance;
    		}

    		float maxLocalNoiseHeight = float.MinValue;
    		float minLocalNoiseHeight = float.MaxValue;

    		float halfWidth = mapWidth / 2f;
    		float halfHeight = mapHeight / 2f;


    		for (int y = 0; y < mapHeight; y++) {
    			for (int x = 0; x < mapWidth; x++) {

    				amplitude = 1;
    				frequency = 1;
    				float noiseHeight = 0;

    				for (int i = 0; i < settings.octaves; i++) {
    					float sampleX = (x-halfWidth + octaveOffsets[i].x) / settings.scale * frequency;
    					float sampleY = (y-halfHeight + octaveOffsets[i].y) / settings.scale * frequency;

    					float perlinValue = Mathf.PerlinNoise (sampleX, sampleY) * 2 - 1;
    					noiseHeight += perlinValue * amplitude;

    					amplitude *= settings.persistance;
    					frequency *= settings.lacunarity;
    				}

    				if (noiseHeight > maxLocalNoiseHeight) {
    					maxLocalNoiseHeight = noiseHeight;
    				} 
    				if (noiseHeight < minLocalNoiseHeight) {
    					minLocalNoiseHeight = noiseHeight;
    				}
    				noiseMap [x, y] = noiseHeight;

    				if (settings.normalizeMode == NormalizeMode.Global) {
    					float normalizedHeight = (noiseMap [x, y] + 1) / (maxPossibleHeight / 0.9f);
    					noiseMap [x, y] = Mathf.Clamp (normalizedHeight, 0, int.MaxValue);
    				}
    			}
    		}

    		if (settings.normalizeMode == NormalizeMode.Local) {
    			for (int y = 0; y < mapHeight; y++) {
    				for (int x = 0; x < mapWidth; x++) {
    					noiseMap [x, y] = Mathf.InverseLerp (minLocalNoiseHeight, maxLocalNoiseHeight, noiseMap [x, y]);
    				}
    			}
    	}

    		return noiseMap;
    	}

        public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

    }

    [System.Serializable]
    public class NoiseSettings {
    	public Noise.NormalizeMode normalizeMode;

    	public float scale = 50;

    	public int octaves = 6;
    	[Range(0,1)]
    	public float persistance =.6f;
    	public float lacunarity = 2;

    	public string seed;
    	public Vector2 offset;
        public int SeedHashCode {
            get {
                return seed.GetHashCode();
            }
        }

        public void ValidateValues() {
            scale = Mathf.Max(scale, 0.01f);
            octaves = Mathf.Max(octaves, 1);
            lacunarity = Mathf.Max(lacunarity, 1);
            persistance = Mathf.Clamp01(persistance);
        
            if (string.IsNullOrEmpty(seed)) {
                seed = System.DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            }
        }
    
    }
}
