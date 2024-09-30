using UnityEngine;
using System.Collections;
using Unity.Netcode;

public class FootStepScript : NetworkBehaviour {
    public float stepRate = 0.5f;
    public float stepCoolDown;
    public Movement movement;
	public AudioSource footstepSource;
	public AudioClip[] footstepClips;
    private Vector3 lastPosition;  // Store the last position of the player
    private float movementThreshold = 0.05f;  // Minimum distance to register movement

    // Start is called before the first frame update
    void Start () {
        lastPosition = transform.position;  // Initialize the lastPosition
    }

    // Update is called once per frame
    void Update () {
        // Check speedMultiplier for adjusting step rate
        if (movement.speedMultiplier > 1) {
            stepRate = 0.35f;
        } else {
            stepRate = 0.5f;
        }

        stepCoolDown -= Time.deltaTime;

        // Calculate the distance traveled since the last frame
        Vector3 movementDelta = transform.position - lastPosition;
        float movementMagnitude = movementDelta.magnitude;

        // Check if the player has moved a significant distance
        if ((movementMagnitude > movementThreshold) && IsOwner) {
            TriggerFootstepServerRpc();
            stepCoolDown = stepRate;
        }

        // Update the last position for the next frame
        lastPosition = transform.position;
    }

	[ServerRpc]
    private void TriggerFootstepServerRpc()
    {
        GameManager.Instance.PlayFootstepClientRpc(OwnerClientId);
    }
}
