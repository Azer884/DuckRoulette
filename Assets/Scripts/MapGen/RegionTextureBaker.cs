using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

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
    ///     scoured tint, each switched on by a threshold rather than blended in.
    ///   * <b>Drifts.</b> A few large patches of brighter snow, so big flat regions are not a
    ///     single block of colour. No fine grain.
    ///   * <b>Pollution.</b> Two flat stain tones around the pipe outfalls.
    ///
    /// Every colour and amount comes from the <see cref="TerrainPalette"/> on the MapGenerator.
    ///
    /// It runs in two places. In the editor, <see cref="BakeAsset"/> writes a PNG and a material
    /// into Assets/Generated so a generated map survives a scene reload. In play mode the map is
    /// regenerated on start (often from a fresh seed), so <see cref="BakeRuntime"/> paints an
    /// in-memory texture for that map - the asset baked in the editor belongs to a different
    /// layout.
    ///
    /// Region borders are only as sharp as the texture is dense, so the texture is sized from
    /// <see cref="TerrainPalette.textureSize"/>, stored uncompressed (block compression smears
    /// shades this close together into visible 4x4 blocks) and filtered trilinear + anisotropic so
    /// the edges stay clean at a grazing angle. The per-texel loop reads only precomputed fields,
    /// so it runs in parallel across rows.
    ///
    /// The terrain mesh carries 0..1 UVs across the grid, so the map lines up cell for cell with no
    /// extra work.
    /// </summary>
    public static class RegionTextureBaker
    {
        const string OutputFolder = "Assets/Generated";
        const string BasePath = OutputFolder + "/RegionMap.png";
        const string MaterialPath = OutputFolder + "/TerrainRegions.mat";

        /// <summary>Toon material the terrain copies its shading setup from.</summary>
        const string ToonTemplatePath = "Assets/Materials/Terrain/Terrain.mat";

        const int MinTextureSize = 256;
        const int MaxTextureSize = 8192;

        /// <summary>Number of terraced height bands.</summary>
        const int HeightBands = 3;

        /// <summary>
        /// Paints the ground into a new in-memory texture and returns a copy of
        /// <paramref name="template"/> using it. The caller owns both and destroys them when the
        /// map is rebuilt.
        /// </summary>
        public static Material BakeRuntime(RegionType[,] regionMap, float[,] heightMap, float[,] pollution,
            TerrainPalette palette, Material template, out Texture2D texture)
        {
            Color32[] pixels = BakePixels(regionMap, heightMap, pollution, palette, out int textureWidth, out int textureHeight);

            texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, true)
            {
                name = "RegionMap (runtime)",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
            };
            texture.SetPixels32(pixels);
            // Mips are generated, then the CPU copy is dropped: nothing reads it back.
            texture.Apply(true, true);

            Material material = template != null
                ? new Material(template)
                : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = "TerrainRegions (runtime)";
            ApplyTexture(material, texture);
            return material;
        }

