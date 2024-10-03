using UnityEngine;
using System.Collections;
using Unity.Netcode;

public class FootStepScript : NetworkBehaviour {
    public float stepRate = 0.5f;
    public float stepCoolDown;
    public Movement movement;
	public AudioSource footstepSource;
	public AudioClip[] footstepClips;

    // Update is called once per frame
    void Update () {
        // Check speedMultiplier for adjusting step rate
        if (movement.speedMultiplier > 1) {
            stepRate = 0.35f;
        } else {
            stepRate = 0.5f;
        }

        stepCoolDown -= Time.deltaTime;

        // Check if the player has moved a significant distance
        if (IsOwner && stepCoolDown < 0) {
            TriggerFootstepServerRpc();
            footstepSource.pitch = 1f + Random.Range(-0.2f, 0.2f);
                int index = Random.Range(0, footstepClips.Length);
                
                // Play footstep sound
                footstepSource.PlayOneShot(footstepClips[index], 0.9f);
            stepCoolDown = stepRate;
        }
    }

	[ServerRpc]
    private void TriggerFootstepServerRpc()
    {
        GameManager.Instance.PlayFootstepClientRpc(OwnerClientId);
    }
}
