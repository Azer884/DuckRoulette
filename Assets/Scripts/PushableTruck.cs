using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The camp truck: a heavy, server-simulated vehicle that players can push.
///
/// Physics only runs on the server (or offline, when the truck was never spawned). NetworkTransform
/// + NetworkRigidbody replicate the result, so every peer sees the truck in the same place; on
/// clients the body is kinematic.
///
/// The truck rolls on frictionless wheel spheres and this component supplies the tyres: sideways
/// velocity is gripped away and rolling resistance slows it along its length. That keeps it
/// independent of the prop's scale, which the map scatter changes per instance and which
/// WheelColliders handle badly.
///
/// Weight: a single player pushing gets it moving slowly and cannot get it past a walking pace;
/// more players push it harder and faster. Pushing from the side barely moves it, like a real
/// parked truck.
///
/// The wheel meshes spin on every peer from the truck's own movement, so clients (which do not
/// simulate) see the wheels turn in step with the replicated position.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PushableTruck : NetworkBehaviour, IPushable
{
    [Header("Wheels")]
    [SerializeField, Tooltip("Wheel meshes, spun about the truck's right axis; the two furthest " +
        "forward also steer. Each is hung from a pivot at its own centre at runtime.")]
    private Transform[] wheels;

    [SerializeField, Tooltip("Wheel radius in the truck's own units (before the prefab root is scaled).")]
    private float wheelRadius = 7.75f;

    [SerializeField, Tooltip("How far the front wheels turn at full lock, in degrees.")]
    private float maxWheelSteerAngle = 30f;

    [SerializeField, Tooltip("How fast the front wheels turn towards the steering angle, in degrees per second.")]
    private float wheelSteerSpeed = 160f;

    [SerializeField, Tooltip("Wheel collider spheres, used to check the truck is on the ground.")]
    private SphereCollider[] wheelContacts;

    [Header("Pushing")]
    [SerializeField, Tooltip("Acceleration one pushing player adds, in m/s². Has to beat the rolling " +
        "resistance for a lone player to move the truck at all.")]
    private float pushAcceleration = 1.1f;

    [SerializeField, Tooltip("Top speed with one player pushing, in m/s.")]
    private float maxPushSpeed = 1.0f;

    [SerializeField, Tooltip("Extra top speed per additional pushing player, in m/s.")]
    private float extraSpeedPerPusher = 0.45f;

    [SerializeField, Tooltip("Seconds a push keeps acting after the last contact message. Pushes arrive " +
        "once per owner frame while a player walks into the truck.")]
    private float pushHoldTime = 0.15f;

    [Header("Tyres")]
    [SerializeField, Tooltip("Deceleration along the truck's length when nobody is pushing, in m/s².")]
    private float rollingResistance = 0.55f;

    [SerializeField, Tooltip("How quickly sideways sliding is killed, per second. Higher = the truck " +
        "only rolls forwards and backwards.")]
    private float sidewaysGrip = 6f;

    private struct Push
    {
        public float Time;
        public Vector3 Direction;
        public Vector3 Point;
    }

    [Header("Stability")]
    [SerializeField, Tooltip("Cap on how fast the physics engine may shove the truck out of an " +
        "overlap, in m/s. Keeps a bad overlap from launching eight tonnes across the map.")]
    private float maxDepenetrationSpeed = 1.5f;

    [SerializeField, Tooltip("Top speed while nobody is driving, in m/s. Pushing never gets near " +
        "it; it only catches the truck being flung by something it should not be.")]
    private float maxUndrivenSpeed = 4f;

    private readonly Dictionary<ulong, Push> pushes = new();
    private readonly List<ulong> expired = new();

    // Players whose body colliders already ignore this truck.
    private readonly HashSet<Ragdoll> ignoredPlayers = new();
    private Collider[] ownColliders;
    private float ignoreScanTimer;

    private Rigidbody body;
    private Vector3 lastPosition;

    private bool Simulates => !IsSpawned || IsServer;

    /// <summary>Set by TruckDriver while someone is behind the wheel: the tyres and pushing step
    /// aside and the driving model takes over the body.</summary>
    public bool IsDriven { get; set; }

    public Rigidbody Body => body;

    /// <summary>-1..1, set on every peer by TruckDriver from the driver's stick. Turns the front
    /// wheels; 0 while nobody drives.</summary>
    public float SteerInput { get; set; }

    // Each wheel mesh is pivoted at the model's origin, not its own centre, so it is hung from a
    // pivot at its centre (built in Awake) and the pivot is what spins and steers.
    private Transform[] wheelPivots;
    private bool[] isFrontWheel;
    private float wheelSpin;
    private float wheelSteer;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        lastPosition = transform.position;
        ownColliders = GetComponentsInChildren<Collider>(true);

        body.maxDepenetrationVelocity = maxDepenetrationSpeed;

        BuildWheelPivots();
    }

    private void BuildWheelPivots()
    {
        if (wheels == null || wheels.Length == 0)
        {
            return;
        }

        wheelPivots = new Transform[wheels.Length];
        isFrontWheel = new bool[wheels.Length];
        float[] along = new float[wheels.Length];

        for (int i = 0; i < wheels.Length; i++)
        {
            Transform wheel = wheels[i];
            if (wheel == null)
            {
                continue;
            }

            Vector3 centre = wheel.TryGetComponent(out Renderer renderer) ? renderer.bounds.center : wheel.position;

            // Under the root (uniform scale), not the mesh's own non-uniformly scaled parent, so
            // turning the pivot can never shear the wheel.
            Transform pivot = new GameObject(wheel.name + "_Pivot").transform;
            pivot.SetParent(transform, false);
            pivot.SetPositionAndRotation(centre, transform.rotation);
            wheel.SetParent(pivot, true);

            wheelPivots[i] = pivot;
            along[i] = pivot.localPosition.z;
        }

        // The front axle: the two wheels furthest along the truck's forward axis.
        for (int pick = 0; pick < 2; pick++)
        {
            int best = -1;
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheelPivots[i] != null && !isFrontWheel[i] && (best < 0 || along[i] > along[best]))
                {
                    best = i;
                }
            }

            if (best >= 0)
            {
                isFrontWheel[best] = true;
            }
        }
    }

    public override void OnNetworkSpawn()
    {
        lastPosition = transform.position;
    }

    private void Update()
    {
        ignoreScanTimer -= Time.deltaTime;
        if (ignoreScanTimer <= 0f)
        {
            ignoreScanTimer = 1f;
            IgnorePlayerBodies();
        }
    }

    // Player bone colliders (hitboxes and ragdoll parts) follow the animation, so to the physics
    // engine they are bodies teleporting into the truck every frame. Each overlap was resolved by
    // shoving the truck out at full speed: a lone pusher's arms threw it across the map, and
    // standing on the roof pushed it into the ground until it shot off. Only the
    // CharacterController has to touch the truck - it is what walks into it (PlayerPushObject)
    // and stands on it.
    private void IgnorePlayerBodies()
    {
        ignoredPlayers.RemoveWhere(p => p == null);
        foreach (Ragdoll player in FindObjectsByType<Ragdoll>())
        {
            if (!ignoredPlayers.Add(player))
            {
                continue;
            }

            foreach (Collider part in player.GetComponentsInChildren<Collider>(true))
            {
                if (part is CharacterController || part.isTrigger)
                {
                    continue;
                }

                foreach (Collider own in ownColliders)
                {
                    if (own != null && !own.isTrigger)
                    {
                        Physics.IgnoreCollision(part, own, true);
                    }
                }
            }
        }
    }

    public void OnPushed(ulong pusherId, Vector3 pusherPosition, Vector3 hitPoint)
    {
        if (!Simulates || IsDriven)
        {
            return;
        }

        Vector3 direction = hitPoint - pusherPosition;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        pushes[pusherId] = new Push { Time = Time.time, Direction = direction.normalized, Point = hitPoint };
        body.WakeUp();
    }

    private void FixedUpdate()
    {
        if (!Simulates || body.isKinematic || IsDriven)
        {
            return;
        }

        float dt = Time.fixedDeltaTime;

        Vector3 flatVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        if (flatVelocity.magnitude > maxUndrivenSpeed)
        {
            body.AddForce(flatVelocity.normalized * maxUndrivenSpeed - flatVelocity, ForceMode.VelocityChange);
        }
        bool grounded = IsGrounded();
        int pushers = PruneExpiredPushes();

        if (grounded)
        {
            Vector3 up = transform.up;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(up, forward).normalized;
            Vector3 velocity = body.linearVelocity;

            // Tyres: kill sideways slide, and roll to a stop along the length.
            float sideways = Vector3.Dot(velocity, right);
            body.AddForce(-right * sideways * Mathf.Clamp01(sidewaysGrip * dt), ForceMode.VelocityChange);

            // The tyres also resist the truck spinning in place: a shove at one corner turns it
            // a little, not like a plate on ice.
            Vector3 angular = body.angularVelocity;
            float yaw = Vector3.Dot(angular, up);
            body.AddTorque(-up * yaw * Mathf.Clamp01(sidewaysGrip * dt), ForceMode.VelocityChange);

            // Rolling resistance only while nobody is pushing. Applied on top of a push it ate
            // most of a lone player's shove, and one duck could barely creep the truck along.
            if (pushers == 0)
            {
                float along = Vector3.Dot(velocity, forward);
                float slowed = Mathf.MoveTowards(along, 0f, rollingResistance * dt);
                body.AddForce(forward * (slowed - along), ForceMode.VelocityChange);
            }
        }

        ApplyPushes(pushers);
    }

    // Drops pushes nobody has refreshed lately and returns how many players are still pushing.
    private int PruneExpiredPushes()
    {
        if (pushes.Count == 0)
        {
            return 0;
        }

        float now = Time.time;
        expired.Clear();
        int active = 0;
        foreach (KeyValuePair<ulong, Push> pair in pushes)
        {
            if (now - pair.Value.Time > pushHoldTime)
            {
                expired.Add(pair.Key);
            }
            else
            {
                active++;
            }
        }

        foreach (ulong id in expired)
        {
            pushes.Remove(id);
        }

        return active;
    }

    private void ApplyPushes(int active)
    {
        if (active == 0)
        {
            return;
        }

        float speedCap = maxPushSpeed + extraSpeedPerPusher * (active - 1);
        Vector3 centre = body.worldCenterOfMass;

        foreach (Push push in pushes.Values)
        {
            if (Vector3.Dot(body.linearVelocity, push.Direction) >= speedCap)
            {
                continue;
            }

            // Pushed at the centre of mass height, so a push turns the truck a little without
            // levering it over.
            Vector3 point = new Vector3(push.Point.x, centre.y, push.Point.z);
            body.AddForceAtPosition(push.Direction * (pushAcceleration * body.mass), point, ForceMode.Force);
        }
    }

    public bool IsGrounded()
    {
        if (wheelContacts == null)
        {
            return true;
        }

        // The truck's own physics scene, not the default one, so this also holds in a preview scene.
        PhysicsScene physics = gameObject.scene.GetPhysicsScene();

        foreach (SphereCollider wheel in wheelContacts)
        {
            if (wheel == null)
            {
                continue;
            }

            float radius = wheel.radius * wheel.transform.lossyScale.y;
            Vector3 centre = wheel.transform.TransformPoint(wheel.center);
            if (physics.Raycast(centre, Vector3.down, out RaycastHit hit, radius * 1.25f, ~0, QueryTriggerInteraction.Ignore) &&
                hit.rigidbody != body)
            {
                return true;
            }
        }

        return false;
    }

    private void LateUpdate()
    {
        Vector3 position = transform.position;
        Vector3 delta = position - lastPosition;
        lastPosition = position;

        if (wheelPivots == null)
        {
            return;
        }

        float radius = wheelRadius * transform.lossyScale.x;

        // A teleport (spawn, late join snap) is not rolling.
        if (radius > 0f && delta.sqrMagnitude > 1e-8f && delta.sqrMagnitude <= radius * radius * 4f)
        {
            float distance = Vector3.Dot(delta, transform.forward);
            wheelSpin = Mathf.Repeat(wheelSpin + distance / radius * Mathf.Rad2Deg, 360f);
        }

        wheelSteer = Mathf.MoveTowards(wheelSteer, Mathf.Clamp(SteerInput, -1f, 1f) * maxWheelSteerAngle,
            wheelSteerSpeed * Time.deltaTime);

        // Spin about the axle first, then turn the whole wheel about the truck's up axis.
        Quaternion spin = Quaternion.Euler(wheelSpin, 0f, 0f);
        Quaternion steered = Quaternion.Euler(0f, wheelSteer, 0f) * spin;
        for (int i = 0; i < wheelPivots.Length; i++)
        {
            if (wheelPivots[i] != null)
            {
                wheelPivots[i].localRotation = isFrontWheel[i] ? steered : spin;
            }
        }
    }
}
