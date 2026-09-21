using UnityEngine;

namespace Weather
{
    /// <summary>
    /// Drives the rain particle systems that are already authored in the game scene (the CFXR
    /// rain fall and splash emitters) off the replicated <see cref="WeatherPhase"/>.
    ///
    /// The emitters keep their authored look - this only scales their emission with the phase,
    /// blows the drops along with the wind, and moves the whole rig with the camera so the rain is
    /// wherever the player is instead of only over one corner of the map.
    ///
    /// Wind is a world-space velocity on the drops, not a tilt of the rig. Tilting the emitter only
    /// turns new drops, trails a lean behind every wind change, and had its sign backwards, so the
    /// rain leaned into the wind instead of going with it.
    ///
    /// Splashes keep the authored CFXR look (the area emitter firing its splash sub-emitter with
    /// inherited colour and size), but each one is placed from script on a raycast hit on the
    /// ground, instead of on a flat rectangle at the camera's floor height. The rectangle floated
    /// over dips and sank into slopes, so the splashes rarely touched the ground.
    ///
    /// The scene has these emitters switched on by default, which meant rain from the first frame
    /// of every match whether or not the roll ever came up. They start off here.
    ///
    /// Everything is moved and tilted through <see cref="rig"/>, NOT through this component's own
    /// transform. That matters because this component lives on the weather root, which also parents
    /// the hail spawn volume: driving the root would drag the hail box along with the camera and
    /// tip it over with the wind slant, so hail would start falling from wherever the local player
    /// happened to be standing.
    /// </summary>
    [DisallowMultipleComponent]
    public class RainVisuals : MonoBehaviour, IWindReceiver
    {
        [SerializeField, Tooltip("Every rain emitter. Their authored emission rate is taken as " +
            "the rate at storm strength and scaled down from there.")]
        private ParticleSystem[] rainEmitters;

        [SerializeField, Tooltip("Optional. Splash emitters, faded with the same intensity. An " +
            "emitter with a sub-emitter is treated as the splash spawner: its splashes are placed " +
            "on the ground from script.")]
        private ParticleSystem[] splashEmitters;

        [SerializeField, Tooltip("The transform that is actually moved and tilted - the parent of " +
            "the rain emitters. Left empty, the common parent of the emitters is used. Never this " +
            "object's own transform unless the emitters are its only children.")]
        private Transform rig;

        [Header("Follow")]
        [SerializeField, Tooltip("Move the rig with the camera. Turn this off if the authored " +
            "emitters already cover the whole map.")]
        private bool followCamera = true;

        [SerializeField] private float heightOffset = 14f;
        [SerializeField] private float followSeconds = 0.25f;

        [SerializeField, Tooltip("Side length in metres of the square the rain falls over while " +
            "following the camera. The CFXR emitters are authored at 100m, which spreads their " +
            "particle budget so thin over the map that almost none of it lands near the player; " +
            "pulling it in around the camera is what makes the rain actually visible.")]
        private float rainAreaSize = 40f;

        [SerializeField, Tooltip("Layers the rain collides with and the splashes are placed on.")]
        // Everything except Ignore Raycast (2), Player (3), Hands (6), Ragdoll (8) and Cape (9), so
        // the probe finds the floor under the duck rather than the duck.
        private LayerMask groundMask = ~((1 << 2) | (1 << 3) | (1 << 6) | (1 << 8) | (1 << 9));

        [Header("Response")]
        [SerializeField, Tooltip("Seconds for the rain to fade in or out on a phase change.")]
        private float fadeSeconds = 4f;

        [SerializeField, Tooltip("Most the rain is ever blown off vertical, in degrees.")]
        private float maxSlantDegrees = 35f;

        [SerializeField, Tooltip("How much of the wind velocity the drops pick up. 1 = they move " +
            "sideways at the wind's speed.")]
        private float windInfluence = 1f;

        [SerializeField, Tooltip("Metres below the rig the drops must still be able to reach, so " +
            "they live long enough to hit the ground under the camera.")]
        private float fallReach = 26f;

