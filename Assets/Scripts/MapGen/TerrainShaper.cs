using System.Collections.Generic;
using UnityEngine;

namespace DuckRoulette.MapGen
{
    /// <summary>
    /// Height-map edits that turn raw noise into a designed playfield: plateaus, flat pads,
    /// walkable paths, the border ridge the mountain meshes sit on, and smoothing.
    ///
    /// Everything works in normalised height units - the same units the noise produces - and is
    /// multiplied by the generator's height multiplier only when the mesh is built.
    /// </summary>
    public static class TerrainShaper
    {
        /// <summary>Raises a plateau with a flat core and a smooth skirt: the bluff.</summary>
        public static void RaisePlateau(float[,] map, Vector2 centre, float radius, float coreFraction, float height)
        {
            int width = map.GetLength(0);
            int mapHeight = map.GetLength(1);
            float core = radius * Mathf.Clamp01(coreFraction);

            // The core is levelled to one height so the top is genuinely usable as a platform,
            // rather than a rounded hill nobody can hold a position on.
            float coreHeight = SampleAverage(map, centre, core) + height;

            int minX = Mathf.Max(0, Mathf.FloorToInt(centre.x - radius));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(centre.x + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(centre.y - radius));
            int maxY = Mathf.Min(mapHeight - 1, Mathf.CeilToInt(centre.y + radius));

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), centre);
                    if (dist > radius)
                    {
                        continue;
                    }

