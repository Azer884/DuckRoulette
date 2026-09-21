using System.Collections.Generic;
using UnityEngine;

namespace DuckRoulette.MapGen
{
    /// <summary>
    /// Carves the river and places the two things that belong to it: the crossing and the outfall
    /// that marks the waterline.
    ///
    /// The river is the one feature that crosses the whole map, so it is cut before anything else
    /// and every later decision - regions, paths, landmarks - is made around it.
    ///
    /// Three passes make the channel read as water rather than as a ditch:
    ///   1. a wide soft pass drags the banks down into a valley;
    ///   2. a narrow pass cuts the channel floor well below the waterline;
    ///   3. the waterline is derived from the finished heights, so the shore is wherever the bed
    ///      crosses the water plane rather than a fixed radius around the spline.
    /// </summary>
    public class RiverPtGenerator : MonoBehaviour
    {
        [Header("Channel")]
        [Tooltip("Radius of the carved channel, in cells.")]
        public float riverWidth = 9f;

        [Tooltip("Height the channel floor is cut to, in normalised height units. This is the " +
                 "bottom of the river bed, not the water surface.")]
        public float riverValue = -1.6f;

        [Tooltip("How far the water surface sits above the channel floor, in normalised height " +
                 "units. This is what makes the water plane visible: the bed has to be below it.")]
        public float waterDepth = 0.95f;

        [Tooltip("How far the banks are kept above the water surface, in normalised height units.")]
        public float bankFreeboard = 0.35f;

        [Tooltip("How far past the channel the banks are dragged down, as a multiple of the width.")]
        public float bankWidthMultiplier = 2f;

        [Header("Crossing")]
        [Tooltip("The bridge. Exactly one is placed, at the middle of the river.")]
        public GameObject bridgePrefab;

        [Tooltip("Fallback span in cells, used only when the water width cannot be measured.")]
        public float bridgeSize = 26f;

        [Tooltip("Measures the actual water width under the crossing and sizes the bridge to it, so " +
                 "the deck lands on dry ground at both ends instead of stopping short in the water.")]
        public bool bridgeSpansBanks = true;

        [Tooltip("How far past each bank the deck reaches, in cells.")]
        public float bridgeBankOverlap = 5.5f;

        [Tooltip("Multiplier on the measured span. A little over one makes the deck bed into the " +
                 "banks instead of ending exactly at the waterline.")]
        public float bridgeSpanScale = 1.15f;

        [Tooltip("How far the deck is pushed down into the bank, in world units, so its ends meet " +
                 "the ground rather than hovering over it.")]
        public float bridgeDeckSink = 0.35f;

        [Tooltip("Extra yaw applied to the bridge, in degrees. The bridge is aimed with its forward " +
                 "along the local flow, which puts its long horizontal axis across the water - that " +
                 "is the span. Set 90 only if the model is built the other way round.")]
        public float bridgeYawOffset;

        [Tooltip("How far above the water surface the bridge deck sits, in world units.")]
        public float bridgeClearance = 1.5f;

        [Header("Outfall")]
        [Tooltip("The tube the river runs out of at its source. Exactly one is placed, set into the " +
                 "boundary wall. It is scenery, not cover.")]
        public GameObject outfallPrefab;

        [Tooltip("Longest horizontal side of the placed outfall, in cells. Ignored when Match " +
                 "River Width is on.")]
        public float outfallSize = 9f;

        [Tooltip("Sizes the outfall to the full width of the channel, so it reads as the mouth the " +
                 "river runs out of rather than a pipe dropped in the water.")]
        public bool outfallMatchRiverWidth = true;

        [Tooltip("Extra yaw applied to the outfall, in degrees. It is aimed down the flow first, so " +
                 "180 turns it to face back up the river at the player.")]
        public float outfallYawOffset = 180f;

        [Tooltip("How far the bottom of the outfall sits above the water surface, in world units. " +
                 "Keeps it out of the riverbed and visible.")]
        public float outfallLift = 0.4f;

        [Tooltip("How far inside the map edge the outfall sits, in cells, measured to its centre. " +
                 "Smaller than half the outfall's own length on purpose: the back of the tube ends " +
                 "up inside the boundary wall and only its mouth shows.")]
        public float outfallInset = 8f;

        [Tooltip("Nudge applied to the outfall after it is placed, in the outfall's own local axes " +
                 "rather than world axes: Z runs along the tube, X across it, Y up. For hand-tuning " +
                 "how far it pokes out of the wall.")]
        public Vector3 outfallOffset;

