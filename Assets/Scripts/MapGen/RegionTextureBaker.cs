#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DuckRoulette.MapGen
{
    /// <summary>
    /// Bakes the map's ground into a flat, toon-shaded texture.
    ///
    /// The rest of the game is cel shaded: flat colour, one hard shadow step, no surface detail.
    /// A ground full of drift noise, sparkle and a detail normal map reads as a photo under a
    /// cartoon, so this bake paints the ground the way the props are painted - a handful of flat
    /// shades with crisp, smoothly curving edges between them:
    ///
    ///   * <b>Regions.</b> Each region keeps its own shade of white. Every texel takes the colour
    ///     of the single strongest region at that point, so borders are clean curves rather than
    ///     gradients.
    ///   * <b>Height bands.</b> Height is cut into a few terraced bands, like contour lines, instead
    ///     of a smooth ramp.
    ///   * <b>Hollows and slopes.</b> Hollows get one flat cool tint and steep ground one flat
    ///     scoured grey, each switched on by a threshold rather than blended in.
    ///   * <b>Drifts.</b> A few large patches of brighter snow, so big flat regions are not a
    ///     single block of colour. No fine grain.
    ///   * <b>Pollution.</b> Two flat soot tones around the pipe outfalls.
    ///
    /// Lighting comes from the mesh normals through the toon shader, so no normal map is baked.
    /// The material copies its shading setup from the toon template so it matches the props.
    ///
    /// The terrain mesh carries 0..1 UVs across the grid, so the map lines up cell for cell with no
    /// extra work, and it is written as a real asset so a generated map survives a scene reload.
    /// </summary>
    public static class RegionTextureBaker
    {
        const string OutputFolder = "Assets/Generated";
        const string BasePath = OutputFolder + "/RegionMap.png";
        const string MaterialPath = OutputFolder + "/TerrainRegions.mat";

        /// <summary>Toon material the terrain copies its shading setup from.</summary>
        const string ToonTemplatePath = "Assets/Materials/Terrain/Terrain.mat";

        /// <summary>Texels per map cell. Eight over a 100 cell map is an 800 pixel square.</summary>
        public const int DefaultSupersample = 8;

        /// <summary>Number of terraced height bands.</summary>
        const int HeightBands = 3;

        /// <summary>Bakes the ground texture and returns the material using it.</summary>
        public static Material Bake(RegionType[,] regionMap, float[,] heightMap, float[,] pollution,
            float heightMultiplier = 5f, int supersample = DefaultSupersample)
        {
            int width = regionMap.GetLength(0);
            int height = regionMap.GetLength(1);
            int textureWidth = width * supersample;
            int textureHeight = height * supersample;

            var basePixels = new Color32[textureWidth * textureHeight];

            float lowest = float.MaxValue;
            float highest = float.MinValue;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    lowest = Mathf.Min(lowest, heightMap[x, y]);
                    highest = Mathf.Max(highest, heightMap[x, y]);
                }
            }

            // Local curvature: a cell minus its blurred surroundings, which tells a hollow from a
            // crest. Smoothed before it is thresholded, so only real hollows get the tint instead
            // of every small dip turning into a speck.
            float[,] curvatureField = Curvature(heightMap, Blur(heightMap, 3));

            // Slope as a field so it can be sampled smoothly. Thresholding a per-cell value would
            // give a staircase edge.
            float[,] slopeField = SlopeField(heightMap);

            // One blurred weight field per region. Taking the strongest region at a point, from
            // fields sampled bilinearly, gives smooth curved borders; taking the nearest cell's
            // region would give the cell grid's staircase.
            RegionType[] regions = PresentRegions(regionMap);
            var weights = new float[regions.Length][,];
            var colours = new Color[regions.Length];
            for (int i = 0; i < regions.Length; i++)
            {
                weights[i] = Blur(Blur(RegionMask(regionMap, regions[i]), 2), 2);
                colours[i] = RegionMarker.ColourFor(regions[i]);
            }

            float[,] smoothPollution = pollution != null ? Blur(pollution, 1) : null;

            var soot = new Color(0.36f, 0.34f, 0.31f);
            var heavySoot = new Color(0.24f, 0.23f, 0.21f);
            // Both stay close to white: scoured snow going grey, and shadowed snow going cool.
            var scoured = new Color(0.72f, 0.73f, 0.75f);
            var hollowShade = new Color(0.80f, 0.86f, 0.94f);

            for (int px = 0; px < textureWidth; px++)
            {
                for (int py = 0; py < textureHeight; py++)
                {
                    // Texel centre in cells. The mesh maps cell x to u = x / width, so texel px
                    // covers cells px / supersample to (px + 1) / supersample.
                    float u = (px + 0.5f) / supersample;
                    float v = (py + 0.5f) / supersample;

                    // ---- region: flat colour of the strongest region ----------------------
                    // A broad warp bends the Voronoi's straight borders into curves. It is kept
                    // low frequency so the edge wobbles gently instead of fraying.
                    float warpX = (RotatedFbm(u, v, 0.05f, 2, 41f, new Vector2(3.1f, 7.9f)) - 0.5f) * 8f;
                    float warpY = (RotatedFbm(u, v, 0.05f, 2, 41f, new Vector2(71f, 23f)) - 0.5f) * 8f;
                    Color colour = StrongestRegionColour(weights, colours, u + warpX, v + warpY);

                    float groundHeight = TerrainShaper.SampleHeight(heightMap, u, v);

                    // ---- height: terraced bands -------------------------------------------
                    float shade = Mathf.InverseLerp(lowest, highest, groundHeight);
                    float band = Mathf.Min(Mathf.Floor(shade * HeightBands), HeightBands - 1) / (HeightBands - 1f);
                    colour *= Mathf.Lerp(0.9f, 1.03f, band);

                    // ---- hollows: one flat cool tint ---------------------------------------
                    if (TerrainShaper.SampleHeight(curvatureField, u, v) < -0.09f)
                    {
                        colour = Color.Lerp(colour, hollowShade * colour.grayscale * 1.08f, 0.55f);
                    }

                    // ---- slope: steep ground is scoured to grey ----------------------------
                    if (TerrainShaper.SampleHeight(slopeField, u, v) > 0.32f)
                    {
                        colour = Color.Lerp(colour, scoured, 0.6f);
                    }

                    // ---- drifts: a few big flat patches of brighter snow --------------------
                    if (RotatedFbm(u, v, 0.07f, 2, 61f, new Vector2(11.3f, 5.7f)) > 0.6f)
                    {
                        colour *= 1.05f;
                    }

                    // ---- pollution: two flat soot tones ------------------------------------
                    if (smoothPollution != null)
                    {
                        float dirt = TerrainShaper.SampleHeight(smoothPollution, u + warpX * 0.5f, v + warpY * 0.5f);
                        if (dirt > 0.6f)
                        {
                            colour = heavySoot;
                        }
                        else if (dirt > 0.25f)
                        {
                            colour = soot;
                        }
                    }

                    // Headroom, so the brightest snow is not blown out under the directional light.
                    colour *= 0.95f;
                    colour.a = 1f;

                    basePixels[py * textureWidth + px] = colour;
                }
            }

            WritePng(BasePath, textureWidth, textureHeight, basePixels);
            var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(BasePath);

            Material material = BuildToonMaterial();
            SetTextureIfPresent(material, "_MainTex", baseMap);
            SetTextureIfPresent(material, "_BaseMap", baseMap);
            // The template tiles its own texture across each prop. This one covers the whole
            // terrain exactly once, so any tiling copied from the template has to go.
            ResetTiling(material, "_MainTex");
            ResetTiling(material, "_BaseMap");
            SetTextureIfPresent(material, "_NormalMap", null);
            SetTextureIfPresent(material, "_BumpMap", null);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        /// <summary>
        /// The terrain material, reset from the toon template on every bake so it always shades
        /// like the rest of the map.
        /// </summary>
        static Material BuildToonMaterial()
        {
            var template = AssetDatabase.LoadAssetAtPath<Material>(ToonTemplatePath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

            if (template == null)
            {
                Debug.LogWarning($"RegionTextureBaker: toon template {ToonTemplatePath} not found, using URP Lit.");
                if (material == null)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    AssetDatabase.CreateAsset(material, MaterialPath);
                }

                return material;
            }

            if (material == null)
            {
                material = new Material(template);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = template.shader;
                material.CopyPropertiesFromMaterial(template);
                material.shaderKeywords = template.shaderKeywords;
                material.renderQueue = template.renderQueue;
            }

            return material;
        }

        static void ResetTiling(Material material, string property)
        {
            if (material.HasProperty(property))
            {
                material.SetTextureScale(property, Vector2.one);
                material.SetTextureOffset(property, Vector2.zero);
            }
        }

        static void SetTextureIfPresent(Material material, string property, Texture texture)
        {
            if (material.HasProperty(property))
            {
                material.SetTexture(property, texture);
            }
        }

        /// <summary>The region types that occur on the map, in enum order.</summary>
        static RegionType[] PresentRegions(RegionType[,] regionMap)
        {
            var present = new SortedSet<RegionType>();
            foreach (RegionType region in regionMap)
            {
                present.Add(region);
            }

            var result = new RegionType[present.Count];
            present.CopyTo(result);
            return result;
        }

        /// <summary>1 where the cell belongs to the region, 0 elsewhere.</summary>
        static float[,] RegionMask(RegionType[,] regionMap, RegionType region)
        {
            int width = regionMap.GetLength(0);
            int height = regionMap.GetLength(1);
            var mask = new float[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    mask[x, y] = regionMap[x, y] == region ? 1f : 0f;
                }
            }

            return mask;
        }

        static Color StrongestRegionColour(float[][,] weights, Color[] colours, float u, float v)
        {
            if (colours.Length == 0)
            {
                return Color.white;
            }

            int best = 0;
            float bestWeight = float.MinValue;

            for (int i = 0; i < weights.Length; i++)
            {
                float weight = TerrainShaper.SampleHeight(weights[i], u, v);
                if (weight > bestWeight)
                {
                    bestWeight = weight;
                    best = i;
                }
            }

            return colours[best];
        }

        static float[,] Curvature(float[,] heightMap, float[,] blurred)
        {
            int width = heightMap.GetLength(0);
            int height = heightMap.GetLength(1);
            var curvature = new float[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    curvature[x, y] = heightMap[x, y] - blurred[x, y];
                }
            }

            return Blur(curvature, 2);
        }

        static float[,] SlopeField(float[,] heightMap)
        {
            int width = heightMap.GetLength(0);
            int height = heightMap.GetLength(1);
            var slopes = new float[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    slopes[x, y] = TerrainShaper.Slope(heightMap, x, y);
                }
            }

            return slopes;
        }

        /// <summary>
        /// Perlin octaves sampled on a rotated, offset domain.
        ///
        /// Unity's Perlin noise is built on an axis-aligned lattice and returns the same value at
        /// every integer coordinate. Sampled at the low frequencies this bake wants - a few lattice
        /// cells across the whole map - that lattice shows up as large rectangular blocks with hard
        /// vertical and horizontal edges. Rotating the domain by an odd angle per field turns those
        /// edges off the axes and makes them invisible; the offset keeps two fields from correlating.
        /// </summary>
        static float RotatedFbm(float x, float y, float frequency, int octaves, float angleDegrees, Vector2 offset)
        {
            float angle = angleDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);

            float rotatedX = (x * cos - y * sin) * frequency + offset.x;
            float rotatedY = (x * sin + y * cos) * frequency + offset.y;
            return Fbm(rotatedX, rotatedY, octaves);
        }

        /// <summary>Stacked Perlin octaves, returned in the 0..1 range.</summary>
        static float Fbm(float x, float y, int octaves)
        {
            float value = 0f;
            float amplitude = 0.5f;
            float total = 0f;

            for (int i = 0; i < octaves; i++)
            {
                value += Mathf.PerlinNoise(x, y) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                x *= 2.07f;
                y *= 2.03f;
            }

            return total > 0f ? value / total : 0f;
        }

        static float[,] Blur(float[,] source, int radius)
        {
            int width = source.GetLength(0);
            int height = source.GetLength(1);
            var result = new float[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    float sum = 0f;
                    int count = 0;

                    for (int ox = -radius; ox <= radius; ox++)
                    {
                        for (int oy = -radius; oy <= radius; oy++)
                        {
                            int sx = Mathf.Clamp(x + ox, 0, width - 1);
                            int sy = Mathf.Clamp(y + oy, 0, height - 1);
                            sum += source[sx, sy];
                            count++;
                        }
                    }

                    result[x, y] = sum / count;
                }
            }

            return result;
        }

        static void WritePng(string path, int width, int height, Color32[] pixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();

            Directory.CreateDirectory(OutputFolder);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
    }
}
#endif
