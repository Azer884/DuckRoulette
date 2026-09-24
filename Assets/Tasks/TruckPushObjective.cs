using System.Collections.Generic;
using DuckRoulette.MapGen;
using Unity.Netcode;
using UnityEngine;

// "Push the truck to the river": a ThreePlus task. When it is handed out the server takes the
// truck's current spot as point A and picks point B on the river bank nearest to it. The group
// pushes the truck there (PushableTruck does the physics); the moment it rolls into the goal the
// task completes for everyone who had it.
//
// The holders see a line of scrolling arrows on the ground from the truck to the goal, plus a
// ring marking the goal. Nobody else sees them.
//
// Goes on Assets/Prefabs/Map/Truck.prefab beside PushableTruck.
[RequireComponent(typeof(PushableTruck))]
public class TruckPushObjective : NetworkBehaviour
{
    [SerializeField, Tooltip("The push-the-truck task (ThreePlus).")]
    private Challenge pushTask;

    [SerializeField, Tooltip("How close the truck's centre has to get to the goal, in metres.")]
    private float goalRadius = 6f;

    [SerializeField, Tooltip("How far back from the water the goal sits, in map cells, so the truck " +
        "stops on the bank instead of rolling in.")]
    private int shoreSetback = 4;

    [Header("Arrows")]
    [SerializeField, Tooltip("Material for the arrow strip and goal ring. A tiling arrow texture on " +
        "an unlit, additive or transparent URP shader; an HDR colour above 1 makes it bloom.")]
    private Material arrowMaterial;
    [SerializeField] private float arrowWidth = 1.6f;
    [SerializeField, Tooltip("Metres between arrow heads along the path.")]
    private float arrowSpacing = 2f;
    [SerializeField, Tooltip("How fast the arrows crawl towards the goal, in metres per second.")]
    private float scrollSpeed = 3f;
    [SerializeField, Tooltip("Height above the ground the strip floats at.")]
    private float groundOffset = 0.25f;
    [SerializeField, Tooltip("Path samples per metre - enough to follow the terrain's bumps.")]
    private float samplesPerMetre = 0.5f;

    private readonly NetworkVariable<bool> active = new(false);
    private readonly NetworkVariable<Vector3> goal = new(Vector3.zero);

    private LineRenderer pathLine;
    private LineRenderer goalRing;
    private Material pathMaterial;
    private float scroll;
    private float rebuildTimer;
    private float serverCheckTimer;
    private readonly List<Vector3> samples = new();
    private readonly RaycastHit[] hits = new RaycastHit[8];

    private const int RingSegments = 40;

    private void OnEnable()
    {
        TaskManager.RegisterObjective(pushTask);
        TaskManager.ServerTaskAssigned += OnServerTaskAssigned;
    }

    private void OnDisable()
    {
        TaskManager.UnregisterObjective(pushTask);
        TaskManager.ServerTaskAssigned -= OnServerTaskAssigned;
        SetArrowsVisible(false);
    }

    public override void OnNetworkDespawn()
    {
        if (pathMaterial != null)
        {
            Destroy(pathMaterial);
        }
    }

    #region Server

    private void OnServerTaskAssigned(Challenge task)
    {
        if (!IsServer || !IsSpawned || task != pushTask)
        {
            return;
        }

        if (TryFindRiverGoal(transform.position, out Vector3 point))
        {
            goal.Value = point;
            active.Value = true;
        }
        else
        {
            // No river on this map: nobody could ever finish it, so it must not lock anyone out.
            Debug.LogWarning("TruckPushObjective: no river bank found, cancelling the push task.");
            TaskManager.CancelTaskEverywhere(pushTask);
        }
    }

    private void Update()
    {
        if (IsServer && active.Value)
        {
            ServerCheck();
        }

        UpdateArrows();
    }

    private void ServerCheck()
    {
        serverCheckTimer += Time.deltaTime;
        if (serverCheckTimer < 0.25f)
        {
            return;
        }

        serverCheckTimer = 0f;

        // The group broke up or the task was swapped out - the goal no longer means anything.
        if (TaskManager.Instance == null || !TaskManager.Instance.IsTaskOpenForAnyone(pushTask))
        {
            active.Value = false;
            return;
        }

        Vector3 offset = transform.position - goal.Value;
        offset.y = 0f;
        if (offset.sqrMagnitude <= goalRadius * goalRadius)
        {
            active.Value = false;
            TaskManager.Instance.CompleteTaskForAllHolders(pushTask);
        }
    }