        [Tooltip("Radius of the ground apron raised under the outfall, in cells. Zero leaves the " +
                 "riverbed alone and the outfall stands over open water.")]
        public float outfallGroundRadius = 11f;

        [Tooltip("How far the apron sits above the water surface, in normalised height units. It " +
                 "wants to be a little above the bottom of the model so the outfall beds into the " +
                 "ground rather than hovering over it.")]
        public float outfallGroundRise = 0.12f;

        /// <summary>Everything the rest of the generator needs to know about the carved river.</summary>
        public class RiverResult
        {
            public List<Vector3> controlPoints = new List<Vector3>();
            public List<Vector3> splinePoints = new List<Vector3>();
            public Vector3 start;
            public Vector3 end;
            public Vector3 mid;
            public float surfaceHeight;

            /// <summary>Cells on the spline, used to seed the water flood fill.</summary>
            public List<Vector2Int> waterSeeds = new List<Vector2Int>();

            /// <summary>Where the crossing ended up, so the outfall can keep away from it.</summary>
            public Vector3 bridgePosition;
            public bool hasBridge;
        }

        /// <summary>Picks a course, carves it and marks the cells it covers as river.</summary>
        public RiverResult Carve(float[,] heightMap, RegionType[,] regionMap, System.Random random)
        {
            int width = heightMap.GetLength(0);
            int height = heightMap.GetLength(1);
            var result = new RiverResult();

            // The river always crosses the whole map: it enters on one edge and leaves on another,
            // so it splits the play space instead of dead-ending in the middle of it.
            int side = random.Next(0, 2);
            int xRandom = random.Next(0, width);
            int yRandom = random.Next(0, height);
            int midPointCount = random.Next(1, 3);

            int xPos1, yPos1;
            if (side == 0)
            {
                xPos1 = xRandom < width / 2 ? 0 : width - 1;
                yPos1 = yRandom;
            }
            else
            {
                xPos1 = xRandom;
                yPos1 = yRandom < height / 2 ? 0 : height - 1;
            }

            int xPos2, yPos2;
            if (side == 0)
            {
                xPos2 = xPos1 == 0 ? width - 1 : 0;
                yPos2 = yPos1 < height / 2 ? random.Next(height / 2, height) : random.Next(0, height / 2);
            }
            else
            {
                yPos2 = yPos1 == 0 ? height - 1 : 0;
                xPos2 = xPos1 < width / 2 ? random.Next(width / 2, width) : random.Next(0, width / 2);
            }

            result.start = new Vector3(xPos1, 0f, yPos1);
            result.end = new Vector3(xPos2, 0f, yPos2);

            var controlPoints = new List<Vector3> { result.start };
            if (midPointCount == 1)
            {
                result.mid = (result.start + result.end) / 2f + new Vector3(
                    (float)(random.NextDouble() - 0.5f) * width * 0.5f,
                    0f,
                    (float)(random.NextDouble() - 0.5f) * height * 0.5f);
                controlPoints.Add(result.mid);
            }
            else
            {
                Vector3 direction = result.end - result.start;
                Vector3 midPoint1 = result.start + direction * 0.33f + new Vector3(
                    (float)(random.NextDouble() - 0.5f) * width * 0.3f,
                    0f,
                    (float)(random.NextDouble() - 0.5f) * height * 0.3f);
                Vector3 midPoint2 = result.start + direction * 0.66f + new Vector3(
                    (float)(random.NextDouble() - 0.5f) * width * 0.3f,
                    0f,
                    (float)(random.NextDouble() - 0.5f) * height * 0.3f);
                result.mid = (midPoint1 + midPoint2) / 2f;
                controlPoints.Add(midPoint1);
                controlPoints.Add(midPoint2);
            }

            controlPoints.Add(result.end);
            result.controlPoints = controlPoints;
            result.splinePoints = GetSplinePoints(controlPoints, 20);
            result.surfaceHeight = riverValue + waterDepth;

            // The valley first. Its floor is set just above the waterline, so the only thing that
            // ever cuts below the water is the channel pass that follows.
            foreach (Vector3 point in result.splinePoints)
            {
                StampBankAtPoint(heightMap, point, riverWidth * bankWidthMultiplier, result.surfaceHeight);
            }

            foreach (Vector3 point in result.splinePoints)
            {
                StampRiverAtPoint(heightMap, point, riverWidth);

                int seedX = Mathf.Clamp(Mathf.RoundToInt(point.x), 0, width - 1);
                int seedY = Mathf.Clamp(Mathf.RoundToInt(point.z), 0, height - 1);
                result.waterSeeds.Add(new Vector2Int(seedX, seedY));
            }

            TerrainShaper.ResolveWater(heightMap, regionMap, null, result.surfaceHeight, result.waterSeeds);
            return result;
        }