        [SerializeField, Tooltip("Particles per second per rain emitter at storm strength, used " +
            "when the emitter's own authored rate is missing or tiny. The CFXR rain drives its " +
            "rate from an editor-only density script, so the value saved in the scene cannot be " +
            "trusted to be anything useful at runtime.")]
        private float fallbackRainRate = 1800f;

        [SerializeField, Tooltip("Same, for splash emitters.")]
        private float fallbackSplashRate = 500f;

        [SerializeField, Tooltip("Most splashes placed in one frame, so a frame hitch cannot turn " +
            "into a burst of raycasts.")]
        private int maxSplashesPerFrame = 64;

        private float[] authoredRainRates;
        private float[] authoredSplashRates;
        private float splashIntensity;
        private float[] splashBudget;
        private float intensity;
        private Camera mainCamera;
        private bool playing;
        private float fallSpeed = 20f;
        private Vector3 drift;

        private void Awake()
        {
            CollectEmittersIfUnset();
            ResolveRig();
            DisableCompetingRateScripts(rainEmitters);
            DisableCompetingRateScripts(splashEmitters);
            KeepEmittersAlive(rainEmitters);
            KeepEmittersAlive(splashEmitters);
            FitAreaToCamera(rainEmitters);
            PrepareRainFall();
            authoredRainRates = CacheRates(rainEmitters, fallbackRainRate);
            authoredSplashRates = CacheRates(splashEmitters, fallbackSplashRate);
            PrepareSplashSpawners();

            SetPlaying(false);
            ApplyIntensity(0f);
        }

        // The CFXR rain ships with CFXR_EmissionBySurface, which rewrites the emission rate from the
        // emitter's size every editor update - including during play mode. It silently undid every
        // rate this component set, which is why the rain never followed the weather. It only exists
        // to help author the effect, so it is switched off at runtime. Matched by name so this does
        // not need a reference into the vendor assembly.
        private static void DisableCompetingRateScripts(ParticleSystem[] systems)
        {
            if (systems == null)
            {
                return;
            }

            foreach (ParticleSystem system in systems)
            {
                if (system == null)
                {
                    continue;
                }

                foreach (MonoBehaviour behaviour in system.GetComponents<MonoBehaviour>())
                {
                    if (behaviour != null && behaviour.GetType().Name == "CFXR_EmissionBySurface")
                    {
                        behaviour.enabled = false;
                    }
                }
            }
        }

        // THE reason the rain never showed. The CFXR prefabs carry CFXR_Effect, whose clear
        // behaviour destroys (or disables) the whole GameObject the moment its particle system
        // stops being alive. This component stops the rain on Awake so a match starts dry - and
        // one CHECK_EVERY_N_FRAME later CFXR_Effect saw a dead system and deleted the rain from
        // the scene. Every storm after that had nothing left to turn on. Setting it to None keeps
        // the rest of CFXR_Effect (its material animation) running. Reflection so this does not
        // need a reference into the vendor assembly.
        private static void KeepEmittersAlive(ParticleSystem[] systems)
        {
            if (systems == null)
            {
                return;
            }

            foreach (ParticleSystem system in systems)
            {
                if (system == null)
                {
                    continue;
                }

                foreach (MonoBehaviour behaviour in system.GetComponents<MonoBehaviour>())
                {
                    if (behaviour == null || behaviour.GetType().Name != "CFXR_Effect")
                    {
                        continue;
                    }

                    System.Reflection.FieldInfo field = behaviour.GetType().GetField("clearBehavior");
                    if (field != null && field.FieldType.IsEnum)
                    {
                        field.SetValue(behaviour, System.Enum.ToObject(field.FieldType, 0));
                    }
                    else
                    {
                        // Unknown version of the script: better to lose its extras than the rain.
                        behaviour.enabled = false;
                    }
                }

                // Belt and braces: a system that is told to stop must never take its object with it.
                ParticleSystem.MainModule main = system.main;
                main.stopAction = ParticleSystemStopAction.None;
            }
        }

        private void FitAreaToCamera(ParticleSystem[] systems)
        {
            if (!followCamera || systems == null || rainAreaSize <= 0f)
            {
                return;
            }

            foreach (ParticleSystem system in systems)
            {
                if (system == null)
                {
                    continue;
                }

                ParticleSystem.ShapeModule shape = system.shape;
                if (!shape.enabled)
                {
                    continue;
                }

                // The CFXR rain is a flat rectangle turned to face down, so x/y are its footprint
                // and z is only its (irrelevant) thickness.
                Vector3 scale = shape.scale;
                shape.scale = new Vector3(rainAreaSize, rainAreaSize, scale.z);
            }
        }

