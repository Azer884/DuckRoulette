using System.Collections.Generic;
using UnityEngine;

namespace DuckRoulette.MapGen
{
    /// <summary>
    /// The distinct play spaces the map is carved into. Every grid cell belongs to exactly one.
    /// Values are stable so they can be stored in the region map and read back by gameplay code.
    /// </summary>
    public enum RegionType
    {
        None = 0,
        River = 1,
        FrozenMarsh = 2,
        PipeYard = 3,
        Camp = 4,
        Clearing = 5,
        Highground = 6,
        Woodland = 7,
        Mountains = 8,
    }

    /// <summary>
    /// One region's anchor on the grid plus the weight that biases the partition towards it.
    /// A larger weight claims more cells.
    /// </summary>
    public struct RegionSeed
    {
        public RegionType type;
        public Vector2 centre;
        public float weight;

        public RegionSeed(RegionType type, Vector2 centre, float weight)
        {
            this.type = type;
            this.centre = centre;
            this.weight = weight;
        }
    }

    /// <summary>
    /// Builds the region layout. Seeds are not scattered blindly: each region has a job in the
    /// map design, so each one is placed inside the part of the grid that suits that job (the
    /// pipe yard hugs a side, the plaza wants the middle, the bluff wants a corner) and only the
    /// exact position inside that area is random.
    /// </summary>
    public static class RegionPartitioner
    {
        /// <summary>
        /// Chooses an anchor for every region. <paramref name="riverPoints"/> is used so the
        /// wetlands follow the water and the drier regions keep their distance from it.
        /// </summary>
        public static List<RegionSeed> PlaceSeeds(int width, int height, System.Random rng, List<Vector3> riverPoints)
        {
            var seeds = new List<RegionSeed>();

            // Wetlands: on the river, roughly a third of the way along it, so the water gets a
            // low-lying region around it instead of cutting straight through dry land.
            Vector3 wetAnchor = riverPoints != null && riverPoints.Count > 0
                ? riverPoints[Mathf.Clamp(riverPoints.Count / 3, 0, riverPoints.Count - 1)]
                : new Vector3(width * 0.5f, 0f, height * 0.5f);
            Vector2 wetCentre = new Vector2(wetAnchor.x, wetAnchor.z);
            seeds.Add(new RegionSeed(RegionType.FrozenMarsh, wetCentre, 1.15f));

            // Pipe yard: pressed against whichever side is furthest from the wetlands. The pipe
            // is a hiding spot, and hiding spots should not share a lane with the slide.
            Vector2 pipeCentre = PickSideAnchor(width, height, rng, wetCentre);
            seeds.Add(new RegionSeed(RegionType.PipeYard, pipeCentre, 0.95f));

            // Highground: a corner bluff kept away from both the pipe yard and the wetlands, so
            // the strongest position on the map is a real walk from the other landmarks.
            Vector2 highCentre = PickCornerAnchor(width, height, rng, pipeCentre, wetCentre);
            seeds.Add(new RegionSeed(RegionType.Highground, highCentre, 0.9f));

            // Boombox plaza: near the middle. It is the loud, open challenge arena and everyone
            // should have to cross it, so it takes the centre and a generous weight.
            Vector2 plazaCentre = new Vector2(
                width * 0.5f + (float)(rng.NextDouble() - 0.5) * width * 0.12f,
                height * 0.5f + (float)(rng.NextDouble() - 0.5) * height * 0.12f);
            seeds.Add(new RegionSeed(RegionType.Clearing, plazaCentre, 1.3f));

            // Homestead: the house challenge, dropped into whatever space is still open so the
            // four landmarks spread around the map instead of clumping on one side.
            Vector2 homeCentre = PickOpenAnchor(width, height, rng, seeds, 18f);
            seeds.Add(new RegionSeed(RegionType.Camp, homeCentre, 0.9f));

            // Woodland: filler and connective tissue. Two anchors, so the trees wrap around the
            // landmarks rather than forming one solid blob against a single edge.
            for (int i = 0; i < 2; i++)
            {
                Vector2 woodCentre = PickOpenAnchor(width, height, rng, seeds, 14f);
                seeds.Add(new RegionSeed(RegionType.Woodland, woodCentre, 1f));
            }

            return seeds;
        }

        /// <summary>
        /// Assigns every cell to its nearest seed using weighted distance, so the weights change
        /// region size. Noise is folded into the distance so borders come out ragged instead of
        /// the straight Voronoi edges that read as artificial.
        /// </summary>
        public static RegionType[,] Partition(int width, int height, List<RegionSeed> seeds,
            float borderNoiseScale, float borderNoiseStrength, System.Random rng)
        {
            var map = new RegionType[width, height];
            float noiseOffsetX = (float)rng.NextDouble() * 1000f;
            float noiseOffsetY = (float)rng.NextDouble() * 1000f;
            float scale = Mathf.Max(0.01f, borderNoiseScale);

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    float wobble = (Mathf.PerlinNoise(noiseOffsetX + x / scale, noiseOffsetY + y / scale) - 0.5f)
                                   * 2f * borderNoiseStrength;

                    float best = float.MaxValue;
                    RegionType bestType = RegionType.Woodland;

                    for (int i = 0; i < seeds.Count; i++)
                    {
                        RegionSeed seed = seeds[i];
                        float dist = Vector2.Distance(new Vector2(x, y), seed.centre) / Mathf.Max(0.01f, seed.weight);
                        dist += wobble;
                        if (dist < best)
                        {
                            best = dist;
                            bestType = seed.type;
                        }
                    }

                    map[x, y] = bestType;
                }
            }

