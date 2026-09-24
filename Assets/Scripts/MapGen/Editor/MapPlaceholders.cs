using UnityEditor;
using UnityEngine;

namespace DuckRoulette.MapGen
{
    /// <summary>
    /// Builds primitive stand-ins for map props that have no art yet.
    ///
    /// Each one is sized roughly like the real thing, uses the project's toon materials, has
    /// colliders, and has its pivot at the base so the scatter can drop it straight onto the
    /// ground. Replace the prefab's contents with the real model when the art lands; the scatter
    /// rules point at the prefab, so nothing else has to change.
    /// </summary>
    public static class MapPlaceholders
    {
        public const string Folder = "Assets/Prefabs/MapGen/Placeholders";

        public const string SnowPilePath = Folder + "/SnowPile.prefab";
        public const string ReedsPath = Folder + "/Reeds.prefab";
        public const string CabinPath = Folder + "/Cabin.prefab";
        public const string TruckEnginePath = Folder + "/TruckEngine.prefab";
        public const string BeerCratePath = Folder + "/BeerCrate.prefab";

        const string Snow = "Assets/Materials/Terrain/Terrain.mat";
        const string Wood = "Assets/Materials/Terrain/Wood.mat";
        const string DarkWood = "Assets/Materials/Terrain/Wood 1.mat";
        const string LightWood = "Assets/Materials/Terrain/Wood 3.mat";
        const string Metal = "Assets/Materials/Terrain/Piples.mat";
        const string Rust = "Assets/Materials/Terrain/Sheet.mat";
        const string Green = "Assets/Materials/Bar/Beer_Bottle_Glass_Green.mat";

        [MenuItem("Tools/Duck Roulette/Map/Create Placeholder Prefabs")]
        public static void CreateAll()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets/Prefabs/MapGen", "Placeholders");
            }

