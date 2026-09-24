using System.Collections.Generic;
using DuckRoulette.MapGen;
using UnityEngine;

// Server-side spawn layout for the start of a match. Players used to be dropped in a straight
// line at (0, 2, clientId * 2), which on the procedural map meant spawning inside a hill, in the
// river, or behind a rock from each other. This picks a random spot on open, dry ground and puts
// everyone in a loose ring around it, facing the middle, with a clear line of sight between every
// pair - the whole game is watching each other, so nobody should start out of view.
//
// Runs on the server only, after the map is built (see PlayerSpawner). The chosen pose goes out
// in the spawn payload, and the owner-authoritative ClientNetworkTransform keeps it from there.
public static class PlayerSpawnPlacer
{
    public struct SpawnPose
    {
        public Vector3 Position;
        public Quaternion Rotation;
    }

    // Player.prefab's CharacterController: center -0.77, height 2.94, so its bottom sits 2.24
    // below the pivot. A little extra so the capsule never starts inside a bump in the ground.
    private const float PivotAboveGround = 2.35f;
    // Where line of sight is measured from. Lower than the real eye height on purpose: if two
    // players can see each other's bodies from here, they can certainly see each other's heads.
    private const float EyeHeight = 2.0f;
    private const float ClearanceRadius = 0.6f;
    private const float MinGroundNormalY = 0.8f;
    private const float WaterMargin = 0.3f;
    // Keeps the ring off the mountain border, which is steep and walled in.
    private const float PlayableFraction = 0.6f;
    private const int Attempts = 400;

    public static List<SpawnPose> Place(int count)
    {
        var result = new List<SpawnPose>(count);
        if (count <= 0)
        {
            return result;
        }

        MapSeedSync sync = MapSeedSync.Current;
        MeshCollider terrain = FindTerrain(sync);
        if (terrain == null)
        {
            // Hand-built map (or no map at all): keep the old layout rather than guess.
            for (int i = 0; i < count; i++)
            {
                result.Add(new SpawnPose { Position = new Vector3(0f, 2f, i * 2f), Rotation = Quaternion.identity });
            }

            return result;
        }

        float waterY = GetWaterY(sync.Generator);
        Bounds bounds = terrain.bounds;
        float radius = Mathf.Clamp(2.5f + count * 0.45f, 3.5f, 6f);

        List<Vector3> openCentres = CollectOpenCentres(sync.Generator, terrain.transform);

        List<SpawnPose> best = null;
        int bestScore = int.MinValue;
        var grounds = new Vector3[count];
        var valid = new bool[count];

        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            // Most tries go to the open regions (clearings, camp), where trees are least likely to
            // stand between two players; the rest roam the whole playable area.
            Vector3 centre = openCentres.Count > 0 && attempt < Attempts * 2 / 3
                ? openCentres[Random.Range(0, openCentres.Count)]
                : new Vector3(
                    Random.Range(bounds.center.x - bounds.extents.x * PlayableFraction, bounds.center.x + bounds.extents.x * PlayableFraction),
                    0f,
                    Random.Range(bounds.center.z - bounds.extents.z * PlayableFraction, bounds.center.z + bounds.extents.z * PlayableFraction));

            if (!TryGround(terrain, bounds, centre, waterY, out Vector3 centreGround))
            {
                continue;
            }

            // Tighten the ring as tries run out: fewer trees fit between players standing closer.
            float ringRadius = Mathf.Lerp(radius, 3f, attempt / (float)Attempts);
            float startAngle = Random.Range(0f, Mathf.PI * 2f);
            int score = 0;
            for (int i = 0; i < count; i++)
            {
                float angle = startAngle + i * Mathf.PI * 2f / count + Random.Range(-0.15f, 0.15f);
                float distance = count == 1 ? 0f : ringRadius * Random.Range(0.85f, 1.1f);
                Vector3 point = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;

                valid[i] = TryGround(terrain, bounds, point, waterY, out grounds[i]) && IsClear(terrain, grounds[i]);
                if (valid[i])
                {
                    score += 100;
                }
                else
                {
                    grounds[i] = centreGround;
                }
            }

            bool allVisible = true;
            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    if (CanSee(grounds[i], grounds[j]))
                    {
                        score++;
                    }
                    else
                    {
                        allVisible = false;
                    }
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = BuildPoses(grounds, centreGround, count);
            }

