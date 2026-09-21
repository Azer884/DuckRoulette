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
    /// no coroutines), the stones come from a pool, and only a downward hit landing on the head
    /// knocks a player down.
    /// </summary>
    [DisallowMultipleComponent]
    public class Hail : NetworkBehaviour
    {
        [SerializeField, Tooltip("Metres above the player's root the strike has to land to count " +
            "as a hit on the head. The duck is about 2m tall.")]
        private float headHeight = 1.45f;

        [SerializeField, Tooltip("How fast the stone has to still be falling for a head strike to " +
            "knock the player down, in metres per second. Stops a stone that has already bounced " +
            "and is trickling off a shoulder from counting.")]
        private float minKnockdownSpeed = 6f;

        [SerializeField, Tooltip("Impact VFX, spawned locally on each client. Optional.")]
        private GameObject impactVfxPrefab;

        [SerializeField] private float impactVfxLifetime = 1f;

        private Rigidbody body;
        private HailSpawner owner;
        private bool hasStruck;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
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
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Clients run the same physics for the visuals, but only the server decides whether
            // anyone actually got hit.
            if (!IsServer || hasStruck)
            {
                return;
            }

            ContactPoint contact = collision.GetContact(0);
            PlayImpactClientRpc(contact.point);

            if (!collision.transform.CompareTag("Hittable"))
            {
                // Ground, props, another stone: let it bounce and let the spawner time it out.
                return;
            }

            hasStruck = true;

            if (IsHeadStrike(collision, contact))
            {
                NetworkObject victim = collision.transform.GetComponentInParent<NetworkObject>();
                if (victim != null && GameManager.Instance != null)
                {
                    GameManager.Instance.StunPlayer(victim.OwnerClientId);
                }
            }

            if (owner != null)
            {
                owner.Recycle(this);
            }
        }

        private bool IsHeadStrike(Collision collision, ContactPoint contact)
        {
            Transform victimRoot = collision.transform.root;
            float heightAboveRoot = contact.point.y - victimRoot.position.y;

            if (heightAboveRoot < headHeight)
            {
                return false;
            }

            // Still coming down hard. relativeVelocity points from the other body to this one, so
            // a falling stone reads as a negative y.
            return collision.relativeVelocity.y <= -minKnockdownSpeed;
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

            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
        }
    }
}
