using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Player
{
    [DisallowMultipleComponent]
    public class NetworkFootstepVfx : NetworkBehaviour
    {
        [SerializeField] private ParticleSystem footstepParticleSystem;
        [SerializeField] private float lifetime = 1.25f;

        private void Awake()
        {
            if (footstepParticleSystem == null)
            {
                footstepParticleSystem = GetComponentInChildren<ParticleSystem>();
            }
        }

        public void SetLifetime(float value)
        {
            lifetime = Mathf.Max(0.05f, value);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (footstepParticleSystem == null)
            {
                footstepParticleSystem = GetComponentInChildren<ParticleSystem>();
            }

            if (footstepParticleSystem != null)
            {
                footstepParticleSystem.Clear(true);
                footstepParticleSystem.Play(true);
            }

            if (IsServer)
            {
                StartCoroutine(DespawnAfterDelay(lifetime));
            }
        }

        private IEnumerator DespawnAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);

            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn();
            }
        }
    }
}