                    float blend = dist <= core ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (dist - core) / (radius - core));
                    map[x, y] = Mathf.Max(map[x, y], Mathf.Lerp(map[x, y], coreHeight, blend));
                }
            }
        }

        /// <summary>
        /// Cuts a ramp from the edge of a plateau down to the surrounding ground. Without this the
        /// bluff is a cliff on every side and the region is unreachable.
        /// </summary>
        public static void CarveRamp(float[,] map, Vector2 top, Vector2 bottom, float width)
        {
            float topHeight = SampleAverage(map, top, 2f);
            float bottomHeight = SampleAverage(map, bottom, 3f);
            int steps = Mathf.Max(8, Mathf.CeilToInt(Vector2.Distance(top, bottom) * 2f));

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector2 point = Vector2.Lerp(top, bottom, t);
                float target = Mathf.Lerp(topHeight, bottomHeight, Mathf.SmoothStep(0f, 1f, t));
                StampDisc(map, point, width, target, 0.75f);
            }
        }

        /// <summary>Levels a disc to one height: used for building pads and the plaza floor.</summary>
        public static void Flatten(float[,] map, Vector2 centre, float radius, float falloff, float? forcedHeight = null)
        {
            float target = forcedHeight ?? SampleAverage(map, centre, radius * 0.5f);
            StampDisc(map, centre, radius, target, falloff);
        }

        /// <summary>Lowers a disc, used to sink the wetlands towards the waterline.</summary>
        public static void Depress(float[,] map, Vector2 centre, float radius, float amount)
        {
            int width = map.GetLength(0);
            int height = map.GetLength(1);

            int minX = Mathf.Max(0, Mathf.FloorToInt(centre.x - radius));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(centre.x + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(centre.y - radius));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(centre.y + radius));

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), centre);
                    if (dist > radius)
                    {
                        continue;
                    }

                    float blend = 1f - Mathf.SmoothStep(0f, 1f, dist / radius);
                    map[x, y] -= amount * blend;
                }
            }
        }

        /// <summary>
        /// Flattens a corridor between two points so players can actually walk it. Paths are what
        /// make the regions read as one map instead of six unrelated islands.
        /// </summary>
        public static void CarvePath(float[,] map, Vector2 from, Vector2 to, float width, float wobble, System.Random rng)
        {
            int steps = Mathf.Max(12, Mathf.CeilToInt(Vector2.Distance(from, to) * 1.5f));
            Vector2 perpendicular = Vector2.Perpendicular((to - from).normalized);
            float phase = (float)rng.NextDouble() * 10f;

            var points = new List<Vector2>(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                // Fade the wobble out at both ends so the path still meets its landmarks.
                float sway = Mathf.Sin(t * Mathf.PI) * wobble * (Mathf.PerlinNoise(phase + t * 3f, phase) - 0.5f) * 2f;
                points.Add(Vector2.Lerp(from, to, t) + perpendicular * sway);
            }

            // Two passes: the first levels the corridor to the local ground, the second smooths
            // the seam between corridor and surroundings.
            for (int i = 0; i < points.Count; i++)
            {
                float t = i / (float)(points.Count - 1);
                float target = Mathf.Lerp(SampleAverage(map, from, 3f), SampleAverage(map, to, 3f), Mathf.SmoothStep(0f, 1f, t));
                StampDisc(map, points[i], width, target, 0.6f);
            }
        }

        /// <summary>
        /// Raises the outer cells of the map into a ridge. The mountain meshes stand on this, so
        /// their bases are buried in terrain and the joins between pieces never show as gaps.
        /// </summary>
        public static void BorderRidge(float[,] map, float thickness, float height, float noiseScale, float noiseStrength, System.Random rng)
        {
            int width = map.GetLength(0);
            int mapHeight = map.GetLength(1);
            float offsetX = (float)rng.NextDouble() * 1000f;
            float offsetY = (float)rng.NextDouble() * 1000f;
            float scale = Mathf.Max(0.01f, noiseScale);

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < mapHeight; y++)
                {
                    float edgeDistance = Mathf.Min(Mathf.Min(x, width - 1 - x), Mathf.Min(y, mapHeight - 1 - y));
                    if (edgeDistance >= thickness)
                    {
                        continue;
                    }

                    float t = 1f - edgeDistance / thickness;
                    float ridge = height * t * t;
                    ridge *= 1f + (Mathf.PerlinNoise(offsetX + x / scale, offsetY + y / scale) - 0.5f) * 2f * noiseStrength;
                    map[x, y] += ridge;
                }
            }
        }

        /// <summary>Box-blur pass. Keeps slopes walkable and hides the stamping artefacts.</summary>
        public static void Smooth(float[,] map, int iterations, float strength = 1f)
        {
            int width = map.GetLength(0);
            int height = map.GetLength(1);
            var buffer = new float[width, height];

            for (int pass = 0; pass < iterations; pass++)
            {
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        float sum = 0f;
                        int count = 0;
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            for (int oy = -1; oy <= 1; oy++)
                            {
                                int sx = x + ox;
                                int sy = y + oy;
                                if (sx < 0 || sx >= width || sy < 0 || sy >= height)
                                {
                                    continue;
                                }

                                sum += map[sx, sy];
                                count++;
                            }
                        }

                        buffer[x, y] = Mathf.Lerp(map[x, y], sum / count, strength);
                    }
                }

                System.Array.Copy(buffer, map, buffer.Length);
            }
        }


        /// <summary>
        /// Decides which cells are water and makes sure water only sits where it is meant to.
        ///
        /// The shoreline is the place where the carved bed crosses the water plane, so it is derived
        /// from the height map rather than from a radius around the river spline. That is the whole
        /// trick: a radius gives a hard boolean edge, and clamping the land next to it to a minimum
        /// height turns that edge into a vertical, stair-stepped wall. Letting the bed slope through
        /// the waterline instead gives a shoreline that meets the water at a shallow angle, which is
        /// what makes it read as a bank.
        ///
        /// A flood fill from the river's own cells keeps the water connected: anything below the
        /// surface that the river cannot reach is a puddle in a hollow somewhere else on the map, so
        /// it is filled in rather than rendered as water.
        /// </summary>
        /// <param name="partition">
        /// Region each cell belongs to when it is not water. Pass null before the regions exist.
        /// </param>
        public static void ResolveWater(float[,] map, RegionType[,] regionMap, RegionType[,] partition,
            float surface, IEnumerable<Vector2Int> seeds)
        {
            int width = map.GetLength(0);
            int height = map.GetLength(1);
            var water = new bool[width, height];
            var queue = new Queue<Vector2Int>();

            foreach (Vector2Int seed in seeds)
            {
                if (seed.x < 0 || seed.y < 0 || seed.x >= width || seed.y >= height)
                {
                    continue;
                }

                if (map[seed.x, seed.y] > surface || water[seed.x, seed.y])
                {
                    continue;
                }

                water[seed.x, seed.y] = true;
                queue.Enqueue(seed);
            }

            int[] offsetX = { 1, -1, 0, 0 };
            int[] offsetY = { 0, 0, 1, -1 };

            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                for (int i = 0; i < 4; i++)
                {
                    int nx = cell.x + offsetX[i];
                    int ny = cell.y + offsetY[i];
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height || water[nx, ny])
                    {
                        continue;
                    }

                    if (map[nx, ny] <= surface)
                    {
                        water[nx, ny] = true;
                        queue.Enqueue(new Vector2Int(nx, ny));
                    }
                }
            }

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (water[x, y])
                    {
                        regionMap[x, y] = RegionType.River;
                        continue;
                    }

                    if (partition != null)
                    {
                        regionMap[x, y] = partition[x, y];
                    }
                    else if (regionMap[x, y] == RegionType.River)
                    {
                        regionMap[x, y] = RegionType.None;
                    }

                    // Dry land that ended up under the waterline but is cut off from the river would
                    // render as a puddle floating in a field. Fill it to just above the surface.
                    if (map[x, y] <= surface)
                    {
                        map[x, y] = surface + 0.06f;
                    }
                }
            }
        }

        /// <summary>Bilinear height lookup, for placing props between grid cells.</summary>
        public static float SampleHeight(float[,] map, float x, float y)
        {
            int width = map.GetLength(0);
            int height = map.GetLength(1);
            x = Mathf.Clamp(x, 0f, width - 1.001f);
            y = Mathf.Clamp(y, 0f, height - 1.001f);

            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            int x1 = Mathf.Min(x0 + 1, width - 1);
            int y1 = Mathf.Min(y0 + 1, height - 1);
            float tx = x - x0;
            float ty = y - y0;

            float bottom = Mathf.Lerp(map[x0, y0], map[x1, y0], tx);
            float top = Mathf.Lerp(map[x0, y1], map[x1, y1], tx);
            return Mathf.Lerp(bottom, top, ty);
        }

        /// <summary>
        /// Steepness at a cell, in normalised height per cell. Used to keep props off cliffs and
        /// to decide where a river bank is too sheer to be a slide entry.
        /// </summary>
        public static float Slope(float[,] map, int x, int y)
        {
            int width = map.GetLength(0);
            int height = map.GetLength(1);
            int xa = Mathf.Max(0, x - 1);
            int xb = Mathf.Min(width - 1, x + 1);
            int ya = Mathf.Max(0, y - 1);
            int yb = Mathf.Min(height - 1, y + 1);

            float dx = (map[xb, y] - map[xa, y]) / Mathf.Max(1, xb - xa);
            float dy = (map[x, yb] - map[x, ya]) / Mathf.Max(1, yb - ya);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        static void StampDisc(float[,] map, Vector2 centre, float radius, float target, float falloff)
        {
            int width = map.GetLength(0);
            int height = map.GetLength(1);
            float core = radius * (1f - Mathf.Clamp01(falloff));

            int minX = Mathf.Max(0, Mathf.FloorToInt(centre.x - radius));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(centre.x + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(centre.y - radius));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(centre.y + radius));

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), centre);
                    if (dist > radius)
                    {
                        continue;
                    }

                    float blend = dist <= core
                        ? 1f
                        : 1f - Mathf.SmoothStep(0f, 1f, (dist - core) / Mathf.Max(0.001f, radius - core));
                    map[x, y] = Mathf.Lerp(map[x, y], target, blend);
                }
            }
        }

        static float SampleAverage(float[,] map, Vector2 centre, float radius)
        {
            int width = map.GetLength(0);
            int height = map.GetLength(1);
            float sum = 0f;
            int count = 0;

            int minX = Mathf.Max(0, Mathf.FloorToInt(centre.x - radius));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(centre.x + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(centre.y - radius));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(centre.y + radius));

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    sum += map[x, y];
                    count++;
                }
            }

            return count > 0 ? sum / count : 0f;
        }
    }
}
