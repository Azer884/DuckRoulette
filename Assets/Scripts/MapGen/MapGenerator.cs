using System.Collections.Generic;
using UnityEngine;

namespace DuckRoulette.MapGen
{
    /// <summary>
    /// Builds the whole playable map from one seed.
    ///
    /// The order matters and is the design, not an implementation detail:
    ///
    ///   1. noise gives the raw ground;
    ///   2. the river is cut first, because it is the one feature that crosses everything and
    ///      every later decision wants to know where it runs;
    ///   3. regions are laid out around the river, each one placed for its role;
    ///   4. the terrain is shaped per region - the clearing is levelled, the bluff is raised, the
    ///      marsh is sunk - so each region has a silhouette you can recognise from a distance;
    ///   5. paths are carved between region centres so the map is one connected space;
    ///   6. the border ridge goes up;
    ///   7. the bridge and the outfall are placed, because they are fixed points the scatter has
    ///      to work around;
    ///   8. the scatter table fills the regions, avoiding everything already placed;
    ///   9. only then is the ground mesh built, so props that flatten the ground under themselves
    ///      are already accounted for;
    ///  10. mountains go on last, standing on the finished border ridge.
    ///
    /// The whole map is snow. Which snow is what tells the regions apart: see
    /// <see cref="RegionMarker"/> for the ground type each region gets.
    /// </summary>
    [RequireComponent(typeof(RiverPtGenerator))]
    public class MapGenerator : MonoBehaviour
    {
        [Header("Grid")]
        public int width = 100;
        public int height = 100;

        [Tooltip("World units per height unit. The height map itself is normalised.")]
        public float heightMultiplier = 5f;

        [Tooltip("Ground noise. Defaults are tuned for a 100 cell map: enough octaves to give " +
                 "rolling cover without the single smooth blob a large scale produces at this size.")]
        public NoiseSettings noiseSettings = new NoiseSettings
        {
            normalizeMode = Noise.NormalizeMode.Local,
            scale = 26f,
            octaves = 5,
            persistance = 0.45f,
            lacunarity = 2.1f,
        };

        [Header("Rendering")]
        public Material terrainMaterial;
        public Material waterMaterial;

        [Tooltip("Bakes the snow type of each region, the height shading and the pollution stains " +
                 "into a texture on the terrain. Editor only; writes Assets/Generated/RegionMap.png " +
                 "and a material beside it.")]
        public bool paintRegionTexture = true;

        [Tooltip("Colours and shading of the painted ground. Press Repaint terrain to apply a " +
                 "change without regenerating the map.")]
        public TerrainPalette terrainPalette = new TerrainPalette();

        [Tooltip("Optional. Kept so the original preview renderer still works.")]
        public MapDisplay display;

        [Header("Region shaping")]
        [Tooltip("Radius of the bluff, in cells.")]
        public float highgroundRadius = 17f;

        [Tooltip("Fraction of the bluff that is dead flat. The rest is the skirt.")]
        [Range(0.1f, 0.9f)]
        public float highgroundCoreFraction = 0.55f;

        [Tooltip("How far the bluff is lifted, in normalised height units.")]
        public float highgroundLift = 1.9f;

        [Tooltip("Width of the single ramp onto the bluff, in cells. One way up is what makes the " +
                 "highground a position worth holding.")]
        public float highgroundRampWidth = 5f;

        public float clearingRadius = 14f;
        public float campPadRadius = 11f;
        public float marshDepth = 0.3f;
        public float pipeYardRadius = 9f;

        [Header("Paths")]
        public float pathWidth = 4f;
        public float pathWobble = 6f;

        [Header("Border")]
        [Tooltip("How many cells at the map edge rise into the mountain ridge.")]
        public float borderThickness = 14f;

        [Tooltip("How high the border ridge rises, in normalised height units.")]
        public float borderHeight = 2.6f;

        [Header("Passes")]
        public int smoothIterations = 2;

        public bool generateOnStart = true;

        [Tooltip("Rolls a fresh seed every time the game starts, so each launch is a different map. " +
                 "Turn it off to keep playing the seed typed into the noise settings - which is what " +
                 "you want while tuning, because otherwise nothing is reproducible.")]
        public bool randomiseSeedOnStart;

        public bool buildMountains = true;
        public bool buildWater = true;

        [Header("Content")]
        public MountainRingBuilder mountains = new MountainRingBuilder();
        public MapPopulator populator = new MapPopulator();

