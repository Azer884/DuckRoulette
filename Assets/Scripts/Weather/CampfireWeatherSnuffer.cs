using UnityEngine;

namespace Weather
{
    /// <summary>
    /// Puts the campfire out when the weather turns, and takes its task off the board with it.
    ///
    /// The task half matters as much as the visual: "Light the campfire" is a real task in the
    /// rotation, and once the rain has doused it nobody can complete it. Leaving it assigned would
    /// lock its holder out of the gun for a round they had no way to finish, so the objective is
    /// disabled (which unregisters it from the rotation) and the server cancels any copy already
    /// dealt out this round.
    ///
    /// Goes on the Campfire in the game scene, beside its TaskObjective.
    /// </summary>
    [DisallowMultipleComponent]
    public class CampfireWeatherSnuffer : MonoBehaviour, IWindReceiver
    {
        [SerializeField, Tooltip("The campfire's task objective. Disabled while the fire is out, " +
            "which is what takes the task out of rotation.")]
        private TaskObjective campfireObjective;

        [SerializeField, Tooltip("Everything that reads as a lit fire: the flame VFX root, the " +
            "embers, the light. All switched off together.")]
        private GameObject[] fireVisuals;

        [SerializeField, Tooltip("Optional. The fire's own light, dimmed out rather than hard cut " +
            "so it does not pop.")]
        private Light fireLight;

        [SerializeField, Tooltip("Optional. The crackle loop, faded with the light.")]
        private AudioSource fireAudio;

        [SerializeField, Tooltip("Optional. Steam burst played once at the moment the rain wins.")]
        private ParticleSystem snuffVfx;

        [Header("Thresholds")]
        [SerializeField, Tooltip("Any rain at all puts it out. Turn off to let a light shower " +
            "leave the fire alone.")]
        private bool rainSnuffs = true;

        [SerializeField, Range(0f, 1f), Tooltip("Wind strength that blows the fire out on its own, " +
            "with no rain at all.")]
        private float windSnuffThreshold = 0.78f;

        [SerializeField, Tooltip("Seconds of clear, calm weather before the fire can be relit.")]
        private float relightDelay = 8f;

        [SerializeField, Tooltip("Seconds for the light and crackle to fade out.")]
        private float fadeSeconds = 1.25f;

        private float baseLightIntensity;
        private float baseAudioVolume;
        private float lit = 1f;
        private float calmTimer;
        private bool snuffed;

        private void Awake()
        {
            if (fireLight != null)
            {
                baseLightIntensity = fireLight.intensity;
            }

            if (fireAudio != null)
            {
                baseAudioVolume = fireAudio.volume;
            }
        }

        private void OnEnable() => WindSystem.Register(this);

        private void OnDisable() => WindSystem.Unregister(this);

        public void OnWind(Vector3 direction, float strength, Vector3 velocity, float deltaTime)
        {
            WeatherSystem weather = WeatherSystem.Instance;
            bool raining = rainSnuffs && weather != null && weather.Phase != WeatherPhase.Clear;
            bool gale = strength >= windSnuffThreshold;

            if (raining || gale)
            {
                calmTimer = 0f;
                if (!snuffed)
                {
                    Snuff();
                }
            }
            else if (snuffed)
            {
                calmTimer += deltaTime;
                if (calmTimer >= relightDelay)
                {
                    Relight();
                }
            }

            // The visuals fade whichever way the state just went, so neither transition snaps.
            float target = snuffed ? 0f : 1f;
            if (!Mathf.Approximately(lit, target))
            {
                lit = Mathf.MoveTowards(lit, target, deltaTime / Mathf.Max(0.01f, fadeSeconds));
                ApplyFade();
            }
        }

        private void ApplyFade()
        {
            if (fireLight != null)
            {
                fireLight.intensity = baseLightIntensity * lit;
                bool shouldBeOn = lit > 0.01f;
                if (fireLight.enabled != shouldBeOn)
                {
                    fireLight.enabled = shouldBeOn;
                }
            }

            if (fireAudio != null)
            {
                fireAudio.volume = baseAudioVolume * lit;
            }
        }

        private void Snuff()
        {
            snuffed = true;

            SetVisuals(false);

            if (snuffVfx != null)
            {
                snuffVfx.Play(true);
            }

            if (campfireObjective != null)
            {
                // OnDisable unregisters the task, which is what stops it being dealt again.
                campfireObjective.enabled = false;
                TaskManager.CancelTaskEverywhere(campfireObjective.Task);
            }
        }

        private void Relight()
        {
            snuffed = false;
            calmTimer = 0f;

            SetVisuals(true);

            if (campfireObjective != null)
            {
                campfireObjective.enabled = true;
            }
        }

        private void SetVisuals(bool value)
        {
            if (fireVisuals == null)
            {
                return;
            }

            foreach (GameObject visual in fireVisuals)
            {
                if (visual != null && visual.activeSelf != value)
                {
                    visual.SetActive(value);
                }
            }
        }
    }
}
