using UnityEngine;

namespace Weather
{
    /// <summary>
    /// Bends a tree with the wind. Transform based rather than a vertex shader, deliberately: the
    /// trees here use the ordinary Trunk/Leaves materials, so a shader route would mean forking
    /// every tree material, while a pivot rotation works on whatever art is dropped in.
    ///
    /// Three things stack:
    ///   - a steady lean away from the wind that scales with strength, so a storm visibly pushes
    ///     the whole treeline one way,
    ///   - gusts: a noise field that rolls across the map in the wind direction, so a gust hits
    ///     the near trees first and travels through the treeline instead of every tree pulsing
    ///     at once,
    ///   - a wobble on top, faster and wider the stronger the wind, with a smaller sideways
    ///     component so the crown traces a loose loop instead of only nodding back and forth.
    ///
    /// Every tree also rolls its own profile from its position (stiffness, sway speed, wobble
    /// size, response time, how far its lean strays from the wind line), so no two trees move
    /// the same way and a row of them never sways as one object.
    ///
    /// The tree must not be marked Batching Static: Unity bakes a static transform into the
    /// combined mesh and the rotation would do nothing. The editor setup clears that flag.
    /// </summary>
    [DisallowMultipleComponent]
    public class TreeWindSway : MonoBehaviour, IWindReceiver
    {
        [SerializeField, Tooltip("Degrees of lean at wind strength 1.")]
        private float maxLeanDegrees = 9f;

        [SerializeField, Tooltip("Extra degrees of wobble on top of the lean, at wind strength 1.")]
        private float maxWobbleDegrees = 4f;

        [SerializeField, Tooltip("Wobble cycles per second at wind strength 1.")]
        private float wobbleFrequency = 1.1f;

        [SerializeField, Tooltip("Below this wind strength the tree is left alone entirely, so a " +
            "still scene costs nothing.")]
        private float idleThreshold = 0.02f;

        [SerializeField, Tooltip("Seconds for the lean to follow a change in the wind. Higher " +
            "values read as a heavier, older tree.")]
        private float responseSeconds = 1.2f;

        [Header("Variation")]
        [SerializeField, Tooltip("How much each tree's stiffness, sway speed, wobble size and " +
            "response time may differ from the values above. 0 makes every tree identical.")]
        [Range(0f, 1f)] private float variation = 0.4f;

        [SerializeField, Tooltip("Largest angle, in degrees, a tree's lean may stray from the " +
            "wind direction.")]
        private float maxLeanYawJitter = 18f;

        [Header("Gusts")]
        [SerializeField, Tooltip("How much gusts add to or take from the wind strength a tree " +
            "feels. 0 disables gusts.")]
        [Range(0f, 1f)] private float gustAmount = 0.35f;

        [SerializeField, Tooltip("Size of a gust in metres, along the wind direction.")]
        private float gustLength = 25f;

        [SerializeField, Tooltip("How fast gusts travel through the treeline, in metres per second " +
            "at wind strength 1.")]
        private float gustSpeed = 9f;

        private Quaternion restRotation;
        private float phaseOffset;
        private float stiffness;
        private float frequencyScale;
        private float wobbleScale;
        private float responseScale;
        private float leanYaw;
        private float sideSwayRatio;
        private float gustTravel;
        private float wobblePhase;
        private Vector3 smoothedLeanAxis;
        private float smoothedLean;
        private bool atRest = true;

        private void Awake()
        {
            restRotation = transform.localRotation;
            // Position-derived, so it is stable across a reload instead of drifting per session.
            Vector3 p = transform.position;
            phaseOffset = Mathf.Repeat(p.x * 0.37f + p.z * 0.91f, 100f);

            stiffness = Vary(Hash01(p, 1f));
            frequencyScale = Vary(Hash01(p, 2f));
            wobbleScale = Vary(Hash01(p, 3f));
            responseScale = Vary(Hash01(p, 4f));
            leanYaw = (Hash01(p, 5f) * 2f - 1f) * maxLeanYawJitter;
            sideSwayRatio = Mathf.Lerp(0.2f, 0.55f, Hash01(p, 6f));
        }