        public float[,] HeightMap { get; private set; }
        public RegionType[,] RegionMap { get; private set; }

        /// <summary>How dirty the snow is, 0 to 1. Driven by the pipe outfalls.</summary>
        public float[,] Pollution { get; private set; }

        public RiverPtGenerator.RiverResult River { get; private set; }

        /// <summary>Region each cell belongs to when it is not water. Kept so the waterline can be
        /// re-derived after the terrain is shaped without losing the region layout.</summary>
        RegionType[,] regionPartition;

        // The play mode bake, owned here so a regenerate does not leak the previous one.
        Material runtimeTerrainMaterial;
        Texture2D runtimeTerrainTexture;
        MeshRenderer terrainRenderer;

        const string GeneratedRootName = "Generated";

        void Start()
        {
            if (!generateOnStart || !Application.isPlaying)
            {
                return;
            }

            // In a network session every peer has to build the same map, so the server picks the
            // seed and MapSeedSync generates on every peer once it arrives.
            if (MapSeedSync.DrivesGeneration(this))
            {
                return;
            }

            if (randomiseSeedOnStart)
            {
                RerollSeed();
            }

            Generate();
        }

        /// <summary>
        /// Puts a fresh seed in the noise settings and returns it.
        ///
        /// A GUID rather than a timestamp: two launches inside the same second would otherwise get
        /// the same map, which is exactly the case someone testing the randomise toggle hits first.
        /// Call this between rounds to change the map without touching anything else.
        /// </summary>
        public string RerollSeed()
        {
            noiseSettings.seed = System.Guid.NewGuid().ToString("N").Substring(0, 12);
            return noiseSettings.seed;
        }

        /// <summary>Wipes any previous output and builds the map again from the current settings.</summary>
        public void Generate()
        {
            noiseSettings.ValidateValues();
            var rng = new System.Random(noiseSettings.SeedHashCode);
            Transform root = PrepareRoot();

            HeightMap = Noise.GenerateNoiseMap(width, height, noiseSettings, Vector2.zero);
            RegionMap = new RegionType[width, height];
            Pollution = new float[width, height];

            RiverPtGenerator river = GetComponent<RiverPtGenerator>();
            River = river.Carve(HeightMap, RegionMap, rng);

            List<RegionSeed> seeds = RegionPartitioner.PlaceSeeds(width, height, rng, River.splinePoints);
            regionPartition = RegionPartitioner.Partition(width, height, seeds, 12f, 4f, rng);

            // The river was carved before the partition, so it is stamped back over the top: the
            // water owns its cells no matter which region they would otherwise belong to.
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (RegionMap[x, y] != RegionType.River)
                    {
                        RegionMap[x, y] = regionPartition[x, y];
                    }
                }
            }

            Dictionary<RegionType, Vector2> centres = ShapeRegions(rng);
            CarvePaths(centres, rng);

            TerrainShaper.BorderRidge(HeightMap, borderThickness, borderHeight, 18f, 0.35f, rng);
            TerrainShaper.Smooth(HeightMap, smoothIterations, 0.85f);

            // Every pass above stamps ground without knowing where the water is, and the border
            // ridge in particular walls off both ends of the river. Re-cutting the channel here is
            // what keeps the water continuous from one map edge to the other.
            river.ReCarveChannel(HeightMap, River);

            // Shaping and smoothing can dip a bank back under the waterline, which would flood it.
            KeepLandAboveWater();

            // Fixed points first, then the scatter, which is told to avoid them.
            populator.Clear();
            river.PlaceRiverProps(River, CreateChild(root, "RiverProps"), HeightMap, heightMultiplier, populator);

            // The outfall raises a bank under itself, so the waterline has to be worked out again
            // before anything is scattered on the result.
            KeepLandAboveWater();
            populator.Populate(CreateChild(root, "Props"), HeightMap, RegionMap, Pollution, heightMultiplier, rng,
                River.surfaceHeight);

            // The mesh is built after the scatter because rules may flatten the ground they stand on.
            BuildTerrain(root);

            if (buildWater)
            {
                BuildWater(root);
            }

            if (buildMountains)
            {
                mountains.Build(CreateChild(root, "Mountains"), width, height, HeightMap, heightMultiplier, rng);
            }

            BuildRegionMarkers(CreateChild(root, "Regions"), centres);

            var summary = new System.Text.StringBuilder();
            foreach (KeyValuePair<string, int> pair in populator.LastCounts)
            {
                summary.Append($"{pair.Key}={pair.Value} ");
            }