#if UNITY_EDITOR
        /// <summary>Bakes the ground texture as an asset and returns the material using it.</summary>
        public static Material BakeAsset(RegionType[,] regionMap, float[,] heightMap, float[,] pollution,
            TerrainPalette palette)
        {
            Color32[] pixels = BakePixels(regionMap, heightMap, pollution, palette, out int textureWidth, out int textureHeight);
            WritePng(BasePath, textureWidth, textureHeight, pixels);
            var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(BasePath);

            Material material = BuildToonMaterial();
            ApplyTexture(material, baseMap);

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }
#endif

        static void ApplyTexture(Material material, Texture texture)
        {
            SetTextureIfPresent(material, "_MainTex", texture);
            SetTextureIfPresent(material, "_BaseMap", texture);
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
        }

        static Color32[] BakePixels(RegionType[,] regionMap, float[,] heightMap, float[,] pollution,
            TerrainPalette palette, out int textureWidth, out int textureHeight)
        {
            palette ??= TerrainPalette.Default;

            int width = regionMap.GetLength(0);
            int height = regionMap.GetLength(1);
            int size = Mathf.Clamp(palette.textureSize, MinTextureSize, MaxTextureSize);
            float texelsPerCell = (float)size / Mathf.Max(width, height);
            int texWidth = Mathf.Max(1, Mathf.RoundToInt(width * texelsPerCell));
            int texHeight = Mathf.Max(1, Mathf.RoundToInt(height * texelsPerCell));
            textureWidth = texWidth;
            textureHeight = texHeight;

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
                colours[i] = palette.ColourFor(regions[i]);
            }

            float[,] smoothPollution = pollution != null && palette.paintPollution ? Blur(pollution, 1) : null;

            // The noise is all low frequency (tens of cells per wave), so it is evaluated once per
            // cell and sampled bilinearly per texel. That keeps the per-texel loop to plain array
            // reads, which is what lets it run on worker threads.
            float[,] warpXField = NoiseField(width, height, 0.05f, 41f, new Vector2(3.1f, 7.9f));
            float[,] warpYField = NoiseField(width, height, 0.05f, 41f, new Vector2(71f, 23f));
            float[,] driftField = NoiseField(width, height, 0.07f, 61f, new Vector2(11.3f, 5.7f));

            // Copied out of the palette so the worker threads never touch the serialized object.
            float lowGroundBrightness = palette.lowGroundBrightness;
            Color hollowTint = palette.hollowTint;
            float hollowStrength = palette.hollowStrength;
            Color slopeTint = palette.slopeTint;
            float slopeStrength = palette.slopeStrength;
            float driftBrightness = palette.driftBrightness;
            Color pollutionLight = palette.pollutionLight;
            Color pollutionHeavy = palette.pollutionHeavy;
            float lightThreshold = palette.pollutionLightThreshold;
            float heavyThreshold = palette.pollutionHeavyThreshold;
            float exposure = palette.exposure;

            var pixels = new Color32[texWidth * texHeight];

            Parallel.For(0, texHeight, py =>
            {
                for (int px = 0; px < texWidth; px++)
                {
                    // Texel centre in cells. The mesh maps cell x to u = x / width, so texel px
                    // covers cells px / texelsPerCell to (px + 1) / texelsPerCell.
                    float u = (px + 0.5f) / texelsPerCell;
                    float v = (py + 0.5f) / texelsPerCell;

                    // ---- region: flat colour of the strongest region ----------------------
                    // A broad warp bends the Voronoi's straight borders into curves. It is kept
                    // low frequency so the edge wobbles gently instead of fraying.
                    float warpX = (TerrainShaper.SampleHeight(warpXField, u, v) - 0.5f) * 8f;
                    float warpY = (TerrainShaper.SampleHeight(warpYField, u, v) - 0.5f) * 8f;
                    Color colour = StrongestRegionColour(weights, colours, u + warpX, v + warpY);

                    float groundHeight = TerrainShaper.SampleHeight(heightMap, u, v);

                    // ---- height: terraced bands -------------------------------------------
                    float shade = Mathf.InverseLerp(lowest, highest, groundHeight);
                    float band = Mathf.Min(Mathf.Floor(shade * HeightBands), HeightBands - 1) / (HeightBands - 1f);
                    colour *= Mathf.Lerp(lowGroundBrightness, 1f, band);

                    // ---- hollows: one flat cool tint ---------------------------------------
                    if (TerrainShaper.SampleHeight(curvatureField, u, v) < -0.09f)
                    {
                        colour = Color.Lerp(colour, colour * hollowTint, hollowStrength);
                    }

                    // ---- slope: steep ground is scoured ------------------------------------
                    if (TerrainShaper.SampleHeight(slopeField, u, v) > 0.32f)
                    {
                        colour = Color.Lerp(colour, slopeTint, slopeStrength);
                    }

                    // ---- drifts: a few big flat patches of brighter snow --------------------
                    if (TerrainShaper.SampleHeight(driftField, u, v) > 0.6f)
                    {
                        colour *= driftBrightness;
                    }

                    // ---- pollution: two flat stain tones -----------------------------------
                    if (smoothPollution != null)
                    {
                        float dirt = TerrainShaper.SampleHeight(smoothPollution, u + warpX * 0.5f, v + warpY * 0.5f);
                        if (dirt > heavyThreshold)
                        {
                            colour = pollutionHeavy;
                        }
                        else if (dirt > lightThreshold)
                        {
                            colour = pollutionLight;
                        }
                    }

                    colour *= exposure;
                    colour.a = 1f;

                    pixels[py * texWidth + px] = colour;
                }
            });

            return pixels;
        }

        /// <summary>One low frequency noise value per cell, for bilinear sampling per texel.</summary>
        static float[,] NoiseField(int width, int height, float frequency, float angleDegrees, Vector2 offset)
        {
            var field = new float[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    field[x, y] = RotatedFbm(x, y, frequency, 2, angleDegrees, offset);
                }
            }

            return field;
        }

#if UNITY_EDITOR
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
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(width, height)), MinTextureSize, MaxTextureSize);
            // Uncompressed: the region shades are a few percent apart, and block compression
            // turns the borders between them into a 4x4 staircase.
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
#endif

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
    }
}