        // Drops fall in world space and are pushed by the wind through velocity over lifetime, and
        // they must live long enough to reach the ground under the camera or they never splash.
        private void PrepareRainFall()
        {
            if (rainEmitters == null)
            {
                return;
            }

            foreach (ParticleSystem system in rainEmitters)
            {
                if (system == null)
                {
                    continue;
                }

                ParticleSystem.MainModule main = system.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                fallSpeed = Mathf.Max(1f, main.startSpeed.constantMax);

                float lifetime = (heightOffset + fallReach) / fallSpeed;
                if (main.startLifetime.constantMax < lifetime)
                {
                    main.startLifetime = lifetime;
                }

                // The drops die on whatever they hit, so rain does not fall through roofs.
                ParticleSystem.CollisionModule collision = system.collision;
                collision.enabled = true;
                collision.type = ParticleSystemCollisionType.World;
                collision.mode = ParticleSystemCollisionMode.Collision3D;
                collision.collidesWith = groundMask;
                collision.lifetimeLoss = 1f;
                collision.bounce = 0f;
                collision.dampen = 1f;

                SetWindVelocity(system, Vector3.zero);
            }
        }

        // The CFXR splash prefab is an area emitter whose particles each fire the visible splash as a
        // birth sub-emitter. Its own emission is switched off and the sub-emitter made Manual, so
        // the script decides where every splash goes and fires it with TriggerSubEmitter. The
        // splash still inherits colour and size exactly as the prefab authored it.
        private void PrepareSplashSpawners()
        {
            splashBudget = new float[splashEmitters != null ? splashEmitters.Length : 0];

            if (splashEmitters == null)
            {
                return;
            }

            foreach (ParticleSystem system in splashEmitters)
            {
                if (system == null)
                {
                    continue;
                }

                ParticleSystem.SubEmittersModule subs = system.subEmitters;
                if (!subs.enabled || subs.subEmittersCount == 0)
                {
                    continue;
                }

                ParticleSystem.EmissionModule emission = system.emission;
                emission.enabled = false;

                ParticleSystem.MainModule main = system.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                for (int i = 0; i < subs.subEmittersCount; i++)
                {
                    subs.SetSubEmitterType(i, ParticleSystemSubEmitterType.Manual);

                    ParticleSystem splash = subs.GetSubEmitterSystem(i);
                    if (splash != null)
                    {
                        ParticleSystem.MainModule splashMain = splash.main;
                        splashMain.simulationSpace = ParticleSystemSimulationSpace.World;
                    }
                }
            }
        }

        private static bool IsSplashSpawner(ParticleSystem system)
        {
            ParticleSystem.SubEmittersModule subs = system.subEmitters;
            return subs.enabled && subs.subEmittersCount > 0;
        }

        // Scatter this frame's share of splashes over the rain square around the camera, each one
        // dropped onto whatever the ray hits first - ground, roof or rock.
        private void PlaceSplashes(Vector3 center, float deltaTime)
        {
            if (splashEmitters == null || authoredSplashRates == null || splashIntensity <= 0f)
            {
                return;
            }

            float half = Mathf.Max(1f, rainAreaSize) * 0.5f;
            float rayTop = center.y + heightOffset;

            for (int e = 0; e < splashEmitters.Length && e < authoredSplashRates.Length; e++)
            {
                ParticleSystem system = splashEmitters[e];
                if (system == null || !IsSplashSpawner(system))
                {
                    continue;
                }

                splashBudget[e] += authoredSplashRates[e] * splashIntensity * deltaTime;
                int count = Mathf.Min((int)splashBudget[e], maxSplashesPerFrame);
                splashBudget[e] = Mathf.Min(splashBudget[e] - count, 1f);

                ParticleSystem.MainModule main = system.main;
                ParticleSystem.SubEmittersModule subs = system.subEmitters;

                for (int n = 0; n < count; n++)
                {
                    var origin = new Vector3(center.x + Random.Range(-half, half), rayTop,
                        center.z + Random.Range(-half, half));
                    if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, heightOffset + fallReach,
                            groundMask, QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }

                    // Stand-in for the particle the area emitter would have spawned, so the splash
                    // inherits the same colour and size it always did.
                    float lifetime = Sample(main.startLifetime);
                    var particle = new ParticleSystem.Particle
                    {
                        position = hit.point + hit.normal * 0.02f,
                        startSize = Sample(main.startSize),
                        startColor = main.startColor.Evaluate(Random.value, Random.value),
                        startLifetime = lifetime,
                        remainingLifetime = lifetime,
                        randomSeed = (uint)Random.Range(1, int.MaxValue),
                    };

                    for (int i = 0; i < subs.subEmittersCount; i++)
                    {
                        system.TriggerSubEmitter(i, ref particle);
                    }
                }
            }
        }

