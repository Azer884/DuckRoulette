using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DuckRoulette.MapGen
{
    /// <summary>
    /// Drops the procedural map into this project's scene and wires it to the game's own art.
    ///
    /// The generator was built in a separate sandbox project against primitive placeholders. Here
    /// there are no placeholders: every rule points at a real prefab, and the only thing carried
    /// across as art is the mountain ring, which was cut out of MapAlpha1.fbx and cannot be
    /// reproduced procedurally.
    ///
    /// <see cref="BuildRules"/> is the map's content design in one place - what each region
    /// contains, how much of it, and how much room it needs.
    /// </summary>
    public static class MapSceneSetup
    {
        const string PieceFolder = "Assets/Models/MountainPieces";
        const string Prefabs = "Assets/Prefabs/";
        const string MapGenObjectName = "MapGen";

        /// <summary>
        /// World units per map cell.
        ///
        /// The generator works in a 100 by 100 grid of cells and everything it places is sized in
        /// cells, so one number here decides how big the arena is in this game's units. The duck
        /// is about 2.4 units wide and 4.6 tall, so 2.5 gives a map roughly a hundred ducks
        /// across and trees around three and a half times the player's height.
        /// </summary>
        const float CellSize = 2.5f;

        [MenuItem("Tools/Duck Roulette/Map/Set Up And Generate")]
        public static void SetUpAndGenerate()
        {
            MapGenerator generator = SetUp();
            if (generator == null)
            {
                return;
            }

            generator.Generate();
            EditorSceneMarkDirty(generator);
        }

        [MenuItem("Tools/Duck Roulette/Map/Set Up Only")]
        public static MapGenerator SetUp()
        {
            MapGenerator generator = Object.FindAnyObjectByType<MapGenerator>();
            if (generator == null)
            {
                var host = GameObject.Find(MapGenObjectName);
                if (host == null)
                {
                    host = new GameObject(MapGenObjectName);
                    Undo.RegisterCreatedObjectUndo(host, "Create MapGen");
                }

                generator = host.GetComponent<MapGenerator>() ?? Undo.AddComponent<MapGenerator>(host);
            }

            if (generator.GetComponent<RiverPtGenerator>() == null)
            {
                Undo.AddComponent<RiverPtGenerator>(generator.gameObject);
            }

            Undo.RecordObject(generator, "Set up map generator");
            Undo.RecordObject(generator.transform, "Scale map");

            // The generator thinks in cells; the scene thinks in units. Scaling the root is the
            // whole conversion, and it keeps props, terrain and mountains in proportion.
            generator.transform.localScale = Vector3.one * CellSize;

            generator.mountains.wallPieces = LoadAssets(PieceFolder, "Mtn_Wall");
            generator.mountains.cornerPieces = LoadAssets(PieceFolder, "Mtn_Corner");
            generator.mountains.pieceScale = SuggestPieceScale(generator);

            RiverPtGenerator river = generator.GetComponent<RiverPtGenerator>();
            Undo.RecordObject(river, "Set up river");
            river.bridgePrefab = Load("Assets/Prefabs/MapGen/Birdge.prefab") ?? river.bridgePrefab;
            river.outfallPrefab = Load("Assets/Prefabs/MapGen/Tube 1.prefab") ?? river.outfallPrefab;

            generator.populator.rules = BuildRules();

            // The real prefabs have larger footprints than the placeholders this was tuned
            // against, so a rule needs more darts thrown before it finds room for everything it
            // wants. The loop is only distance checks, so this is cheap.
            generator.populator.attemptsPerInstance = 400;

            EditorUtility.SetDirty(generator);
            EditorUtility.SetDirty(river);

            int missing = generator.populator.rules.Count(
                rule => rule.prefabs == null || rule.prefabs.Length == 0 || rule.prefabs[0] == null);

            Debug.Log($"MapSceneSetup: {generator.mountains.wallPieces.Length} wall pieces, " +
                      $"{generator.mountains.cornerPieces.Length} corner pieces, cell size {CellSize}, " +
                      $"{generator.populator.rules.Count} scatter rules ({missing} with no prefab).");
            return generator;
        }

        /// <summary>
        /// Hides whatever hand-built map is already in the scene, without deleting it.
        ///
        /// Deactivating rather than removing is deliberate: the old map is the reference for what
        /// the generated one has to live up to, and it is the thing to compare against when a
        /// seed comes out wrong.
        /// </summary>
        [MenuItem("Tools/Duck Roulette/Map/Hide Old Map")]
        public static void HideOldMap()
        {
            string[] names =
            {
                "Map", "MapAlpha1", "Terrain", "OldMap", "Environment", "Level", "Ground",
            };

            int hidden = 0;
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == MapGenObjectName)
                {
                    continue;
                }

                bool matches = names.Any(name => root.name.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                               || root.GetComponentInChildren<Terrain>(true) != null;

                if (!matches || !root.activeSelf)
                {
                    continue;
                }

                Undo.RecordObject(root, "Hide old map");
                root.SetActive(false);
                hidden++;
                Debug.Log($"MapSceneSetup: hid '{root.name}'.");
            }

            Debug.Log($"MapSceneSetup: hid {hidden} old map object(s).");
        }

        /// <summary>The map's content design, pointing at this project's own prefabs.</summary>
        public static List<ScatterRule> BuildRules()
        {
            var rules = new List<ScatterRule>();

            // --- Landmarks: the things you navigate by --------------------------------

            // The pipe drains into the yard and stains the snow around it. It snaps to the map
            // boundary so it reads as coming out of the mountain wall, and it is the only rule
            // that writes to the pollution map.
            rules.Add(new ScatterRule
            {
                label = "Pipe",
                prefabs = Group(Prefabs + "Pipe.prefab"),
                regions = new List<RegionType> { RegionType.PipeYard },
                count = new Vector2Int(1, 2),
                role = PropRole.Landmark,
                sizeTarget = 11f,
                scaleJitter = new Vector2(0.95f, 1.1f),
                hugEdge = true,
                faceAwayFromEdge = true,
                edgeSnap = 5f,
                edgeMargin = 4,
                maxSlope = 2f,
                flattenRadius = 7f,
                pollutionRadius = 14f,
                selfSpacing = 16f,
                tiltDegrees = 0f,
                // Slid back until its rear end is buried in the border wall, so the pipe reads as
                // coming out of the mountain rather than lying in front of it.
                snapToWall = true,
                wallBury = 0.35f,
            });

            // Igloos own the bluff, spread out, so the top of the map is a place with structures
            // to fight around rather than an empty platform.
            rules.Add(new ScatterRule
            {
                label = "Igloos",
                prefabs = Group("Assets/Models/Igloo.fbx"),
                regions = new List<RegionType> { RegionType.Highground },
                count = new Vector2Int(2, 4),
                role = PropRole.Landmark,
                sizeTarget = 7f,
                selfSpacing = 11f,
                flattenRadius = 4.5f,
                maxSlope = 0.2f,
                tiltDegrees = 0f,
                // Igloo.fbx has its pivot at the centre of the dome, so without this half of it
                // ends up underground, door included.
                sitOnBase = true,
                // The entrance tunnel is on the model's +Z. Facing it at the plateau keeps the
                // door off the cliff edge, and the clearance keeps other props out of the way.
                faceRegionCentre = true,
                frontClearance = 4f,
                // The model has no collider. A non-convex mesh collider keeps the doorway open.
                addMeshCollider = true,
            });

            // The cabin is the camp's anchor. Its doorway is on +Z, facing into the camp.
            rules.Add(new ScatterRule
            {
                label = "Cabin",
                prefabs = Group(MapPlaceholders.CabinPath),
                regions = new List<RegionType> { RegionType.Camp },
                count = new Vector2Int(1, 1),
                role = PropRole.Landmark,
                sizeTarget = 7f,
                scaleJitter = Vector2.one,
                flattenRadius = 5f,
                maxSlope = 0.18f,
                tiltDegrees = 0f,
                faceRegionCentre = true,
                frontClearance = 3f,
            });

            rules.Add(new ScatterRule
            {
                label = "Tents",
                prefabs = Group(Prefabs + "Tent.prefab"),
                regions = new List<RegionType> { RegionType.Camp },
                count = new Vector2Int(2, 3),
                role = PropRole.Landmark,
                sizeTarget = 5f,
                selfSpacing = 7f,
                flattenRadius = 3f,
                maxSlope = 0.18f,
                tiltDegrees = 0f,
            });

            // One fire, at the camp. It is the map's only warm light, so it is a strong landmark
            // and a bad place to hide next to.
            rules.Add(new ScatterRule
            {
                label = "Campfire",
                prefabs = Group(Prefabs + "Campfire.prefab"),
                regions = new List<RegionType> { RegionType.Camp },
                count = new Vector2Int(1, 1),
                role = PropRole.Landmark,
                sizeTarget = 3f,
                scaleJitter = Vector2.one,
                flattenRadius = 3f,
                maxSlope = 0.18f,
                tiltDegrees = 0f,
            });

            // --- Cover: where a hider can break line of sight --------------------------

            // The truck is the biggest single hiding spot on the map, so the role spacing keeps
            // it well away from the smaller cover rather than letting it stack with it.
            rules.Add(new ScatterRule
            {
                label = "Truck",
                prefabs = Group(Prefabs + "Truck.prefab"),
                regions = new List<RegionType> { RegionType.Camp, RegionType.PipeYard },
                count = new Vector2Int(1, 1),
                role = PropRole.Cover,
                sizeTarget = 9f,
                scaleJitter = Vector2.one,
                flattenRadius = 5f,
                maxSlope = 0.15f,
                tiltDegrees = 0f,
            });

            rules.Add(new ScatterRule
            {
                label = "Hiding logs",
                prefabs = Group(Prefabs + "HidingLog.prefab"),
                regions = new List<RegionType> { RegionType.Woodland, RegionType.FrozenMarsh },
                count = new Vector2Int(2, 5),
                role = PropRole.Cover,
                sizeTarget = 6f,
                alignToGround = true,
                maxSlope = 0.3f,
            });

            // --- Challenges: destinations that cost you time and noise ------------------

            rules.Add(new ScatterRule
            {
                label = "Blackjack table",
                prefabs = Group(Prefabs + "BlackjackTable.prefab"),
                regions = new List<RegionType> { RegionType.Clearing },
                count = new Vector2Int(1, 1),
                role = PropRole.Challenge,
                sizeTarget = 5.5f,
                scaleJitter = Vector2.one,
                flattenRadius = 6f,
                maxSlope = 0.12f,
                tiltDegrees = 0f,
            });

            rules.Add(new ScatterRule
            {
                label = "Bumbox",
                prefabs = Group(Prefabs + "Bumbox.prefab"),
                regions = new List<RegionType>
                {
                    RegionType.Camp, RegionType.Woodland, RegionType.PipeYard,
                },
                count = new Vector2Int(1, 2),
                role = PropRole.Challenge,
                sizeTarget = 3f,
                flattenRadius = 2f,
                maxSlope = 0.2f,
            });

            rules.Add(new ScatterRule
            {
                label = "Truck engine",
                prefabs = Group(MapPlaceholders.TruckEnginePath),
                regions = new List<RegionType> { RegionType.PipeYard, RegionType.Camp },
                count = new Vector2Int(1, 1),
                role = PropRole.Challenge,
                sizeTarget = 2.4f,
                scaleJitter = Vector2.one,
                flattenRadius = 2f,
                maxSlope = 0.2f,
                tiltDegrees = 0f,
            });

            rules.Add(new ScatterRule
            {
                label = "Beer crate",
                prefabs = Group(MapPlaceholders.BeerCratePath),
                regions = new List<RegionType> { RegionType.Woodland, RegionType.Camp, RegionType.PipeYard },
                count = new Vector2Int(1, 2),
                role = PropRole.Challenge,
                sizeTarget = 1.4f,
                scaleJitter = Vector2.one,
                maxSlope = 0.25f,
                tiltDegrees = 2f,
            });

            // Snow piles are cover: a duck can crouch behind one. Spread across the open regions
            // where there is little else to hide behind.
            rules.Add(new ScatterRule
            {
                label = "Snow piles",
                prefabs = Group(MapPlaceholders.SnowPilePath),
                regions = new List<RegionType>
                {
                    RegionType.PipeYard, RegionType.Clearing, RegionType.Highground, RegionType.Woodland,
                    RegionType.FrozenMarsh,
                },
                count = new Vector2Int(4, 7),
                role = PropRole.Cover,
                sizeTarget = 3.5f,
                scaleJitter = new Vector2(0.8f, 1.25f),
                maxSlope = 0.3f,
                tiltDegrees = 0f,
            });

            // --- Dressing: no gameplay contract, only readability -----------------------

            rules.Add(new ScatterRule
            {
                label = "Reeds",
                prefabs = Group(MapPlaceholders.ReedsPath),
                regions = new List<RegionType> { RegionType.FrozenMarsh },
                count = new Vector2Int(25, 45),
                role = PropRole.Decor,
                sizeTarget = 1.4f,
                scaleJitter = new Vector2(0.7f, 1.3f),
                footprintPadding = 0f,
                tiltDegrees = 6f,
                maxSlope = 0.4f,
            });

            rules.Add(new ScatterRule
            {
                label = "Trees",
                prefabs = Group(Prefabs + "Tree.prefab"),
                regions = new List<RegionType>
                {
                    RegionType.Woodland, RegionType.Camp, RegionType.Highground, RegionType.FrozenMarsh,
                },
                count = new Vector2Int(40, 65),
                role = PropRole.Decor,
                sizeTarget = 7f,
                scaleJitter = new Vector2(0.75f, 1.35f),
                selfSpacing = 5f,
                // Negative padding on purpose. The footprint circle is measured from the whole
                // mesh, which for a conifer is the canopy - but what a log or a rock has to stay
                // out of is the trunk. Shrinking it lets the ground dressing sit under the
                // branches, where it belongs, instead of ringing every tree with empty snow.
                footprintPadding = -1.6f,
                tiltDegrees = 3f,
                maxSlope = 0.35f,
                edgeMargin = 8,
            });

            rules.Add(new ScatterRule
            {
                label = "Logs",
                prefabs = Group(Prefabs + "Log.prefab"),
                regions = new List<RegionType>
                {
                    RegionType.Woodland, RegionType.Camp, RegionType.FrozenMarsh,
                },
                count = new Vector2Int(14, 22),
                role = PropRole.Decor,
                sizeTarget = 2.4f,
                alignToGround = true,
                footprintPadding = 0.2f,
                maxSlope = 0.35f,
            });

            // The project's own rock plus the ones cut out of the reference map, so the scatter
            // has more than one silhouette to work with.
            var rocks = new List<GameObject>();
            GameObject projectRock = Load(Prefabs + "Rock.prefab");
            if (projectRock != null)
            {
                rocks.Add(projectRock);
            }

            rocks.AddRange(LoadAssets(PieceFolder, "Rock_"));

            rules.Add(new ScatterRule
            {
                label = "Rocks",
                prefabs = rocks.ToArray(),
                regions = new List<RegionType>
                {
                    RegionType.Highground, RegionType.PipeYard, RegionType.Woodland, RegionType.Clearing,
                },
                count = new Vector2Int(12, 20),
                role = PropRole.Decor,
                sizeTarget = 3.2f,
                scaleJitter = new Vector2(0.6f, 1.8f),
                alignToGround = true,
                maxSlope = 0.4f,
                // Loose rocks can be pushed around. Only the mountain ring is static rock.
                physicsBody = true,
            });

            return rules;
        }

        /// <summary>
        /// Scale that makes a mountain piece the right size for this map. The ring was cut around
        /// a much larger reference map, so the pieces are measured and scaled to give roughly
        /// four slabs per map edge.
        /// </summary>
        public static float SuggestPieceScale(MapGenerator generator)
        {
            GameObject[] pieces = generator.mountains.wallPieces;
            if (pieces == null || pieces.Length == 0)
            {
                return generator.mountains.pieceScale;
            }

            var lengths = new List<float>();
            foreach (GameObject piece in pieces)
            {
                if (piece == null)
                {
                    continue;
                }

                float length = MountainRingBuilder.GetLocalBounds(piece).size.x;
                if (length > 0.001f)
                {
                    lengths.Add(length);
                }
            }

            if (lengths.Count == 0)
            {
                return generator.mountains.pieceScale;
            }

            lengths.Sort();
            return generator.width / 4f / lengths[lengths.Count / 2];
        }

        static GameObject Load(string path)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static GameObject[] Group(params string[] paths)
        {
            return paths.Select(Load).Where(asset => asset != null).ToArray();
        }

        static GameObject[] LoadAssets(string folder, string prefix)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogWarning($"MapSceneSetup: {folder} does not exist.");
                return new GameObject[0];
            }

            return AssetDatabase.FindAssets($"{prefix} t:GameObject", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => System.IO.Path.GetFileNameWithoutExtension(path).StartsWith(prefix))
                .OrderBy(path => path)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(asset => asset != null)
                .ToArray();
        }

        static void EditorSceneMarkDirty(MapGenerator generator)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }
    }
}
