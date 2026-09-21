using UnityEngine;
using UnityEngine.Audio;

namespace Weather
{
    /// <summary>
    /// The sound of the sky: a wind bed whose volume and pitch ride the gusts, and a rain bed
    /// that gets heavier with the phase. Both are 2D loops - weather has no position - and both
    /// are driven from the same wind tick as everything else, so a gust you can see in the trees
    /// is the gust you hear.
    ///
    /// Clips come from SFXManager so they sit with the rest of the game's audio and go through
    /// the same mixer group (and so its startup warning catches any that are still unassigned).
    /// </summary>
    [DisallowMultipleComponent]
    public class WeatherAudio : MonoBehaviour, IWindReceiver
    {
        [Header("Wind")]
        [SerializeField, Range(0f, 1f), Tooltip("Volume of the wind bed at wind strength 1.")]
        private float windMaxVolume = 0.55f;

        [SerializeField, Tooltip("Wind strength below which the bed is silent.")]
        private float windFloor = 0.08f;

        [SerializeField, Tooltip("Pitch at no wind and at full gale. A little pitch movement is " +
            "what sells a gust more than volume does.")]
        private Vector2 windPitchRange = new(0.85f, 1.25f);

        [Header("Rain")]
        [SerializeField, Range(0f, 1f), Tooltip("Volume of the rain bed at storm strength.")]
        private float rainMaxVolume = 0.7f;

        [SerializeField, Tooltip("Extra volume the rain picks up from the wind on top of the " +
            "phase, so a squall is audibly harder than steady rain.")]
        private float rainWindBoost = 0.25f;

        [Header("Hail")]
        [SerializeField, Range(0f, 1f)] private float hailMaxVolume = 0.5f;

        [Header("Blend")]
        [SerializeField, Tooltip("Seconds for a bed to reach its target volume.")]
        private float blendSeconds = 3f;

        [Header("Clip overrides")]
        [SerializeField, Tooltip("Used when set; otherwise SFXManager's rainLoopClip. SFXManager " +
            "lives in the Loading scene, so a session started straight from the game scene has no " +
            "clips unless one is set here.")]
        private AudioClip rainClipOverride;

        [SerializeField] private AudioClip windClipOverride;
        [SerializeField] private AudioClip hailClipOverride;

        private AudioSource windSource;
        private AudioSource rainSource;
        private AudioSource hailSource;

        private void Awake()
        {
            AudioMixerGroup group = SFXManager.Instance != null ? SFXManager.Instance.sfxMixerGroup : null;

            windSource = CreateLoop("Wind Bed", group);
            rainSource = CreateLoop("Rain Bed", group);
            hailSource = CreateLoop("Hail Bed", group);
        }

        private void Start() => AssignClips();

        // Retried from OnWind until every bed has something, rather than tried once in Start: the
        // SFXManager singleton can come up after this scene object does, and a single early miss
        // used to leave the rain silent for the whole match.
        private bool AssignClips()
        {
            SFXManager sfx = SFXManager.Instance;

            AssignIfEmpty(windSource, windClipOverride != null ? windClipOverride : sfx != null ? sfx.windLoopClip : null);
            AssignIfEmpty(rainSource, rainClipOverride != null ? rainClipOverride : sfx != null ? sfx.rainLoopClip : null);
            AssignIfEmpty(hailSource, hailClipOverride != null ? hailClipOverride : sfx != null ? sfx.hailLoopClip : null);

            if (sfx != null && sfx.sfxMixerGroup != null)
            {
                SetGroup(windSource, sfx.sfxMixerGroup);
                SetGroup(rainSource, sfx.sfxMixerGroup);
                SetGroup(hailSource, sfx.sfxMixerGroup);
            }

            return rainSource != null && rainSource.clip != null;
        }

        private static void AssignIfEmpty(AudioSource source, AudioClip clip)
        {
            if (source != null && source.clip == null && clip != null)
            {
                source.clip = clip;
            }
        }

        private static void SetGroup(AudioSource source, AudioMixerGroup group)
        {
            if (source != null && source.outputAudioMixerGroup == null)
            {
                source.outputAudioMixerGroup = group;
            }
        }

        private void OnEnable() => WindSystem.Register(this);

        private void OnDisable() => WindSystem.Unregister(this);

        public void OnWind(Vector3 direction, float strength, Vector3 velocity, float deltaTime)
        {
            if (rainSource != null && rainSource.clip == null)
            {
                AssignClips();
            }

            WeatherSystem weather = WeatherSystem.Instance;
            float rain = weather != null ? weather.RainIntensity : 0f;
            bool storming = weather != null && weather.Phase == WeatherPhase.Storm;

            float windTarget = Mathf.InverseLerp(windFloor, 1f, strength) * windMaxVolume;
            Blend(windSource, windTarget, deltaTime);

            if (windSource != null && windSource.isPlaying)
            {
                windSource.pitch = Mathf.Lerp(windPitchRange.x, windPitchRange.y, strength);
            }

            // The rain itself gets louder with the wind driving it, which is the "rain gets
            // stronger with the wind" part of the effect.
            float rainTarget = rain > 0f ? Mathf.Clamp01(rain + strength * rainWindBoost) * rainMaxVolume : 0f;
            Blend(rainSource, rainTarget, deltaTime);

            Blend(hailSource, storming ? hailMaxVolume : 0f, deltaTime);
        }

        private void Blend(AudioSource source, float target, float deltaTime)
        {
            if (source == null || source.clip == null)
            {
                return;
            }

            source.volume = Mathf.MoveTowards(source.volume, target, deltaTime / Mathf.Max(0.01f, blendSeconds));

            bool shouldPlay = source.volume > 0.001f || target > 0.001f;
            if (shouldPlay && !source.isPlaying)
            {
                source.Play();
            }
            else if (!shouldPlay && source.isPlaying)
            {
                source.Stop();
            }
        }

        private AudioSource CreateLoop(string sourceName, AudioMixerGroup group)
        {
            var child = new GameObject(sourceName);
            child.transform.SetParent(transform, false);

            AudioSource source = child.AddComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.outputAudioMixerGroup = group;
            return source;
        }
    }
}
