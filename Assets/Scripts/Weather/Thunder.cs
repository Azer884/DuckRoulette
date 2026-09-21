using System.Collections.Generic;
using UnityEngine;

namespace Weather
{
    /// <summary>
    /// Lightning and thunder during a storm. Purely local, like the wind: strikes are a pure
    /// function of the replicated weather seed and the shared server clock on
    /// <see cref="WeatherSystem"/>, so every peer sees the same flash at the same moment with
    /// nothing extra sent over the network.
    ///
    /// The clock is cut into slots; each slot may hold one strike at a seeded offset inside it,
    /// at a seeded spot inside the strike area. A strike is:
    ///   - a jagged bolt (line renderers, HDR so bloom picks it up) from the sky to the ground,
    ///   - a flash: a big point light at the bolt, plus <see cref="Flash"/>, which
    ///     <see cref="DayNightCycle"/> adds to the main light, ambient and skybox,
    ///   - an impact particle effect where it lands,
    ///   - thunder after a delay that grows with the distance to this peer's camera, quieter and
    ///     deeper the farther away it is.
    ///
    /// Clips come from this component's overrides, then SFXManager's thunderClips. With neither,
    /// a rumble is synthesised at startup so a storm is never silent.
    /// </summary>
    [DisallowMultipleComponent]
    public class Thunder : MonoBehaviour
    {
        public static Thunder Instance { get; private set; }

        [Header("Timing")]
        [SerializeField, Tooltip("Seconds per strike slot. Each slot holds at most one strike.")]
        private float slotSeconds = 12f;

        [SerializeField, Range(0f, 1f), Tooltip("Chance a slot actually has a strike.")]
        private float strikeChance = 0.75f;

        [SerializeField, Tooltip("Seconds from flash to thunder, nearest and farthest strike.")]
        private Vector2 soundDelayRange = new(0.2f, 3f);

        [Header("Flash")]
        [SerializeField, Range(0f, 2f)] private float flashStrength = 1f;

        [SerializeField, Tooltip("Seconds a flash (both of its flickers) takes to die away.")]
        private float flashSeconds = 0.6f;

        [Header("Bolt")]
        [SerializeField, Tooltip("Where strikes land. Left empty, the first BoxCollider under this " +
            "object (the hail spawn volume, sized to the level) is used, then a circle of " +
            "strikeRadius around this object.")]
        private BoxCollider strikeArea;

        [SerializeField] private float strikeRadius = 70f;

        [SerializeField, Tooltip("Layers the bolt can land on.")]
        private LayerMask groundMask = ~((1 << 2) | (1 << 3) | (1 << 6) | (1 << 8) | (1 << 9));

        [SerializeField, Tooltip("Metres above the ground the bolt starts.")]
        private float boltHeight = 90f;

        [SerializeField] private float boltWidth = 1.1f;

        [SerializeField, ColorUsage(false, true), Tooltip("Above 1 so URP's bloom makes it glow.")]
        private Color boltColor = new(3.2f, 3.6f, 5f, 1f);

        [SerializeField, Tooltip("Optional. Left empty, an unlit material is made at runtime.")]
        private Material boltMaterial;

        [Header("Flash light")]
        [SerializeField] private Color flashLightColor = new(0.8f, 0.87f, 1f);
        [SerializeField] private float flashLightIntensity = 60f;
        [SerializeField] private float flashLightRange = 160f;

        [Header("Impact")]
        [SerializeField, Tooltip("Particle effect spawned where the bolt lands.")]
        private GameObject impactEffect;

        [SerializeField] private float impactScale = 5f;

        [SerializeField, Tooltip("Distance from the camera at which a strike counts as far: " +
            "longest thunder delay, quietest, dimmest.")]
        private float farDistance = 150f;

        [Header("Sound")]
        [SerializeField, Range(0f, 1f), Tooltip("Volume of the nearest strike.")]
        private float maxVolume = 1f;

        [SerializeField, Range(0f, 1f), Tooltip("Volume of the farthest strike.")]
        private float minVolume = 0.45f;

        [SerializeField, Tooltip("Used when set; otherwise SFXManager's thunderClips, otherwise a " +
            "synthesised rumble.")]
        private AudioClip[] thunderClipOverrides;

        private readonly List<PendingSound> pending = new();
        private AudioSource source;
        private AudioClip[] generatedClips;
        private double lastClock = double.NaN;
        private float flashStartTime = -100f;
        private float flashScale;
        private float flashEnvelope;
        private LineRenderer[] bolts;
        private Light flashLight;
        private Material runtimeBoltMaterial;
        private Camera mainCamera;

