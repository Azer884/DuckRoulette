using UnityEngine;
using UnityEngine.Rendering;

namespace Weather
{
    /// <summary>
    /// The visible half of the day/night cycle. Purely local: it reads the replicated clock off
    /// <see cref="WeatherSystem"/> and drives lights, ambient, fog, the sun/moon spheres and the
    /// skybox from it, so there is nothing here to keep in sync and nothing to validate.
    ///
    /// Dusk and dawn are two OVERLAPPING fades, not one value and its complement. Both are driven
    /// by the sun's real elevation, but the moon's band sits higher than the sun's, so the moon is
    /// already up and contributing while the sun is still setting; there is never an instant where
    /// one has finished and the other has not started. A complement (moon = 1 - sun) still reads as
    /// a cut, because the hand-over happens at a single point rather than across a window.
    ///
    /// Ambient is blended from <see cref="nightAmbient"/> to <see cref="dayAmbient"/> on that same
    /// day factor, so it moves in one direction through dusk and the night colour is its floor.
    ///
    /// The sun light is the scene's only main light, day and night. Moonlight is folded into it
    /// (direction, colour and intensity blended by how much each body contributes) rather than
    /// handed to the separate moon light. The toon materials shade almost entirely off URP's main
    /// light, so disabling the sun at the end of dusk promoted the moon light to main light and the
    /// whole scene snapped from horizon lighting to moonlight in one frame.
    ///
    /// Layout it expects (the editor setup builds exactly this):
    ///   Sky Rig                 - this component, sits at the world origin
    ///     Sun Pivot             - rotated by time of day
    ///       Directional Light   - the sun, plus a Sun sphere pushed far out along -Z
    ///       Moon Pivot          - the same pivot turned 180 degrees
    ///         Moon Light        - a dim directional light, faded up at night
    ///         Moon sphere
    /// </summary>
    [DisallowMultipleComponent]
    public class DayNightCycle : MonoBehaviour
    {
        [Header("Rig")]
        [SerializeField, Tooltip("Rotated around X by time of day. The sun light and sphere hang off it.")]
        private Transform sunPivot;

        [SerializeField, Tooltip("The main light, day and night. Moonlight is blended into it.")]
        private Light sunLight;

        [SerializeField, Tooltip("Legacy separate moon light. Kept switched off: the moon is " +
            "rendered through the sun light so the main light never changes hands.")]
        private Light moonLight;

        [SerializeField, Tooltip("Unlit sphere that reads as the sun in the sky. Optional.")]
        private Renderer sunSphere;

        [SerializeField, Tooltip("Larger additive sphere around the sun (Weather/BodyGlow). This " +
            "is the corona that bloom bleeds outwards. Optional.")]
        private Renderer sunGlow;

        [SerializeField, Tooltip("Unlit sphere that reads as the moon. Drop a moon texture on its " +
            "material later - the colour here is applied as a tint. Optional.")]
        private Renderer moonSphere;

        [SerializeField, Tooltip("Larger additive sphere around the moon. Optional.")]
        private Renderer moonGlow;

        [Header("Orientation")]
        [SerializeField, Tooltip("Compass direction the sun travels along, in degrees.")]
        private float sunYaw = 30f;

        [Header("Cross-fade")]
        [SerializeField, Tooltip("Sun elevation (the -1..1 sine of its altitude) at which the sun " +
            "is fully out. Close to zero, so the sun hands over around the horizon and the sky " +
            "goes straight to night instead of grinding through a long dark dusk.")]
        private float sunOutElevation = -0.06f;

        [SerializeField, Tooltip("Sun elevation at which the sun is at full strength.")]
        private float sunFullElevation = 0.10f;

        [SerializeField, Tooltip("Sun elevation at which the moon reaches full strength. Keep this " +
            "ABOVE sunOutElevation - that overlap is what stops the hand-over reading as a cut.")]
        private float moonFullElevation = 0.02f;

        [SerializeField, Tooltip("Sun elevation at which the moon starts to appear, so it is " +
            "already in the sky and already lighting the scene while the sun is setting.")]
        private float moonRiseElevation = 0.30f;

