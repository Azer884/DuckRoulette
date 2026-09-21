using System.Collections.Generic;
using UnityEngine;

namespace DuckRoulette.MapGen
{
    /// <summary>
    /// Rebuilds the reference map's mountain wall around a procedurally generated map.
    ///
    /// The mountains in MapAlpha1.fbx are one hand-made mesh, which is exactly why they cannot be
    /// reproduced by noise. Instead the Blender step cuts that mesh into a library of overlapping
    /// wall slabs and corner blocks, all normalised to the same frame: the piece runs along local
    /// +X, its origin sits mid-length on the inner face at ground level, and the body of the
    /// mountain extends towards local +Z - so local -Z is the direction the map lies in. This
    /// builder walks the four map edges and drops those pieces end to end.
    ///
    /// Three things keep the result from reading as tiling:
    ///   * pieces are cut with overlap and placed with overlap, so neighbours interpenetrate and
    ///     there is no shared silhouette edge where two pieces meet;
    ///   * every piece is picked at random from the library, including pre-mirrored variants, and
    ///     gets a small yaw, scale and height jitter;
    ///   * the bases are pushed below the border ridge the terrain generator raises, so no piece
    ///     ever shows a cut bottom face.
    /// </summary>
    [System.Serializable]
    public class MountainRingBuilder
    {
        [Header("Piece library")]
        [Tooltip("Wall slabs from Assets/Models/MountainPieces (Mtn_Wall_*).")]
        public GameObject[] wallPieces;

        [Tooltip("Corner blocks from Assets/Models/MountainPieces (Mtn_Corner_*).")]
        public GameObject[] cornerPieces;

        [Header("Fit")]
        [Tooltip("Scale applied to every piece. The source ring was built around a 600 unit map, " +
                 "so for a 100 cell map this wants to be around 0.17.")]
        public float pieceScale = 0.17f;

        [Tooltip("Fraction of a piece's length that the next piece overlaps it by. This is what " +
                 "closes the seams, so it wants to be generous - below about a third the ring " +
                 "starts showing gaps wherever a piece happens to be thin.")]
        [Range(0.05f, 0.7f)]
        public float overlap = 0.42f;

        [Tooltip("How far each edge run starts before the corner and ends after it, in cells, so " +
                 "the walls and the corner blocks always share geometry.")]
        public float cornerPadding = 9f;

        [Tooltip("Scale multiplier for corner blocks. The walls already overrun the corners, so the " +
                 "block only has to fill the diagonal - at full size it reads as a spur sticking out " +
                 "of the ring.")]
        public float cornerScale = 0.85f;

        [Tooltip("How far corner blocks sit outside the map corner, in cells.")]
        public float cornerInset = 2f;

        [Tooltip("Rows of pieces per edge. The source mountains are spiky pinnacles, so a single " +
                 "row leaves daylight between the peaks; a second, staggered row behind it closes " +
                 "the silhouette without making the front row look like a wall.")]
        [Range(1, 4)]
        public int rows = 2;

        [Tooltip("How far each extra row sits further out from the map, in cells.")]
        public float rowOffset = 7f;

        [Tooltip("Size multiplier per extra row. Rows further out are bigger, so they show above " +
                 "and between the front row instead of hiding behind it.")]
        public float rowScale = 1.2f;

        [Tooltip("How far each piece is pushed inwards past the map edge, in cells. Buries the " +
                 "inner foot of the mountain in the border ridge.")]
        public float edgeOverlap = 3f;

        [Tooltip("How far each piece is sunk below the terrain it stands on, in world units.")]
        public float sinkDepth = 2.5f;

        [Tooltip("Rotation applied to a piece before the placement yaw. The FBX arrives Z-up from " +
                 "Blender, so -90 on X stands it up; height then runs along +Y and the body of the " +
                 "mountain extends along +Z.")]
        public Vector3 pieceRotationOffset = new Vector3(-90f, 0f, 0f);

        [Header("Variation")]
        public float yawJitterDegrees = 4f;
        public Vector2 scaleJitter = new Vector2(0.9f, 1.25f);
        public float heightJitter = 2f;
        public float alongJitter = 1f;

        [Header("Blocking")]
        [Tooltip("Adds invisible box colliders along the four edges. Cheaper and far more reliable " +
                 "than mesh colliders on the mountain meshes themselves.")]
        public bool addBoundaryWalls = true;

        public float boundaryWallHeight = 60f;