        private static float Sample(ParticleSystem.MinMaxCurve curve) => curve.mode switch
        {
            ParticleSystemCurveMode.Constant => curve.constant,
            ParticleSystemCurveMode.TwoConstants => Random.Range(curve.constantMin, curve.constantMax),
            _ => curve.Evaluate(0f, Random.value),
        };

        private static void SetWindVelocity(ParticleSystem system, Vector3 velocity)
        {
            ParticleSystem.VelocityOverLifetimeModule module = system.velocityOverLifetime;
            module.enabled = true;
            module.space = ParticleSystemSimulationSpace.World;
            // A little spread per drop so the sheet of rain does not move as one rigid block. All
            // three axes share a curve mode, which the module requires.
            module.x = new ParticleSystem.MinMaxCurve(velocity.x * 0.85f, velocity.x * 1.15f);
            module.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            module.z = new ParticleSystem.MinMaxCurve(velocity.z * 0.85f, velocity.z * 1.15f);
        }

        // The emitters' own parent, so moving the rain cannot move anything else on the weather
        // root. Falls back to this transform only when there is nothing else to use.
        private void ResolveRig()
        {
            if (rig != null)
            {
                return;
            }

            if (rainEmitters != null)
            {
                foreach (ParticleSystem system in rainEmitters)
                {
                    if (system != null && system.transform != transform)
                    {
                        rig = system.transform.parent != null && system.transform.parent != transform
                            ? system.transform.parent
                            : system.transform;
                        return;
                    }
                }
            }

            rig = transform;
        }

        private void OnEnable() => WindSystem.Register(this);

        private void OnDisable() => WindSystem.Unregister(this);