        [SerializeField, Tooltip("Extra smoothing in seconds on top of the geometric blend. Keeps " +
            "a fast day length from still reading as a step.")]
        private float blendSmoothing = 0.6f;

        [Header("Sun")]
        [SerializeField, Tooltip("Sun colour across the day. Key it warm at 0.25/0.75 (dawn/dusk).")]
        private Gradient sunColor = DefaultSunGradient();

        [SerializeField, Tooltip("Sun brightness at its peak.")]
        private float sunMaxIntensity = 1.9f;

        [SerializeField, Tooltip("How far above 1 the sun sphere is driven. Anything over 1 is " +
            "what URP's bloom picks up (the scene profile thresholds at 0.9), so this is the dial " +
            "for how much the sun glows rather than just being a bright ball.")]
        private float sunGlowIntensity = 5.5f;

        [SerializeField, Tooltip("Extra brightness on the sun's corona sphere on top of the core.")]
        private float sunCoronaIntensity = 2.2f;

        [Header("Moon")]
        [SerializeField] private Color moonColor = new(0.62f, 0.72f, 1f);

        [SerializeField, Tooltip("Moonlight at full night. Deliberately not tiny - the night is " +
            "meant to be readable, not black.")]
        private float moonMaxIntensity = 0.75f;

        [SerializeField, Tooltip("The moon LIGHT is held at this fixed elevation instead of being " +
            "exactly opposite the sun. Anti-solar is physically right and looks wrong: at sunset " +
            "both lights graze the ground from opposite horizons, so upward-facing surfaces get " +
            "almost nothing from either and the scene dips dark however the intensities are keyed. " +
            "A fixed high angle removes that dip. The moon SPHERE still rises opposite the sun.")]
        private float moonLightElevation = 55f;

        [SerializeField, Tooltip("Compass direction the moon light comes from, in degrees.")]
        private float moonLightYaw = 210f;

        [SerializeField, Range(0f, 1f), Tooltip("Shadow strength at full night, as a fraction of " +
            "the sun light's authored shadow strength.")]
        private float moonShadowStrength = 0.5f;

        [Header("Horizon")]
        [SerializeField, Range(0f, 0.5f), Tooltip("Lowest the main light is ever allowed to shine " +
            "from, as the sine of its elevation (0.12 is about 7 degrees). A light grazing the " +
            "ground, or shining up from under it, makes shadows crawl and the toon shading flip " +
            "light/dark every frame - that was the flicker at sunset. The sun SPHERE still sets.")]
        private float minLightElevation = 0.12f;

        [SerializeField, Range(0f, 1f), Tooltip("Elevation (sine) at which shadows are back at full " +
            "strength. Between minLightElevation and this they fade out, so the long, unstable " +
            "shadows of a low light never show.")]
        private float shadowFullElevation = 0.35f;

        [Header("Lightning")]
        [SerializeField, Tooltip("Ambient added at the peak of a lightning flash.")]
        private Color lightningAmbient = new(0.75f, 0.8f, 1f);

        [SerializeField, Tooltip("Skybox exposure added at the peak of a lightning flash.")]
        private float lightningSkyboxBoost = 1.6f;

        [SerializeField, Tooltip("Main light intensity added at the peak of a lightning flash.")]
        private float lightningLightIntensity = 1.8f;

        [SerializeField, Tooltip("How far above 1 the moon sphere is driven, so it glows too.")]
        private float moonGlowIntensity = 2.4f;

        [SerializeField, Tooltip("Extra brightness on the moon's corona sphere.")]
        private float moonCoronaIntensity = 1.1f;

        [Header("Ambient / fog")]
        [SerializeField, Tooltip("Ambient light in full daylight.")]
        private Color dayAmbient = new(0.55f, 0.58f, 0.62f);

        [SerializeField, Tooltip("Ambient light in full night. Never goes below this, so the night " +
            "is dim rather than black - and the moments right after sunset can never be darker " +
            "than the middle of the night.")]
        private Color nightAmbient = new(0.26f, 0.30f, 0.42f);