        /// <summary>
        /// Places the whole ring under <paramref name="parent"/>. Heights are read from the
        /// generated height map so the mountains follow the border ridge instead of floating.
        /// </summary>
        public void Build(Transform parent, int width, int height, float[,] heightMap,
            float heightMultiplier, System.Random rng)
        {
            if (wallPieces == null || wallPieces.Length == 0)
            {
                Debug.LogWarning("MountainRingBuilder: no wall pieces assigned, skipping the ring.");
                return;
            }

            // Each edge is described by where it starts, which way it runs, and the yaw that turns
            // a canonical piece (body on +Z, map on -Z) so its mass sits outside the play space.
            var edges = new[]
            {
                new EdgeRun(new Vector3(0f, 0f, height), Vector3.right, width, 0f, Vector3.back),
                new EdgeRun(new Vector3(width, 0f, 0f), Vector3.left, width, 180f, Vector3.forward),
                new EdgeRun(new Vector3(0f, 0f, 0f), Vector3.forward, height, 270f, Vector3.right),
                new EdgeRun(new Vector3(width, 0f, height), Vector3.back, height, 90f, Vector3.left),
            };

            foreach (EdgeRun edge in edges)
            {
                BuildEdge(parent, edge, heightMap, heightMultiplier, rng);
            }

            BuildCorners(parent, width, height, heightMap, heightMultiplier, rng);

            if (addBoundaryWalls)
            {
                BuildBoundaryWalls(parent, width, height);
            }
        }

        void BuildEdge(Transform parent, EdgeRun edge, float[,] heightMap, float heightMultiplier, System.Random rng)
        {
            for (int row = 0; row < Mathf.Max(1, rows); row++)
            {
                float rowSize = Mathf.Pow(rowScale, row);

                // Every row starts at a different point along the edge, so the joins in one row
                // never line up with the joins in the row behind it.
                float phase = row == 0 ? 0f : (float)rng.NextDouble() * 20f;

                // The run starts before the corner and finishes after it, so a corner is always
                // covered by two walls and a corner block rather than by the block alone.
                float travelled = -cornerPadding - phase;
                int lastPick = -1;
                int guard = 0;

                while (travelled < edge.length + cornerPadding && guard++ < 128)
                {
                    int pick = PickIndex(wallPieces.Length, lastPick, rng);
                    lastPick = pick;
                    GameObject prefab = wallPieces[pick];
                    if (prefab == null)
                    {
                        continue;
                    }

                    float scale = pieceScale * rowSize * Mathf.Lerp(scaleJitter.x, scaleJitter.y, (float)rng.NextDouble());
                    Bounds bounds = GetLocalBounds(prefab);
                    float pieceLength = Mathf.Max(1f, bounds.size.x * scale);

                    // Centre the piece on the stretch of edge it covers, then jitter it along the
                    // edge so the joins do not land on a regular rhythm.
                    float centreDistance = travelled + pieceLength * 0.5f;
                    centreDistance += (float)(rng.NextDouble() - 0.5) * alongJitter;

                    // The front row is pushed slightly into the map so its foot is buried in the
                    // border ridge; each row behind it steps further out.
                    float inwardOffset = edgeOverlap - row * rowOffset;
                    Vector3 position = edge.origin + edge.direction * centreDistance + edge.inward * inwardOffset;

                    float ground = SampleWorldHeight(heightMap, position, heightMultiplier);
                    position.y = ground - sinkDepth + (float)(rng.NextDouble() - 0.5) * heightJitter;

                    float yaw = edge.yaw + (float)(rng.NextDouble() - 0.5) * yawJitterDegrees * 2f;
                    Spawn(prefab, parent, position, yaw, scale, rng);

                    travelled += pieceLength * (1f - overlap);
                }
            }
        }

        void BuildCorners(Transform parent, int width, int height, float[,] heightMap,
            float heightMultiplier, System.Random rng)
        {
            if (cornerPieces == null || cornerPieces.Length == 0)
            {
                return;
            }

            var corners = new[]
            {
                new CornerSpot(new Vector3(0f, 0f, 0f), 225f, new Vector3(1f, 0f, 1f)),
                new CornerSpot(new Vector3(width, 0f, 0f), 135f, new Vector3(-1f, 0f, 1f)),
                new CornerSpot(new Vector3(width, 0f, height), 45f, new Vector3(-1f, 0f, -1f)),
                new CornerSpot(new Vector3(0f, 0f, height), 315f, new Vector3(1f, 0f, -1f)),
            };

            int lastPick = -1;
            foreach (CornerSpot corner in corners)
            {
                int pick = PickIndex(cornerPieces.Length, lastPick, rng);
                lastPick = pick;
                GameObject prefab = cornerPieces[pick];
                if (prefab == null)
                {
                    continue;
                }

                float scale = pieceScale * cornerScale * Mathf.Lerp(scaleJitter.x, scaleJitter.y, (float)rng.NextDouble());
                // Pushed outwards, away from the play space, so the block bulks out the corner
                // rather than poking into the map.
                Vector3 position = corner.position - corner.inward.normalized * cornerInset;
                position.y = SampleWorldHeight(heightMap, position, heightMultiplier) - sinkDepth;

                float yaw = corner.yaw + (float)(rng.NextDouble() - 0.5) * yawJitterDegrees;
                Spawn(prefab, parent, position, yaw, scale, rng);
            }
        }