        /// <summary>
        /// Places the crossing and the outfall. Both are reserved with the populator so the scatter
        /// table keeps its distance and nothing ends up standing inside them.
        /// </summary>
        public void PlaceRiverProps(RiverResult river, Transform parent, float[,] heightMap,
            float heightMultiplier, MapPopulator populator)
        {
            PlaceBridge(river, parent, heightMap, heightMultiplier, populator);
            PlaceOutfall(river, parent, heightMap, heightMultiplier, populator);
        }

        void PlaceBridge(RiverResult river, Transform parent, float[,] heightMap, float heightMultiplier,
            MapPopulator populator)
        {
            if (bridgePrefab == null || river.splinePoints.Count < 3)
            {
                return;
            }

            // The middle of the spline, not the middle of the straight line between the ends: on a
            // bent river those are different places and the second one is often dry land.
            int middle = river.splinePoints.Count / 2;
            Vector3 point = river.splinePoints[middle];
            Vector3 tangent = (river.splinePoints[Mathf.Min(middle + 1, river.splinePoints.Count - 1)]
                               - river.splinePoints[Mathf.Max(middle - 1, 0)]).normalized;
            if (tangent.sqrMagnitude < 0.0001f)
            {
                tangent = Vector3.forward;
            }

            // Measure the water the bridge has to cross, out along the line it will sit on, and
            // size the deck to reach dry ground on both sides. A fixed span either stops short in
            // the water or overshoots into the trees, depending on where on the river it lands.
            Vector3 across = Vector3.Cross(tangent, Vector3.up).normalized;
            float span = bridgeSize;
            float deckHeight = river.surfaceHeight;

            if (bridgeSpansBanks)
            {
                float reach = riverWidth * 3f;
                float left = MeasureToBank(heightMap, point, -across, river.surfaceHeight, reach, out float leftHeight);
                float right = MeasureToBank(heightMap, point, across, river.surfaceHeight, reach, out float rightHeight);
                span = (left + right + bridgeBankOverlap * 2f) * bridgeSpanScale;

                // The lower of the two banks, not the higher: sitting the deck at the higher one
                // leaves the other end floating.
                deckHeight = Mathf.Min(leftHeight, rightHeight);

                // Centre the deck on the water rather than on the spline point: on a bend the
                // channel is not symmetrical about the line the spline passes through.
                point += across * ((right - left) * 0.5f);
            }

            GameObject bridge = MapSpawner.Spawn(bridgePrefab, parent);
            bridge.name = "Bridge";

            // Aimed along the flow, then turned across it. The offset is exposed because which
            // local axis is "long" depends on the model.
            Quaternion along = Quaternion.LookRotation(tangent, Vector3.up);
            bridge.transform.localRotation = along * Quaternion.Euler(0f, bridgeYawOffset, 0f);

            float deck = Mathf.Max(deckHeight * heightMultiplier - bridgeDeckSink,
                river.surfaceHeight * heightMultiplier + bridgeClearance);
            bridge.transform.localPosition = new Vector3(point.x, deck, point.z);
            river.bridgePosition = new Vector3(point.x, deck, point.z);
            river.hasBridge = true;
            FitToSize(bridge, span);
            bridge.isStatic = true;

            populator?.Reserve(new Vector2(point.x, point.z), span * 0.5f);
        }