        [SerializeField, Tooltip("Warm bounce added during twilight only, at its strongest when " +
            "the sun and moon overlap most.")]
        private Color duskAmbient = new(0.34f, 0.22f, 0.20f);

        [SerializeField] private Gradient fogColor = DefaultAmbientGradient();

        [SerializeField, Tooltip("Leave off if the scene authors its own fog.")]
        private bool driveFog = true;

        [SerializeField, Tooltip("Leave off to keep the scene's own ambient mode (e.g. Skybox " +
            "ambient) instead of the flat colour driven from the gradient above.")]
        private bool driveAmbient = true;

        [Header("Weather response")]
        [SerializeField, Range(0f, 1f), Tooltip("How far rain pulls the sun down. 1 = fully overcast " +
            "at storm strength.")]
        private float overcastDimming = 0.7f;

        [SerializeField, Tooltip("Colour the sky drifts towards under heavy rain.")]
        private Color overcastTint = new(0.34f, 0.36f, 0.4f);

        [SerializeField, Tooltip("Seconds for the overcast blend to catch up to a phase change.")]
        private float overcastBlendSeconds = 5f;

        [Header("Skybox")]
        [SerializeField, Tooltip("Optional. A material instance is used, so the asset on disk is " +
            "never written to. Use a Skybox/WeatherPanoramic material to get the night and storm " +
            "panoramas cross-faded; any other skybox shader still gets tint and exposure.")]
        private Material skyboxMaterial;

        [SerializeField] private float skyboxDayExposure = 1.15f;

        [SerializeField, Tooltip("Not tiny: the night panorama already reads as night, so dropping " +
            "exposure much further only makes the sky muddy.")]
        private float skyboxNightExposure = 0.7f;

        [SerializeField, Tooltip("Degrees per minute the panorama drifts, so the sky is not " +
            "perfectly static. 0 to hold it still.")]
        private float skyboxRotationPerMinute = 1.5f;

        private static readonly int ExposureId = Shader.PropertyToID("_Exposure");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int RotationId = Shader.PropertyToID("_Rotation");
        private static readonly int DayNightBlendId = Shader.PropertyToID("_DayNightBlend");
        private static readonly int StormBlendId = Shader.PropertyToID("_StormBlend");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");

        private Material skyboxInstance;
        private Material originalSkybox;
        private MaterialPropertyBlock sphereBlock;
        private float overcast;
        private float dayFactor = 1f;
        private float nightFactor;
        private float skyboxRotation;
        private float sunShadowStrength = 1f;

        /// <summary>1 in full daylight, 0 in full night. Drives everything the sun owns.</summary>
        public float DayFactor => dayFactor;

        /// <summary>1 in full night, 0 in full daylight. Deliberately NOT 1 - DayFactor: the two
        /// bands overlap through twilight, which is what makes the hand-over invisible.</summary>
        public float NightFactor => nightFactor;

        private void Awake()
        {
            if (skyboxMaterial != null)
            {
                // Never touch the shared asset: driving RenderSettings.skybox directly would dirty
                // the material file itself and leave the project changed after a play session.
                originalSkybox = RenderSettings.skybox;
                skyboxInstance = new Material(skyboxMaterial);
                RenderSettings.skybox = skyboxInstance;
            }

            sphereBlock = new MaterialPropertyBlock();

            if (sunLight != null)
            {
                sunShadowStrength = sunLight.shadowStrength;
            }

            // Start settled rather than fading up from whatever the last scene left behind.
            float startTime = WeatherSystem.Instance != null ? WeatherSystem.Instance.NormalizedTime : 0.4f;
            dayFactor = EvaluateDayFactor(startTime);
            nightFactor = EvaluateNightFactor(startTime);
        }

        private void OnDestroy()
        {
            if (skyboxInstance == null)
            {
                return;
            }

            // Put the authored material back before dropping the instance, or the scene that comes
            // next renders against a destroyed skybox.
            if (RenderSettings.skybox == skyboxInstance)
            {
                RenderSettings.skybox = originalSkybox;
            }

            Destroy(skyboxInstance);
        }