            return map;
        }

        /// <summary>Centre of mass of every cell belonging to a region.</summary>
        public static Vector2 RegionCentroid(RegionType[,] regionMap, RegionType type)
        {
            int width = regionMap.GetLength(0);
            int height = regionMap.GetLength(1);
            Vector2 sum = Vector2.zero;
            int count = 0;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (regionMap[x, y] == type)
                    {
                        sum += new Vector2(x, y);
                        count++;
                    }
                }
            }

            return count > 0 ? sum / count : new Vector2(width * 0.5f, height * 0.5f);
        }

        /// <summary>
        /// The cell of a region closest to its centroid.
        ///
        /// Regions are rarely convex - the river cuts chunks out of them - so the raw centroid can
        /// land in a neighbouring region or in the water. Landmarks have to sit inside the region
        /// they belong to, so they use this instead.
        /// </summary>
        public static Vector2 RegionAnchor(RegionType[,] regionMap, RegionType type, int edgeMargin = 0)
        {
            Vector2 centroid = RegionCentroid(regionMap, type);
            int width = regionMap.GetLength(0);
            int height = regionMap.GetLength(1);

            float best = float.MaxValue;
            Vector2 anchor = centroid;
            bool found = false;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (regionMap[x, y] != type)
                    {
                        continue;
                    }

                    // Landmarks must not end up inside the border ridge or under a mountain.
                    if (x < edgeMargin || y < edgeMargin || x >= width - edgeMargin || y >= height - edgeMargin)
                    {
                        continue;
                    }

                    float dist = ((new Vector2(x, y)) - centroid).sqrMagnitude;
                    if (dist < best)
                    {
                        best = dist;
                        anchor = new Vector2(x, y);
                        found = true;
                    }
                }
            }

            // Nothing far enough from the edge: fall back to the unrestricted anchor.
            if (!found && edgeMargin > 0)
            {
                return RegionAnchor(regionMap, type, 0);
            }

            return found ? anchor : centroid;
        }

        /// <summary>Every cell of a region, used when scattering props inside it.</summary>
        public static List<Vector2Int> RegionCells(RegionType[,] regionMap, RegionType type)
        {
            int width = regionMap.GetLength(0);
            int height = regionMap.GetLength(1);
            var cells = new List<Vector2Int>();

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (regionMap[x, y] == type)
                    {
                        cells.Add(new Vector2Int(x, y));
                    }
                }
            }

            return cells;
        }

        static Vector2 PickSideAnchor(int width, int height, System.Random rng, Vector2 away)
        {
            // One candidate per edge, inset far enough that the region still has room once the
            // mountain border eats into the outer cells.
            float inset = Mathf.Min(width, height) * 0.16f;
            Vector2[] candidates =
            {
                new Vector2(inset, height * (0.3f + (float)rng.NextDouble() * 0.4f)),
                new Vector2(width - inset, height * (0.3f + (float)rng.NextDouble() * 0.4f)),
                new Vector2(width * (0.3f + (float)rng.NextDouble() * 0.4f), inset),
                new Vector2(width * (0.3f + (float)rng.NextDouble() * 0.4f), height - inset),
            };

            Vector2 best = candidates[0];
            float bestDist = -1f;
            foreach (Vector2 candidate in candidates)
            {
                float dist = Vector2.Distance(candidate, away);
                if (dist > bestDist)
                {
                    bestDist = dist;
                    best = candidate;
                }
            }

            return best;
        }

        static Vector2 PickCornerAnchor(int width, int height, System.Random rng, params Vector2[] away)
        {
            float inset = Mathf.Min(width, height) * 0.22f;
            Vector2[] corners =
            {
                new Vector2(inset, inset),
                new Vector2(width - inset, inset),
                new Vector2(inset, height - inset),
                new Vector2(width - inset, height - inset),
            };

            Vector2 best = corners[0];
            float bestScore = float.MinValue;
            foreach (Vector2 corner in corners)
            {
                float score = 0f;
                foreach (Vector2 other in away)
                {
                    score += Vector2.Distance(corner, other);
                }

                // Small random tiebreak, so the bluff is not always in the same corner when the
                // other regions happen to land symmetrically.
                score += (float)rng.NextDouble() * 4f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = corner;
                }
            }

            return best;
        }

        static Vector2 PickOpenAnchor(int width, int height, System.Random rng, List<RegionSeed> taken, float minDistance)
        {
            Vector2 best = new Vector2(width * 0.5f, height * 0.5f);
            float bestDist = -1f;

            // Best-candidate sampling: throw darts, keep the one furthest from everything placed.
            for (int attempt = 0; attempt < 48; attempt++)
            {
                Vector2 candidate = new Vector2(
                    Mathf.Lerp(width * 0.18f, width * 0.82f, (float)rng.NextDouble()),
                    Mathf.Lerp(height * 0.18f, height * 0.82f, (float)rng.NextDouble()));

                float nearest = float.MaxValue;
                foreach (RegionSeed seed in taken)
                {
                    nearest = Mathf.Min(nearest, Vector2.Distance(candidate, seed.centre));
                }

                if (nearest > bestDist)
                {
                    bestDist = nearest;
                    best = candidate;
                }

                if (nearest > minDistance * 1.6f)
                {
                    break;
                }
            }

            return best;
        }
    }
}
