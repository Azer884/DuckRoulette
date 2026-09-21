using UnityEngine;

namespace Weather
{
    /// <summary>
    /// Bends a tree with the wind. Transform based rather than a vertex shader, deliberately: the
    /// trees here use the ordinary Trunk/Leaves materials, so a shader route would mean forking
    /// every tree material, while a pivot rotation works on whatever art is dropped in.
    ///
    /// Two things stack:
    ///   - a steady lean away from the wind that scales with strength, so a storm visibly pushes
    ///     the whole treeline one way,
    ///   - a noise wobble on top, faster and wider the stronger the wind, so it never looks frozen.
    ///
    /// Each tree gets its own phase offset, otherwise a row of them sways as one object.
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

        private Quaternion restRotation;
        private float phaseOffset;
        private Vector3 smoothedLeanAxis;
        private float smoothedLean;
        private bool atRest = true;

        private void Awake()
        {
            restRotation = transform.localRotation;
            // Position-derived, so it is stable across a reload instead of drifting per session.
            Vector3 p = transform.position;
            phaseOffset = Mathf.Repeat(p.x * 0.37f + p.z * 0.91f, 100f);
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

            // Bending away from the wind means rotating about the axis perpendicular to it.
            Vector3 leanAxis = Vector3.Cross(Vector3.up, direction);
            if (leanAxis.sqrMagnitude < 0.0001f)
            {
                leanAxis = Vector3.right;
            }

            float follow = deltaTime / Mathf.Max(0.01f, responseSeconds);
            smoothedLeanAxis = smoothedLeanAxis.sqrMagnitude < 0.0001f
                ? leanAxis.normalized
                : Vector3.Slerp(smoothedLeanAxis, leanAxis.normalized, Mathf.Clamp01(follow));
            smoothedLean = Mathf.MoveTowards(smoothedLean, strength, follow);

            float time = Time.time;
            float wobble = Mathf.Sin((time * wobbleFrequency * Mathf.Lerp(0.6f, 2.2f, smoothedLean)) + phaseOffset);
            // A second, slower term keeps the motion from reading as a clean sine wave.
            wobble = wobble * 0.75f + Mathf.Sin(time * 0.37f + phaseOffset * 1.7f) * 0.25f;

            float degrees = smoothedLean * maxLeanDegrees + wobble * smoothedLean * maxWobbleDegrees;

            transform.localRotation = Quaternion.AngleAxis(degrees, smoothedLeanAxis) * restRotation;
        }
    }
}