        private void LateUpdate()
        {
            WeatherSystem weather = WeatherSystem.Instance;
            float time = weather != null ? weather.NormalizedTime : 0.4f;
            float rain = weather != null ? weather.RainIntensity : 0f;

            overcast = Mathf.MoveTowards(overcast, rain, Time.deltaTime / Mathf.Max(0.01f, overcastBlendSeconds));

            ApplyRotation(time);

            float dayTarget = EvaluateDayFactor(time);
            float nightTarget = EvaluateNightFactor(time);
            if (blendSmoothing <= 0f)
            {
                dayFactor = dayTarget;
                nightFactor = nightTarget;
            }
            else
            {
                float step = Time.deltaTime / blendSmoothing;
                dayFactor = Mathf.MoveTowards(dayFactor, dayTarget, step);
                nightFactor = Mathf.MoveTowards(nightFactor, nightTarget, step);
            }

            ApplyLights(time);
            ApplyAmbient(time);
            ApplySkybox(time);
            ApplySpheres();
        }

        private static float LightningFlash => Thunder.Instance != null ? Thunder.Instance.Flash : 0f;

        private void ApplyRotation(float time)
        {
            if (sunPivot == null)
            {
                return;
            }

            // At 0.25 (sunrise) the sun sits on the horizon coming up, at 0.5 overhead.
            float elevation = (time - 0.25f) * 360f;
            sunPivot.rotation = Quaternion.Euler(elevation, sunYaw, 0f);
        }

        /// <summary>The -1..1 sine of the sun's altitude. 1 = overhead, 0 = on the horizon.</summary>
        private static float SunElevation(float time) => Mathf.Sin((time - 0.25f) * 2f * Mathf.PI);

        // Geometric, not a keyed curve: how high the sun actually is decides the blend, so the
        // hand-over always lands on the horizon no matter what the day length or start time is.
        private float EvaluateDayFactor(float time)
        {
            float full = Mathf.Max(sunFullElevation, sunOutElevation + 0.001f);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(sunOutElevation, full, SunElevation(time)));
        }

