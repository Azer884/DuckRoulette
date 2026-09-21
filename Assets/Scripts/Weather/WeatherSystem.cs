using System;
using Unity.Netcode;
using UnityEngine;

namespace Weather
{
    /// <summary>
    /// Server-authoritative owner of the sky: the day/night clock, the current
    /// <see cref="WeatherPhase"/>, and the wind.
    ///
    /// Only four things are ever replicated, and none of them per frame:
    ///   - the server time the day cycle started from,
    ///   - the current phase and the server time it runs out,
    ///   - a wind seed.
    /// Everything else - the sun angle, the wind direction and gust strength - is a pure function
    /// of those values and <see cref="NetworkManager.ServerTime"/>, which Netcode already keeps in
    /// step on every peer. So every client draws the same sun and feels the same gust with zero
    /// per-frame bandwidth, and a late joiner lands on the right time of day the moment it spawns.
    ///
    /// Match decisions stay in GameManager: it does the rain roll (GameManager.StartRain) and the
    /// small second roll that turns a shower into a storm, then raises OnWeatherChange. This class
    /// only runs that decision out and decides what it looks and sounds like.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeatherSystem : NetworkBehaviour
    {
        public static WeatherSystem Instance { get; private set; }

        [Header("Day / night")]
        [SerializeField, Tooltip("Real seconds for one full day + night.")]
        private float dayLengthSeconds = 420f;

        [SerializeField, Range(0f, 1f), Tooltip("Where the clock starts when a match begins. " +
            "0 = midnight, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset.")]
        private float startTimeOfDay = 0.34f;

        [SerializeField, Range(0.5f, 1f), Tooltip("Normalized time the sun sets (night begins).")]
        private float duskTime = 0.78f;

        [SerializeField, Range(0f, 0.5f), Tooltip("Normalized time the sun rises (night ends).")]
        private float dawnTime = 0.24f;

        [Header("Rain")]
        [SerializeField, Tooltip("GameManager's climbing rain roll is multiplied by this after " +
            "dusk, so rain is markedly more likely at night.")]
        private float nightRainChanceMultiplier = 2.5f;

        [SerializeField, Tooltip("Seconds a plain shower lasts before the sky clears.")]
        private float rainDuration = 60f;

        [SerializeField, Tooltip("Seconds a storm lasts before it falls back to a plain shower.")]
        private float stormDuration = 45f;

        [Header("Wind")]
        [SerializeField, Tooltip("How fast the wind swings around, in Perlin units per second. " +
            "Small values give slow, believable direction changes.")]
        private float windTurnSpeed = 0.035f;

        [SerializeField, Tooltip("How fast gusts come and go, in Perlin units per second.")]
        private float windGustSpeed = 0.14f;

        [SerializeField, Range(0f, 1f)] private float clearWindStrength = 0.18f;
        [SerializeField, Range(0f, 1f)] private float rainWindStrength = 0.5f;
        [SerializeField, Range(0f, 1f)] private float stormWindStrength = 1f;

        [SerializeField, Tooltip("Metres per second at wind strength 1. Drives cloth, particles " +
            "and tree sway amplitude.")]
        private float windSpeedAtFullStrength = 14f;

        [SerializeField, Tooltip("Seconds for the felt wind to catch up to a phase change, so a " +
            "storm rolls in instead of snapping on.")]
        private float windBlendSeconds = 6f;

        [Header("Testing")]
        [SerializeField, Tooltip("Set to Rain or Storm to make the weather fire on its own shortly " +
            "after the match starts, instead of waiting for GameManager's climbing roll (which " +
            "only becomes likely after several minutes). Leave on Clear for a real build.")]
        private WeatherPhase forcePhaseOnStart = WeatherPhase.Clear;

        [SerializeField, Tooltip("Seconds after the server starts before the forced phase fires.")]
        private float forcePhaseDelay = 6f;

        // --- replicated state ---
        private readonly NetworkVariable<double> dayStartServerTime = new();
        private readonly NetworkVariable<byte> phase = new((byte)WeatherPhase.Clear);
        private readonly NetworkVariable<int> windSeed = new();
        // Server time the current phase runs out. Replicated instead of a countdown so the HUD on
        // every client can show the same remaining seconds without a per-frame sync.
        private readonly NetworkVariable<double> phaseEndsAt = new();

        // --- local, derived every frame ---
        private float smoothedWindStrength;
        private float forceTimer;

        /// <summary>Raised locally on every peer when the replicated phase changes.</summary>
        public static event Action<WeatherPhase> PhaseChanged;

        public WeatherPhase Phase => (WeatherPhase)phase.Value;

        /// <summary>0 = midnight, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset. Same on every peer.</summary>
        public float NormalizedTime { get; private set; }

        /// <summary>Seconds on the shared day clock. Same on every peer, so anything keyed off it
        /// (thunder) happens at the same moment everywhere.</summary>
        public double Clock { get; private set; }

        /// <summary>The match's replicated weather seed.</summary>
        public int Seed => windSeed.Value;

        public bool IsNight => NormalizedTime >= duskTime || NormalizedTime < dawnTime;

        /// <summary>What GameManager multiplies its rain roll by. Nights are wetter.</summary>
        public float RainChanceMultiplier => IsNight ? nightRainChanceMultiplier : 1f;

        /// <summary>Unit vector the wind blows towards, on the XZ plane.</summary>
        public Vector3 WindDirection { get; private set; } = Vector3.forward;

        /// <summary>0..1 including gusts. Drives everything that reacts to wind.</summary>
        public float WindStrength => smoothedWindStrength;

        /// <summary>Direction * strength * top speed, in metres per second.</summary>
        public Vector3 WindVelocity => WindDirection * (smoothedWindStrength * windSpeedAtFullStrength);

        /// <summary>How long the current weather event has left, in seconds. 0 when clear.</summary>
        public float PhaseSecondsRemaining
        {
            get
            {
                if (Phase == WeatherPhase.Clear || NetworkManager == null || !NetworkManager.IsListening)
                {
                    return 0f;
                }

                return Mathf.Max(0f, (float)(phaseEndsAt.Value - NetworkManager.ServerTime.Time));
            }
        }

        /// <summary>How long the current phase lasts in total, for a progress ratio. Read off the
        /// serialized durations rather than replicated, because they are authored content every
        /// peer already has.</summary>
        public float PhaseTotalSeconds => Phase switch
        {
            WeatherPhase.Rain => rainDuration,
            WeatherPhase.Storm => stormDuration,
            _ => 0f,
        };

        /// <summary>Rain intensity as a 0..1 ramp, for emission rates and audio volume.</summary>
        public float RainIntensity => Phase switch
        {
            WeatherPhase.Rain => 0.55f,
            WeatherPhase.Storm => 1f,
            _ => 0f,
        };

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            NormalizedTime = startTimeOfDay;
            smoothedWindStrength = clearWindStrength;
        }

