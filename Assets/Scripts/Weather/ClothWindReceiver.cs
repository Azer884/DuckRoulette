using UnityEngine;

namespace Weather
{
    /// <summary>
    /// Feeds the live wind into a Unity <see cref="Cloth"/> - the tents. Cloth is simulated
    /// locally on every peer, which is what we want: canvas flapping is pure decoration, nobody
    /// stands on it, and replicating a few hundred vertices per tent would be absurd. Every peer
    /// still sees the same flapping because the wind itself is deterministic (see
    /// <see cref="WeatherSystem"/>).
    ///
    /// Cloth needs a SkinnedMeshRenderer on the same GameObject; the editor setup converts the
    /// tent's MeshRenderer for you and paints the anchored vertices.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Cloth))]
    public class ClothWindReceiver : MonoBehaviour, IWindReceiver
    {
        [SerializeField, Tooltip("How hard the wind pushes the cloth, relative to its metres per " +
            "second. Canvas is heavy, so well under 1.")]
        private float windScale = 0.45f;

        [SerializeField, Tooltip("Buffeting added on top of the steady push, at wind strength 1. " +
            "Without this the cloth just leans and holds.")]
        private float maxRandomAcceleration = 6f;

        [SerializeField, Tooltip("Seconds for the cloth to notice a change in the wind, so a gust " +
            "arrives as a shove rather than a step.")]
        private float responseSeconds = 0.6f;

        [Header("Simulation cost")]
        [SerializeField, Tooltip("Cloth beyond this distance from the camera is suspended - it is " +
            "off screen or too small to read, and cloth is the most expensive thing in the scene. " +
            "0 disables the culling.")]
        private float simulationDistance = 45f;

        [SerializeField, Tooltip("Seconds between distance checks. The camera does not move fast " +
            "enough to need this every frame.")]
        private float distanceCheckInterval = 0.5f;

        private Cloth cloth;
        private Vector3 smoothedAcceleration;
        private Camera mainCamera;
        private float nextDistanceCheck;
        private bool suspended;

        private void Awake()
        {
            cloth = GetComponent<Cloth>();
        }

        private void OnEnable() => WindSystem.Register(this);

        private void OnDisable()
        {
            WindSystem.Unregister(this);

            if (cloth != null)
            {
                cloth.externalAcceleration = Vector3.zero;
                cloth.randomAcceleration = Vector3.zero;
            }
        }

        public void OnWind(Vector3 direction, float strength, Vector3 velocity, float deltaTime)
        {
            if (cloth == null)
            {
                return;
            }

            if (UpdateSuspension())
            {
                return;
            }

            Vector3 target = velocity * windScale;
            smoothedAcceleration = Vector3.Lerp(smoothedAcceleration, target,
                Mathf.Clamp01(deltaTime / Mathf.Max(0.01f, responseSeconds)));

            cloth.externalAcceleration = smoothedAcceleration;

            float buffet = strength * maxRandomAcceleration;
            cloth.randomAcceleration = new Vector3(buffet, buffet * 0.5f, buffet);
        }

        /// <summary>Returns true when the cloth is currently suspended and should be left alone.</summary>
        private bool UpdateSuspension()
        {
            if (simulationDistance <= 0f)
            {
                return false;
            }

            if (Time.time >= nextDistanceCheck)
            {
                nextDistanceCheck = Time.time + Mathf.Max(0.05f, distanceCheckInterval);

                if (mainCamera == null || !mainCamera.isActiveAndEnabled)
                {
                    mainCamera = Camera.main;
                }

                if (mainCamera != null)
                {
                    float sqrDistance = (mainCamera.transform.position - transform.position).sqrMagnitude;
                    bool tooFar = sqrDistance > simulationDistance * simulationDistance;

                    if (tooFar != suspended)
                    {
                        suspended = tooFar;
                        cloth.enabled = !tooFar;
                        if (tooFar)
                        {
                            cloth.externalAcceleration = Vector3.zero;
                            cloth.randomAcceleration = Vector3.zero;
                            smoothedAcceleration = Vector3.zero;
                        }
                    }
                }
            }

            return suspended;
        }
    }
}
