using System.Collections.Generic;
using UnityEngine;

namespace DuckRoulette.MapGen
{
    /// <summary>
    /// Fills the map from a scatter table.
    ///
    /// Every prop on the map comes from a <see cref="ScatterRule"/>: what to spawn, how many, which
    /// regions it may appear in, how big it ends up and how much room it needs. Adding content means
    /// adding a rule, not editing this file.
    ///
    /// Two things are enforced here rather than left to luck:
    ///
    ///   * <b>Nothing intersects.</b> Each placed prop records the radius of its own footprint, and
    ///     a candidate is rejected unless it clears every existing footprint plus a margin.
    ///   * <b>Hide-and-seek spacing.</b> The game is hide and seek, so the distance between two
    ///     hiding places is a balance number. Two hiding spots too close together let one sweep by
    ///     the seeker clear both; too far apart and a spotted hider has nowhere to rotate to. The
    ///     role-to-role spacing table below is the lever for that, and it is deliberately separate
    ///     from the footprint check.
    /// </summary>
    [System.Serializable]
    public class MapPopulator
    {
        [Header("Scatter table")]
        [Tooltip("Everything the map spawns. Order matters: earlier rules get first pick of space, " +
                 "so landmarks and cover should come before decor.")]
        public List<ScatterRule> rules = new List<ScatterRule>();

        [Header("Hide-and-seek spacing")]
        [Tooltip("Minimum distance between two hiding spots, in cells. Roughly the distance a " +
                 "seeker covers in two seconds: close enough that a hider can rotate between them, " +
                 "far enough that one sweep does not clear both.")]
        public float coverToCover = 14f;

        [Tooltip("Minimum distance between two challenge props, in cells. Challenges are " +
                 "destinations, so they want to be spread across different parts of the map.")]
        public float challengeToChallenge = 26f;

        [Tooltip("Minimum distance from a hiding spot to a challenge, in cells. A challenge draws " +
                 "the seeker in and makes noise; cover sitting right next to one is too strong.")]
        public float coverToChallenge = 9f;

        [Tooltip("Minimum distance between two landmarks, in cells, so structures read as separate " +
                 "places rather than one village.")]
        public float landmarkToLandmark = 12f;

        [Tooltip("Gap left between any two footprints, in cells, on top of their own radii.")]
        public float generalClearance = 1f;

        [Header("Budget")]
        [Tooltip("Placement attempts per wanted instance before a rule gives up.")]
        public int attemptsPerInstance = 120;

        /// <summary>One placed prop, kept so later placements can avoid it.</summary>
        struct Placement
        {
            public Vector2 position;
            public float radius;
            public PropRole role;
        }

        readonly List<Placement> placed = new List<Placement>();

        /// <summary>Water surface for the current run, in normalised height. Nothing is placed at or
        /// below it.</summary>
        float waterLevel = float.NegativeInfinity;

        /// <summary>Middle of each region for the current run, in cells.</summary>
        readonly Dictionary<RegionType, Vector2> regionCentres = new Dictionary<RegionType, Vector2>();

        /// <summary>How far above the water surface a footprint must stay, in normalised height.</summary>
        const float WaterMargin = 0.03f;

        /// <summary>What each rule managed to place on the last run, for reporting.</summary>
        public Dictionary<string, int> LastCounts { get; private set; } = new Dictionary<string, int>();

        /// <summary>
        /// Reserves a circle so the scatter avoids it. Used for the river props, which are placed
        /// by the river generator before the scatter table runs.
        /// </summary>
        public void Reserve(Vector2 position, float radius, PropRole role = PropRole.Landmark)
        {
            placed.Add(new Placement { position = position, radius = radius, role = role });
        }

        public void Clear()
        {
            placed.Clear();
            LastCounts = new Dictionary<string, int>();
        }

