using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

public class FootStepScript : NetworkBehaviour {
    public float stepRate = 0.5f;
    public float stepCoolDown;
    public Movement movement;
    public AudioSource footstepSource;
    public AudioClip[] footstepClips;
    private CharacterController controller;
    public CinemachineImpulseSource impulseSource;

    #region Input Things
    private InputActionAsset inputActions;
    private void Awake()
    {
        controller = movement.GetComponent<CharacterController>();
        inputActions = GetComponent<InputSystem>().inputActions;
    }
    #endregion

    // NetworkVariable to track whether the player is walking
    private NetworkVariable<bool> isWalking = new(
        false, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Owner
    );

    void Update () {
        // Adjust step rate based on movement speed
        if (IsOwner)
        {
            if (movement.speedMultiplier > 1) {
                stepRate = 0.35f;
            } else {
                stepRate = 0.5f;
            }
    
            stepCoolDown -= Time.deltaTime;
    
            // Determine if the player is walking
            isWalking.Value =
                (inputActions.FindAction("Move").ReadValue<Vector2>() != Vector2.zero)
                && movement.realMovementSpeed > 1.2f  // Use movement speed instead of velocity magnitude
                && controller.isGrounded;
        }

        // Only the owning player can trigger their own footsteps
        if (isWalking.Value && stepCoolDown < 0f) 
        {
            if (IsOwner)
            {
                impulseSource.GenerateImpulse();
            }
            PlayFootstep();
            stepCoolDown = stepRate;
        }
    }

    private void PlayFootstep() {
        if (footstepClips.Length > 0) {
            footstepSource.pitch = 1f + Random.Range(-0.2f, 0.2f);
            int index = Random.Range(0, footstepClips.Length);
            Debug.Log("Playing footstep sound: " + index);
            footstepSource.PlayOneShot(footstepClips[index], 0.9f);
        } else {
            Debug.LogWarning("Footstep clips not assigned.");
        }
    }
}
