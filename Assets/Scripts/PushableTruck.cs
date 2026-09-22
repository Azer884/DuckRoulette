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
    [SerializeField, Tooltip("Wheel meshes, pivoted at their centre, spun about the truck's right axis.")]
    private Transform[] wheels;

    [SerializeField, Tooltip("Wheel radius in the truck's own units (before the prefab root is scaled).")]
    private float wheelRadius = 7.75f;

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

    private readonly Dictionary<ulong, Push> pushes = new();
    private readonly List<ulong> expired = new();

    private Rigidbody body;
    private Vector3 lastPosition;

    private bool Simulates => !IsSpawned || IsServer;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        lastPosition = transform.position;
    }

    public override void OnNetworkSpawn()
    {
        lastPosition = transform.position;
    }

    public void OnPushed(ulong pusherId, Vector3 pusherPosition, Vector3 hitPoint)
    {
        if (!Simulates)
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
        if (!Simulates || body.isKinematic)
        {
            return;
        }

        float dt = Time.fixedDeltaTime;
        bool grounded = IsGrounded();

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

            float along = Vector3.Dot(velocity, forward);
            float slowed = Mathf.MoveTowards(along, 0f, rollingResistance * dt);
            body.AddForce(forward * (slowed - along), ForceMode.VelocityChange);
        }

        ApplyPushes();
    }

    private void ApplyPushes()
    {
        if (pushes.Count == 0)
        {
            return;
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

    private bool IsGrounded()
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

        if (wheels == null || wheels.Length == 0 || delta.sqrMagnitude < 1e-8f)
        {
            return;
        }

        float radius = wheelRadius * transform.lossyScale.x;
        if (radius <= 0f)
        {
            return;
        }

        // A teleport (spawn, late join snap) is not rolling.
        if (delta.sqrMagnitude > radius * radius * 4f)
        {
            return;
        }

        float distance = Vector3.Dot(delta, transform.forward);
        float degrees = distance / radius * Mathf.Rad2Deg;
        Vector3 axle = transform.right;

        foreach (Transform wheel in wheels)
        {
            if (wheel != null)
            {
                wheel.Rotate(axle, degrees, Space.World);
            }
        }
    }
}