        /// <summary>Runs every rule in order.</summary>
        /// <param name="waterSurface">River surface in normalised height. Nothing is placed where any
        /// part of its footprint is at or below it.</param>
        public void Populate(Transform parent, float[,] heightMap, RegionType[,] regionMap,
            float[,] pollution, float heightMultiplier, System.Random rng,
            float waterSurface = float.NegativeInfinity)
        {
            LastCounts = new Dictionary<string, int>();
            waterLevel = waterSurface;

            // Cells grouped by region once rather than per rule: the scan is the expensive part.
            var cellsByRegion = new Dictionary<RegionType, List<Vector2Int>>();
            int width = regionMap.GetLength(0);
            int height = regionMap.GetLength(1);

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    RegionType region = regionMap[x, y];
                    if (!cellsByRegion.TryGetValue(region, out List<Vector2Int> list))
                    {
                        list = new List<Vector2Int>();
                        cellsByRegion[region] = list;
                    }

                    list.Add(new Vector2Int(x, y));
                }
            }

            regionCentres.Clear();
            foreach (KeyValuePair<RegionType, List<Vector2Int>> pair in cellsByRegion)
            {
                Vector2 sum = Vector2.zero;
                foreach (Vector2Int cell in pair.Value)
                {
                    sum += cell;
                }

                regionCentres[pair.Key] = sum / Mathf.Max(1, pair.Value.Count);
            }

