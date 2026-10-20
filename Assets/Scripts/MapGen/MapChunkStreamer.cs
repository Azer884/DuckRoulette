using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuckRoulette.MapGen
{
    /// <summary>
    /// Splits the generated map into a grid of chunks and only draws the chunks the local camera
    /// can see.
    ///
    /// Each chunk is in one of three states:
    ///   * <b>Visible</b> - drawn normally.
    ///   * <b>Shadow only</b> - outside the camera's view. Not drawn, but still casts shadows, so a
    ///     mountain or tree behind the player keeps its shadow on the ground in front of them.
    ///   * <b>Hidden</b> - a prop chunk further than <see cref="viewDistance"/>. Not drawn at all.
    ///
    /// Terrain pieces and mountain pieces are never hidden by distance, only by view: they are the
    /// ground and the skyline, and hiding them by distance would open holes in the map.
    ///
    /// The test runs in <see cref="RenderPipelineManager.beginCameraRendering"/> for the main
    /// camera, after every script has moved it for the frame and before Unity culls and draws.
    /// A test in Update or on a timer lags a fast camera turn, and the map visibly pops in at the
    /// edge of the screen. Other cameras (the Scene view) see the result of the game camera's
    /// test, which is how the streaming can be watched in the editor.
    ///
    /// Only the <b>visual</b> side of a chunk is streamed: renderers, lights and the wind sway on
    /// trees. Colliders are never touched and GameObjects are never deactivated - the terrain's
    /// collider is one whole-map mesh on the Terrain object, separate from its render pieces - so
    /// the ground and every prop stay solid whether anyone is looking at them or not. That is what
    /// keeps a rigidbody resting on a hidden chunk from falling through it, and it keeps the
    /// server's physics identical to what clients see.
    ///
    /// Anything that moves is left out entirely: networked props (truck, rocks, ball, bumbox) are
    /// unparented from the map on spawn and never reach this list, and any prop carrying a
    /// Rigidbody is skipped, because the chunk it was sorted into would go stale the moment it
    /// rolled away. The water plane spans the whole map and is left alone.
    ///
    /// Purely local. Nothing here is replicated: each peer streams the map for its own camera.
    /// </summary>
    [DisallowMultipleComponent]
    public class MapChunkStreamer : MonoBehaviour
    {
        [Tooltip("Width of one chunk, in map cells. Also the size of each terrain render piece.")]
        [Min(4)] public int chunkCells = 16;

        [Tooltip("Prop chunks further than this from the camera are hidden, in world units.")]
        public float viewDistance = 70f;

        [Tooltip("Extra distance a visible chunk is allowed before it hides again, so a chunk on " +
                 "the boundary does not flicker while the player stands still.")]
        public float hysteresis = 6f;

        [Tooltip("How far a chunk's bounds are grown before the view test, in world units. A chunk " +
                 "just off the edge of the screen is still drawn, so nothing is caught half drawn.")]
        public float viewMargin = 4f;

        enum State
        {
            Visible,
            ShadowOnly,
            Hidden,
        }

        class Chunk
        {
            public bool viewOnly;
            public Bounds bounds;
            public bool hasBounds;
            public State state = State.Visible;
            public readonly List<Renderer> renderers = new List<Renderer>();
            public readonly List<ShadowCastingMode> shadowModes = new List<ShadowCastingMode>();
            public readonly List<Light> lights = new List<Light>();
            public readonly List<Behaviour> behaviours = new List<Behaviour>();
        }

        readonly Dictionary<(Vector2Int cell, bool viewOnly), Chunk> chunks =
            new Dictionary<(Vector2Int cell, bool viewOnly), Chunk>();
        readonly Plane[] frustum = new Plane[6];

        /// <summary>Number of chunks built by the last <see cref="Rebuild"/>.</summary>
        public int ChunkCount => chunks.Count;

        /// <summary>Number of chunks currently drawn.</summary>
        public int VisibleCount { get; private set; }

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            ShowAll();
        }

        /// <summary>
        /// Sorts every child of the given roots into chunks. Called by <see cref="MapGenerator"/>
        /// after every generate, with only the roots that generate just built, so objects from the
        /// previous map that are still waiting for their deferred Destroy are never picked up.
        /// </summary>
        /// <param name="propRoots">Hidden by distance, shadow only outside the view.</param>
        /// <param name="viewOnlyRoots">Shadow only outside the view, never hidden: terrain pieces
        /// and mountains.</param>
        public void Rebuild(Transform[] propRoots, Transform[] viewOnlyRoots)
        {
            ShowAll();
            chunks.Clear();

            if (!Application.isPlaying)
            {
                return;
            }

            AddRoots(propRoots, false);
            AddRoots(viewOnlyRoots, true);

            VisibleCount = chunks.Count;
        }

        void AddRoots(Transform[] roots, bool viewOnly)
        {
            if (roots == null)
            {
                return;
            }

            foreach (Transform root in roots)
            {
                if (root == null)
                {
                    continue;
                }

                for (int i = 0; i < root.childCount; i++)
                {
                    AddObject(root.GetChild(i), viewOnly);
                }
            }
        }

        void AddObject(Transform item, bool viewOnly)
        {
            if (item.GetComponentInChildren<Rigidbody>(true) != null)
            {
                return;
            }

            var renderers = new List<Renderer>();
            Bounds itemBounds = default;
            foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled)
                {
                    continue;
                }

                if (renderers.Count == 0)
                {
                    itemBounds = renderer.bounds;
                }
                else
                {
                    itemBounds.Encapsulate(renderer.bounds);
                }

                renderers.Add(renderer);
            }

            // Nothing to draw (the boundary walls, region markers): nothing to stream.
            if (renderers.Count == 0)
            {
                return;
            }

            // Keyed by the middle of what is drawn rather than the pivot: terrain pieces all sit at
            // the map origin with their vertices out in the grid. In the map's own space a cell is
            // one unit, whatever the map object is scaled to.
            Vector3 local = transform.InverseTransformPoint(itemBounds.center);
            var cell = new Vector2Int(Mathf.FloorToInt(local.x / chunkCells), Mathf.FloorToInt(local.z / chunkCells));

            if (!chunks.TryGetValue((cell, viewOnly), out Chunk chunk))
            {
                chunk = new Chunk { viewOnly = viewOnly };
                chunks[(cell, viewOnly)] = chunk;
            }

            foreach (Renderer renderer in renderers)
            {
                chunk.renderers.Add(renderer);
                chunk.shadowModes.Add(renderer.shadowCastingMode);
            }

            if (chunk.hasBounds)
            {
                chunk.bounds.Encapsulate(itemBounds);
            }
            else
            {
                chunk.bounds = itemBounds;
                chunk.hasBounds = true;
            }

            foreach (Light light in item.GetComponentsInChildren<Light>(true))
            {
                if (light.enabled)
                {
                    chunk.lights.Add(light);
                }
            }

            foreach (Weather.TreeWindSway sway in item.GetComponentsInChildren<Weather.TreeWindSway>(true))
            {
                if (sway.enabled)
                {
                    chunk.behaviours.Add(sway);
                }
            }
        }

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (chunks.Count == 0 || camera != Camera.main)
            {
                return;
            }

            Vector3 eye = camera.transform.position;
            GeometryUtility.CalculateFrustumPlanes(camera, frustum);
            float showSqr = viewDistance * viewDistance;
            float keepSqr = (viewDistance + hysteresis) * (viewDistance + hysteresis);

            int visibleCount = 0;
            foreach (Chunk chunk in chunks.Values)
            {
                State state = Evaluate(chunk, eye, showSqr, keepSqr);
                if (state != chunk.state)
                {
                    Apply(chunk, state);
                }

                if (state == State.Visible)
                {
                    visibleCount++;
                }
            }

            VisibleCount = visibleCount;
        }

        State Evaluate(Chunk chunk, Vector3 eye, float showSqr, float keepSqr)
        {
            if (!chunk.viewOnly)
            {
                float limitSqr = chunk.state == State.Hidden ? showSqr : keepSqr;
                if (chunk.bounds.SqrDistance(eye) > limitSqr)
                {
                    return State.Hidden;
                }
            }

            Bounds grown = chunk.bounds;
            grown.Expand(viewMargin * 2f);
            return GeometryUtility.TestPlanesAABB(frustum, grown) ? State.Visible : State.ShadowOnly;
        }

        static void Apply(Chunk chunk, State state)
        {
            chunk.state = state;

            for (int i = 0; i < chunk.renderers.Count; i++)
            {
                Renderer renderer = chunk.renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                ShadowCastingMode original = chunk.shadowModes[i];
                switch (state)
                {
                    case State.Visible:
                        renderer.enabled = true;
                        renderer.shadowCastingMode = original;
                        break;
                    case State.ShadowOnly:
                        // A renderer that never cast shadows has nothing left to draw.
                        renderer.enabled = original != ShadowCastingMode.Off;
                        renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                        break;
                    default:
                        renderer.enabled = false;
                        renderer.shadowCastingMode = original;
                        break;
                }
            }

            // A light off screen still lights what is on screen, so only distance turns it off.
            foreach (Light light in chunk.lights)
            {
                if (light != null)
                {
                    light.enabled = state != State.Hidden;
                }
            }

            foreach (Behaviour behaviour in chunk.behaviours)
            {
                if (behaviour != null)
                {
                    behaviour.enabled = state == State.Visible;
                }
            }
        }

        void ShowAll()
        {
            foreach (Chunk chunk in chunks.Values)
            {
                if (chunk.state != State.Visible)
                {
                    Apply(chunk, State.Visible);
                }
            }
        }

        void OnDrawGizmosSelected()
        {
            foreach (Chunk chunk in chunks.Values)
            {
                Gizmos.color = chunk.state == State.Visible
                    ? new Color(0.2f, 1f, 0.3f, 0.6f)
                    : chunk.state == State.ShadowOnly
                        ? new Color(1f, 0.8f, 0.2f, 0.4f)
                        : new Color(1f, 0.3f, 0.2f, 0.3f);
                Gizmos.DrawWireCube(chunk.bounds.center, chunk.bounds.size);
            }
        }
    }
}