            Save(BuildSnowPile(), SnowPilePath);
            Save(BuildReeds(), ReedsPath);
            Save(BuildCabin(), CabinPath);
            // The engine prefab has since been made a networked pick-up (TruckEngine, NetworkObject,
            // Rigidbody, task reference) by hand; rebuilding it here would wipe all of that.
            if (AssetDatabase.LoadAssetAtPath<GameObject>(TruckEnginePath) == null)
            {
                Save(BuildTruckEngine(), TruckEnginePath);
            }
            Save(BuildBeerCrate(), BeerCratePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"MapPlaceholders: placeholder prefabs written to {Folder}.");
        }

        /// <summary>A low mound of snow, big enough for a duck to crouch behind.</summary>
        static GameObject BuildSnowPile()
        {
            var root = new GameObject("SnowPile");
            Part(root, PrimitiveType.Sphere, Snow, new Vector3(0f, 0.6f, 0f), new Vector3(4f, 2.2f, 3.2f));
            Part(root, PrimitiveType.Sphere, Snow, new Vector3(1.3f, 0.5f, 0.6f), new Vector3(2.4f, 1.6f, 2.2f));
            Part(root, PrimitiveType.Sphere, Snow, new Vector3(-1.2f, 0.4f, -0.5f), new Vector3(2.2f, 1.3f, 2f));
            return root;
        }

        /// <summary>A clump of frozen reeds. Thin, so no colliders: it hides, it does not block.</summary>
        static GameObject BuildReeds()
        {
            var root = new GameObject("Reeds");
            var random = new System.Random(7);
            for (int i = 0; i < 9; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float distance = (float)random.NextDouble() * 0.7f;
                float tall = 1.6f + (float)random.NextDouble() * 1.2f;
                GameObject stalk = Part(root, PrimitiveType.Cylinder, i % 3 == 0 ? Wood : LightWood,
                    new Vector3(Mathf.Cos(angle) * distance, tall * 0.5f, Mathf.Sin(angle) * distance),
                    new Vector3(0.08f, tall * 0.5f, 0.08f));
                stalk.transform.localRotation = Quaternion.Euler(
                    ((float)random.NextDouble() - 0.5f) * 16f, 0f, ((float)random.NextDouble() - 0.5f) * 16f);
                Object.DestroyImmediate(stalk.GetComponent<Collider>());
            }

            return root;
        }

        /// <summary>A log cabin with a doorway on its +Z side. The inside is open and walkable.</summary>
        static GameObject BuildCabin()
        {
            var root = new GameObject("Cabin");
            const float width = 6f;
            const float depth = 5f;
            const float wallHeight = 3f;
            const float thickness = 0.3f;
            const float doorWidth = 1.6f;
            const float doorHeight = 2.4f;

            Part(root, PrimitiveType.Cube, DarkWood, new Vector3(0f, 0.05f, 0f), new Vector3(width, 0.1f, depth));

            // Back and sides.
            Part(root, PrimitiveType.Cube, Wood, new Vector3(0f, wallHeight * 0.5f, -depth * 0.5f),
                new Vector3(width, wallHeight, thickness));
            Part(root, PrimitiveType.Cube, Wood, new Vector3(-width * 0.5f, wallHeight * 0.5f, 0f),
                new Vector3(thickness, wallHeight, depth));
            Part(root, PrimitiveType.Cube, Wood, new Vector3(width * 0.5f, wallHeight * 0.5f, 0f),
                new Vector3(thickness, wallHeight, depth));

            // Front wall in three pieces around the doorway.
            float side = (width - doorWidth) * 0.5f;
            Part(root, PrimitiveType.Cube, Wood, new Vector3(-(doorWidth + side) * 0.5f, wallHeight * 0.5f, depth * 0.5f),
                new Vector3(side, wallHeight, thickness));
            Part(root, PrimitiveType.Cube, Wood, new Vector3((doorWidth + side) * 0.5f, wallHeight * 0.5f, depth * 0.5f),
                new Vector3(side, wallHeight, thickness));
            Part(root, PrimitiveType.Cube, Wood, new Vector3(0f, (wallHeight + doorHeight) * 0.5f, depth * 0.5f),
                new Vector3(doorWidth, wallHeight - doorHeight, thickness));

            // Pitched roof, snow on top.
            GameObject left = Part(root, PrimitiveType.Cube, DarkWood, new Vector3(-width * 0.26f, wallHeight + 0.8f, 0f),
                new Vector3(width * 0.6f, 0.25f, depth + 0.8f));
            left.transform.localRotation = Quaternion.Euler(0f, 0f, 28f);
            GameObject right = Part(root, PrimitiveType.Cube, DarkWood, new Vector3(width * 0.26f, wallHeight + 0.8f, 0f),
                new Vector3(width * 0.6f, 0.25f, depth + 0.8f));
            right.transform.localRotation = Quaternion.Euler(0f, 0f, -28f);
            GameObject snowLeft = Part(root, PrimitiveType.Cube, Snow, new Vector3(-width * 0.26f, wallHeight + 0.98f, 0f),
                new Vector3(width * 0.58f, 0.15f, depth + 0.7f));
            snowLeft.transform.localRotation = Quaternion.Euler(0f, 0f, 28f);
            GameObject snowRight = Part(root, PrimitiveType.Cube, Snow, new Vector3(width * 0.26f, wallHeight + 0.98f, 0f),
                new Vector3(width * 0.58f, 0.15f, depth + 0.7f));
            snowRight.transform.localRotation = Quaternion.Euler(0f, 0f, -28f);
            return root;
        }

        /// <summary>A block engine on a pallet. Challenge prop, so it only needs to read as a machine.</summary>
        static GameObject BuildTruckEngine()
        {
            var root = new GameObject("TruckEngine");
            Part(root, PrimitiveType.Cube, LightWood, new Vector3(0f, 0.1f, 0f), new Vector3(2.2f, 0.2f, 1.6f));
            Part(root, PrimitiveType.Cube, Metal, new Vector3(0f, 0.75f, 0f), new Vector3(1.6f, 1.1f, 1.1f));
            Part(root, PrimitiveType.Cube, Rust, new Vector3(0f, 1.45f, 0f), new Vector3(1.2f, 0.3f, 0.8f));
            GameObject pulley = Part(root, PrimitiveType.Cylinder, Metal, new Vector3(0.9f, 0.8f, 0f),
                new Vector3(0.6f, 0.08f, 0.6f));
            pulley.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            return root;
        }

        /// <summary>A wooden crate with bottle necks showing over the rim.</summary>
        static GameObject BuildBeerCrate()
        {
            var root = new GameObject("BeerCrate");
            Part(root, PrimitiveType.Cube, Wood, new Vector3(0f, 0.3f, 0f), new Vector3(1.2f, 0.6f, 0.8f));
            for (int x = 0; x < 4; x++)
            {
                for (int z = 0; z < 2; z++)
                {
                    GameObject bottle = Part(root, PrimitiveType.Cylinder, Green,
                        new Vector3(-0.42f + x * 0.28f, 0.7f, -0.18f + z * 0.36f), new Vector3(0.14f, 0.18f, 0.14f));
                    Object.DestroyImmediate(bottle.GetComponent<Collider>());
                }
            }

            return root;
        }

        static GameObject Part(GameObject root, PrimitiveType type, string materialPath, Vector3 position, Vector3 scale)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = type.ToString();
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;

            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material != null)
            {
                part.GetComponent<MeshRenderer>().sharedMaterial = material;
            }

            return part;
        }

        /// <summary>Writes a placeholder, but never over an existing prefab: that may already hold
        /// the real art.</summary>
        static void Save(GameObject root, string path)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }

            Object.DestroyImmediate(root);
        }
    }
}