            if (allVisible && score >= count * 100)
            {
                return best;
            }
        }

        if (best != null)
        {
            Debug.LogWarning("PlayerSpawnPlacer: no layout had every player in view of each other; using the closest one.");
            return best;
        }

        Debug.LogWarning("PlayerSpawnPlacer: found no dry ground to spawn on; spawning at the map centre.");
        Vector3 fallback = bounds.center + Vector3.up * (bounds.extents.y + PivotAboveGround);
        for (int i = 0; i < count; i++)
        {
            result.Add(new SpawnPose { Position = fallback + Vector3.right * i * 1.5f, Rotation = Quaternion.identity });
        }

        return result;
    }

    private static List<SpawnPose> BuildPoses(Vector3[] grounds, Vector3 centre, int count)
    {
        var poses = new List<SpawnPose>(count);
        for (int i = 0; i < count; i++)
        {
            Vector3 toCentre = centre - grounds[i];
            toCentre.y = 0f;
            Quaternion rotation = toCentre.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toCentre) : Quaternion.identity;
            poses.Add(new SpawnPose { Position = grounds[i] + Vector3.up * PivotAboveGround, Rotation = rotation });
        }

        return poses;
    }

    // World positions of every cell in a region with little scatter on it. The terrain mesh puts
    // cell (x, y) at local (x, height, y), so the terrain transform maps it straight to the world.
    private static List<Vector3> CollectOpenCentres(MapGenerator generator, Transform terrain)
    {
        var centres = new List<Vector3>();
        RegionType[,] regions = generator.RegionMap;
        if (regions == null)
        {
            return centres;
        }

        for (int x = 0; x < regions.GetLength(0); x++)
        {
            for (int y = 0; y < regions.GetLength(1); y++)
            {
                if (regions[x, y] == RegionType.Clearing || regions[x, y] == RegionType.Camp)
                {
                    Vector3 world = terrain.TransformPoint(x, 0f, y);
                    centres.Add(new Vector3(world.x, 0f, world.z));
                }
            }
        }

        return centres;
    }

    private static MeshCollider FindTerrain(MapSeedSync sync)
    {
        if (sync == null || !sync.IsBuilt || sync.Generator == null)
        {
            return null;
        }

        Transform terrain = sync.Generator.transform.Find("Generated/Terrain");
        return terrain != null ? terrain.GetComponent<MeshCollider>() : null;
    }

    private static float GetWaterY(MapGenerator generator)
    {
        if (generator.River == null)
        {
            return float.NegativeInfinity;
        }

        return generator.transform.TransformPoint(0f, generator.River.surfaceHeight * generator.heightMultiplier, 0f).y;
    }

    // Must land on the terrain itself (not on a prop), on gentle slope, above the waterline.
    private static bool TryGround(MeshCollider terrain, Bounds bounds, Vector3 point, float waterY, out Vector3 ground)
    {
        ground = point;
        var origin = new Vector3(point.x, bounds.max.y + 50f, point.z);
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, bounds.size.y + 200f, ~0, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        ground = hit.point;
        return hit.collider == terrain && hit.normal.y >= MinGroundNormalY && hit.point.y > waterY + WaterMargin;
    }

    // Nothing but the ground itself where the player's body will stand.
    private static bool IsClear(MeshCollider terrain, Vector3 ground)
    {
        Vector3 bottom = ground + Vector3.up * (ClearanceRadius + 0.2f);
        Vector3 top = ground + Vector3.up * (PivotAboveGround + 0.6f);
        foreach (Collider hit in Physics.OverlapCapsule(bottom, top, ClearanceRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit != terrain)
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanSee(Vector3 a, Vector3 b)
    {
        return !Physics.Linecast(a + Vector3.up * EyeHeight, b + Vector3.up * EyeHeight, ~0, QueryTriggerInteraction.Ignore);
    }
}