        void BuildBoundaryWalls(Transform parent, int width, int height)
        {
            var walls = new GameObject("BoundaryWalls");
            walls.transform.SetParent(parent, false);

            // Thin slabs just outside the play space. The mountains are the visual; these are what
            // the player actually collides with, so the collision cost stays flat.
            AddWall(walls.transform, new Vector3(width * 0.5f, boundaryWallHeight * 0.5f, -1f), new Vector3(width + 8f, boundaryWallHeight, 2f));
            AddWall(walls.transform, new Vector3(width * 0.5f, boundaryWallHeight * 0.5f, height + 1f), new Vector3(width + 8f, boundaryWallHeight, 2f));
            AddWall(walls.transform, new Vector3(-1f, boundaryWallHeight * 0.5f, height * 0.5f), new Vector3(2f, boundaryWallHeight, height + 8f));
            AddWall(walls.transform, new Vector3(width + 1f, boundaryWallHeight * 0.5f, height * 0.5f), new Vector3(2f, boundaryWallHeight, height + 8f));
        }

        static void AddWall(Transform parent, Vector3 centre, Vector3 size)
        {
            var wall = new GameObject("Wall");
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = centre;
            BoxCollider box = wall.AddComponent<BoxCollider>();
            box.size = size;
        }

        void Spawn(GameObject prefab, Transform parent, Vector3 position, float yaw, float scale, System.Random rng)
        {
            GameObject instance = MapSpawner.Spawn(prefab, parent);
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(pieceRotationOffset);

            // Slightly different scale on each axis so repeated pieces do not read as clones. The
            // scale is in the piece's own space, which is still Z-up, so the height axis is Z.
            float lateral = scale * Mathf.Lerp(0.95f, 1.1f, (float)rng.NextDouble());
            float vertical = scale * Mathf.Lerp(0.9f, 1.2f, (float)rng.NextDouble());
            instance.transform.localScale = new Vector3(lateral, scale, vertical);
            instance.isStatic = true;
        }

        static int PickIndex(int count, int avoid, System.Random rng)
        {
            if (count <= 1)
            {
                return 0;
            }

            int pick = rng.Next(count);
            if (pick == avoid)
            {
                pick = (pick + 1 + rng.Next(count - 1)) % count;
            }

            return pick;
        }

        static float SampleWorldHeight(float[,] heightMap, Vector3 worldPosition, float heightMultiplier)
        {
            return TerrainShaper.SampleHeight(heightMap, worldPosition.x, worldPosition.z) * heightMultiplier;
        }

        /// <summary>
        /// Combined bounds of a prefab's meshes in the prefab root's own space.
        ///
        /// The child transform matters here and cannot be skipped: Blender's FBX export leaves the
        /// mesh Z-up and puts the -90 degree correction on the root, so raw mesh bounds report the
        /// height on the wrong axis.
        /// </summary>
        public static Bounds GetLocalBounds(GameObject prefab)
        {
            var filters = prefab.GetComponentsInChildren<MeshFilter>();
            bool started = false;
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            Matrix4x4 rootToLocal = prefab.transform.worldToLocalMatrix;

            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                Matrix4x4 matrix = rootToLocal * filter.transform.localToWorldMatrix;
                Bounds mesh = filter.sharedMesh.bounds;
                Vector3 centre = mesh.center;
                Vector3 extents = mesh.extents;

                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? -extents.x : extents.x,
                        (corner & 2) == 0 ? -extents.y : extents.y,
                        (corner & 4) == 0 ? -extents.z : extents.z);
                    Vector3 point = matrix.MultiplyPoint3x4(centre + offset);

                    if (!started)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        started = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            return started ? bounds : new Bounds(Vector3.zero, Vector3.one);
        }

        struct EdgeRun
        {
            public Vector3 origin;
            public Vector3 direction;
            public float length;
            public float yaw;
            public Vector3 inward;

            public EdgeRun(Vector3 origin, Vector3 direction, float length, float yaw, Vector3 inward)
            {
                this.origin = origin;
                this.direction = direction;
                this.length = length;
                this.yaw = yaw;
                this.inward = inward;
            }
        }

        struct CornerSpot
        {
            public Vector3 position;
            public float yaw;
            public Vector3 inward;

            public CornerSpot(Vector3 position, float yaw, Vector3 inward)
            {
                this.position = position;
                this.yaw = yaw;
                this.inward = inward;
            }
        }
    }
}