        // Spreads a 0-1 roll into a multiplier around 1, e.g. 0.6-1.4 at variation 0.4.
        private float Vary(float roll) => 1f + (roll * 2f - 1f) * variation;

        // Cheap stable hash of a position, so each tree keeps the same personality across reloads.
        private static float Hash01(Vector3 p, float seed)
        {
            float h = Mathf.Sin(p.x * 12.9898f + p.z * 78.233f + seed * 37.719f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        private void OnEnable() => WindSystem.Register(this);

        private void OnDisable()
        {
            WindSystem.Unregister(this);
            transform.localRotation = restRotation;
            atRest = true;
        }

        public void OnWind(Vector3 direction, float strength, Vector3 velocity, float deltaTime)
        {
            if (strength < idleThreshold && smoothedLean < 0.01f)
            {
                if (!atRest)
                {
                    transform.localRotation = restRotation;
                    atRest = true;
                }

                return;
            }

            atRest = false;

            // Gusts travel downwind: the noise is sampled along the wind line and scrolled over
            // time, so the same gust reaches trees further downwind a moment later.
            gustTravel += deltaTime * gustSpeed * Mathf.Max(strength, 0.2f);
            // A 2D field (along and across the wind), so neighbours feel related gusts but a gust
            // front is not a perfectly straight line.
            Vector3 position = transform.position;
            float along = Vector3.Dot(position, direction);
            float across = Vector3.Dot(position, Vector3.Cross(Vector3.up, direction));
            float scale = 1f / Mathf.Max(1f, gustLength);
            float gust = Mathf.PerlinNoise((along - gustTravel) * scale, across * scale * 0.5f + 17.3f);
            float felt = strength * (1f + (gust * 2f - 1f) * gustAmount);

            // Bending away from the wind means rotating about the axis perpendicular to it. Each
            // tree strays a little off the wind line so the treeline does not lean in lockstep.
            Vector3 leanDirection = Quaternion.AngleAxis(leanYaw, Vector3.up) * direction;
            Vector3 leanAxis = Vector3.Cross(Vector3.up, leanDirection);
            if (leanAxis.sqrMagnitude < 0.0001f)
            {
                leanAxis = Vector3.right;
            }

            float follow = deltaTime / Mathf.Max(0.01f, responseSeconds * responseScale);
            smoothedLeanAxis = smoothedLeanAxis.sqrMagnitude < 0.0001f
                ? leanAxis.normalized
                : Vector3.Slerp(smoothedLeanAxis, leanAxis.normalized, Mathf.Clamp01(follow));
            smoothedLean = Mathf.MoveTowards(smoothedLean, felt, follow);

            float time = Time.time;
            float frequency = wobbleFrequency * frequencyScale * Mathf.Lerp(0.6f, 2.2f, smoothedLean);
            // Accumulated rather than time * frequency: the frequency moves with every gust, and
            // multiplying it by a large Time.time would make the phase jump.
            wobblePhase = Mathf.Repeat(wobblePhase + deltaTime * frequency, Mathf.PI * 200f);
            float wobble = Mathf.Sin(wobblePhase + phaseOffset);
            // A second, slower term keeps the motion from reading as a clean sine wave.
            wobble = wobble * 0.75f + Mathf.Sin(time * 0.37f + phaseOffset * 1.7f) * 0.25f;

            // Sideways sway, off-frequency from the main wobble so the crown loops rather than nods.
            float side = Mathf.Sin(wobblePhase * 0.63f + phaseOffset * 2.3f) * sideSwayRatio;

            float degrees = smoothedLean * maxLeanDegrees * stiffness
                + wobble * smoothedLean * maxWobbleDegrees * wobbleScale;
            float sideDegrees = side * smoothedLean * maxWobbleDegrees * wobbleScale;

            Vector3 sideAxis = Vector3.Cross(smoothedLeanAxis, Vector3.up);
            transform.localRotation = Quaternion.AngleAxis(sideDegrees, sideAxis)
                * Quaternion.AngleAxis(degrees, smoothedLeanAxis) * restRotation;
        }
    }
}