        private struct PendingSound
        {
            public float PlayAt;
            public float Volume;
            public float Pitch;
            public int ClipRoll;
        }

        /// <summary>0..1(ish) brightness of the current lightning flash.</summary>
        public float Flash { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;

            var child = new GameObject("Thunder");
            child.transform.SetParent(transform, false);
            source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.outputAudioMixerGroup = SFXManager.Instance != null ? SFXManager.Instance.sfxMixerGroup : null;

            if (strikeArea == null)
            {
                strikeArea = GetComponentInChildren<BoxCollider>(true);
            }

            BuildBolt();
        }

        private void BuildBolt()
        {
            Material material = boltMaterial;
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                {
                    shader = Shader.Find("Sprites/Default");
                }

                runtimeBoltMaterial = new Material(shader) { name = "Lightning Bolt (runtime)" };
                runtimeBoltMaterial.SetColor("_BaseColor", boltColor);
                runtimeBoltMaterial.SetColor("_Color", boltColor);
                material = runtimeBoltMaterial;
            }

            var root = new GameObject("Lightning");
            root.transform.SetParent(transform, false);

            // One main channel and two branches off it.
            bolts = new LineRenderer[3];
            for (int i = 0; i < bolts.Length; i++)
            {
                var child = new GameObject(i == 0 ? "Bolt" : "Branch");
                child.transform.SetParent(root.transform, false);
                LineRenderer line = child.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.sharedMaterial = material;
                line.startColor = boltColor;
                line.endColor = boltColor;
                line.alignment = LineAlignment.View;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.widthCurve = AnimationCurve.Linear(0f, i == 0 ? 1f : 0.55f, 1f, i == 0 ? 0.6f : 0.1f);
                line.enabled = false;
                bolts[i] = line;
            }

            var lightObject = new GameObject("Lightning Flash");
            lightObject.transform.SetParent(root.transform, false);
            flashLight = lightObject.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = flashLightColor;
            flashLight.range = flashLightRange;
            flashLight.shadows = LightShadows.None;
            flashLight.intensity = 0f;
            flashLight.enabled = false;
        }

        // Synthesised up front rather than on the first strike, where building the clips would
        // hitch the frame the flash lands on.
        private void Start()
        {
            SFXManager sfx = SFXManager.Instance;
            if (!HasAny(thunderClipOverrides) && (sfx == null || !HasAny(sfx.thunderClips)))
            {
                Clips();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (runtimeBoltMaterial != null)
            {
                Destroy(runtimeBoltMaterial);
            }

            if (generatedClips != null)
            {
                foreach (AudioClip clip in generatedClips)
                {
                    Destroy(clip);
                }
            }
        }

        private void Update()
        {
            WeatherSystem weather = WeatherSystem.Instance;
            UpdateFlash();
            UpdateStrikeVisuals();
            PlayDueSounds();

            if (weather == null)
            {
                return;
            }

            double clock = weather.Clock;
            bool storming = weather.Phase == WeatherPhase.Storm;

            // First frame, a clock that went backwards (a network time correction), or a big jump
            // (a late join): start counting from now instead of firing every strike in between.
            if (double.IsNaN(lastClock) || clock < lastClock || clock - lastClock > slotSeconds)
            {
                lastClock = clock;
                return;
            }

            if (storming && slotSeconds > 0.5f)
            {
                long firstSlot = (long)System.Math.Floor(lastClock / slotSeconds);
                long lastSlot = (long)System.Math.Floor(clock / slotSeconds);
                for (long slot = firstSlot; slot <= lastSlot; slot++)
                {
                    TryStrike(weather.Seed, slot, clock);
                }
            }

            lastClock = clock;
        }

        private void TryStrike(int seed, long slot, double clock)
        {
            if (Hash(seed, slot, 0) > strikeChance)
            {
                return;
            }

            // Kept off the slot edges so two strikes are never back to back.
            double strikeAt = (slot + 0.1 + 0.8 * Hash(seed, slot, 1)) * slotSeconds;
            if (strikeAt <= lastClock || strikeAt > clock)
            {
                return;
            }

            Vector3 ground = StrikePoint(seed, slot);
            var random = new System.Random(unchecked(seed * 7919 + (int)slot));
            BuildBoltShape(ground, random);
            SpawnImpact(ground);

            // Real distance to this peer's camera: the flash is shared, how far away it sounds is not.
            if (mainCamera == null || !mainCamera.isActiveAndEnabled)
            {
                mainCamera = Camera.main;
            }

            float distance = mainCamera != null
                ? Mathf.InverseLerp(0f, Mathf.Max(1f, farDistance), Vector3.Distance(mainCamera.transform.position, ground))
                : Hash(seed, slot, 2);

            flashStartTime = Time.time;
            flashScale = flashStrength * Mathf.Lerp(1f, 0.45f, distance);

            pending.Add(new PendingSound
            {
                PlayAt = Time.time + Mathf.Lerp(soundDelayRange.x, soundDelayRange.y, distance),
                Volume = Mathf.Lerp(maxVolume, minVolume, distance),
                Pitch = Mathf.Lerp(1.05f, 0.8f, distance) * Mathf.Lerp(0.95f, 1.05f, Hash(seed, slot, 3)),
                ClipRoll = (int)(Hash(seed, slot, 4) * 1000f),
            });
        }

        // Seeded, so every peer puts the bolt in the same place.
        private Vector3 StrikePoint(int seed, long slot)
        {
            float u = Hash(seed, slot, 5);
            float v = Hash(seed, slot, 6);

            Vector3 point;
            if (strikeArea != null)
            {
                Bounds bounds = strikeArea.bounds;
                point = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, u), bounds.max.y,
                    Mathf.Lerp(bounds.min.z, bounds.max.z, v));
            }
            else
            {
                float angle = u * Mathf.PI * 2f;
                float radius = Mathf.Sqrt(v) * strikeRadius;
                point = transform.position + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            }

