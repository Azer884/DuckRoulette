using UnityEngine;

namespace Player
{
    // Owner-only, local-space particle system parented to the camera: thin streaks fly
    // past the view while running to sell speed, like the old screen-space speed lines
    // but as real wind-line particles instead of a UI overlay (no networking - same
    // local-only pattern as the FOV zoom in Movement).
    public class RunningWindLinesEffect : MonoBehaviour
    {
        [SerializeField] private Movement movement;
        [SerializeField] private ParticleSystem windLines;
        [SerializeField] private float buildUpDuration = 1.5f;
        [SerializeField] private float fadeOutDuration = 0.5f;
        [SerializeField] private float maxEmissionRate = 40f;

        private ParticleSystem.EmissionModule emission;
        private float buildUp;

        private void Awake()
        {
            if (windLines != null)
            {
                emission = windLines.emission;
                emission.rateOverTimeMultiplier = 0f;
            }
        }

        private void OnEnable()
        {
            if (windLines != null)
            {
                windLines.Play(true);
            }
        }

        private void OnDisable()
        {
            if (windLines != null)
            {
                windLines.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void Update()
        {
            if (movement == null || windLines == null)
            {
                return;
            }

            float delta = movement.IsRunning ? Time.deltaTime / buildUpDuration : -Time.deltaTime / fadeOutDuration;
            buildUp = Mathf.Clamp01(buildUp + delta);

            emission.rateOverTimeMultiplier = buildUp * maxEmissionRate;
        }
    }
}