        // Left unassigned, this takes every particle system under the rig and splits them by name.
        // The rain emitters are authored prefab instances (CFXR4 Rain Falling / Rain Splashes), so
        // hand-wiring the arrays would break the moment one of them is replaced or re-nested.
        private void CollectEmittersIfUnset()
        {
            if ((rainEmitters != null && rainEmitters.Length > 0) ||
                (splashEmitters != null && splashEmitters.Length > 0))
            {
                return;
            }

            ParticleSystem[] all = GetComponentsInChildren<ParticleSystem>(true);
            var rain = new System.Collections.Generic.List<ParticleSystem>(all.Length);
            var splash = new System.Collections.Generic.List<ParticleSystem>(all.Length);

            foreach (ParticleSystem system in all)
            {
                if (system.name.IndexOf("Splash", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    splash.Add(system);
                }
                else
                {
                    rain.Add(system);
                }
            }

            rainEmitters = rain.ToArray();
            splashEmitters = splash.ToArray();
        }

        public void OnWind(Vector3 direction, float strength, Vector3 velocity, float deltaTime)
        {
            WeatherSystem weather = WeatherSystem.Instance;
            float target = weather != null ? weather.RainIntensity : 0f;

            intensity = Mathf.MoveTowards(intensity, target, deltaTime / Mathf.Max(0.01f, fadeSeconds));

            bool shouldPlay = intensity > 0.001f;
            if (shouldPlay != playing)
            {
                SetPlaying(shouldPlay);

                if (shouldPlay)
                {
                    Debug.Log($"RainVisuals: rain on, {(rainEmitters != null ? rainEmitters.Length : 0)} " +
                        $"emitter(s), {(authoredRainRates.Length > 0 ? authoredRainRates[0] : 0f):0}/s at full strength.");
                }
            }

            if (!shouldPlay)
            {
                return;
            }

            ApplyIntensity(intensity);
            ApplyWind(velocity);
            Follow(deltaTime);
        }

        private void ApplyIntensity(float value)
        {
            ApplyRates(rainEmitters, authoredRainRates, value);

            // Splashes only really read once it is properly raining, so they come in later.
            splashIntensity = Mathf.InverseLerp(0.2f, 1f, value);
        }

        private void ApplyWind(Vector3 windVelocity)
        {
            drift = new Vector3(windVelocity.x, 0f, windVelocity.z) * windInfluence;
            drift = Vector3.ClampMagnitude(drift, fallSpeed * Mathf.Tan(maxSlantDegrees * Mathf.Deg2Rad));

            // Upright: the slant comes from the drops' own velocity now.
            rig.rotation = Quaternion.identity;

            foreach (ParticleSystem system in rainEmitters)
            {
                if (system != null)
                {
                    SetWindVelocity(system, drift);
                }
            }
        }

        private void Follow(float deltaTime)
        {
            if (!followCamera)
            {
                return;
            }

            if (mainCamera == null || !mainCamera.isActiveAndEnabled)
            {
                mainCamera = Camera.main;
                if (mainCamera == null)
                {
                    return;
                }
            }

            // Emit upwind by however far the drops drift on the way down, so the rain still lands
            // around the player instead of blowing off to one side of them.
            float fallSeconds = heightOffset / fallSpeed;
            Vector3 target = mainCamera.transform.position + Vector3.up * heightOffset - drift * fallSeconds;
            rig.position = Vector3.Lerp(rig.position, target,
                Mathf.Clamp01(deltaTime / Mathf.Max(0.01f, followSeconds)));

            PlaceSplashes(mainCamera.transform.position, deltaTime);
        }

        private void SetPlaying(bool value)
        {
            playing = value;
            SetPlaying(rainEmitters, value);
            SetPlaying(splashEmitters, value);
        }

        private static void SetPlaying(ParticleSystem[] systems, bool value)
        {
            if (systems == null)
            {
                return;
            }

            foreach (ParticleSystem system in systems)
            {
                if (system == null)
                {
                    continue;
                }

                if (value)
                {
                    system.Play(true);
                }
                else
                {
                    system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }

        private static float[] CacheRates(ParticleSystem[] systems, float fallback)
        {
            if (systems == null)
            {
                return System.Array.Empty<float>();
            }

            var rates = new float[systems.Length];
            for (int i = 0; i < systems.Length; i++)
            {
                float authored = systems[i] != null ? systems[i].emission.rateOverTime.constant : 0f;
                // An authored zero (or near it) means the rate was being supplied by the editor
                // script above, not saved - so there is nothing to scale from.
                rates[i] = authored > 1f ? Mathf.Min(authored, fallback * 3f) : fallback;

                // Room for the rate we are about to ask for; a capped buffer is another way the
                // rain can end up invisible.
                if (systems[i] != null)
                {
                    ParticleSystem.MainModule main = systems[i].main;
                    int needed = Mathf.CeilToInt(rates[i] * Mathf.Max(0.5f, main.startLifetime.constantMax) * 1.2f);
                    if (main.maxParticles < needed)
                    {
                        main.maxParticles = needed;
                    }
                }
            }

            return rates;
        }

        private static void ApplyRates(ParticleSystem[] systems, float[] authored, float scale)
        {
            if (systems == null || authored == null)
            {
                return;
            }

            scale = Mathf.Clamp01(scale);
            for (int i = 0; i < systems.Length && i < authored.Length; i++)
            {
                if (systems[i] == null)
                {
                    continue;
                }

                ParticleSystem.EmissionModule emission = systems[i].emission;
                // A fresh constant curve rather than the multiplier, so it holds whatever curve
                // mode the vendor prefab was saved with.
                emission.rateOverTime = new ParticleSystem.MinMaxCurve(authored[i] * scale);
            }
        }
    }
}
