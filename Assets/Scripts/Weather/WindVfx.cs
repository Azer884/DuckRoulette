using UnityEngine;

namespace Weather
{
    /// <summary>
    /// The visible wind: streaked "wind line" particles blowing across the play space, authored
    /// here rather than in a canned prefab so they actually obey the procedural wind.
    ///
    /// Two things make it dynamic instead of a loop that happens to point somewhere:
    ///   - the emitter rides the camera from upwind, re-aimed every frame, so the lines always
    ///     sweep into view no matter where the player is or which way the wind has swung;
    ///   - the particles already in the air are steered. Every frame their velocities are turned
    ///     towards the current wind velocity at a limited rate, with a little per-particle noise
    ///     so they curl rather than moving as a slab. A gust or a direction change therefore bends
    ///     the streaks that are mid-flight instead of only affecting ones emitted afterwards.
    ///
    /// Each streak is a particle trail, so what is drawn is the path the particle actually flew;
    /// the steering bends that path, and the trail shows the bend.
    ///
    /// The whole particle system is configured in <see cref="Configure"/>, so the look is
    /// reproducible and the editor setup does not have to hand-author dozens of modules.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public class WindVfx : MonoBehaviour, IWindReceiver
    {
        [SerializeField, Tooltip("The wind-line particle system. Defaults to the one on this " +
            "object; leave the auto-configure flag on to have its modules set up from the values " +
            "below on Awake.")]
        private ParticleSystem windLines;

        [SerializeField, Tooltip("Apply the module setup below on Awake. Turn off once the system " +
            "has been hand-tuned in the inspector, or the tuning is overwritten every play.")]
        private bool autoConfigure = true;

        [Header("Emitter volume")]
        [SerializeField, Tooltip("Metres upwind of the camera the emitter sits.")]
        private float upwindOffset = 26f;

        [SerializeField, Tooltip("Metres above the camera the emitter sits. Kept low on purpose - " +
            "wind lines high overhead are invisible from a third-person camera and only cost fill.")]
        private float heightOffset = 1.2f;

        [SerializeField, Tooltip("Width and height of the emission slab, in metres. Wide and flat, " +
            "so the streaks sweep across the play space at roughly head height instead of " +
            "filling a tall column.")]
        private Vector2 emitterSize = new(54f, 6f);

        [SerializeField, Tooltip("Seconds for the emitter to catch up to the camera. Some lag " +
            "stops the lines snapping around when the player spins.")]
        private float followSeconds = 0.35f;

        [Header("Response")]
        [SerializeField, Tooltip("Below this wind strength nothing is emitted.")]
        private float visibleThreshold = 0.15f;

        [SerializeField, Tooltip("Streaks born per second at full gale. Low on purpose - a few " +
            "long streaks read as wind, a swarm of them reads as static.")]
        private float maxEmissionRate = 14f;
        [SerializeField] private float minStartSpeed = 9f;
        [SerializeField] private float maxStartSpeed = 32f;

        [SerializeField, Tooltip("Longer life means longer visible streaks, because each one is " +
            "stretched along the distance it covers.")]
        private float particleLifetime = 4.2f;

        [SerializeField, Tooltip("Hard ceiling on live streaks, so a gale costs a fixed amount.")]
        private int maxParticles = 70;

        [Header("Steering")]
        [SerializeField, Tooltip("How fast an airborne streak turns to match a change in the wind, " +
            "as a fraction of the difference per second. Lower means a wider, lazier arc; higher " +
            "snaps the streak onto the wind. 0 leaves them flying straight.")]
        private float steerRate = 1.3f;

        [SerializeField, Tooltip("Scale of the per-particle curl added on top of the steering. " +
            "This is what bends each streak into its own arc rather than a straight line.")]
        private float curlStrength = 4.5f;

        [SerializeField, Tooltip("How fast the curl pattern evolves.")]
        private float curlSpeed = 0.5f;

        [SerializeField, Range(0f, 1f), Tooltip("How much of the curl is allowed to act vertically. " +
            "Low, so streaks snake sideways and stay near the ground instead of climbing away.")]
        private float verticalCurl = 0.18f;

        [SerializeField, Tooltip("Metres per second of sink applied to a streak's vertical speed, " +
            "so nothing drifts up out of frame over a long life.")]
        private float verticalSettle = 1.4f;

        [Header("Look")]
        [SerializeField, Tooltip("Material for the streaks. A soft additive or alpha-blended line " +
            "texture; Assets/Materials/WindLines.mat is the one this project ships.")]
        private Material lineMaterial;

        [SerializeField, Tooltip("Streak width in metres at its head. The tail tapers to nothing.")]
        private Vector2 lineWidth = new(0.05f, 0.11f);

        [SerializeField, Range(0.02f, 1f), Tooltip("Streak length, as the fraction of a particle's " +
            "life its trail remembers. The trail is the particle's actual flight path, so the " +
            "longer this is the more of the curve you see.")]
        private float trailLength = 0.16f;

        [SerializeField, Tooltip("Metres between trail vertices. Smaller gives a smoother curve at " +
            "the cost of more vertices per streak.")]
        private float trailVertexSpacing = 0.35f;

        [SerializeField] private Color lineColor = new(1f, 1f, 1f, 0.4f);

        private ParticleSystem.EmissionModule emission;
        private ParticleSystem.MainModule main;
        private ParticleSystem.Particle[] buffer;
        private Camera mainCamera;
        private bool playing;

        private void Awake()
        {
            if (windLines == null)
            {
                windLines = GetComponent<ParticleSystem>();
            }

            if (windLines == null)
            {
                enabled = false;
                return;
            }

            if (autoConfigure)
            {
                Configure();
            }

            emission = windLines.emission;
            main = windLines.main;
            emission.rateOverTimeMultiplier = 0f;
            buffer = new ParticleSystem.Particle[Mathf.Max(16, main.maxParticles)];
        }

        private void OnEnable() => WindSystem.Register(this);

        private void OnDisable()
        {
            WindSystem.Unregister(this);
            Stop();
        }

        /// <summary>Sets up every module the effect needs. Public so the editor setup can call it
        /// on a bare ParticleSystem and get the finished look without duplicating the values.</summary>
        public void Configure()
        {
            ParticleSystem.MainModule mainModule = windLines.main;
            mainModule.duration = 5f;
            mainModule.loop = true;
            mainModule.startLifetime = particleLifetime;
            mainModule.startSpeed = minStartSpeed;
            mainModule.startSize = new ParticleSystem.MinMaxCurve(lineWidth.x, lineWidth.y);
            mainModule.startColor = lineColor;
            mainModule.maxParticles = maxParticles;
            mainModule.gravityModifier = 0f;
            mainModule.playOnAwake = false;
            // World space, or the streaks would be dragged along by the emitter following the
            // camera instead of blowing past it.
            mainModule.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.ShapeModule shape = windLines.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(emitterSize.x, emitterSize.y, 1f);
            shape.rotation = Vector3.zero;

            ParticleSystem.EmissionModule emissionModule = windLines.emission;
            emissionModule.enabled = true;
            emissionModule.rateOverTime = 0f;

            // Fade in and out at the ends of life so streaks never pop into or out of existence.
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = windLines.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.22f),
                    new GradientAlphaKey(1f, 0.7f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            // Steering is done in code, so the built-in noise module stays off - two sources of
            // sideways motion would fight each other.
            ParticleSystem.NoiseModule noise = windLines.noise;
            noise.enabled = false;

            // Trails, not stretched billboards. A stretched billboard is a straight quad pointed
            // along the current velocity, so however much the particle turns it can only ever draw
            // a straight line. A trail records where the particle has actually been, so a streak
            // that is being steered by the wind shows the curve it took.
            ParticleSystem.TrailModule trails = windLines.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.ratio = 1f;
            trails.lifetime = trailLength;
            trails.minVertexDistance = trailVertexSpacing;
            trails.worldSpace = true;
            trails.dieWithParticles = true;
            trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
            trails.sizeAffectsWidth = true;
            trails.inheritParticleColor = true;
            // Thin tail, full head: reads as motion in the direction of travel.
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));