        void PlaceOutfall(RiverResult river, Transform parent, float[,] heightMap, float heightMultiplier,
            MapPopulator populator)
        {
            if (outfallPrefab == null || river.splinePoints.Count < 2)
            {
                return;
            }

            // The river's source, not its mouth: the tube is what the water comes out of. Walking
            // forward from the first spline point, the first position far enough in from the edge
            // that the mouth clears the wall while the back of the tube stays buried in it.
            int width = heightMap.GetLength(0);
            int height = heightMap.GetLength(1);
            int index = 0;
            while (index < river.splinePoints.Count - 1)
            {
                Vector3 candidate = river.splinePoints[index];
                float edge = Mathf.Min(
                    Mathf.Min(candidate.x, width - 1 - candidate.x),
                    Mathf.Min(candidate.z, height - 1 - candidate.z));

                float toBridge = river.hasBridge
                    ? Vector2.Distance(new Vector2(candidate.x, candidate.z),
                        new Vector2(river.bridgePosition.x, river.bridgePosition.z))
                    : float.MaxValue;

                if (edge >= outfallInset && toBridge >= bridgeSize * 0.5f + riverWidth + 6f)
                {
                    break;
                }

                index++;
            }

            Vector3 point = river.splinePoints[index];

            // Aimed the way the water runs, so the tube reads as the source it flows out of.
            Vector3 tangent = (river.splinePoints[Mathf.Min(index + 4, river.splinePoints.Count - 1)] - point).normalized;
            if (tangent.sqrMagnitude < 0.0001f)
            {
                tangent = Vector3.forward;
            }

            GameObject outfall = MapSpawner.Spawn(outfallPrefab, parent);
            outfall.name = "RiverOutfall";
            outfall.transform.localRotation = Quaternion.LookRotation(tangent, Vector3.up)
                                              * Quaternion.Euler(0f, outfallYawOffset, 0f);

            // Matching the channel width is what makes it read as part of the river instead of a
            // prop standing in it.
            float size = outfallMatchRiverWidth ? riverWidth * 2f : outfallSize;
            outfall.transform.localPosition = new Vector3(point.x, 0f, point.z);
            FitToSize(outfall, size);

            // Raise abutments under it. The outfall sits over the channel, so without ground it
            // hangs in the air - but a solid pad would dam the river, so the strip the water runs
            // through is left alone and only the ground either side of it comes up.
            if (outfallGroundRadius > 0f)
            {
                StampOutfallAbutments(heightMap, new Vector2(point.x, point.z), new Vector2(tangent.x, tangent.z),
                    outfallGroundRadius, river.surfaceHeight + outfallGroundRise);
            }

            // Sit the bottom of the model on the waterline rather than its pivot, so it is never
            // half-buried in the riverbed no matter where the prefab's origin happens to be.
            float water = river.surfaceHeight * heightMultiplier;
            Bounds bounds = MountainRingBuilder.GetLocalBounds(outfall);
            float bottom = bounds.center.y - bounds.extents.y;
            // The nudge is applied in the outfall's own space, so X runs along the tube and Z across
            // it however the river happens to be pointing on this seed.
            Vector3 basePosition = new Vector3(
                point.x,
                water + outfallLift - bottom * outfall.transform.localScale.y,
                point.z);
            outfall.transform.localPosition = basePosition + outfall.transform.localRotation * outfallOffset;

            outfall.isStatic = true;

            Vector3 placed = outfall.transform.localPosition;
            populator?.Reserve(new Vector2(placed.x, placed.z), size * 0.6f);
        }

        /// <summary>Catmull-Rom sampling along the control points.</summary>
        public static List<Vector3> GetSplinePoints(List<Vector3> controlPoints, int subdivisionsPerSegment)
        {
            var padded = new List<Vector3> { controlPoints[0] };
            padded.AddRange(controlPoints);
            padded.Add(controlPoints[controlPoints.Count - 1]);

            var splinePoints = new List<Vector3>();
            for (int i = 0; i < padded.Count - 3; i++)
            {
                for (int j = 0; j <= subdivisionsPerSegment; j++)
                {
                    float t = j / (float)subdivisionsPerSegment;
                    splinePoints.Add(Noise.CatmullRom(padded[i], padded[i + 1], padded[i + 2], padded[i + 3], t));
                }
            }

            return splinePoints;
        }

        /// <summary>
        /// Scales a prop so its longest <b>horizontal</b> side is the target. Height is deliberately
        /// ignored: the bridge model is as tall as it is long, and sizing on the overall longest
        /// side shrinks the span to fit the railings.
        /// </summary>
        /// <summary>
        /// Distance from a point to the first dry cell along a direction, and the height of that
        /// cell. Used to find where the water actually ends, rather than assuming it ends at the
        /// carve radius.
        /// </summary>
        static float MeasureToBank(float[,] heightMap, Vector3 from, Vector3 direction, float surface,
            float maxDistance, out float bankHeight)
        {
            bankHeight = surface;
            for (float distance = 0f; distance <= maxDistance; distance += 0.5f)
            {
                Vector3 probe = from + direction * distance;
                float height = TerrainShaper.SampleHeight(heightMap, probe.x, probe.z);
                if (height > surface + 0.02f)
                {
                    bankHeight = height;
                    return distance;
                }
            }

            return maxDistance;
        }