    // Point B: the land cell a few cells back from the water that is closest to the truck. Works
    // on the generator's region grid, so it holds for whatever shape the river took this seed.
    private bool TryFindRiverGoal(Vector3 from, out Vector3 point)
    {
        point = default;
        MapGenerator generator = FindAnyObjectByType<MapGenerator>();
        if (generator == null || generator.RegionMap == null)
        {
            return false;
        }

        Transform root = generator.transform.Find("Generated");
        if (root == null)
        {
            root = generator.transform;
        }

        RegionType[,] regions = generator.RegionMap;
        int width = regions.GetLength(0);
        int height = regions.GetLength(1);

        // Multi-source BFS: each cell's step distance to the nearest water cell.
        int[,] distance = new int[width, height];
        Queue<Vector2Int> queue = new();
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (regions[x, y] == RegionType.River)
                {
                    distance[x, y] = 0;
                    queue.Enqueue(new Vector2Int(x, y));
                }
                else
                {
                    distance[x, y] = int.MaxValue;
                }
            }
        }

        if (queue.Count == 0)
        {
            return false;
        }

        Vector2Int[] steps = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            int next = distance[cell.x, cell.y] + 1;
            if (next > shoreSetback)
            {
                continue;
            }

            foreach (Vector2Int step in steps)
            {
                Vector2Int n = cell + step;
                if (n.x < 0 || n.y < 0 || n.x >= width || n.y >= height || distance[n.x, n.y] <= next)
                {
                    continue;
                }

                distance[n.x, n.y] = next;
                queue.Enqueue(n);
            }
        }

        Vector3 local = root.InverseTransformPoint(from);
        float bestSqr = float.MaxValue;
        Vector2Int best = new(-1, -1);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (distance[x, y] != shoreSetback)
                {
                    continue;
                }

                float dx = x - local.x;
                float dy = y - local.z;
                float sqr = dx * dx + dy * dy;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = new Vector2Int(x, y);
                }
            }
        }

        if (best.x < 0)
        {
            return false;
        }

        point = root.TransformPoint(new Vector3(best.x, 0f, best.y));
        point.y = SampleGround(point, point.y);
        return true;
    }

    #endregion

    #region Arrows (every peer, shown to holders only)

    private void UpdateArrows()
    {
        bool show = active.Value && arrowMaterial != null && TaskManager.Instance != null &&
                    TaskManager.Instance.IsTaskOpenForLocalPlayer(pushTask);
        SetArrowsVisible(show);
        if (!show)
        {
            return;
        }

        scroll -= scrollSpeed / Mathf.Max(0.01f, arrowSpacing) * Time.deltaTime;
        scroll %= 1f;
        pathMaterial.mainTextureOffset = new Vector2(scroll, 0f);

        // The ring breathes so it reads as a destination, not a decal.
        float pulse = 1f + Mathf.Sin(Time.time * 3f) * 0.06f;
        goalRing.transform.localScale = Vector3.one * pulse;

        rebuildTimer -= Time.deltaTime;
        if (rebuildTimer > 0f)
        {
            return;
        }

        rebuildTimer = 0.2f;
        RebuildPath();
    }

    private void SetArrowsVisible(bool visible)
    {
        if (visible && pathLine == null)
        {
            CreateArrowObjects();
        }

        if (pathLine != null && pathLine.gameObject.activeSelf != visible)
        {
            pathLine.gameObject.SetActive(visible);
            goalRing.gameObject.SetActive(visible);
            rebuildTimer = 0f;
        }
    }

    private void CreateArrowObjects()
    {
        pathMaterial = new Material(arrowMaterial);

        // Not parented to the truck: it moves and turns, and the path has to stay on the ground.
        pathLine = CreateLine("TruckPushArrows", pathMaterial);
        pathLine.textureMode = LineTextureMode.Tile;
        pathLine.textureScale = new Vector2(1f / Mathf.Max(0.1f, arrowSpacing), 1f);
        pathLine.widthMultiplier = arrowWidth;

        goalRing = CreateLine("TruckPushGoal", arrowMaterial);
        goalRing.loop = true;
        goalRing.useWorldSpace = false;
        goalRing.widthMultiplier = arrowWidth * 0.5f;
        goalRing.textureMode = LineTextureMode.Stretch;
    }

    private LineRenderer CreateLine(string objectName, Material material)
    {
        GameObject go = new(objectName);
        go.transform.SetParent(null, false);

        // TransformZ alignment with Z pointing up lays the ribbon flat on the ground.
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        LineRenderer line = go.AddComponent<LineRenderer>();
        line.alignment = LineAlignment.TransformZ;
        line.sharedMaterial = material;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.numCornerVertices = 2;
        go.SetActive(false);
        return line;
    }

    private void RebuildPath()
    {
        Vector3 start = transform.position;
        Vector3 end = goal.Value;
        Vector3 flat = end - start;
        flat.y = 0f;
        float length = flat.magnitude;

        samples.Clear();
        int count = Mathf.Max(2, Mathf.CeilToInt(length * samplesPerMetre) + 1);
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            Vector3 p = Vector3.Lerp(start, end, t);
            p.y = SampleGround(p, p.y) + groundOffset;
            samples.Add(p);
        }

        pathLine.positionCount = samples.Count;
        pathLine.SetPositions(samples.ToArray());

        // Local space, drawn in the ring's XY plane: the object is pitched 90 degrees (see
        // CreateLine), which lays that plane on the ground and points Z, the ribbon normal, down.
        goalRing.transform.position = new Vector3(end.x, SampleGround(end, end.y) + groundOffset, end.z);
        goalRing.positionCount = RingSegments;
        for (int i = 0; i < RingSegments; i++)
        {
            float a = i / (float)RingSegments * Mathf.PI * 2f;
            goalRing.SetPosition(i, new Vector3(Mathf.Cos(a) * goalRadius, Mathf.Sin(a) * goalRadius, 0f));
        }
    }

    // Ground height under a point, ignoring the truck, players and anything else with a rigidbody.
    private float SampleGround(Vector3 point, float fallback)
    {
        Vector3 origin = new(point.x, point.y + 60f, point.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, 200f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MinValue;
        for (int i = 0; i < count; i++)
        {
            Collider c = hits[i].collider;
            if (c.attachedRigidbody != null || c.transform.IsChildOf(transform))
            {
                continue;
            }

            best = Mathf.Max(best, hits[i].point.y);
        }

        return best > float.MinValue ? best : fallback;
    }

    #endregion
}