            var trailFade = new Gradient();
            trailFade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            trails.colorOverTrail = new ParticleSystem.MinMaxGradient(trailFade);

            var renderer = windLines.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                // The particle head itself draws nothing; the trail is the whole effect.
                renderer.renderMode = ParticleSystemRenderMode.None;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sortingOrder = 0;

                if (lineMaterial != null)
                {
                    renderer.trailMaterial = lineMaterial;
                }
            }
        }

        public void OnWind(Vector3 direction, float strength, Vector3 velocity, float deltaTime)
        {
            FollowCamera(direction, deltaTime);

            float visible = Mathf.InverseLerp(visibleThreshold, 1f, strength);

            if (visible > 0.001f)
            {
                Play();
                emission.rateOverTimeMultiplier = visible * maxEmissionRate;
                main.startSpeedMultiplier = Mathf.Lerp(minStartSpeed, maxStartSpeed, strength);
            }
            else if (playing)
            {
                // StopEmitting, not Clear: the lines already in the air blow out naturally.
                Stop();
            }

            SteerParticles(velocity, deltaTime);
        }

        // This is what makes the effect follow the procedural wind rather than only being aimed by
        // it at birth: every live streak is turned towards the current wind velocity.
        private void SteerParticles(Vector3 velocity, float deltaTime)
        {
            if (steerRate <= 0f || windLines.particleCount == 0)
            {
                return;
            }

            if (buffer == null || buffer.Length < windLines.main.maxParticles)
            {
                buffer = new ParticleSystem.Particle[windLines.main.maxParticles];
            }

            int count = windLines.GetParticles(buffer);
            float follow = 1f - Mathf.Exp(-steerRate * deltaTime);
            float curlTime = Time.time * curlSpeed;

            // The curl is applied across the wind rather than in world axes, so it always reads as
            // the streak weaving around its own path instead of drifting in some fixed direction.
            Vector3 flow = velocity.sqrMagnitude > 0.0001f ? velocity.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, flow);
            if (side.sqrMagnitude < 0.0001f)
            {
                side = Vector3.right;
            }

            side.Normalize();

            for (int i = 0; i < count; i++)
            {
                Vector3 current = buffer[i].velocity;

                // Sampled from the particle's own position, so neighbouring streaks curl
                // differently and the flow reads as air rather than a conveyor belt.
                Vector3 p = buffer[i].position * 0.06f;
                float lateral = Mathf.PerlinNoise(p.y + curlTime, p.z) - 0.5f;
                float vertical = Mathf.PerlinNoise(p.x + curlTime, p.y) - 0.5f;

                Vector3 curl = side * (lateral * curlStrength * 2f) +
                    Vector3.up * (vertical * curlStrength * 2f * verticalCurl);

                Vector3 target = velocity + curl;
                // Settle: a slow sink that cancels any net lift the noise would otherwise give a
                // long-lived streak, keeping the whole effect low in the frame.
                target.y -= verticalSettle;

                buffer[i].velocity = Vector3.Lerp(current, target, follow);
            }

            windLines.SetParticles(buffer, count);
        }

        private void FollowCamera(Vector3 direction, float deltaTime)
        {
            if (mainCamera == null || !mainCamera.isActiveAndEnabled)
            {
                mainCamera = Camera.main;
                if (mainCamera == null)
                {
                    return;
                }
            }

            Vector3 target = mainCamera.transform.position - direction * upwindOffset + Vector3.up * heightOffset;
            transform.position = Vector3.Lerp(transform.position, target,
                Mathf.Clamp01(deltaTime / Mathf.Max(0.01f, followSeconds)));

            if (direction.sqrMagnitude > 0.0001f)
            {
                // The emission slab faces along the wind, so new streaks start upwind of the
                // player and fly towards them.
                transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }
        }

        private void Play()
        {
            if (playing)
            {
                return;
            }

            windLines.Play(true);
            playing = true;
        }

        private void Stop()
        {
            if (windLines == null || !playing)
            {
                return;
            }

            windLines.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            playing = false;
        }
    }
}
