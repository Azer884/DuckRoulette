using Unity.Netcode;
using UnityEngine;

namespace Weather
{
    /// <summary>
    /// One hailstone. Replaces the old Rocks component on Assets/Prefabs/Hail.prefab.
    ///
    /// What was wrong with the old one:
    ///   - it ran a ServerRpc on itself from the server (DestroyServerRpc) purely to start a
    ///     coroutine, a full round trip through the message pipeline to call a local method;
    ///   - every stone started a 10 second coroutine at spawn and a second one on impact, so a
    ///     stone that landed had two despawn timers racing and could try to despawn twice;
    ///   - any collision with a "Hittable" knocked the player down, whatever it hit them on and
    ///     whichever direction the stone was travelling - a stone rolling into an ankle stunned
    ///     just as hard as one landing on the skull;
    ///   - it despawned on every collision, including the ground, so it could never bounce, and
    ///     each stone was a fresh Instantiate/Destroy of a NetworkObject.
    ///
    /// Now: the lifetime is owned by <see cref="HailSpawner"/> (one timer loop for all stones,
    /// no coroutines), the stones come from a pool, and a stone that is still falling hard when it
    /// hits any part of a player knocks that player down (server-side, through GameManager.StunPlayer).
    ///
    /// The earlier knockdown check never fired: it required collision.relativeVelocity.y to be
    /// strongly negative, but for a stone falling onto a still player Unity reports that velocity
    /// with a positive y. The strike speed now comes from the stone's own velocity in the physics
    /// step before the contact, which does not depend on that sign convention.
    /// </summary>
    [DisallowMultipleComponent]
    public class Hail : NetworkBehaviour
    {
        [SerializeField, Tooltip("How fast the stone has to still be falling for a strike to " +
            "knock the player down, in metres per second. Stops a stone that has already bounced " +
            "and is trickling off a shoulder from counting.")]
        private float minKnockdownSpeed = 6f;

        [SerializeField, Tooltip("Seconds after a hail knockdown before hail can knock the same " +
            "player down again. Longer than the longest knockout plus stand-up, so a storm cannot " +
            "keep a player pinned by resetting their wake-up timer with every stone.")]
        private float knockdownCooldown = 9f;

        // Server only. Shared by every stone: clientId -> Time.time the next hail knockdown is allowed.
        private static readonly System.Collections.Generic.Dictionary<ulong, float> nextKnockdownAt = new();

        [SerializeField, Tooltip("Impact VFX, spawned locally on each client. Optional.")]
        private GameObject impactVfxPrefab;

        [SerializeField] private float impactVfxLifetime = 1f;

        private Rigidbody body;
        private HailSpawner owner;
        private bool hasStruck;

        // Server only. The stone's velocity at the end of the last physics step, i.e. before the
        // solver resolved the contact that OnCollisionEnter is reporting.
        private Vector3 lastVelocity;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            if (IsServer && body != null && !body.isKinematic)
            {
                lastVelocity = body.linearVelocity;
            }
        }

        /// <summary>Called by <see cref="HailSpawner"/> right after it takes this stone out of the
        /// pool, so the stone knows who to hand itself back to.</summary>
        public void Launch(HailSpawner spawner, Vector3 initialVelocity)
        {
            owner = spawner;
            hasStruck = false;

            if (body != null)
            {
                body.isKinematic = false;
                body.linearVelocity = initialVelocity;
                body.angularVelocity = Random.insideUnitSphere * 4f;
            }

            lastVelocity = initialVelocity;
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Clients run the same physics for the visuals; the server owns the stone's lifetime.
            if (!IsServer || hasStruck)
            {
                return;
            }

            ContactPoint contact = collision.GetContact(0);
            PlayImpactClientRpc(contact.point);

            NetworkObject victim = FindPlayer(collision.collider);
            if (victim == null)
            {
                // Ground, props, another stone: let it bounce and let the spawner time it out.
                return;
            }

            hasStruck = true;

            // Downward speed going into the hit. Stops a stone that has already bounced and is
            // trickling off a shoulder from counting.
            if (-lastVelocity.y >= minKnockdownSpeed)
            {
                TryKnockDown(victim.OwnerClientId);
            }

            if (owner != null)
            {
                owner.Recycle(this);
            }
        }

        // Any collider on a spawned player counts: the CharacterController, the Player-layer body
        // colliders and the ragdoll bones all sit under the player's NetworkObject.
        private static NetworkObject FindPlayer(Collider hit)
        {
            NetworkObject netObj = hit != null ? hit.GetComponentInParent<NetworkObject>() : null;
            return netObj != null && netObj.IsSpawned && netObj.IsPlayerObject ? netObj : null;
        }

        private void TryKnockDown(ulong clientId)
        {
            if (GameManager.Instance == null)
            {
                return;
            }

            float now = Time.time;
            if (nextKnockdownAt.TryGetValue(clientId, out float allowedAt) && now < allowedAt)
            {
                return;
            }

            nextKnockdownAt[clientId] = now + knockdownCooldown;
            GameManager.Instance.StunPlayer(clientId);
        }

        [ClientRpc(Delivery = RpcDelivery.Unreliable)]
        private void PlayImpactClientRpc(Vector3 position)
        {
            if (impactVfxPrefab != null)
            {
                VfxManager.SpawnOneShot(impactVfxPrefab, position, impactVfxLifetime);
            }
        }

        /// <summary>Called by the spawner before the stone goes back in the pool.</summary>
        public void ResetForPool()
        {
            hasStruck = false;
            owner = null;
            lastVelocity = Vector3.zero;

            if (body != null)
            {
                // Clients hold the stone kinematic (NetworkRigidbody), and setting a kinematic
                // body's velocity only logs a warning.
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                body.isKinematic = true;
            }
        }
    }
}