            var top = new Vector3(point.x, point.y + 200f, point.z);
            return Physics.Raycast(top, Vector3.down, out RaycastHit hit, 600f, groundMask, QueryTriggerInteraction.Ignore)
                ? hit.point
                : new Vector3(point.x, transform.position.y, point.z);
        }

        // Midpoint displacement: jagged at every scale, like a real channel, instead of a zigzag.
        private void BuildBoltShape(Vector3 ground, System.Random random)
        {
            if (bolts == null)
            {
                return;
            }

            Vector3 skyOffset = new((float)(random.NextDouble() - 0.5) * boltHeight * 0.3f, 0f,
                (float)(random.NextDouble() - 0.5) * boltHeight * 0.3f);
            Vector3 top = ground + Vector3.up * boltHeight + skyOffset;
            Vector3[] main = Jagged(top, ground, 5, boltHeight * 0.12f, random);
            SetLine(bolts[0], main);

            for (int i = 1; i < bolts.Length; i++)
            {
                Vector3 from = main[random.Next(main.Length / 6, main.Length / 2)];
                Vector3 outward = new((float)(random.NextDouble() - 0.5), 0f, (float)(random.NextDouble() - 0.5));
                Vector3 to = from + outward.normalized * boltHeight * 0.25f +
                    Vector3.down * boltHeight * (0.2f + 0.2f * (float)random.NextDouble());
                SetLine(bolts[i], Jagged(from, to, 4, boltHeight * 0.05f, random));
            }
        }

        private static Vector3[] Jagged(Vector3 from, Vector3 to, int depth, float spread, System.Random random)
        {
            int count = (1 << depth) + 1;
            var points = new Vector3[count];
            points[0] = from;
            points[count - 1] = to;

            for (int step = count - 1; step > 1; step /= 2)
            {
                for (int i = 0; i + step < count; i += step)
                {
                    Vector3 mid = (points[i] + points[i + step]) * 0.5f;
                    mid += new Vector3((float)(random.NextDouble() - 0.5), (float)(random.NextDouble() - 0.5) * 0.3f,
                        (float)(random.NextDouble() - 0.5)) * spread;
                    points[i + step / 2] = mid;
                }

                spread *= 0.55f;
            }

            return points;
        }

        private static void SetLine(LineRenderer line, Vector3[] points)
        {
            line.positionCount = points.Length;
            line.SetPositions(points);
        }

        private void SpawnImpact(Vector3 ground)
        {
            if (flashLight != null)
            {
                flashLight.transform.position = ground + Vector3.up * 25f;
            }

            if (impactEffect == null)
            {
                return;
            }

            GameObject effect = Instantiate(impactEffect, ground, Quaternion.identity);
            effect.transform.localScale *= impactScale;
            // CFXR effects clean themselves up; this is the backstop for anything else.
            Destroy(effect, 8f);
        }

        private void UpdateStrikeVisuals()
        {
            bool visible = flashEnvelope > 0.12f;

            if (bolts != null)
            {
                foreach (LineRenderer line in bolts)
                {
                    if (line == null)
                    {
                        continue;
                    }

                    if (line.enabled != visible)
                    {
                        line.enabled = visible;
                    }

                    line.widthMultiplier = boltWidth * flashEnvelope;
                }
            }

            if (flashLight != null)
            {
                bool lit = Flash > 0.01f;
                if (flashLight.enabled != lit)
                {
                    flashLight.enabled = lit;
                }

                flashLight.intensity = flashLightIntensity * Flash;
            }
        }

        // Two flickers and a fade, which reads as lightning far better than one smooth pulse.
        private void UpdateFlash()
        {
            float t = Time.time - flashStartTime;
            if (t < 0f || t > flashSeconds)
            {
                Flash = 0f;
                flashEnvelope = 0f;
                return;
            }

            float scale = flashSeconds / 0.6f;
            float first = Mathf.Exp(-t / (0.07f * scale));
            float second = t > 0.13f * scale ? 0.8f * Mathf.Exp(-(t - 0.13f * scale) / (0.12f * scale)) : 0f;
            float fadeOut = 1f - Mathf.SmoothStep(0f, 1f, t / flashSeconds);
            flashEnvelope = Mathf.Max(first, second) * fadeOut;
            Flash = flashEnvelope * flashScale;
        }

        private void PlayDueSounds()
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (Time.time < pending[i].PlayAt)
                {
                    continue;
                }

                PendingSound sound = pending[i];
                pending.RemoveAt(i);

                AudioClip[] clips = Clips();
                if (source == null || clips.Length == 0)
                {
                    continue;
                }

                if (source.outputAudioMixerGroup == null && SFXManager.Instance != null)
                {
                    source.outputAudioMixerGroup = SFXManager.Instance.sfxMixerGroup;
                }

                source.pitch = sound.Pitch;
                source.PlayOneShot(clips[sound.ClipRoll % clips.Length], sound.Volume);
            }
        }

        private AudioClip[] Clips()
        {
            if (HasAny(thunderClipOverrides))
            {
                return thunderClipOverrides;
            }

            SFXManager sfx = SFXManager.Instance;
            if (sfx != null && HasAny(sfx.thunderClips))
            {
                return sfx.thunderClips;
            }

            return generatedClips ??= new[] { GenerateRumble(11, 5.5f), GenerateRumble(29, 6.5f), GenerateRumble(47, 4.5f) };
        }

        private static bool HasAny(AudioClip[] clips)
        {
            if (clips == null)
            {
                return false;
            }

            foreach (AudioClip clip in clips)
            {
                if (clip == null)
                {
                    return false;
                }
            }

            return clips.Length > 0;
        }

        // Deterministic 0..1 from (seed, slot, salt), identical on every peer.
        private static float Hash(int seed, long slot, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u;
                h ^= (uint)slot * 0x85EBCA77u + (uint)(slot >> 32) * 0xC2B2AE3Du;
                h ^= (uint)salt * 0x27D4EB2Fu;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        // A stand-in rumble: a sharp crack, then brown noise under a few rolling swells that dies
        // away over the clip. Good enough that a storm is never silent before real clips exist.
        private static AudioClip GenerateRumble(int seed, float seconds)
        {
            const int rate = 22050;
            int count = Mathf.CeilToInt(rate * seconds);
            var data = new float[count];
            var random = new System.Random(seed);

            int swellCount = 3 + random.Next(3);
            var swellTimes = new float[swellCount];
            var swellGains = new float[swellCount];
            for (int i = 0; i < swellCount; i++)
            {
                swellTimes[i] = 0.3f + (float)random.NextDouble() * seconds * 0.55f;
                swellGains[i] = 0.4f + (float)random.NextDouble() * 0.6f;
            }

            float brown = 0f;
            float low = 0f;
            float peak = 0.0001f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                float white = (float)(random.NextDouble() * 2.0 - 1.0);

                brown = brown * 0.997f + white * 0.04f;
                low += (brown - low) * 0.06f;

                float envelope = Mathf.Clamp01(t / 0.04f) * Mathf.Exp(-t * 0.75f);
                for (int s = 0; s < swellCount; s++)
                {
                    float d = (t - swellTimes[s]) / 0.45f;
                    envelope += swellGains[s] * Mathf.Exp(-d * d) * Mathf.Exp(-t * 0.35f);
                }

                float crack = white * Mathf.Exp(-t * 22f) * 0.25f;
                float fadeOut = Mathf.Clamp01((seconds - t) / 0.5f);
                data[i] = (low * envelope + crack) * fadeOut;
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }

            float gain = 0.9f / peak;
            for (int i = 0; i < count; i++)
            {
                data[i] *= gain;
            }

            AudioClip clip = AudioClip.Create($"Thunder (generated {seed})", count, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