            foreach (ScatterRule rule in rules)
            {
                if (rule == null || !rule.enabled)
                {
                    continue;
                }

                int spawned = RunRule(rule, parent, heightMap, regionMap, pollution, heightMultiplier,
                    cellsByRegion, rng);
                LastCounts[rule.label] = spawned;
            }
        }

        int RunRule(ScatterRule rule, Transform parent, float[,] heightMap, RegionType[,] regionMap,
            float[,] pollution, float heightMultiplier, Dictionary<RegionType, List<Vector2Int>> cellsByRegion,
            System.Random rng)
        {
            var candidates = new List<Vector2Int>();
            foreach (KeyValuePair<RegionType, List<Vector2Int>> pair in cellsByRegion)
            {
                if (rule.Allows(pair.Key))
                {
                    candidates.AddRange(pair.Value);
                }
            }

            if (candidates.Count == 0 || rule.prefabs == null || rule.prefabs.Length == 0)
            {
                return 0;
            }

            int width = heightMap.GetLength(0);
            int height = heightMap.GetLength(1);

            // Drop cells inside the edge margin up front. An edge-hugging rule walks its candidate
            // list in order, so leaving rejects in the list burns the whole attempt budget on cells
            // that were never going to pass.
            candidates.RemoveAll(cell => EdgeDistance(cell, width, height) < rule.edgeMargin);
            if (candidates.Count == 0)
            {
                Debug.LogWarning($"MapPopulator: '{rule.label}' has no cells left once its edge " +
                                 $"margin of {rule.edgeMargin} is applied.");
                return 0;
            }

            // Edge-hugging rules walk their candidates from the boundary inwards instead of sampling.
            if (rule.hugEdge)
            {
                candidates.Sort((a, b) => EdgeDistance(a, width, height).CompareTo(EdgeDistance(b, width, height)));
            }

            int wanted = rule.RollCount(rng);
            int attempts = Mathf.Max(1, wanted) * Mathf.Max(1, attemptsPerInstance);
            int spawned = 0;
            int ownStart = placed.Count;

            for (int attempt = 0; attempt < attempts && spawned < wanted; attempt++)
            {
                Vector2Int cell = rule.hugEdge
                    ? candidates[Mathf.Min(attempt, candidates.Count - 1)]
                    : candidates[rng.Next(candidates.Count)];

                Vector2 point = new Vector2(cell.x + (float)rng.NextDouble(), cell.y + (float)rng.NextDouble());

                if (rule.edgeSnap > 0f)
                {
                    point = SnapToEdge(point, width, height, rule.edgeSnap);
                }

                if (TerrainShaper.Slope(heightMap, Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.y)) > rule.maxSlope)
                {
                    continue;
                }

                GameObject prefab = rule.PickPrefab(rng);
                if (prefab == null)
                {
                    continue;
                }

                float jitter = Mathf.Lerp(rule.scaleJitter.x, rule.scaleJitter.y, (float)rng.NextDouble());
                float radius = FootprintRadius(prefab, rule, jitter) + rule.footprintPadding;

                if (!IsClear(point, radius, rule, ownStart) || NearWater(point, radius, heightMap, regionMap))
                {
                    continue;
                }

                float yaw = PickYaw(rule, point, regionMap, rng);

                // A door needs somewhere to walk up to: the patch in front of it has to be dry and
                // free, and it is reserved once the prop is placed.
                Vector2 front = point;
                float frontRadius = rule.frontClearance * 0.5f;
                if (frontRadius > 0f)
                {
                    front = point + YawDirection(yaw) * (radius + frontRadius);
                    if (!IsClear(front, frontRadius, rule, ownStart) || NearWater(front, frontRadius, heightMap, regionMap))
                    {
                        continue;
                    }
                }

                if (rule.flattenRadius > 0f)
                {
                    TerrainShaper.Flatten(heightMap, point, rule.flattenRadius, 0.6f);
                }

                // The base height is taken before any wall snap, so a snapped prop stays level with
                // the ground it was placed on while its back sinks into the wall.
                float? fixedGround = null;
                if (rule.snapToWall)
                {
                    fixedGround = TerrainShaper.SampleHeight(heightMap, point.x, point.y) * heightMultiplier;
                    point = SnapToWall(point, yaw, ScaledBounds(prefab, rule, jitter), fixedGround.Value,
                        rule.wallBury, heightMap, heightMultiplier);
                }

                GameObject instance = Spawn(prefab, parent, rule, point, yaw, jitter, heightMap,
                    heightMultiplier, fixedGround, rng);
                if (instance == null)
                {
                    continue;
                }

                if (frontRadius > 0f)
                {
                    placed.Add(new Placement { position = front, radius = frontRadius, role = PropRole.Decor });
                }

                if (rule.pollutionRadius > 0f && pollution != null)
                {
                    StampPollution(pollution, point, rule.pollutionRadius);
                }

                placed.Add(new Placement { position = point, radius = radius, role = rule.role });
                spawned++;
            }

            if (spawned < wanted)
            {
                Debug.LogWarning($"MapPopulator: '{rule.label}' placed {spawned} of {wanted}. " +
                                 "Either its regions are too small or its spacing is too generous.");
            }

            return spawned;
        }

        GameObject Spawn(GameObject prefab, Transform parent, ScatterRule rule, Vector2 point, float yaw,
            float jitter, float[,] heightMap, float heightMultiplier, float? fixedGround, System.Random rng)
        {
            GameObject instance = MapSpawner.Spawn(prefab, parent);
            if (instance == null)
            {
                return null;
            }

            float ground = fixedGround ?? TerrainShaper.SampleHeight(heightMap, point.x, point.y) * heightMultiplier;
            instance.transform.localPosition = new Vector3(point.x, ground + rule.yOffset, point.y);

            if (rule.alignToGround)
            {
                Vector3 normal = GroundNormal(heightMap, point, heightMultiplier);
                instance.transform.localRotation = Quaternion.AngleAxis(yaw, Vector3.up)
                                                   * Quaternion.FromToRotation(Vector3.up, normal);
            }
            else
            {
                instance.transform.localRotation = Quaternion.Euler(
                    (float)(rng.NextDouble() - 0.5) * rule.tiltDegrees,
                    yaw,
                    (float)(rng.NextDouble() - 0.5) * rule.tiltDegrees);
            }

            ApplyScale(instance, rule, jitter);

            if (rule.sitOnBase)
            {
                SitOnBase(instance);
            }

            if (rule.addMeshCollider)
            {
                AddMeshColliders(instance, false);
            }

            if (rule.physicsBody)
            {
                MakePhysical(instance, rule.physicsDensity);
            }
            else
            {
                instance.isStatic = true;
            }

            return instance;
        }

        float PickYaw(ScatterRule rule, Vector2 point, RegionType[,] regionMap, System.Random rng)
        {
            int width = regionMap.GetLength(0);
            int height = regionMap.GetLength(1);

            if (rule.snapToWall)
            {
                // Exactly square to the wall, or one side of the prop floats off it.
                return EdgeFacingYaw(point, width, height);
            }

            if (rule.faceRegionCentre)
            {
                RegionType region = RegionAt(regionMap, point);
                if (regionCentres.TryGetValue(region, out Vector2 centre) && (centre - point).sqrMagnitude > 0.01f)
                {
                    Vector2 toCentre = centre - point;
                    return Mathf.Atan2(toCentre.x, toCentre.y) * Mathf.Rad2Deg
                           + (float)(rng.NextDouble() - 0.5) * 30f;
                }
            }

            if (rule.faceAwayFromEdge)
            {
                return EdgeFacingYaw(point, width, height) + (float)(rng.NextDouble() - 0.5) * 20f;
            }

            return (float)rng.NextDouble() * 360f;
        }

        /// <summary>Local +Z of a prop with this yaw, in cell coordinates.</summary>
        static Vector2 YawDirection(float yaw)
        {
            float radians = yaw * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        }

        static RegionType RegionAt(RegionType[,] regionMap, Vector2 point)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt(point.x), 0, regionMap.GetLength(0) - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(point.y), 0, regionMap.GetLength(1) - 1);
            return regionMap[x, y];
        }

        /// <summary>
        /// True when any part of a footprint touches the river: a water cell, or ground at or below
        /// the water surface. The centre and a ring at the footprint edge are both checked, so a
        /// wide prop cannot hang its edge out over the water.
        /// </summary>
        bool NearWater(Vector2 point, float radius, float[,] heightMap, RegionType[,] regionMap)
        {
            const int ringSamples = 12;
            for (int i = 0; i <= ringSamples; i++)
            {
                Vector2 sample = point;
                if (i > 0)
                {
                    float angle = i * Mathf.PI * 2f / ringSamples;
                    sample += new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Mathf.Max(radius, 0.5f);
                }

                if (RegionAt(regionMap, sample) == RegionType.River)
                {
                    return true;
                }

                if (TerrainShaper.SampleHeight(heightMap, sample.x, sample.y) <= waterLevel + WaterMargin)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Slides a point towards the nearest map edge until the back of a prop of this size meets
        /// ground as high as the middle of the prop - the face of the border wall - then pushes it
        /// a little further so the back is buried rather than just touching.
        /// </summary>
        static Vector2 SnapToWall(Vector2 point, float yaw, Bounds size, float baseHeight, float bury,
            float[,] heightMap, float heightMultiplier)
        {
            Vector2 outward = -YawDirection(yaw);
            // Distance from the pivot back to the rear face. Measured from the bounds rather than
            // assumed to be half the depth: pipe models tend to have their pivot at one end.
            float backDistance = -size.min.z;
            float wallHeight = baseHeight + size.center.y;
            const float step = 0.25f;
            const float maxTravel = 30f;

            for (float travel = 0f; travel <= maxTravel; travel += step)
            {
                Vector2 back = point + outward * (backDistance + travel);
                if (TerrainShaper.SampleHeight(heightMap, back.x, back.y) * heightMultiplier >= wallHeight)
                {
                    return point + outward * (travel + size.size.z * bury);
                }
            }

            return point;
        }

        /// <summary>The prefab bounds once scaled the way this rule will scale it, in cells.</summary>
        static Bounds ScaledBounds(GameObject prefab, ScatterRule rule, float jitter)
        {
            Bounds bounds = MountainRingBuilder.GetLocalBounds(prefab);
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float scale = rule.sizeTarget > 0f && longest > 0.0001f ? rule.sizeTarget / longest * jitter : jitter;
            return new Bounds(bounds.center * scale, bounds.size * scale);
        }

        /// <summary>
        /// Moves a prop up or down so the lowest point of its meshes is at its position.
        ///
        /// Worked out from the mesh bounds rather than Renderer.bounds, which can still describe
        /// the prefab's old transform straight after the prop has been scaled.
        /// </summary>
        static void SitOnBase(GameObject instance)
        {
            Bounds local = MountainRingBuilder.GetLocalBounds(instance);
            Transform transform = instance.transform;
            Matrix4x4 toParent = Matrix4x4.TRS(transform.localPosition, transform.localRotation, transform.localScale);

            float lowest = float.MaxValue;
            for (int corner = 0; corner < 8; corner++)
            {
                var offset = new Vector3(
                    (corner & 1) == 0 ? -local.extents.x : local.extents.x,
                    (corner & 2) == 0 ? -local.extents.y : local.extents.y,
                    (corner & 4) == 0 ? -local.extents.z : local.extents.z);
                lowest = Mathf.Min(lowest, toParent.MultiplyPoint3x4(local.center + offset).y);
            }

            transform.localPosition += Vector3.up * (transform.localPosition.y - lowest);
        }

        /// <summary>Adds a MeshCollider to every mesh of a prop that has no collider at all.</summary>
        static void AddMeshColliders(GameObject instance, bool convex)
        {
            if (instance.GetComponentInChildren<Collider>(true) != null)
            {
                return;
            }

            foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                var collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = convex;
            }
        }

        /// <summary>
        /// Turns a prop into a loose physics object: convex colliders, a Rigidbody weighted by its
        /// size, and nothing marked static.
        /// </summary>
        static void MakePhysical(GameObject instance, float density)
        {
            AddMeshColliders(instance, true);

            // A non-convex MeshCollider cannot sit under a moving Rigidbody.
            foreach (MeshCollider collider in instance.GetComponentsInChildren<MeshCollider>(true))
            {
                collider.convex = true;
            }

            foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.isStatic = false;
            }

            Rigidbody body = instance.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = instance.AddComponent<Rigidbody>();
            }

            Vector3 size = Vector3.Scale(MountainRingBuilder.GetLocalBounds(instance).size, instance.transform.lossyScale);
            float volume = Mathf.Abs(size.x * size.y * size.z);
            body.mass = Mathf.Clamp(volume * density, 5f, 5000f);
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        static void ApplyScale(GameObject instance, ScatterRule rule, float jitter)
        {
            if (rule.sizeTarget <= 0f)
            {
                instance.transform.localScale = Vector3.one * jitter;
                return;
            }

            instance.transform.localScale = Vector3.one;
            Bounds bounds = MountainRingBuilder.GetLocalBounds(instance);
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (longest < 0.0001f)
            {
                instance.transform.localScale = Vector3.one * jitter;
                return;
            }

            instance.transform.localScale = Vector3.one * (rule.sizeTarget / longest * jitter);
        }

        /// <summary>
        /// Radius of the circle that contains the prop's footprint once it has been scaled to fit.
        ///
        /// Half the diagonal, not half the longest side: the prop is rotated to a random yaw, so
        /// the circle has to contain the footprint at every angle. Using the longest side lets two
        /// props whose corners face each other clip.
        /// </summary>
        public static float FootprintRadius(GameObject prefab, ScatterRule rule, float jitter)
        {
            Bounds bounds = MountainRingBuilder.GetLocalBounds(prefab);
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float diagonal = Mathf.Sqrt(bounds.size.x * bounds.size.x + bounds.size.z * bounds.size.z);

            if (rule.sizeTarget > 0f && longest > 0.0001f)
            {
                return diagonal * 0.5f * (rule.sizeTarget / longest * jitter);
            }

            return diagonal * 0.5f * jitter;
        }

        bool IsClear(Vector2 point, float radius, ScatterRule rule, int ownStart)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                Placement other = placed[i];
                float distance = Vector2.Distance(point, other.position);

                // Footprints must never overlap, whatever the roles are.
                float required = radius + other.radius + generalClearance;

                // On top of that, role pairs have a design distance.
                required = Mathf.Max(required, RoleSpacing(rule.role, other.role));

                // And a rule can insist its own instances stay apart.
                if (i >= ownStart)
                {
                    required = Mathf.Max(required, rule.selfSpacing);
                }

                if (distance < required)
                {
                    return false;
                }
            }

            return true;
        }

        float RoleSpacing(PropRole a, PropRole b)
        {
            if (a == PropRole.Cover && b == PropRole.Cover)
            {
                return coverToCover;
            }

            if (a == PropRole.Challenge && b == PropRole.Challenge)
            {
                return challengeToChallenge;
            }

            if ((a == PropRole.Cover && b == PropRole.Challenge) || (a == PropRole.Challenge && b == PropRole.Cover))
            {
                return coverToChallenge;
            }

            if (a == PropRole.Landmark && b == PropRole.Landmark)
            {
                return landmarkToLandmark;
            }

            return 0f;
        }

        static void StampPollution(float[,] pollution, Vector2 centre, float radius)
        {
            int width = pollution.GetLength(0);
            int height = pollution.GetLength(1);

            int minX = Mathf.Max(0, Mathf.FloorToInt(centre.x - radius));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(centre.x + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(centre.y - radius));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(centre.y + radius));

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), centre);
                    if (distance > radius)
                    {
                        continue;
                    }

                    float strength = 1f - Mathf.SmoothStep(0f, 1f, distance / radius);
                    pollution[x, y] = Mathf.Max(pollution[x, y], strength);
                }
            }
        }

        static Vector3 GroundNormal(float[,] heightMap, Vector2 point, float heightMultiplier)
        {
            int x = Mathf.RoundToInt(point.x);
            int y = Mathf.RoundToInt(point.y);
            int width = heightMap.GetLength(0);
            int height = heightMap.GetLength(1);

            int xa = Mathf.Max(0, x - 1);
            int xb = Mathf.Min(width - 1, x + 1);
            int ya = Mathf.Max(0, y - 1);
            int yb = Mathf.Min(height - 1, y + 1);

            float dx = (heightMap[xb, y] - heightMap[xa, y]) * heightMultiplier / Mathf.Max(1, xb - xa);
            float dy = (heightMap[x, yb] - heightMap[x, ya]) * heightMultiplier / Mathf.Max(1, yb - ya);
            return new Vector3(-dx, 1f, -dy).normalized;
        }

        /// <summary>
        /// Moves a point so it sits exactly <paramref name="distance"/> cells from whichever map
        /// edge it is closest to, keeping its position along that edge. Used to push the pipe
        /// outfall back until its wall meets the mountains.
        /// </summary>
        static Vector2 SnapToEdge(Vector2 point, int width, int height, float distance)
        {
            float left = point.x;
            float right = width - 1 - point.x;
            float bottom = point.y;
            float top = height - 1 - point.y;
            float nearest = Mathf.Min(Mathf.Min(left, right), Mathf.Min(bottom, top));

            if (Mathf.Approximately(nearest, left))
            {
                return new Vector2(distance, point.y);
            }

            if (Mathf.Approximately(nearest, right))
            {
                return new Vector2(width - 1 - distance, point.y);
            }

            if (Mathf.Approximately(nearest, bottom))
            {
                return new Vector2(point.x, distance);
            }

            return new Vector2(point.x, height - 1 - distance);
        }

        static float EdgeDistance(Vector2Int cell, int width, int height)
        {
            return Mathf.Min(Mathf.Min(cell.x, width - 1 - cell.x), Mathf.Min(cell.y, height - 1 - cell.y));
        }

        static float EdgeFacingYaw(Vector2 point, int width, int height)
        {
            float left = point.x;
            float right = width - 1 - point.x;
            float bottom = point.y;
            float top = height - 1 - point.y;
            float nearest = Mathf.Min(Mathf.Min(left, right), Mathf.Min(bottom, top));

            // Point the prop inwards, away from whichever edge it is closest to.
            if (Mathf.Approximately(nearest, left))
            {
                return 90f;
            }

            if (Mathf.Approximately(nearest, right))
            {
                return 270f;
            }

            return Mathf.Approximately(nearest, bottom) ? 0f : 180f;
        }
    }
}