        public override void OnNetworkSpawn()
        {
            phase.OnValueChanged += OnPhaseValueChanged;

            if (IsServer)
            {
                dayStartServerTime.Value = NetworkManager.ServerTime.Time;
                // One seed for the whole match: the wind is then a deterministic function of the
                // shared clock, so nobody has to replicate a direction or a gust.
                windSeed.Value = UnityEngine.Random.Range(1, 100000);
                GameManager.OnWeatherChange += OnGameManagerWeatherChange;

                if (forcePhaseOnStart != WeatherPhase.Clear)
                {
                    forceTimer = Mathf.Max(0.1f, forcePhaseDelay);
                }
            }

            // A late joiner needs the sky it is arriving into, not a fade up from Clear.
            Sample(Time.deltaTime, snapWind: true);
            PhaseChanged?.Invoke(Phase);
        }

        public override void OnNetworkDespawn()
        {
            phase.OnValueChanged -= OnPhaseValueChanged;

            if (IsServer)
            {
                GameManager.OnWeatherChange -= OnGameManagerWeatherChange;
            }
        }

        public override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            base.OnDestroy();
        }

        private void OnGameManagerWeatherChange(WeatherPhase rolled) => BeginWeather(rolled);

        /// <summary>Server only. Starts a shower or a storm. GameManager owns the roll that gets
        /// here; this only runs it out and lets it decay.</summary>
        public void BeginWeather(WeatherPhase newPhase)
        {
            if (!IsServer || newPhase == WeatherPhase.Clear)
            {
                return;
            }

            phase.Value = (byte)newPhase;
            phaseEndsAt.Value = NetworkManager.ServerTime.Time +
                (newPhase == WeatherPhase.Storm ? stormDuration : rainDuration);

            // Loud on purpose: "the weather never happens" is otherwise indistinguishable from
            // "the weather happened and nothing reacted", and they have very different fixes.
            Debug.Log($"WeatherSystem: {newPhase} started, running for " +
                $"{(newPhase == WeatherPhase.Storm ? stormDuration : rainDuration):0} seconds.");
        }

        /// <summary>Starts a weather event from a menu item or the console, for testing. Server
        /// only - a client calling it changes nothing, and says so rather than failing quietly.</summary>
        public static void DebugForce(WeatherPhase newPhase)
        {
            if (Instance == null)
            {
                Debug.LogWarning("WeatherSystem: no instance in the scene - enter play mode with " +
                    "the game scene loaded first.");
                return;
            }

            if (!Instance.IsServer)
            {
                Debug.LogWarning("WeatherSystem: only the host decides the weather; this peer is a client.");
                return;
            }

            if (newPhase == WeatherPhase.Clear)
            {
                Instance.phase.Value = (byte)WeatherPhase.Clear;
                return;
            }

            Instance.BeginWeather(newPhase);
        }

        private void OnPhaseValueChanged(byte previous, byte current)
        {
            PhaseChanged?.Invoke((WeatherPhase)current);
        }

        private void Update()
        {
            if (IsServer && forceTimer > 0f)
            {
                forceTimer -= Time.deltaTime;
                if (forceTimer <= 0f)
                {
                    BeginWeather(forcePhaseOnStart);
                }
            }

            if (IsServer && Phase != WeatherPhase.Clear && NetworkManager.ServerTime.Time >= phaseEndsAt.Value)
            {
                // A storm blows itself out into a shower first, so the sky never snaps from hail
                // to sunshine in one frame.
                if (Phase == WeatherPhase.Storm)
                {
                    phase.Value = (byte)WeatherPhase.Rain;
                    phaseEndsAt.Value = NetworkManager.ServerTime.Time + rainDuration;
                }
                else
                {
                    phase.Value = (byte)WeatherPhase.Clear;
                }
            }

            Sample(Time.deltaTime, snapWind: false);
        }

        private void Sample(float deltaTime, bool snapWind)
        {
            double clock = NetworkManager != null && NetworkManager.IsListening
                ? NetworkManager.ServerTime.Time - dayStartServerTime.Value
                : Time.timeSinceLevelLoad;
            Clock = clock;

            NormalizedTime = Mathf.Repeat(startTimeOfDay + (float)(clock / Mathf.Max(1f, dayLengthSeconds)), 1f);

            // Perlin on a single shared clock: continuous (no snapping the way Random would give),
            // cheap, and identical on every peer because the clock and the seed are.
            float seed = windSeed.Value * 0.37f;
            float turn = Mathf.PerlinNoise(seed, (float)clock * windTurnSpeed);
            float angle = turn * 720f; // two full turns across the noise range, so it can circle.
            WindDirection = new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad), 0f, Mathf.Cos(angle * Mathf.Deg2Rad));

            float baseStrength = Phase switch
            {
                WeatherPhase.Rain => rainWindStrength,
                WeatherPhase.Storm => stormWindStrength,
                _ => clearWindStrength,
            };

            // Gusts ride on top of the phase floor rather than replacing it, so a storm never
            // goes fully still.
            float gust = Mathf.PerlinNoise(seed + 13.7f, (float)clock * windGustSpeed);
            float target = Mathf.Clamp01(baseStrength * Mathf.Lerp(0.55f, 1.25f, gust));

            smoothedWindStrength = snapWind
                ? target
                : Mathf.MoveTowards(smoothedWindStrength, target, deltaTime / Mathf.Max(0.01f, windBlendSeconds));
        }
    }
}