            Debug.Log($"MapGenerator: built {width}x{height} map from seed '{noiseSettings.seed}'. {summary}");
        }

        /// <summary>
        /// Gives each region the shape its role needs. This is the step that turns one noise field
        /// into places that feel different to stand in.
        /// </summary>
        Dictionary<RegionType, Vector2> ShapeRegions(System.Random rng)
        {
            var centres = new Dictionary<RegionType, Vector2>();
            int margin = Mathf.RoundToInt(borderThickness * 0.7f);

            foreach (RegionType type in System.Enum.GetValues(typeof(RegionType)))
            {
                if (type == RegionType.None || type == RegionType.Mountains)
                {
                    continue;
                }

                // Keep landmarks clear of the border ridge the mountains stand on.
                centres[type] = RegionPartitioner.RegionAnchor(RegionMap, type, margin);
            }

            // Frozen marsh: sunk towards the waterline so the ground by the river is genuinely low
            // and the river reads as the bottom of the map rather than a trench in a flat field.
            if (centres.TryGetValue(RegionType.FrozenMarsh, out Vector2 marsh))
            {
                TerrainShaper.Depress(HeightMap, marsh, 22f, marshDepth);
            }

            // Clearing: a level arena floor. The blackjack table needs a readable, open space.
            if (centres.TryGetValue(RegionType.Clearing, out Vector2 clearing))
            {
                TerrainShaper.Flatten(HeightMap, clearing, clearingRadius, 0.45f);
            }

            // Camp: a flat pad big enough for the tents, the fire and the truck, with the rolling
            // ground around it left alone.
            if (centres.TryGetValue(RegionType.Camp, out Vector2 camp))
            {
                TerrainShaper.Flatten(HeightMap, camp, campPadRadius, 0.5f);
            }

            // Highground: a real plateau with a flat top and one ramp. Everything around it stays
            // steep, which is what makes holding the top mean something.
            if (centres.TryGetValue(RegionType.Highground, out Vector2 highground))
            {
                TerrainShaper.RaisePlateau(HeightMap, highground, highgroundRadius, highgroundCoreFraction, highgroundLift);

                Vector2 rampTarget = FindRampTarget(highground, centres);
                Vector2 direction = (rampTarget - highground).normalized;
                Vector2 rampTop = highground + direction * highgroundRadius * 0.55f;
                Vector2 rampBottom = highground + direction * (highgroundRadius + 9f);
                TerrainShaper.CarveRamp(HeightMap, rampTop, rampBottom, highgroundRampWidth);
            }

            // Pipe yard: kept low and broken. It is the dirty end of the map, so short sightlines
            // and uneven ground are the point.
            if (centres.TryGetValue(RegionType.PipeYard, out Vector2 pipeYard))
            {
                TerrainShaper.Flatten(HeightMap, pipeYard, pipeYardRadius, 0.7f);
            }

            return centres;
        }

        /// <summary>
        /// Connects region centres with walkable corridors. A minimum spanning tree gives one
        /// connected map with no redundant highways, then a couple of extra links are added so
        /// players are not funnelled down a single route.
        /// </summary>
        void CarvePaths(Dictionary<RegionType, Vector2> centres, System.Random rng)
        {
            var nodes = new List<Vector2>();
            foreach (KeyValuePair<RegionType, Vector2> pair in centres)
            {
                if (pair.Key == RegionType.River)
                {
                    continue;
                }

                nodes.Add(pair.Value);
            }

            if (nodes.Count < 2)
            {
                return;
            }

            var connected = new List<int> { 0 };
            var remaining = new List<int>();
            for (int i = 1; i < nodes.Count; i++)
            {
                remaining.Add(i);
            }

            while (remaining.Count > 0)
            {
                float best = float.MaxValue;
                int bestFrom = connected[0];
                int bestTo = remaining[0];

                foreach (int from in connected)
                {
                    foreach (int to in remaining)
                    {
                        float dist = Vector2.Distance(nodes[from], nodes[to]);
                        if (dist < best)
                        {
                            best = dist;
                            bestFrom = from;
                            bestTo = to;
                        }
                    }
                }

                TerrainShaper.CarvePath(HeightMap, nodes[bestFrom], nodes[bestTo], pathWidth, pathWobble, rng);
                connected.Add(bestTo);
                remaining.Remove(bestTo);
            }

            // Two shortcut links, so the map has loops rather than a pure tree of dead ends.
            for (int extra = 0; extra < 2 && nodes.Count > 3; extra++)
            {
                int a = rng.Next(nodes.Count);
                int b = rng.Next(nodes.Count);
                if (a == b)
                {
                    continue;
                }

                TerrainShaper.CarvePath(HeightMap, nodes[a], nodes[b], pathWidth * 0.8f, pathWobble, rng);
            }
        }

        Vector2 FindRampTarget(Vector2 highground, Dictionary<RegionType, Vector2> centres)
        {
            // The ramp faces whichever neighbouring landmark is closest, so the climb is on the
            // route players are already walking instead of hidden round the back.
            float best = float.MaxValue;
            Vector2 target = new Vector2(width * 0.5f, height * 0.5f);

            foreach (KeyValuePair<RegionType, Vector2> pair in centres)
            {
                if (pair.Key == RegionType.Highground || pair.Key == RegionType.River)
                {
                    continue;
                }

                float dist = Vector2.Distance(pair.Value, highground);
                if (dist < best)
                {
                    best = dist;
                    target = pair.Value;
                }
            }

            return target;
        }

        /// <summary>
        /// Re-derives the waterline after the terrain has been shaped.
        ///
        /// Region shaping, the paths, the border ridge and the smoothing pass all move ground near
        /// the river, so the shoreline the river carved is no longer where the water actually meets
        /// the land. Recomputing it from the final heights keeps the two in agreement, and fills in
        /// any hollow elsewhere on the map that ended up below the waterline.
        /// </summary>
        void KeepLandAboveWater()
        {
            TerrainShaper.ResolveWater(HeightMap, RegionMap, regionPartition, River.surfaceHeight, River.waterSeeds);
        }

        void BuildTerrain(Transform root)
        {
            MeshData meshData = MeshGenerator.GenerateTerrainMesh(HeightMap, heightMultiplier);
            Mesh mesh = meshData.CreateMesh();
            mesh.name = "ProceduralTerrain";

            Transform terrain = CreateChild(root, "Terrain");
            var filter = terrain.gameObject.AddComponent<MeshFilter>();
            var renderer = terrain.gameObject.AddComponent<MeshRenderer>();
            var collider = terrain.gameObject.AddComponent<MeshCollider>();

            filter.sharedMesh = mesh;
            collider.sharedMesh = mesh;

            terrainRenderer = renderer;
            PaintTerrain();

            terrain.gameObject.isStatic = true;

            if (display != null)
            {
                display.DrawMesh(meshData);
            }
        }

        /// <summary>
        /// Re-bakes the ground texture from <see cref="terrainPalette"/> onto the terrain built by
        /// the last <see cref="Generate"/>, without regenerating anything. Returns false when there
        /// is no generated map in memory to paint.
        /// </summary>
        public bool RepaintTerrain()
        {
            if (RegionMap == null || HeightMap == null || terrainRenderer == null)
            {
                return false;
            }

            PaintTerrain();
            return true;
        }

        void PaintTerrain()
        {
            Material material = terrainMaterial;

            if (paintRegionTexture)
            {
                if (Application.isPlaying)
                {
                    // Play mode builds its own map, so the texture baked in the editor belongs to a
                    // different layout; paint one for this map instead.
                    ReleaseRuntimeTerrain();
                    runtimeTerrainMaterial = RegionTextureBaker.BakeRuntime(RegionMap, HeightMap, Pollution,
                        terrainPalette, terrainMaterial, out runtimeTerrainTexture);
                    material = runtimeTerrainMaterial;
                }
    #if UNITY_EDITOR
                else
                {
                    material = RegionTextureBaker.BakeAsset(RegionMap, HeightMap, Pollution, terrainPalette);
                }
    #endif
            }

            if (material != null)
            {
                terrainRenderer.sharedMaterial = material;
            }
        }

        void ReleaseRuntimeTerrain()
        {
            if (runtimeTerrainMaterial != null)
            {
                Destroy(runtimeTerrainMaterial);
                runtimeTerrainMaterial = null;
            }

            if (runtimeTerrainTexture != null)
            {
                Destroy(runtimeTerrainTexture);
                runtimeTerrainTexture = null;
            }
        }

        void OnDestroy()
        {
            if (Application.isPlaying)
            {
                ReleaseRuntimeTerrain();
            }
        }

        void BuildWater(Transform root)
        {
            Transform water = CreateChild(root, "Water");
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "WaterSurface";
            plane.transform.SetParent(water, false);

            // Unity's plane primitive is 10 units across, hence the tenth. The terrain mesh spans
            // cells 0..width-1, so the plane matches that and not the cell count, or it pokes out
            // past the shoreline as a rim of water round the whole map.
            plane.transform.localScale = new Vector3((width - 1) / 10f, 1f, (height - 1) / 10f);
            plane.transform.localPosition = new Vector3(
                (width - 1) * 0.5f,
                River.surfaceHeight * heightMultiplier,
                (height - 1) * 0.5f);

            // The plane spans the whole map, so it is a visual only. The river's collider is a
            // separate mesh that covers the water cells and nothing else.
            MeshCollider planeCollider = plane.GetComponent<MeshCollider>();
            if (planeCollider != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(planeCollider);
                }
                else
                {
                    DestroyImmediate(planeCollider);
                }
            }

            if (waterMaterial != null)
            {
                plane.GetComponent<MeshRenderer>().sharedMaterial = waterMaterial;
            }

            plane.tag = IceTag;
            BuildRiverCollider(water);
        }

        /// <summary>Tag the player movement reads to slide on ice.</summary>
        const string IceTag = "Ice";

        /// <summary>
        /// A walkable sheet of ice over the river: one quad per water cell at the water surface,
        /// grown by a cell so it tucks under the banks instead of leaving a gap at the shore.
        /// Tagged Ice so players slide on it, and it stops them dropping into the channel.
        /// </summary>
        void BuildRiverCollider(Transform water)
        {
            var isWater = new bool[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (RegionMap[x, y] != RegionType.River)
                    {
                        continue;
                    }

                    for (int ox = -1; ox <= 1; ox++)
                    {
                        for (int oy = -1; oy <= 1; oy++)
                        {
                            int nx = x + ox;
                            int ny = y + oy;
                            if (nx >= 0 && ny >= 0 && nx < width && ny < height)
                            {
                                isWater[nx, ny] = true;
                            }
                        }
                    }
                }
            }

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float surface = River.surfaceHeight * heightMultiplier;

            for (int x = 0; x < width - 1; x++)
            {
                for (int y = 0; y < height - 1; y++)
                {
                    if (!isWater[x, y])
                    {
                        continue;
                    }

                    int start = vertices.Count;
                    vertices.Add(new Vector3(x, surface, y));
                    vertices.Add(new Vector3(x, surface, y + 1));
                    vertices.Add(new Vector3(x + 1, surface, y + 1));
                    vertices.Add(new Vector3(x + 1, surface, y));
                    triangles.Add(start);
                    triangles.Add(start + 1);
                    triangles.Add(start + 2);
                    triangles.Add(start);
                    triangles.Add(start + 2);
                    triangles.Add(start + 3);
                }
            }

            if (triangles.Count == 0)
            {
                return;
            }

            var mesh = new Mesh { name = "RiverIce" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            Transform ice = CreateChild(water, "RiverIce");
            ice.gameObject.tag = IceTag;
            ice.gameObject.isStatic = true;
            var collider = ice.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
        }

        void BuildRegionMarkers(Transform root, Dictionary<RegionType, Vector2> centres)
        {
            var counts = new Dictionary<RegionType, int>();
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    RegionType type = RegionMap[x, y];
                    counts.TryGetValue(type, out int current);
                    counts[type] = current + 1;
                }
            }

            foreach (KeyValuePair<RegionType, Vector2> pair in centres)
            {
                var marker = new GameObject($"Region_{pair.Key}");
                marker.transform.SetParent(root, false);
                float ground = TerrainShaper.SampleHeight(HeightMap, pair.Value.x, pair.Value.y) * heightMultiplier;
                marker.transform.localPosition = new Vector3(pair.Value.x, ground, pair.Value.y);

                var component = marker.AddComponent<RegionMarker>();
                component.region = pair.Key;
                counts.TryGetValue(pair.Key, out int cells);
                component.cellCount = cells;
                component.radius = Mathf.Sqrt(Mathf.Max(1, cells) / Mathf.PI);
                component.snowType = RegionMarker.SnowTypeFor(pair.Key);
            }
        }

        Transform PrepareRoot()
        {
            MapSpawner.DespawnNetworkProps();

            Transform existing = transform.Find(GeneratedRootName);
            if (existing != null)
            {
                MapSpawner.ClearChildren(existing);
                return existing;
            }

            var root = new GameObject(GeneratedRootName);
            root.transform.SetParent(transform, false);
            return root.transform;
        }

        static Transform CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }
    }
}
