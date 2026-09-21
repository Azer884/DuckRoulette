using System.Collections.Generic;
using UnityEngine;

namespace Weather
{
    /// <summary>
    /// Local distributor for the wind <see cref="WeatherSystem"/> computes.
    ///
    /// Everything that reacts to wind - trees, tent cloth, the wind-line VFX - implements
    /// <see cref="IWindReceiver"/> and is ticked from this one Update instead of each running its
    /// own. Thirteen trees plus three tents is sixteen MonoBehaviour.Update calls saved, and more
    /// usefully it means the whole scene samples the same wind value in the same frame, so a gust
    /// hits the trees, the tents and the particles together rather than one frame apart.
    ///
    /// It also publishes the wind as shader globals, so any material that wants vertex sway can
    /// pick it up without a per-renderer script:
    ///   float4 _WeatherWindDirection  (xyz = unit direction, w = strength 0..1)
    ///   float  _WeatherWindStrength
    ///   float3 _WeatherWindVelocity   (direction * strength * top speed, m/s)
    /// </summary>
    [DisallowMultipleComponent]
    public class WindSystem : MonoBehaviour
    {
        public static WindSystem Instance { get; private set; }

        private static readonly int WindDirectionId = Shader.PropertyToID("_WeatherWindDirection");
        private static readonly int WindStrengthId = Shader.PropertyToID("_WeatherWindStrength");
        private static readonly int WindVelocityId = Shader.PropertyToID("_WeatherWindVelocity");

        // Registered rather than found: a FindObjectsByType sweep would cost a full scene walk and
        // would miss anything spawned later (a tent rebuilt between rounds).
        private static readonly List<IWindReceiver> receivers = new();

        [SerializeField, Tooltip("Fallback direction and strength used before the networked " +
            "WeatherSystem exists - in the Tutorial scene, or in the editor outside play mode.")]
        private Vector3 fallbackDirection = new(0.7f, 0f, 0.7f);

        [SerializeField, Range(0f, 1f)] private float fallbackStrength = 0.2f;

        [SerializeField, Tooltip("Metres per second at strength 1. Kept in step with " +
            "WeatherSystem's own value; only used for the fallback path.")]
        private float fallbackSpeedAtFullStrength = 14f;

        /// <summary>Unit vector the wind blows towards, on the XZ plane.</summary>
        public Vector3 Direction { get; private set; } = Vector3.forward;

        /// <summary>0..1, gusts included.</summary>
        public float Strength { get; private set; }

        /// <summary>Direction * strength * top speed, in metres per second.</summary>
        public Vector3 Velocity { get; private set; }

        public static void Register(IWindReceiver receiver)
        {
            if (receiver != null && !receivers.Contains(receiver))
            {
                receivers.Add(receiver);
            }
        }

        public static void Unregister(IWindReceiver receiver)
        {
            if (receiver != null)
            {
                receivers.Remove(receiver);
            }
        }

        /// <summary>The current wind, or a sane still-air default when no WindSystem is live -
        /// so a receiver never has to null-check the singleton itself.</summary>
        public static void Sample(out Vector3 direction, out float strength, out Vector3 velocity)
        {
            if (Instance != null)
            {
                direction = Instance.Direction;
                strength = Instance.Strength;
                velocity = Instance.Velocity;
                return;
            }

            direction = Vector3.forward;
            strength = 0f;
            velocity = Vector3.zero;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            Direction = fallbackDirection.sqrMagnitude > 0.0001f ? fallbackDirection.normalized : Vector3.forward;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            WeatherSystem weather = WeatherSystem.Instance;
            if (weather != null)
            {
                Direction = weather.WindDirection;
                Strength = weather.WindStrength;
                Velocity = weather.WindVelocity;
            }
            else
            {
                Direction = fallbackDirection.sqrMagnitude > 0.0001f ? fallbackDirection.normalized : Vector3.forward;
                Strength = fallbackStrength;
                Velocity = Direction * (fallbackStrength * fallbackSpeedAtFullStrength);
            }

            Shader.SetGlobalVector(WindDirectionId, new Vector4(Direction.x, Direction.y, Direction.z, Strength));
            Shader.SetGlobalFloat(WindStrengthId, Strength);
            Shader.SetGlobalVector(WindVelocityId, Velocity);

            float deltaTime = Time.deltaTime;
            // Backwards so a receiver that unregisters itself mid-tick (a tent despawning) does
            // not shuffle the ones still to be ticked.
            for (int i = receivers.Count - 1; i >= 0; i--)
            {
                IWindReceiver receiver = receivers[i];
                if (receiver == null)
                {
                    receivers.RemoveAt(i);
                    continue;
                }

                receiver.OnWind(Direction, Strength, Velocity, deltaTime);
            }
        }
    }

    /// <summary>Anything the wind pushes on. Registered with <see cref="WindSystem"/> for the
    /// lifetime it wants to be ticked.</summary>
    public interface IWindReceiver
    {
        void OnWind(Vector3 direction, float strength, Vector3 velocity, float deltaTime);
    }
}