        // Its own band, sitting higher than the sun's, so the moon is already contributing while
        // the sun is still on its way out. The two overlap instead of meeting at a point.
        private float EvaluateNightFactor(float time)
        {
            float rise = Mathf.Max(moonRiseElevation, moonFullElevation + 0.001f);
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(moonFullElevation, rise, SunElevation(time)));
        }

        private void ApplyLights(float time)
        {
            float dim = 1f - overcast * overcastDimming;

            if (moonLight != null && moonLight.enabled)
            {
                moonLight.enabled = false;
            }

            if (sunLight == null)
            {
                return;
            }

            float sunIntensity = dayFactor * sunMaxIntensity;
            float moonIntensity = nightFactor * moonMaxIntensity;

            // How much of the combined light is moonlight. Everything about the main light slides
            // along this, so direction, colour and shadows all travel with the intensity instead of
            // the light jumping from one body to the other.
            float total = sunIntensity + moonIntensity;
            float moonShare = total > 0.0001f ? moonIntensity / total : (nightFactor >= dayFactor ? 1f : 0f);

            // Same rotation ApplyRotation gives the pivot, so it works without one.
            Quaternion sunRotation = Quaternion.Euler((time - 0.25f) * 360f, sunYaw, 0f);

            // Fixed angle, not anti-solar - see the tooltip on moonLightElevation for why.
            Quaternion moonRotation = Quaternion.Euler(moonLightElevation, moonLightYaw, 0f);

            Quaternion rotation = Quaternion.Slerp(sunRotation, moonRotation, moonShare);

            // Hold the light a little above the horizon. Only the pitch is changed, so it still
            // comes from the side of the sky the sun is setting on.
            Vector3 forward = rotation * Vector3.forward;
            float elevation = -forward.y;
            if (elevation < minLightElevation)
            {
                Vector3 flat = new(forward.x, 0f, forward.z);
                flat = flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.forward;
                Vector3 lifted = flat * Mathf.Sqrt(1f - minLightElevation * minLightElevation) +
                    Vector3.down * minLightElevation;
                rotation = Quaternion.FromToRotation(forward, lifted) * rotation;
                elevation = minLightElevation;
            }

            sunLight.transform.rotation = rotation;

            float flash = LightningFlash;
            sunLight.intensity = total * dim + flash * lightningLightIntensity;

            Color color = Color.Lerp(sunColor.Evaluate(time), moonColor, moonShare);
            color = Color.Lerp(color, overcastTint, overcast * 0.6f);
            sunLight.color = Color.Lerp(color, lightningAmbient, Mathf.Clamp01(flash));
            float shadowFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(minLightElevation,
                Mathf.Max(shadowFullElevation, minLightElevation + 0.01f), elevation));
            sunLight.shadowStrength = sunShadowStrength * shadowFade *
                Mathf.Lerp(1f, moonShadowStrength, moonShare);

            // Never switched off: it is RenderSettings.sun, and a disabled sun hands the main-light
            // role to whatever directional light is left, which is exactly the cut this avoids.
            if (!sunLight.enabled)
            {
                sunLight.enabled = true;
            }
        }

        private void ApplyAmbient(float time)
        {
            if (driveAmbient)
            {
                // Driven by the same day factor as the lights, NOT by a gradient keyed on time of
                // day. A time gradient is what made the half hour after sunset darker than
                // midnight: its dusk key sat between two much brighter neighbours, so ambient dived
                // as the sun left and only climbed back later. Blending night to day on the sun's
                // own fade means ambient can only ever move one way through dusk, and night is its
                // floor by construction.
                Color ambient = Color.Lerp(nightAmbient, dayAmbient, dayFactor);

                // Twilight bonus, peaking where the two fades overlap most (dayFactor and
                // nightFactor both around 0.5), so dusk and dawn get their warm bounce without
                // ever dipping below the night floor.
                float twilight = Mathf.Clamp01(dayFactor * nightFactor * 4f);
                ambient += duskAmbient * twilight;

                ambient = Color.Lerp(ambient, overcastTint * 0.6f, overcast);
                ambient += lightningAmbient * LightningFlash;
                ambient.a = 1f;

                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = ambient;
            }

            if (driveFog)
            {
                RenderSettings.fogColor = Color.Lerp(fogColor.Evaluate(time), overcastTint, overcast);
            }
        }

        private void ApplySkybox(float time)
        {
            if (skyboxInstance == null)
            {
                return;
            }

            float exposure = Mathf.Lerp(skyboxNightExposure, skyboxDayExposure, dayFactor) * (1f - overcast * 0.55f) +
                lightningSkyboxBoost * LightningFlash;

            if (skyboxInstance.HasProperty(ExposureId))
            {
                skyboxInstance.SetFloat(ExposureId, exposure);
            }

            if (skyboxInstance.HasProperty(TintId))
            {
                // Half-grey is the neutral tint for a skybox shader, so lerp from there rather
                // than from the sun colour, which would double the warmth at noon. At night it
                // goes back to neutral instead of following the sun gradient into deep blue - the
                // night panorama is already blue, and tinting it again crushed it.
                Color neutral = new(0.5f, 0.5f, 0.5f, 1f);
                Color warm = Color.Lerp(neutral, sunColor.Evaluate(time) * 0.5f, 0.6f);
                Color graded = Color.Lerp(neutral, warm, dayFactor);
                skyboxInstance.SetColor(TintId, Color.Lerp(graded, overcastTint * 0.6f, overcast));
            }

            // Only Skybox/WeatherPanoramic has these; a stock panoramic material just gets the
            // tint and exposure grading above.
            if (skyboxInstance.HasProperty(DayNightBlendId))
            {
                // The moon's band, not the sun's complement, so the night sky starts bleeding in
                // while the sun is still setting - the same overlap the lights use.
                skyboxInstance.SetFloat(DayNightBlendId, nightFactor);
            }

            if (skyboxInstance.HasProperty(StormBlendId))
            {
                skyboxInstance.SetFloat(StormBlendId, overcast);
            }

            if (skyboxRotationPerMinute != 0f && skyboxInstance.HasProperty(RotationId))
            {
                skyboxRotation = Mathf.Repeat(skyboxRotation + skyboxRotationPerMinute * Time.deltaTime / 60f, 360f);
                skyboxInstance.SetFloat(RotationId, skyboxRotation);
            }
        }

        // Spheres, so they read as bodies in the sky from any angle and need no billboarding.
        // Each body is two renderers: an unlit core driven well above 1 so URP's bloom bleeds it
        // outwards (that overbright value is the whole trick - a body clamped to 1 reads as a flat
        // disc no matter how bright the material looks in the inspector), and a larger additive
        // corona sphere for the halo around it.
        //
        // Both are faded rather than switched, and each body follows its OWN factor, so through
        // twilight the sun is still setting while the moon is already visible in the same sky.
        private void ApplySpheres()
        {
            // Cloud cover hides the bodies, but never completely - a faint disc behind the cloud
            // is better than the sky being empty.
            float clear = 1f - overcast * 0.85f;

            float sunVisible = Mathf.Clamp01(dayFactor * 1.35f) * clear;
            Color sunBase = new(1f, 0.93f, 0.72f);
            SetBody(sunSphere, sunGlow, sunBase, sunVisible, sunGlowIntensity, sunCoronaIntensity);

            float moonVisible = Mathf.Clamp01(nightFactor * 1.35f) * clear;
            SetBody(moonSphere, moonGlow, moonColor, moonVisible, moonGlowIntensity, moonCoronaIntensity);
        }

        private void SetBody(Renderer core, Renderer glow, Color tint, float visible, float glowIntensity,
            float coronaIntensity)
        {
            bool shouldRender = visible > 0.01f;

            if (core != null)
            {
                if (core.enabled != shouldRender)
                {
                    core.enabled = shouldRender;
                }

                if (shouldRender)
                {
                    // Squared, so the body dims fast as it sets instead of hanging around bright
                    // just under the horizon.
                    Color hdr = tint * Mathf.Lerp(1f, glowIntensity, visible * visible);
                    hdr.a = 1f;
                    sphereBlock.Clear();
                    sphereBlock.SetColor(BaseColorId, hdr);
                    core.SetPropertyBlock(sphereBlock);
                }
            }

            if (glow == null)
            {
                return;
            }

            if (glow.enabled != shouldRender)
            {
                glow.enabled = shouldRender;
            }

            if (!shouldRender)
            {
                return;
            }

            // The corona carries the fade in its own alpha as well as its colour, because it is
            // additive - a corona at full strength over a dim body would look detached.
            Color corona = tint * (coronaIntensity * visible);
            corona.a = visible;
            sphereBlock.Clear();
            sphereBlock.SetColor(GlowColorId, corona);
            glow.SetPropertyBlock(sphereBlock);
        }

        private static Gradient DefaultSunGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.15f, 0.2f, 0.35f), 0.0f),
                    new GradientColorKey(new Color(1f, 0.55f, 0.3f), 0.25f),
                    new GradientColorKey(new Color(1f, 0.96f, 0.86f), 0.5f),
                    new GradientColorKey(new Color(1f, 0.45f, 0.25f), 0.78f),
                    new GradientColorKey(new Color(0.15f, 0.2f, 0.35f), 1.0f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }

        private static Gradient DefaultAmbientGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.06f, 0.08f, 0.16f), 0.0f),
                    new GradientColorKey(new Color(0.35f, 0.3f, 0.32f), 0.25f),
                    new GradientColorKey(new Color(0.55f, 0.58f, 0.62f), 0.5f),
                    new GradientColorKey(new Color(0.32f, 0.26f, 0.3f), 0.78f),
                    new GradientColorKey(new Color(0.06f, 0.08f, 0.16f), 1.0f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }
    }
}