        /// <summary>
        /// Cuts the channel again after the rest of the terrain has been shaped.
        ///
        /// Region pads, paths, the border ridge and the smoothing pass all stamp over the ground
        /// without knowing where the water is, and any one of them can fill the channel and leave
        /// the river in disconnected pools. Re-cutting is cheap, idempotent (the stamp only ever
        /// lowers ground) and guarantees the water runs from one map edge to the other.
        /// </summary>
        public void ReCarveChannel(float[,] heightMap, RiverResult river)
        {
            foreach (Vector3 point in river.splinePoints)
            {
                StampRiverAtPoint(heightMap, point, riverWidth);
            }
        }

        static void FitToSize(GameObject instance, float targetSize)
        {
            if (targetSize <= 0f)
            {
                return;
            }

            instance.transform.localScale = Vector3.one;
            Bounds bounds = MountainRingBuilder.GetLocalBounds(instance);
            float span = Mathf.Max(bounds.size.x, bounds.size.z);
            if (span > 0.0001f)
            {
                instance.transform.localScale = Vector3.one * (targetSize / span);
            }
        }

        /// <summary>
        /// Raises the ground on both sides of the channel under the outfall, leaving the water's own
        /// width clear so the river still runs through it.
        /// </summary>
        void StampOutfallAbutments(float[,] heightMap, Vector2 centre, Vector2 flow, float radius, float target)
        {
            int width = heightMap.GetLength(0);
            int height = heightMap.GetLength(1);
            Vector2 along = flow.sqrMagnitude > 0.0001f ? flow.normalized : Vector2.up;
            Vector2 across = new Vector2(-along.y, along.x);
            float gap = riverWidth * 0.75f;

            int minX = Mathf.Max(0, Mathf.FloorToInt(centre.x - radius));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(centre.x + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(centre.y - radius));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(centre.y + radius));

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    Vector2 offset = new Vector2(x, y) - centre;
                    float distance = offset.magnitude;
                    if (distance > radius)
                    {
                        continue;
                    }

                    // How far off the centre line of the flow this cell is. Inside the gap is water.
                    float lateral = Mathf.Abs(Vector2.Dot(offset, across));
                    if (lateral < gap)
                    {
                        continue;
                    }

                    float blend = Mathf.Clamp01((lateral - gap) / Mathf.Max(0.001f, radius - gap));
                    blend *= 1f - Mathf.SmoothStep(0.6f, 1f, distance / radius);
                    heightMap[x, y] = Mathf.Max(heightMap[x, y], Mathf.Lerp(heightMap[x, y], target, blend));
                }
            }
        }

        void StampRiverAtPoint(float[,] heightMap, Vector3 position, float radius)
        {
            int width = heightMap.GetLength(0);
            int height = heightMap.GetLength(1);

            int minX = Mathf.Max(0, Mathf.FloorToInt(position.x - radius));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(position.x + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(position.z - radius));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(position.z + radius));

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(position.x, position.z));
                    if (dist > radius)
                    {
                        continue;
                    }

                    // A single smooth cosine from the floor at the centre to nothing at the rim.
                    // No flat section and no hard break: the bed has to pass through the waterline
                    // at a shallow angle or the shoreline renders as a stair-stepped wall.
                    float t = dist / radius;
                    float blend = 0.5f * (1f + Mathf.Cos(t * Mathf.PI));
                    heightMap[x, y] = Mathf.Min(heightMap[x, y], Mathf.Lerp(heightMap[x, y], riverValue, blend));
                }
            }
        }

        void StampBankAtPoint(float[,] heightMap, Vector3 position, float radius, float surface)
        {
            int width = heightMap.GetLength(0);
            int height = heightMap.GetLength(1);

            int minX = Mathf.Max(0, Mathf.FloorToInt(position.x - radius));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(position.x + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(position.z - radius));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(position.z + radius));

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(position.x, position.z));
                    if (dist > radius)
                    {
                        continue;
                    }

                    // A gentle cosine shoulder down to a valley floor that sits just above the
                    // waterline. Dragging it below the water instead would flood the whole valley,
                    // which is what a bank target derived from the bed depth used to do.
                    float t = dist / radius;
                    float blend = 0.5f * (1f + Mathf.Cos(t * Mathf.PI)) * 0.5f;
                    heightMap[x, y] = Mathf.Lerp(heightMap[x, y], surface + bankFreeboard, blend);
                }
            }
        }

    }
}
